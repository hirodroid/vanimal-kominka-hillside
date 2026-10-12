using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-013: 同じカートグループの相手の声だけ、距離減衰をほぼなくす (各端末のローカル設定。同期変数は増やさない)。
    ///
    /// - 所属の正本は本体の同期配列 (cartRider / cartState / cartGroup / groupState)。自分の所属は `_MyGroup()` (降車を送った後は -1)。
    ///   相手 = 自分と同じ cartGroup の Riding / Ending のカートの cartRider (0 と自分を除く)。グループが Free なら誰もいない。
    /// - 掛ける・戻すのは ApplyCore の末尾 (VoiceSync) と OnPlayerJoined。ApplyCore は Owner の Commit・OnDeserialization・Start・
    ///   自分の降車 (LeaveCore)・1 秒の安全網から呼ばれる。毎フレームは見ない。
    /// - 差分: 適用中の playerId の一覧 (_voiceOn) と、今あるべき相手 (_voiceWant) を毎回突き合わせ、外れた相手は VRChat の既定値へ戻し、
    ///   新しい相手にだけ掛ける。空いた枠・playerId の再利用で前の相手へ残さない。
    /// - プレイヤーが無効 (退室・まだ読み込まれていない) なら SetVoice* を呼ばない (例外で Behaviour が halted になるとカート全体が止まる)。
    ///   保持中の相手も毎回有効性を確かめ、退室して無効なら (同期配列に ID が残っていても) 一覧から外すだけ。まだ無効な相手は一覧に入れず、
    ///   OnPlayerJoined か次の ApplyCore で掛け直す。OnPlayerJoined は入ってきた人の ID を一覧から捨ててから判定し直す (ID の再利用で
    ///   前の人への適用を「適用済み」と見なさない)。OnPlayerLeft は本体にあるので Voice では override しない。
    /// - 自分自身・別グループ・徒歩の人には触れない。ミュート・個別音量 (利用者側の設定) と VolumetricRadius には触れない。
    /// - ワールドに他の音声制御は無い (2026-10-12 grep: SetVoice* は UdonSharp 付属の WorldAudioSettings だけで、シーン・プレハブからの参照 0 件)。
    ///   他の制御を足すときは、戻す値をこの既定値でなくその制御の値にする設計が要る。
    /// - Voice 側から ApplyCore / Commit / 着座・降車を呼ばない (本体の入れ子の禁止に従う)。</summary>
    public partial class KH_CartManager
    {
        // VRChat の既定値 (https://creators.vrchat.com/worlds/udon/players/player-audio/ 2026-10-12 確認)
        public const float VoiceDefaultNear = 0f, VoiceDefaultFar = 25f, VoiceDefaultGain = 15f;
        public const bool VoiceDefaultLowpass = true;

        [Header("IMP-WLD-013 同じグループの声 (試作値。near は隊列の最大長 (8 台 - 1) x gap = 21 m より大きく)")]
        [Tooltip("ここまでは減衰なし (m)。VRChat 既定 0")]
        public float voiceNear = 25f;
        [Tooltip("ここで聞こえなくなる (m)。VRChat 既定 25。far だけ伸ばしても後方は小さいままなので near を上げる")]
        public float voiceFar = 40f;
        [Tooltip("声の増幅 (dB, 0..24)。VRChat 既定 15 のまま")]
        public float voiceGain = 15f;
        [Tooltip("遠い声のローパス。VRChat 既定 on のまま (後方がこもるなら実機で off を試す)")]
        public bool voiceLowpass = true;

        // ---- ローカル (同期しない) ----
        private int[] _voiceOn = new int[MaxCarts];     // 適用中の playerId (先頭 _voiceCount 個)
        private int _voiceCount;
        private int[] _voiceWant = new int[MaxCarts];   // 今あるべき相手 (VoiceSync の作業用)

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // 途中参加: 自分が入った直後は相手がまだ無効なことがある。入ってきた相手が既に同じグループにいる場合も、ここで掛け直す
            // Codex 2026-10-12 (1): 入ってきた人の ID が一覧に残っていれば (退室後に所属なしの中間状態を受け取らないまま ID が再利用された)、
            // 前の人への適用として捨てる (SetVoice は呼ばない。入ってきた人は既定の状態なので、所属していれば下の VoiceSync で掛け直す)。
            // OnPlayerLeft は本体 (KH_CartManager.cs) にあり partial で二重に override できないので、退室は VoiceSync の有効性確認で落とす
            if (Utilities.IsValid(player)) VoiceForget(player.playerId);
            VoiceSync();
        }

        /// <summary>今の所属に合わせて、相手の声の設定を掛ける・戻す (冪等。差分だけ API を呼ぶ)。</summary>
        private void VoiceSync()
        {
            if (_voiceOn == null || _voiceOn.Length < MaxCarts) { _voiceOn = new int[MaxCarts]; _voiceCount = 0; }
            if (_voiceWant == null || _voiceWant.Length < MaxCarts) _voiceWant = new int[MaxCarts];
            VRCPlayerApi lp = Networking.LocalPlayer;
            int me = Utilities.IsValid(lp) ? lp.playerId : -1;
            int want = VoiceTargets(me, _MyGroup(), cartRider, cartState, cartGroup, groupState, _voiceWant);

            // 外れた相手を既定へ戻す (残す相手は前へ詰める)
            int keep = 0;
            for (int i = 0; i < _voiceCount; i++)
            {
                int pid = _voiceOn[i];
                // Codex 2026-10-12 (1): 保持中の相手も毎回有効性を確かめる。退室して無効なら API を呼ばずに落とす
                // (同期配列に ID が残っていても保持しない。再参加・ID の再利用は未適用として扱い直す)
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(pid)))
                {
                    Debug.Log("[KH_Cart] voice-drop player=" + pid + " (gone)");
                    continue;
                }
                if (VoiceHas(_voiceWant, want, pid))
                {
                    _voiceOn[keep] = pid;
                    keep++;
                    continue;
                }
                VoiceRestore(pid);
            }
            _voiceCount = keep;

            // 新しい相手に掛ける (無効な相手は一覧に入れず、次回に回す)
            for (int i = 0; i < want; i++)
            {
                int pid = _voiceWant[i];
                if (VoiceHas(_voiceOn, _voiceCount, pid)) continue;
                if (_voiceCount >= _voiceOn.Length) break;
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                if (!Utilities.IsValid(p)) continue;
                // far を先に広げてから near を上げる (near < far を保つ)
                p.SetVoiceDistanceFar(voiceFar);
                p.SetVoiceDistanceNear(voiceNear);
                p.SetVoiceGain(voiceGain);
                p.SetVoiceLowpass(voiceLowpass);
                _voiceOn[_voiceCount] = pid;
                _voiceCount++;
                Debug.Log("[KH_Cart] voice-apply player=" + pid + " group=" + _MyGroup() + " near=" + voiceNear + " far=" + voiceFar + " count=" + _voiceCount);
            }
        }

        /// <summary>その相手を VRChat の既定値へ戻す。無効 (退室済み) なら API を呼ばずに一覧から外すだけ。</summary>
        private void VoiceRestore(int pid)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
            if (!Utilities.IsValid(p))
            {
                Debug.Log("[KH_Cart] voice-drop player=" + pid + " (gone)");
                return;
            }
            // near を先に下げてから far を戻す
            p.SetVoiceDistanceNear(VoiceDefaultNear);
            p.SetVoiceDistanceFar(VoiceDefaultFar);
            p.SetVoiceGain(VoiceDefaultGain);
            p.SetVoiceLowpass(VoiceDefaultLowpass);
            Debug.Log("[KH_Cart] voice-restore player=" + pid);
        }

        /// <summary>同じグループの相手の playerId を outIds の先頭へ入れ、件数を返す。副作用なし (Edit の検査 Cart/2 が Reflection で呼ぶ)。
        /// me <= 0・myGroup が範囲外・そのグループが Free なら 0。相手は Riding / Ending で cartGroup == myGroup、cartRider が 0 でも me でもない人 (重複は 1 回)。</summary>
        private int VoiceTargets(int me, int myGroup, int[] rider, int[] state, int[] group, int[] gState, int[] outIds)
        {
            if (me <= 0 || !ValidGroup(myGroup) || rider == null || state == null || group == null || gState == null || outIds == null) return 0;
            if (myGroup >= gState.Length || gState[myGroup] == GroupFree) return 0;
            int len = Mathf.Min(rider.Length, Mathf.Min(state.Length, group.Length));
            int n = 0;
            for (int c = 0; c < len; c++)
            {
                int pid = rider[c];
                if (pid <= 0 || pid == me) continue;
                if (!IsRidingState(state[c]) || group[c] != myGroup) continue;
                if (VoiceHas(outIds, n, pid)) continue;
                if (n >= outIds.Length) break;
                outIds[n] = pid;
                n++;
            }
            return n;
        }

        /// <summary>その ID を適用中の一覧から外す (SetVoice は呼ばない)。ID の再利用で前の人への適用を引き継がないため。</summary>
        private void VoiceForget(int pid)
        {
            if (_voiceOn == null) return;
            int keep = 0;
            for (int i = 0; i < _voiceCount; i++)
            {
                if (_voiceOn[i] == pid) continue;
                _voiceOn[keep] = _voiceOn[i];
                keep++;
            }
            if (keep != _voiceCount) Debug.Log("[KH_Cart] voice-forget player=" + pid + " (joined; re-evaluate)");
            _voiceCount = keep;
        }

        private bool VoiceHas(int[] ids, int count, int pid)
        {
            for (int i = 0; i < count; i++) if (ids[i] == pid) return true;
            return false;
        }
    }
}
