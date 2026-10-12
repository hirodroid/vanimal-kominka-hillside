using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace KominkaDogRun
{
    /// <summary>Personal dog names, favorite, wall board, and a cached roster of visit tags.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_DogBook : UdonSharpBehaviour
    {
        public const string SaveKey = "KD_DogBook_v1";
        public KD_DogPack pack;
        public KD_Language language;
        public GameObject index, detail, help;
        public TMP_Text[] cardNames, cardBreeds;
        public Image[] cardSurfaces;
        public TMP_Text title, breed, status, favoriteLabel, message;
        public TMP_InputField nickname;
        public Button saveButton, favoriteButton;
        public RawImage portrait;
        public Texture2D[] portraits;
        public string[] breedKeys;
        public TMP_Text[] staticLabels;
        public string[] staticKeys;
        public float hideDistance = 4f;
        // 裏山改変 (DES-DOG-009 2026-10-08): インスタンス共通の毛色の行 (詳細ページの下段、[閉じる] の左)。
        // 呼び名の [保存]・お気に入りとは別の段に置き、個人設定と同じボタンで確定しない。furColor が null なら行ごと隠す。
        // 書けるのは KH_FurColor の Owner (= インスタンスのマスター) だけ。ほかの人には色名と「マスターが決めます」だけを出す
        public KominkaHillside.KH_FurColor furColor;
        public TMP_Text furValue, furNote;
        public GameObject[] furParts;       // 行の全部品 (見た目と押す面)。furColor が無いときに隠す
        public GameObject[] furEditParts;   // ◀ ▶ の見た目と押す面。Owner で色が 2 つ以上あるときだけ出す
        private int _furEditShown = -1, _furPartsShown = -1;
        private string[] _names = new string[6];
        private int _favorite = -1, _selected;
        private bool _restored;
        private float _nextRefresh, _nextRoster, _messageUntil;
        // 裏山改変 (VER-ENV-002 2026-10-12): 板から離れている間 (ページを開いていない・12 m より遠い) は Refresh を 2 s ごとに間引く。
        // 近づいたかは 0.5 s ごとに見るので、近くでは今まで通り 0.5 s。ボタン類は従来どおりその場で Refresh を呼ぶ
        private const float RefreshNearDistance = 12f, RefreshFarSeconds = 2f;
        private float _nextFarRefresh;
        private string _messageKey = "";
        private VRCPlayerApi[] _players = new VRCPlayerApi[100];
        private KD_PlayerDogTag[] _tags = new KD_PlayerDogTag[100];
        private int _playerCount;
        private KD_PlayerDogTag _localTag;
        public bool Restored { get { return _restored; } }
        public int Favorite { get { return _favorite; } }
        void Start() { _Close(); Refresh(); }
        // IMP-DOG-034: the "?" opens one private explanation page; nothing else on the board changes.
        public void _OpenHelp()
        {
            if (help != null) help.SetActive(true);
            if (detail != null) detail.SetActive(false);
            if (index != null) index.SetActive(false);
        }
        public void _CloseHelp() { _Close(); }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || _restored) return;
            string saved;
            if (PlayerData.TryGetString(player, SaveKey, out saved)) Restore(saved);
            _restored = true; _nextRoster = 0;
            if (nickname != null) nickname.SetTextWithoutNotify(_names[_selected] == null ? "" : _names[_selected]);
            Refresh();
        }
        void Restore(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return;
            string[] parts = saved.Split('|');
            if (parts.Length != 8 || parts[0] != "1") return;
            int favorite;
            if (!int.TryParse(parts[1], out favorite) || favorite < -1 || favorite >= 6) return;
            _favorite = favorite;
            for (int i = 0; i < 6; i++) _names[i] = CleanName(parts[i + 2]);
        }
        // Names are limited to characters the shared Static font carries (2026-09-17 user decision: capacity first).
        // ASCII printable, hiragana, katakana and the prolonged sound mark; full-width ASCII is folded to half-width.
        // Kanji, emoji and half-width kana are dropped (they would render as blanks).
        public const int NameLimit = 12;
        // net: callable-ok: pure sanitizer, no persistent or scene writes
        public string CleanName(string value) { return Filter(value).Trim(); }
        // net: callable-ok: pure character filter, no writes
        public string Filter(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string result = "";
            int count = 0;
            for (int i = 0; i < value.Length && count < NameLimit; i++)
            {
                int c = value[i];
                if (c >= 0xFF01 && c <= 0xFF5E) c -= 0xFEE0;
                if (c == 0x3000) c = 0x20;
                bool ok = (c >= 0x20 && c <= 0x7E && c != '|')
                    || (c >= 0x3041 && c <= 0x3096) || (c >= 0x30A1 && c <= 0x30F6) || c == 0x30FC;
                if (!ok) continue;
                result += ((char)c).ToString();
                count++;
            }
            return result;
        }
        // Wired to the input's onValueChanged: drop unusable characters as they are typed and say why.
        public void _ValidateName()
        {
            if (nickname == null) return;
            string typed = nickname.text;
            string kept = Filter(typed);
            if (kept == typed) return;
            nickname.SetTextWithoutNotify(kept);
            _messageKey = "dogbook.name.filtered"; _messageUntil = Time.time + 4f; Refresh();
        }
        void Save()
        {
            if (!_restored) return;
            string data = "1|" + _favorite.ToString();
            for (int i = 0; i < 6; i++) data += "|" + (_names[i] == null ? "" : _names[i]);
            PlayerData.SetString(SaveKey, data);
            if (_localTag != null) _localTag._Publish(_favorite);
        }
        public void _Select0() { Open(0); }
        public void _Select1() { Open(1); }
        public void _Select2() { Open(2); }
        public void _Select3() { Open(3); }
        public void _Select4() { Open(4); }
        public void _Select5() { Open(5); }
        void Open(int dog)
        {
            _selected = dog; _messageKey = ""; _messageUntil = 0;
            if (nickname != null) nickname.SetTextWithoutNotify(_names[dog] == null ? "" : _names[dog]);
            if (detail != null) detail.SetActive(true);
            if (help != null) help.SetActive(false);
            if (index != null) index.SetActive(false);
            Refresh();
        }
        public void _Close()
        {
            if (detail != null) detail.SetActive(false);
            if (help != null) help.SetActive(false);
            if (index != null) index.SetActive(true);
        }
        public void _SaveName()
        {
            if (!_restored || nickname == null) return;
            _names[_selected] = CleanName(nickname.text); nickname.SetTextWithoutNotify(_names[_selected]);
            Save(); _messageKey = "dogbook.saved"; _messageUntil = Time.time + 4f; Refresh();
        }
        // 裏山改変 (DES-DOG-009): 押した瞬間に全員へ反映 (確定ボタンは無い)。Owner でなければ KH_FurColor が何もしない
        public void _FurPrev() { if (furColor != null) furColor._Step(_selected, -1); Refresh(); }
        public void _FurNext() { if (furColor != null) furColor._Step(_selected, 1); Refresh(); }
        public void _ToggleFavorite()
        {
            if (!_restored) return;
            _favorite = _favorite == _selected ? -1 : _selected;
            Save(); Refresh();
        }
        string Text(string key) { return language != null ? language._Text(key) : key; }
        string Breed(int i) { return breedKeys != null && i < breedKeys.Length ? Text(breedKeys[i]) : ""; }
        string Name(int i) { return string.IsNullOrEmpty(_names[i]) ? Breed(i) : _names[i]; }
        void Update()
        {
            if (Time.time >= _nextRoster) { _nextRoster = Time.time + 2f; CacheRoster(); }
            bool open = (detail != null && detail.activeSelf) || (help != null && help.activeSelf);
            if (open && Utilities.IsValid(Networking.LocalPlayer)
                && Vector3.Distance(Networking.LocalPlayer.GetPosition(), transform.position) > hideDistance) _Close();
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + .5f;
            if (!open && Time.time < _nextFarRefresh)
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (Utilities.IsValid(local)
                    && (local.GetPosition() - transform.position).sqrMagnitude > RefreshNearDistance * RefreshNearDistance) return;
            }
            _nextFarRefresh = Time.time + RefreshFarSeconds; Refresh();
        }
        void CacheRoster()
        {
            VRCPlayerApi.GetPlayers(_players); _playerCount = Mathf.Min(VRCPlayerApi.GetPlayerCount(), _players.Length);
            _localTag = null;
            for (int i = 0; i < _players.Length; i++)
            {
                _tags[i] = null;
                if (i >= _playerCount || !Utilities.IsValid(_players[i])) continue;
                var objects = Networking.GetPlayerObjects(_players[i]);
                for (int j = 0; objects != null && j < objects.Length; j++)
                {
                    if (objects[j] == null || Networking.GetOwner(objects[j]) != _players[i]) continue;
                    var tag = objects[j].GetComponent<KD_PlayerDogTag>();
                    if (tag != null) { _tags[i] = tag; if (_players[i].isLocal) _localTag = tag; break; }
                }
            }
            if (_restored && _localTag != null) _localTag._Publish(_favorite);
        }
        // net: callable-ok: cached, authenticated read-only lookup
        public KD_PlayerDogTag TagFor(int playerId)
        {
            for (int i = 0; i < _playerCount; i++)
                if (Utilities.IsValid(_players[i]) && _players[i].playerId == playerId && _tags[i] != null
                    && Networking.GetOwner(_tags[i].gameObject) == _players[i]) return _tags[i];
            return null;
        }
        // net: callable-ok: deterministic choice from synchronized preferences; no writes
        public int PendingPlayer(int dog, float roll, float normalCycleSeconds)
        {
            int now = Networking.GetServerTimeInMilliseconds();
            int count = 0;
            for (int i = 0; i < _playerCount; i++)
            {
                if (!Utilities.IsValid(_players[i])) continue;
                var tag = TagFor(_players[i].playerId);
                if (tag == null) continue;
                // Wait through at least a complete normal schedule window, also after an owner change.
                if (tag.Greeted && tag.GreetedDog == dog && now - tag.GreetedMs < Mathf.Max(45000f, normalCycleSeconds * 2000f)) return -1;
                if (tag.FavoriteDog == dog && !tag.Greeted) count++;
            }
            int wanted = Mathf.Min(count - 1, Mathf.FloorToInt(roll * count));
            for (int i = 0; i < _playerCount && wanted >= 0; i++)
            {
                if (!Utilities.IsValid(_players[i])) continue;
                var tag = TagFor(_players[i].playerId);
                if (tag == null || tag.FavoriteDog != dog || tag.Greeted) continue;
                if (wanted-- == 0) return _players[i].playerId;
            }
            return -1;
        }
        // IMP-DOG-035. Only players already welcomed this visit are candidates,
        // spaced per dog (any player) and per player (this dog). Intervals are seconds.
        // net: callable-ok: deterministic choice from synchronized visit marks; no writes
        public int DropByPlayer(int dog, float roll, float dogInterval, float playerInterval, float afterGreeting)
        {
            int now = Networking.GetServerTimeInMilliseconds();
            int count = 0;
            for (int i = 0; i < _playerCount; i++)
            {
                if (!Utilities.IsValid(_players[i])) continue;
                var tag = TagFor(_players[i].playerId);
                if (tag == null) continue;
                if (tag.GreetedDog == dog && tag.Greeted && now - tag.GreetedMs < dogInterval * 1000f) return -1;
                int visited = tag.VisitedMs(dog);
                if (visited != 0 && now - visited < dogInterval * 1000f) return -1;
                if (DropByCandidate(tag, dog, now, playerInterval, afterGreeting)) count++;
            }
            int wanted = Mathf.Min(count - 1, Mathf.FloorToInt(roll * count));
            for (int i = 0; i < _playerCount && wanted >= 0; i++)
            {
                if (!Utilities.IsValid(_players[i])) continue;
                var tag = TagFor(_players[i].playerId);
                if (tag == null || !DropByCandidate(tag, dog, now, playerInterval, afterGreeting)) continue;
                if (wanted-- == 0) return _players[i].playerId;
            }
            return -1;
        }
        bool DropByCandidate(KD_PlayerDogTag tag, int dog, int now, float playerInterval, float afterGreeting)
        {
            if (tag.FavoriteDog != dog || !tag.Greeted) return false;
            if (tag.GreetedDog == dog && now - tag.GreetedMs < afterGreeting * 1000f) return false;
            int visited = tag.VisitedMs(dog);
            if (visited != 0 && now - visited < playerInterval * 1000f) return false;
            return true;
        }
        void Refresh()
        {
            for (int i = 0; staticLabels != null && staticKeys != null && i < staticLabels.Length && i < staticKeys.Length; i++)
                if (staticLabels[i] != null) staticLabels[i].text = Text(staticKeys[i]);
            for (int i = 0; i < 6; i++)
            {
                if (cardNames != null && i < cardNames.Length && cardNames[i] != null) cardNames[i].text = Name(i);
                if (cardBreeds != null && i < cardBreeds.Length && cardBreeds[i] != null)
                    cardBreeds[i].text = string.IsNullOrEmpty(_names[i]) ? (_favorite == i ? Text("dogbook.favorite.mark") : "") : Breed(i) + (_favorite == i ? " / " + Text("dogbook.favorite.mark") : "");
                if (cardSurfaces != null && i < cardSurfaces.Length && cardSurfaces[i] != null)
                    cardSurfaces[i].color = _favorite == i ? new Color(.42f,.36f,.26f,1f) : new Color(.20f,.18f,.16f,1f);
            }
            if (nickname != null) nickname.interactable = _restored;
            if (saveButton != null) saveButton.interactable = _restored;
            if (favoriteButton != null) favoriteButton.interactable = _restored;
            if (title != null) title.text = Name(_selected);
            if (breed != null) breed.text = string.IsNullOrEmpty(_names[_selected]) ? "" : Breed(_selected);
            if (portrait != null && portraits != null && _selected < portraits.Length) portrait.texture = portraits[_selected];
            if (favoriteLabel != null) favoriteLabel.text = Text(_favorite == _selected ? "dogbook.favorite.remove" : "dogbook.favorite.add");
            if (message != null) message.text = Text(!_restored ? "dogbook.loading" : Time.time < _messageUntil ? _messageKey : "dogbook.private");
            if (status != null) status.text = Text(StatusKey(_selected));
            RefreshFur();
        }
        // 裏山改変 (DES-DOG-009): 毛色の行。文字列は翻訳表の値をそのまま入れるだけ (連結しない)。表示の切り替えは変わった時だけ
        void RefreshFur()
        {
            int parts = furColor != null ? 1 : 0;
            if (parts != _furPartsShown)
            {
                _furPartsShown = parts; _furEditShown = -1;
                for (int i = 0; furParts != null && i < furParts.Length; i++) if (furParts[i] != null) furParts[i].SetActive(parts == 1);
            }
            if (furColor == null) return;
            int count = furColor.OptionCount(_selected);
            bool owner = furColor._CanEdit();
            int edit = owner && count > 1 ? 1 : 0;
            if (furValue != null) furValue.text = Text(furColor.NameKey(_selected, furColor.ColorOf(_selected)));
            if (furNote != null) furNote.text = Text(count <= 1 ? "furcolor.none" : owner ? "furcolor.label" : "furcolor.master.only");
            if (edit == _furEditShown) return;
            _furEditShown = edit;
            for (int i = 0; furEditParts != null && i < furEditParts.Length; i++) if (furEditParts[i] != null) furEditParts[i].SetActive(edit == 1);
        }
        string StatusKey(int i)
        {
            if (pack == null || pack.dogWander == null || i >= pack.dogWander.Length) return "dogbook.state.free";
            var w = pack.dogWander[i]; var f = pack.dogFetch[i];
            if (w == null || f == null) return "dogbook.state.free";
            if (f.PetActive || (w.petController != null && w.petController.IsPetting)) return "dogbook.state.pet";
            if (!f.IsIdle) return "dogbook.state.fetch";
            // 裏山改変 (DES-DOG-009): リード・水の中・洗い場は古民家に無い状態 (「のんびり」と出さない)
            if (w.hillsideLeashes != null && w.hillsideLeashes._IsLeashed(i)) return "dogbook.state.walk";
            if (f.WaterPose != 0) return "dogbook.state.water";
            if (w.IsBeingWashed || w.WashHeldSynced) return "dogbook.state.wash";
            if (w.IsSleepCalled) return "dogbook.state.companion";
            if (w.IsLiveWatching) return "dogbook.state.live";
            if (w.IsResting || w.IsAsleep) return "dogbook.state.rest";
            if (f.GreetingActive) return "dogbook.state.greet";
            if (w.IsPlaying) return "dogbook.state.play";
            return "dogbook.state.free";
        }
    }
}
