using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 §15 / §15.5 (2026-10-04 user): the filmer's camera controls and the camera drone (the marker at the camera's position).
    ///
    /// Controls (local, never synced, never saved; per mode, back to the defaults when the monitor closes):
    /// - [近く] [遠く]: the distance from the subject × ctlDistStep a press, ctlDistMin..ctlDistMax of the mode's own distance; never closer than ctlMinDist
    /// - [↑] [↓]: the camera height + ctlUpStep a press, ctlUpMin..ctlUpMax m
    /// - [←] [→]: around the subject's vertical axis, ctlYawStep a press (wraps at ±180°). §16: in 一周 they are the speed instead (÷ / × orbitSpeedStep,
    ///   orbitDegMin..orbitDegMax deg/s; [戻す] and every open give orbitDegPerSec again)
    /// - [向き▲] [向き▼]: tilt the camera itself (its position stays), ctlPitchStep a press, ctlPitchMin..ctlPitchMax and never beyond fov / 2 − 3°
    ///   so the look point stays inside the frame. Works in every mode.
    /// - [戻す]: this mode's values back to the defaults.
    /// Which buttons a mode takes is CtlKind (KH_VideoCamera.cs): CtlKindAll = the camera is "direction / distance / height from the subject"
    /// (犬追従・じぶん / 人・背中ごし・犬目線) and ApplyControls moves that offset; CtlKindDistUp = the mode itself multiplies its distances by
    /// CtlDist() and adds CtlUp() to its height (みち: [←][→] dimmed; 一周: [←][→] = speed); CtlKindNone = 定点・通り過ぎ・ディスク・キャッチ待ち
    /// keep their own placement and those buttons are dimmed. Both are applied to the mode's wanted pose (ModeTarget) BEFORE the shared
    /// post-processing, so the occlusion clip, the floor and the dog push-out still apply. Another person's pose is the one TryPersonPose
    /// returned (the gate is unchanged).
    ///
    /// The drone (shown by default, [ドローン: 見せる / 見せない], not saved): my own mailbox's KH_FilmDrone. While the monitor is open and it is
    /// shown, it sits at the camera's pose on every render tick (my screen: the local pose, never the synced one). Layer 17 (the monitor's), which
    /// this camera does not draw. Hidden when within droneHideNearMe of my head. Its pose is SENT (droneSendHz) only while it is shown AND a film
    /// session with another person is in use (that person sees it, KH_TravelMailbox.Film.cs FilmDroneUpdate); otherwise nothing is sent.
    /// </summary>
    public partial class KH_VideoCamera
    {
        public const string WordCtlNear = "近く", WordCtlFar = "遠く", WordCtlUp = "↑", WordCtlDown = "↓", WordCtlLeft = "←", WordCtlRight = "→";
        public const string WordCtlTiltUp = "向き▲", WordCtlTiltDown = "向き▼", WordCtlReset = "戻す";
        public const string WordDrone = "ドローン", WordShow = "見せる", WordHide = "見せない";
        // W5: the translation keys (the Word* above are the Japanese defaults). The arrows are the same in every language (no key)
        public const string KeyDrone = "video.drone", KeyShow = "video.drone.show", KeyHide = "video.drone.hide";
        public const int CtlNear = 0, CtlFar = 1, CtlUpIdx = 2, CtlDownIdx = 3, CtlLeftIdx = 4, CtlRightIdx = 5, CtlTiltUpIdx = 6, CtlTiltDownIdx = 7, CtlResetIdx = 8;

        /// <summary>The control row's labels in button order (the builder writes them; the glyphs are baked from them).</summary>
        public static string[] CtlWords()
        {
            return new string[] { WordCtlNear, WordCtlFar, WordCtlUp, WordCtlDown, WordCtlLeft, WordCtlRight, WordCtlTiltUp, WordCtlTiltDown, WordCtlReset };
        }

        /// <summary>W5: the translation keys of the control row in button order ("" = an arrow, the same in every language).</summary>
        public static string[] CtlKeys()
        {
            return new string[] { "video.ctl.near", "video.ctl.far", "", "", "", "", "video.ctl.tiltup", "video.ctl.tiltdown", "video.ctl.reset" };
        }

        [Header("§15.5 カメラの操作 (試作値。Video/1 が書く)")]
        public float ctlDistMin = 0.6f, ctlDistMax = 2.5f, ctlDistStep = 1.25f, ctlUpMin = -0.6f, ctlUpMax = 3f, ctlUpStep = 0.3f, ctlYawStep = 20f;
        public float ctlPitchMin = -30f, ctlPitchMax = 30f, ctlPitchStep = 5f, ctlMinDist = 0.4f, ctlPitchTau = 0.25f;
        [Tooltip("操作のボタンの背景 (CtlWords の順)。対象外の撮り方では暗くする")]
        public Image[] ctlImages;
        [Header("§15.5 ドローン (位置の目印)")]
        public float droneSendHz = 5f, droneHideNearMe = 0.3f;
        public bool droneShowDefault = true;
        public Image droneImage;
        public TMP_Text droneLabel;

        // per mode (ModeCountAll: the 10 buttons + the pattern-only おかえり, §17); defaults: distance × 1, height + 0, around + 0, tilt 0
        private float[] _cvDist, _cvUp, _cvYaw, _cvPitch;
        private float _tiltNow;
        private bool _droneShow = true, _droneSending;
        private float _droneNextSend;
        private int _droneSends;   // harness read-out

        void CtlEnsure()
        {
            if (_cvDist != null && _cvDist.Length == ModeCountAll) return;
            _cvDist = new float[ModeCountAll]; _cvUp = new float[ModeCountAll]; _cvYaw = new float[ModeCountAll]; _cvPitch = new float[ModeCountAll];
            CtlResetAll();
        }

        void CtlResetAll()
        {
            if (_cvDist == null || _cvDist.Length != ModeCountAll) { CtlEnsure(); return; }
            for (int m = 0; m < ModeCountAll; m++) { _cvDist[m] = 1f; _cvUp[m] = 0f; _cvYaw[m] = 0f; _cvPitch[m] = 0f; }
            _tiltNow = 0f;
            _orbitSpeed = orbitDegPerSec;   // §16: 一周's speed starts at the default on every open
        }

        /// <summary>§16: this mode's [近く][遠く] multiplier and [↑][↓] height (the CtlKindDistUp modes apply them to their own distances / height).</summary>
        float CtlDist() { return _cvDist != null && _mode >= 0 && _mode < _cvDist.Length ? _cvDist[_mode] : 1f; }
        float CtlUp() { return _cvUp != null && _mode >= 0 && _mode < _cvUp.Length ? _cvUp[_mode] : 0f; }

        // ---- the buttons (onClick → SendCustomEvent; local only) ----
        public void _CtlNear() { CtlStep(CtlNear); }
        public void _CtlFar() { CtlStep(CtlFar); }
        public void _CtlUp() { CtlStep(CtlUpIdx); }
        public void _CtlDown() { CtlStep(CtlDownIdx); }
        public void _CtlLeft() { CtlStep(CtlLeftIdx); }
        public void _CtlRight() { CtlStep(CtlRightIdx); }
        public void _CtlTiltUp() { CtlStep(CtlTiltUpIdx); }
        public void _CtlTiltDown() { CtlStep(CtlTiltDownIdx); }
        public void _CtlReset() { CtlStep(CtlResetIdx); }

        void CtlStep(int which)
        {
            if (!_open) return;
            CtlEnsure();
            int m = _mode;
            int kind = CtlKind(m);
            if (which == CtlTiltUpIdx) _cvPitch[m] = Mathf.Clamp(_cvPitch[m] + ctlPitchStep, ctlPitchMin, ctlPitchMax);
            else if (which == CtlTiltDownIdx) _cvPitch[m] = Mathf.Clamp(_cvPitch[m] - ctlPitchStep, ctlPitchMin, ctlPitchMax);
            else if (which == CtlResetIdx) { _cvDist[m] = 1f; _cvUp[m] = 0f; _cvYaw[m] = 0f; _cvPitch[m] = 0f; if (m == ModeOrbit) _orbitSpeed = orbitDegPerSec; }
            else if (kind != CtlKindNone)
            {
                if (which == CtlNear) _cvDist[m] = Mathf.Clamp(_cvDist[m] / ctlDistStep, ctlDistMin, ctlDistMax);
                else if (which == CtlFar) _cvDist[m] = Mathf.Clamp(_cvDist[m] * ctlDistStep, ctlDistMin, ctlDistMax);
                else if (which == CtlUpIdx) _cvUp[m] = Mathf.Clamp(_cvUp[m] + ctlUpStep, ctlUpMin, ctlUpMax);
                else if (which == CtlDownIdx) _cvUp[m] = Mathf.Clamp(_cvUp[m] - ctlUpStep, ctlUpMin, ctlUpMax);
                else if (kind == CtlKindAll && which == CtlLeftIdx) _cvYaw[m] = WrapDeg(_cvYaw[m] - ctlYawStep);
                else if (kind == CtlKindAll && which == CtlRightIdx) _cvYaw[m] = WrapDeg(_cvYaw[m] + ctlYawStep);
                // §16: in 一周 [←] = slower, [→] = faster
                else if (m == ModeOrbit && which == CtlLeftIdx) _orbitSpeed = Mathf.Clamp(_orbitSpeed / Mathf.Max(orbitSpeedStep, 1.01f), orbitDegMin, orbitDegMax);
                else if (m == ModeOrbit && which == CtlRightIdx) _orbitSpeed = Mathf.Clamp(_orbitSpeed * Mathf.Max(orbitSpeedStep, 1.01f), orbitDegMin, orbitDegMax);
            }
            // Codex review of §16 (2026-10-08): 待って寄る keeps its placed point and 追いこし / 寄って引く their offset, so a distance / height change
            // reached them only at the next move. [近く][遠く][↑][↓][戻す] in みち start the current move again from its cut (placed anew with the new
            // values). 後ずさり / 横ならび read CtlDist / CtlUp every tick and need nothing
            if (m == ModePath && which != CtlTiltUpIdx && which != CtlTiltDownIdx && which != CtlLeftIdx && which != CtlRightIdx
                && (_pathMove == PathWaitApproach || _pathMove == PathOvertake || _pathMove == PathPullAway))
            { _pathHave = false; _cutNext = true; }
            DrawControls();
        }

        static float WrapDeg(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        /// <summary>Distance / height / around on the mode's wanted position, around the subject (the occlusion / floor / push-out run after this).
        /// Only the CtlKindAll modes; the CtlKindDistUp modes already used CtlDist() / CtlUp() in their own pose.</summary>
        void ApplyControls()
        {
            if (CtlKind(_mode) != CtlKindAll || _cvDist == null) return;
            float mult = _cvDist[_mode], up = _cvUp[_mode], yaw = _cvYaw[_mode];
            if (mult == 1f && up == 0f && yaw == 0f) return;
            Vector3 off = _wantPos - _subject;
            off = Quaternion.AngleAxis(yaw, Vector3.up) * off;
            off *= mult;
            off += Vector3.up * up;   // the height first: the minimum distance below is on the FINAL position (Codex review 3)
            float len = off.magnitude;
            if (len < ctlMinDist)
            {
                // never inside the subject (the subject stays in the frame). A zero offset escapes upward (above the subject, never into the ground)
                off = len > 0.0001f ? off * (ctlMinDist / len) : Vector3.up * ctlMinDist;
            }
            _wantPos = _subject + off;
        }

        /// <summary>The tilt on top of the look rotation (ApplyPose). Smoothed; limited so the look point stays inside the frame.</summary>
        Quaternion ApplyTilt(Quaternion rot, float dt, bool cut)
        {
            float want = _cvPitch != null && _mode >= 0 && _mode < _cvPitch.Length ? _cvPitch[_mode] : 0f;
            float limit = Mathf.Max(0f, Mathf.Min(Mathf.Max(Mathf.Abs(ctlPitchMin), Mathf.Abs(ctlPitchMax)), _fov * 0.5f - 3f));
            want = Mathf.Clamp(want, -limit, limit);
            _tiltNow = cut ? want : Mathf.Lerp(_tiltNow, want, BlendFactor(dt, ctlPitchTau));
            if (Mathf.Abs(_tiltNow) < 0.01f) return rot;
            return Quaternion.AngleAxis(-_tiltNow, rot * Vector3.right) * rot;   // positive = up
        }

        // ================================================================ the drone

        /// <summary>[ドローン: 見せる / 見せない]. Hiding also stops sending at once.</summary>
        public void _ToggleDrone()
        {
            if (!_open) return;
            _droneShow = !_droneShow;
            if (!_droneShow) DroneOff();
            DrawControls();
        }

        KH_FilmDrone MyDrone()
        {
            KH_TravelMailbox b = PersonMyBox();
            return b != null ? b._Drone() : null;
        }

        /// <summary>Every render tick while open: my drone at the camera's pose (local), hidden near my head; the pose is sent only while shown and a
        /// film session with another person is in use.</summary>
        void DroneTick(float now)
        {
            if (!_droneShow) return;
            KH_FilmDrone d = MyDrone();
            if (d == null) return;
            Transform ct = cam.transform;
            bool nearMe = SelfHead() && (ct.position - _selfHead).magnitude < droneHideNearMe;
            if (nearMe) d._Hide(); else d._PlaceLocal(ct.position, ct.rotation);
            KH_TravelMailbox box = PersonMyBox();
            bool send = DroneMaySend(box);
            if (send)
            {
                if (now >= _droneNextSend)
                {
                    _droneNextSend = now + (droneSendHz > 0.1f ? 1f / droneSendHz : 0.2f);
                    d._Send(ct.position, ct.rotation, box._FilmTarget(), box._FilmSeqNow());
                    _droneSending = true; _droneSends++;
                }
            }
            else if (_droneSending) { d._StopSend(); _droneSending = false; }
        }

        /// <summary>The pose may go out only while the drone is shown and the permission GATE holds right now (in use AND the subject's synced table still
        /// has (me, seq) in a box the subject owns) - not merely the session phase, which lags the table by up to one FilmCheck (Codex review 1).</summary>
        bool DroneMaySend(KH_TravelMailbox box)
        {
            return _droneShow && box != null && box._FilmPhase() == KH_TravelMailbox.FilmInUse && box._FilmGate(box._FilmTarget());
        }

        /// <summary>Every frame while sending (PostLateUpdate, before the render-tick wait and the fade return): stop at once when the gate fails.
        /// One field test when not sending.</summary>
        void DroneSendCheck()
        {
            if (!_droneSending) return;
            KH_TravelMailbox box = PersonMyBox();
            if (DroneMaySend(box)) return;
            KH_FilmDrone d = MyDrone();
            if (d != null) d._StopSend();
            _droneSending = false;
        }

        /// <summary>The drone off my screen and not sent (hidden by the toggle, or the monitor closed).</summary>
        void DroneOff()
        {
            KH_FilmDrone d = MyDrone();
            if (d != null) { d._Hide(); d._StopSend(); }
            _droneSending = false;
        }

        void DrawControls()
        {
            if (_monitorHidden) return;   // DES-WLD-004: monitor-only drawing
            int kind = CtlKind(_mode);
            bool sides = kind == CtlKindAll || _mode == ModeOrbit;   // §16: [←][→] = around (CtlKindAll) or 一周's speed; dimmed in みち
            if (ctlImages != null)
                for (int i = 0; i < ctlImages.Length; i++)
                {
                    if (ctlImages[i] == null) continue;
                    bool on = i >= CtlTiltUpIdx || (i == CtlLeftIdx || i == CtlRightIdx ? sides : kind != CtlKindNone);   // tilt and reset work in every mode
                    ctlImages[i].color = on ? modeOffColor : modeDimColor;
                }
            if (droneLabel != null) droneLabel.text = Tx(KeyDrone, WordDrone) + ": " + (_droneShow ? Tx(KeyShow, WordShow) : Tx(KeyHide, WordHide));
            if (droneImage != null) droneImage.color = _droneShow ? modeOnColor : modeOffColor;
        }

        public Color modeDimColor = new Color(0.20f, 0.18f, 0.17f);
    }
}
