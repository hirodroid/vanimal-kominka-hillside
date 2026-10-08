using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 §17 (2026-10-08): 撮影パターン — named flows of cuts the user picks on the pattern board beside the monitor (the user's
    /// 2026-10-04 memo: "パターン 1、パターン 2 のように選べる", "Vlog がいろいろな種類で作れる", "パターン専用のメニューのボード"). おまかせ is one of
    /// them (it switches flows by the scene). AI reading, the user has not seen it yet.
    ///
    /// - One cut at a time: the player only ever calls the shot entry (ShotApply, the body of _SetShot), so ONE camera / ONE RT draws ONE cut
    ///   (the Issue's rule). It runs every PatTick s (0.5) inside PostLateUpdate, on the frame after a shot event (RaiseShot only sets a flag:
    ///   switching inside the mode function would draw one frame of the old framing), and on any frame where a throw is seen while a cut waits
    ///   for it ("投げた" is immediate).
    /// - The flows are DATA (CutTable, PatternSeqStarts / PatternSeqLengths; U# has no structs, so one flat float table, PatCols columns per
    ///   row): the mode, its argument (the みち move, the 一周 way, or ArgAuto / ArgSpotWash / ArgDogNear), the shortest and longest time, the
    ///   advance condition (Adv*) and a delay after it. A row ends when its condition came and its shortest time passed (+ the delay), or at
    ///   its longest time, or after two ticks without a subject (_state lost; 通り過ぎ with no point ahead). The last row goes back to the first.
    ///   A row whose subject is missing at the start (no dog / no wash spot) is skipped. Video/2 checks the table; adding a flow = adding rows
    ///   and a range.
    /// - Manual: while a pattern runs the mode buttons only light the current cut. A mode button, ◀ ▶, [人: …] or [とめる] stops it; × stops it.
    ///   The control buttons act on the current cut as before.
    /// - Permission: a pattern reads only me, dogs and discs. Its じぶん cut is always ME (SelfFollow), also while another person is chosen for
    ///   じぶん (that film session then times out like any other mode, KH_VideoCamera.People.cs PersonTick); it never calls the person-pose gate (People.cs) and
    ///   never sends a request.
    /// - The dog: the board's dog when the pattern starts; the frisbee flows use the dog of the disc I hold / threw last (rechecked every tick);
    ///   おまかせ's wash flow uses the dog at the wash station.
    /// - おかえり (ModeWelcome = 10, internal, no button): the dog bringing back the disc I threw, seen from my side (KH_VideoCamera.ModesPlay.cs).
    /// The board's texts (W5): Tx(key, ja) in the player's language (UITranslations.json video.pattern.* / video.scene.*); the Word* constants and
    /// PatternName / PatternNote / SceneWord are the Japanese defaults. The names and notes are static labels rewritten by TextsRefresh.
    /// </summary>
    public partial class KH_VideoCamera
    {
        // ---- §17 the internal mode (no button: ModeCount stays the number of buttons; _SetShot and the switch take < ModeCountAll) ----
        public const int ModeWelcome = 10, ModeCountAll = 11;
        public const string WordModeWelcome = "おかえり";
        // ---- advance conditions of a cut (the table's ColAdv) ----
        public const int AdvTime = 0, AdvCatch = 1, AdvPassed = 2, AdvMoveDone = 3, AdvOrbitEnd = 4, AdvThrown = 5, AdvReturning = 6, AdvDelivered = 7, AdvHolding = 8, AdvCount = 9;
        // ---- the table's ColArg besides a picker value (みち move / 一周 way): the mode's own choice / the wash spot / the dog only when near ----
        public const int ArgAuto = -1, ArgSpotWash = -2, ArgDogNear = -3;
        public const int PatCols = 6, ColMode = 0, ColArg = 1, ColMin = 2, ColMax = 3, ColAdv = 4, ColAfter = 5;
        // ---- flows (ranges of the table) and the board ----
        public const int SeqFrisbee1 = 0, SeqFrisbee2 = 1, SeqWalk1 = 2, SeqWalk2 = 3, SeqPlay = 4, SeqWash = 5, SeqIdle = 6, SeqCount = 7;
        public const int PatternCount = 6, PatternOmakase = 5;
        public const int DogRuleBoard = 0, DogRuleThrow = 1, DogRuleWash = 2;
        // ---- おまかせ's scenes (design §6.2 without ふれあい) ----
        public const int SceneNone = -1, SceneFrisbee = 0, SceneWash = 1, SceneWalk = 2, SceneIdle = 3, SceneCount = 4;
        // ---- why the last cut ended (harness read-out) ----
        public const int AdvByNone = 0, AdvByCondition = 1, AdvByMax = 2, AdvByLost = 3, AdvByScene = 4;
        public const float PatTick = 0.5f, PatFallbackSeconds = 5f;

        public const string WordPatternTitle = "パターン";
        public const string WordPatternStop = "とめる";
        public const string WordPatternIdle = "パターンを選ぶと、撮り方が自動で切り替わります。撮り方のボタンを押すと止まります";
        public const string WordPatternRetry = "撮れるカットを探しています";
        public const string WordSceneFrisbee = "フリスビー", WordSceneWash = "洗う", WordSceneWalk = "散歩", WordSceneIdle = "ふだん";
        // W5: the translation keys
        public const string KeyPatternTitle = "video.pattern.title", KeyPatternStop = "video.pattern.stop", KeyPatternIdle = "video.pattern.idle", KeyPatternRetry = "video.pattern.retry";
        /// <summary>W5: the keys of a pattern's name / note (PatternName / PatternNote are the Japanese defaults).</summary>
        public static string PatternNameKey(int p) { return p >= 0 && p < PatternCount ? "video.pattern.name" + p.ToString() : ""; }
        public static string PatternNoteKey(int p) { return p >= 0 && p < PatternCount ? "video.pattern.note" + p.ToString() : ""; }
        /// <summary>W5: the key of an おまかせ scene (SceneWord is the Japanese default).</summary>
        public static string SceneKey(int scene)
        {
            switch (scene)
            {
                case SceneFrisbee: return "video.scene.frisbee";
                case SceneWash: return "video.scene.wash";
                case SceneWalk: return "video.scene.walk";
                case SceneIdle: return "video.scene.idle";
            }
            return "";
        }

        /// <summary>§17 the board's pattern names (button labels).</summary>
        public static string PatternName(int p)
        {
            switch (p)
            {
                case 0: return "フリスビー 1";
                case 1: return "フリスビー 2";
                case 2: return "散歩 Vlog 1";
                case 3: return "散歩 Vlog 2";
                case 4: return "犬とあそぶ";
                case 5: return "おまかせ";
            }
            return "?";
        }

        /// <summary>§17 the one- or two-line explanation beside each pattern.</summary>
        public static string PatternNote(int p)
        {
            switch (p)
            {
                case 0: return "投げて、追って戻る。じぶん → キャッチ待ち → 戻る犬に犬追従";
                case 1: return "投げて、こっちから見る。じぶん → キャッチ待ち → おかえり (自分の側から)";
                case 2: return "道を歩く。待って寄る → 背中ごし → 横ならび → 犬追従 → 追いこし";
                case 3: return "景色と一周。通り過ぎ → じぶん → 離れながら一周";
                case 4: return "犬目線 → 犬追従 → 近づきながら一周 → 寄って引く";
                case 5: return "フリスビー・洗う・散歩・ふだんを見分けて切り替える";
            }
            return "";
        }

        /// <summary>§17 the flow of a board pattern; -1 = おまかせ (the flow follows the scene, SceneSeq).</summary>
        public static int PatternSeq(int p)
        {
            if (p >= 0 && p < PatternOmakase) return p;   // patterns 0..4 are flows 0..4
            return -1;
        }

        /// <summary>§17 おまかせ: the flow of a scene.</summary>
        public static int SceneSeq(int scene)
        {
            switch (scene)
            {
                case SceneFrisbee: return SeqFrisbee1;
                case SceneWash: return SeqWash;
                case SceneWalk: return SeqWalk1;
            }
            return SeqIdle;
        }

        public static string SceneWord(int scene)
        {
            switch (scene)
            {
                case SceneFrisbee: return WordSceneFrisbee;
                case SceneWash: return WordSceneWash;
                case SceneWalk: return WordSceneWalk;
                case SceneIdle: return WordSceneIdle;
            }
            return "";
        }

        /// <summary>§17 whose dog a flow films: the board's dog / the dog of the disc I hold or threw last / the dog at the wash station.</summary>
        public static int SeqDogRule(int seq)
        {
            if (seq == SeqFrisbee1 || seq == SeqFrisbee2) return DogRuleThrow;
            if (seq == SeqWash) return DogRuleWash;
            return DogRuleBoard;
        }

        /// <summary>§17 the first row of each flow in CutTable (rows, not floats).</summary>
        public static int[] PatternSeqStarts() { return new int[] { 0, 3, 6, 11, 14, 18, 21 }; }
        /// <summary>§17 the number of rows of each flow.</summary>
        public static int[] PatternSeqLengths() { return new int[] { 3, 3, 5, 3, 4, 3, 3 }; }

        /// <summary>§17 the cut table: PatCols floats per row = mode, argument, shortest s, longest s, advance condition, delay s after it.
        /// Time rows (AdvTime) play their longest time; their shortest time is when おまかせ may cut away for a new scene. Trial values.</summary>
        public static float[] CutTable()
        {
            return new float[]
            {
                // 0 フリスビー 1「投げて、追って戻る」
                ModeSelfFollow, ArgAuto, 0f, 30f, AdvThrown, 0f,
                ModeCatchWait, ArgAuto, 0f, 15f, AdvCatch, 0f,
                ModeDogFollow, ArgAuto, 0f, 25f, AdvDelivered, 0f,
                // 1 フリスビー 2「投げて、こっちから見る」
                ModeSelfFollow, ArgAuto, 0f, 30f, AdvThrown, 0f,
                ModeCatchWait, ArgAuto, 0f, 15f, AdvCatch, 1.5f,
                ModeWelcome, ArgAuto, 0f, 25f, AdvDelivered, 0f,
                // 2 散歩 Vlog 1「道を歩く」
                ModePath, PathWaitApproach, 3f, 30f, AdvMoveDone, 0f,
                ModeTps, ArgAuto, 3f, 10f, AdvTime, 0f,
                ModePath, PathBeside, 3f, 10f, AdvTime, 0f,
                ModeDogFollow, ArgAuto, 3f, 10f, AdvTime, 0f,
                ModePath, PathOvertake, 3f, 25f, AdvMoveDone, 0f,
                // 3 散歩 Vlog 2「景色と一周」
                ModePassBy, ArgAuto, 3f, 60f, AdvPassed, 0f,
                ModeSelfFollow, ArgAuto, 3f, 12f, AdvTime, 0f,
                ModeOrbit, OrbitFarther, 3f, 70f, AdvOrbitEnd, 0f,
                // 4 犬とあそぶ
                ModeDogEye, ArgAuto, 3f, 12f, AdvTime, 0f,
                ModeDogFollow, ArgAuto, 3f, 12f, AdvTime, 0f,
                ModeOrbit, OrbitCloser, 3f, 70f, AdvOrbitEnd, 0f,
                ModePath, PathPullAway, 3f, 25f, AdvMoveDone, 0f,
                // 5 おまかせ: 洗う (the wash spot I called / within patWashSpotMax, the washed dog's eye, the spot again)
                ModeSpot, ArgSpotWash, 5f, 20f, AdvTime, 0f,
                ModeDogEye, ArgAuto, 5f, 10f, AdvTime, 0f,
                ModeSpot, ArgSpotWash, 5f, 20f, AdvTime, 0f,
                // 6 おまかせ: ふだん (me, a slow circle, the board's dog when it is within patDogNear)
                ModeSelfFollow, ArgAuto, 5f, 30f, AdvTime, 0f,
                ModeOrbit, OrbitSteady, 5f, 60f, AdvTime, 0f,
                ModeDogFollow, ArgDogNear, 5f, 30f, AdvTime, 0f,
            };
        }

        /// <summary>§17 the shot event a condition waits for (0 = none: a level condition or time).</summary>
        public static int ShotOfAdv(int adv)
        {
            switch (adv)
            {
                case AdvCatch: return ShotCatch;
                case AdvPassed: return ShotPassed;
                case AdvMoveDone: return ShotMoveDone;
                case AdvOrbitEnd: return ShotOrbitEnd;
            }
            return ShotNone;
        }

        [Header("§17 撮影パターン (配線は Video/1)")]
        [Tooltip("パターンの板のボタンの背景 (PatternCount 個、板の並び)")]
        public Image[] patternImages;
        [Tooltip("選択中の下線 (PatternCount 個)")]
        public GameObject[] patternLines;
        public TMP_Text patternLabel;
        [Header("§17 撮影パターン (試作値。Video/1 が書く)")]
        [Tooltip("おまかせ: 着いた洗い場がこれ以内なら「洗う」(m)。定点は自分が呼んだ洗い場、無ければ patWashSpotMax 以内の洗い場。犬追従 (ふだん) は選んだ犬が patDogNear 以内のときだけ")]
        public float patWashNear = 4f, patWashSpotMax = 6f, patDogNear = 15f;
        [Tooltip("おかえり (内部): 自分の足元から犬の方へ welcomeDist (犬までの半分より遠くへは出ない)・右へ welcomeSide・高さ welcomeHeight、見る先は犬の頭 (注視の時定数 welcomeLookTau)")]
        public float welcomeFov = 55f, welcomeDist = 1.6f, welcomeSide = 0.8f, welcomeHeight = 0.45f, welcomeLookTau = 0.1f;

        // ---- runtime (local). The harness reads several of these by name ----
        private bool _patRun, _patStepNow, _patSawCarry;
        private int _patIdx = -1, _patSeq = -1, _patRow = -1, _patScene = SceneNone, _patPendScene = SceneNone, _patPendCount;
        private int _patDog, _patThrowDog = -1, _patLost, _patCuts, _patAdvBy;
        private int _patDogSwaps, _patPersonCuts;   // read-outs (Codex review of §17): dog-change restarts / new subjects at the person ↔ me border
        private float _patCutAt, _patNextTick, _patCondAt = -1f;
        private int _patShownKey = -999;
        private float[] _pt;
        private int[] _psStart, _psLen;

        public bool PatternRunning { get { return _patRun; } }

        // ---- the board's buttons (onClick → SendCustomEvent; local only) ----
        public void _Pattern0() { PatternStart(0); }
        public void _Pattern1() { PatternStart(1); }
        public void _Pattern2() { PatternStart(2); }
        public void _Pattern3() { PatternStart(3); }
        public void _Pattern4() { PatternStart(4); }
        public void _Pattern5() { PatternStart(5); }
        public void _PatternStop() { if (_patRun) PatternStop(); DrawPanel(); }

        void PatEnsure()
        {
            if (_pt != null) return;
            _pt = CutTable(); _psStart = PatternSeqStarts(); _psLen = PatternSeqLengths();
        }

        float PatF(int row, int col)
        {
            int i = (_psStart[_patSeq] + row) * PatCols + col;
            return i >= 0 && i < _pt.Length ? _pt[i] : 0f;
        }

        int PatI(int row, int col) { return Mathf.RoundToInt(PatF(row, col)); }

        void PatternStart(int p)
        {
            if (!_open || p < 0 || p >= PatternCount) return;
            PatEnsure();
            float now = Time.time;
            _patRun = true; _patIdx = p; _patDog = _dog; _patThrowDog = -1;
            _patScene = SceneNone; _patPendScene = SceneNone; _patPendCount = 0; _patAdvBy = AdvByNone;
            PersonUiClose();   // the list / confirmation close (nothing is sent)
            PatPersonBorder();   // Codex review (4): another person's じぶん → my じぶん is a new subject even though the mode number stays
            if (p == PatternOmakase)
            {
                PatUpdateThrowDog();
                _patScene = PatScene();
                PatEnterSeq(SceneSeq(_patScene), now);
            }
            else
            {
                if (SeqDogRule(PatternSeq(p)) == DogRuleThrow) PatUpdateThrowDog();
                PatEnterSeq(PatternSeq(p), now);
            }
            DrawPanel();
        }

        /// <summary>Stops the pattern (a mode button, ◀ ▶, [人: …], [とめる], ×). The current cut stays as a manual choice, except the internal
        /// おかえり, which becomes 犬追従 (it has no button).</summary>
        void PatternStop()
        {
            _patRun = false; _patIdx = -1; _patSeq = -1; _patRow = -1; _patScene = SceneNone; _patStepNow = false;
            PatPersonBorder();   // Codex review (4): back to filming the chosen person = a new subject (no smoothing from my framing)
            if (_mode == ModeWelcome)
            {
                _mode = ModeDogFollow;
                if (_open) NewSubject();
            }
        }

        /// <summary>Codex review (4): じぶん films another person (the person framing of People.cs, or its dog fallback) when no pattern runs and _person >= 0, but always
        /// me inside a pattern. At that border the mode number stays the same, so the "same shot" shortcut would keep the old subject's smoothing,
        /// heading and _personLive. Here the person picture is dropped and NewSubject runs (cut, detached until the new subject's first render).</summary>
        void PatPersonBorder()
        {
            if (!_open || _mode != ModeSelfFollow || (_person < 0 && !_personLive)) return;
            _personLive = false;
            NewSubject();
            _patPersonCuts++;
        }

        void PatEnterSeq(int seq, float now)
        {
            if (seq < 0 || seq >= _psStart.Length) seq = SeqIdle;
            _patSeq = seq;
            PatCutFrom(0, now);
        }

        /// <summary>Starts the first usable row from `row` on (wrapping); none usable: じぶん for PatFallbackSeconds, then try again.</summary>
        void PatCutFrom(int row, float now)
        {
            int len = _psLen[_patSeq];
            for (int k = 0; k < len; k++)
            {
                int r = (row + k) % len;
                if (PatUsable(r)) { PatCut(r, now); return; }
            }
            _patRow = -1; _patCutAt = now; _patCondAt = -1f; _patLost = 0; _patNextTick = now + PatTick;
            if (_mode != ModeSelfFollow) ShotApply(ModeSelfFollow, -1, -1); else DrawPanel();
        }

        /// <summary>Whether a row's subject exists right now (a dog for the dog modes, the wash spot, the board's dog near enough).</summary>
        bool PatUsable(int r)
        {
            int mode = PatI(r, ColMode), arg = PatI(r, ColArg);
            if (mode < 0 || mode >= ModeCountAll) return false;
            int dog = PatDogNow();
            if (arg == ArgDogNear)
                return DogValid(dog) && SelfHead() && (DogSubject(dog) - _selfHead).magnitude <= patDogNear;
            if (PickerKind(mode) == 1 && !DogValid(dog)) return false;
            if (mode == ModeSpot)
            {
                if (spotPositions == null || spotPositions.Length == 0) return false;
                if (arg == ArgSpotWash) return PatWashSpot() >= 0;
            }
            return true;
        }

        void PatCut(int r, float now)
        {
            int mode = PatI(r, ColMode), arg = PatI(r, ColArg);
            int dog = PatDogNow();
            int spot = -1;
            if (mode == ModeSpot) spot = arg == ArgSpotWash ? PatWashSpot() : NearestSpot();
            bool same = mode == _mode && (!UsesDog(mode) || dog == _dog) && (mode != ModeSpot || spot == _spot);
            if (mode == ModeSelfFollow && _personLive) same = false;   // Codex review (4): the picture is another person, the cut is me
            if (mode == ModePath && arg >= 0 && arg < PathMoveCount) { if (_pathMove != arg) same = false; _pathMove = arg; }
            if (mode == ModeOrbit && arg >= 0 && arg < OrbitKindCount) { if (_orbitKind != arg) same = false; _orbitKind = arg; }
            _patRow = r; _patCutAt = now; _patCondAt = -1f; _patLost = 0; _patSawCarry = false; _patCuts++;
            _patNextTick = now + PatTick;   // at least one render tick of this cut before it is judged
            // the same shot again (e.g. じぶん at the end of one flow and the start of the next) is not cut: no black frame, no restart
            if (same) DrawPanel(); else ShotApply(mode, dog, spot);
        }

        static bool UsesDog(int mode) { return PickerKind(mode) == 1 || mode == ModeWelcome; }

        /// <summary>The dog of the current flow (DogRule*).</summary>
        int PatDogNow()
        {
            int rule = _patSeq >= 0 ? SeqDogRule(_patSeq) : DogRuleBoard;
            if (rule == DogRuleThrow && _patThrowDog >= 0) return _patThrowDog;
            if (rule == DogRuleWash)
            {
                int st = PatWashStation();
                int held = st >= 0 ? washCalls[st]._HeldDog() : -1;
                if (DogValid(held)) return held;
            }
            return _patDog;
        }

        // ================================================================ the clock

        /// <summary>Every frame while a pattern runs (PostLateUpdate, before the render-tick wait): the 0.5 s step, the step right after a shot
        /// event, and the throw check (a cut waiting for "投げた", or おまかせ outside the frisbee scene).</summary>
        void PatternFrame(float now)
        {
            // Codex review (2): only the 0.5 s period moves _patNextTick and counts "twice in a row" (おまかせ's scene, the lost subject); a step
            // asked for by a shot event or a throw is extra and never advances those counters
            bool periodic = now >= _patNextTick;
            if (!periodic && !_patStepNow && !PatFastThrow()) return;
            _patStepNow = false;
            if (periodic) _patNextTick = now + PatTick;
            PatternStep(now, periodic);
        }

        bool PatFastThrow()
        {
            if (_patIdx == PatternOmakase && _patScene != SceneFrisbee) return PatMyFlightAny();
            if (_patRow >= 0 && _patSeq >= 0 && PatI(_patRow, ColAdv) == AdvThrown) return PatThrown(PatDogNow()) || PatMyFlightAny();
            return false;
        }

        void PatternStep(float now, bool periodic)
        {
            if (!_patRun || !_open || _patSeq < 0) return;
            float el = now - _patCutAt;
            if (_patIdx == PatternOmakase || SeqDogRule(_patSeq) == DogRuleThrow) PatUpdateThrowDog();
            if (_patIdx == PatternOmakase)
            {
                int sc = PatScene();
                if (sc != _patScene)
                {
                    if (periodic) { if (sc == _patPendScene) _patPendCount++; else { _patPendScene = sc; _patPendCount = 1; } }
                    bool thrown = sc == SceneFrisbee && PatMyFlightAny();   // "投げた" switches at once (§6.2), on any step
                    float min = _patRow >= 0 ? PatF(_patRow, ColMin) : 0f;
                    if (thrown || (_patPendCount >= 2 && el >= min))
                    {
                        _patScene = sc; _patPendScene = SceneNone; _patPendCount = 0; _patAdvBy = AdvByScene;
                        PatEnterSeq(SceneSeq(sc), now);
                        return;
                    }
                }
                else { _patPendScene = SceneNone; _patPendCount = 0; }
            }
            if (_patRow < 0)
            {
                if (el >= PatFallbackSeconds) PatCutFrom(0, now);
                return;
            }
            // the flow's dog changed (I picked up / threw another dog's disc): a dog cut follows it with a cut; other cuts just remember it
            int want = PatDogNow();
            if (want != _dog && DogValid(want))
            {
                // Codex review (1): the row's condition time, the carry history and the lost count belonged to the old dog (its catch must not end
                // the new dog's cut, its carry must not count as a delivery). A dog cut starts again with the new dog (its time too) and this step
                // ends here; another cut only remembers the dog and judges the new one below
                _patCondAt = -1f; _patSawCarry = false; _patLost = 0;
                if (UsesDog(_mode))
                {
                    _patCutAt = now; _patNextTick = now + PatTick; _patDogSwaps++;
                    ShotApply(_mode, want, -1);
                    return;
                }
                _dog = want;
            }
            // no subject on two periodic ticks in a row (about 1 s): the next row
            bool miss = _state == StateLost || (_mode == ModePassBy && _passIdx < 0);
            if (periodic) _patLost = miss ? _patLost + 1 : 0;
            if (_patLost >= 2) { _patAdvBy = AdvByLost; PatAdvance(now); return; }
            int adv = PatI(_patRow, ColAdv);
            if (_patCondAt < 0f && PatCondLevel(adv, _dog)) _patCondAt = now;
            float mn = PatF(_patRow, ColMin), mx = PatF(_patRow, ColMax), after = PatF(_patRow, ColAfter);
            if (_patCondAt >= 0f && el >= mn && now >= _patCondAt + after) { _patAdvBy = AdvByCondition; PatAdvance(now); return; }
            if (el >= mx) { _patAdvBy = AdvByMax; PatAdvance(now); }
        }

        void PatAdvance(float now)
        {
            int len = _psLen[_patSeq];
            PatCutFrom((_patRow + 1) % len, now);
        }

        /// <summary>RaiseShot's hook: an event the current row waits for latches its time and asks for a step on the next frame (never switches
        /// inside the mode function).</summary>
        void PatternOnShot(int kind)
        {
            if (!_patRun || _patRow < 0 || _patSeq < 0) return;
            int adv = PatI(_patRow, ColAdv);
            if (ShotOfAdv(adv) != kind || kind == ShotNone) return;
            if (_patCondAt < 0f) _patCondAt = Time.time;
            _patStepNow = true;
        }

        /// <summary>The level conditions (read on each step): 投げた / 戻り始めた / 受け渡した / 自分がディスクを持った. Time and the shot events: false here.</summary>
        bool PatCondLevel(int adv, int dog)
        {
            if (adv == AdvThrown) return PatThrown(dog) || PatMyFlightAny();
            if (adv == AdvHolding) return SelfHoldsDisc(dog);
            KH_FrisbeeCatch c = CatcherOf(dog);
            if (c == null) return false;
            int s = c.State;
            if (adv == AdvReturning) return s == KH_FrisbeeCatch.StateReturn || s == KH_FrisbeeCatch.StateWaterCarry || s == KH_FrisbeeCatch.StateWaterExit;
            if (adv == AdvDelivered)
            {
                bool carrying = c.IsCarrying;
                if (carrying) _patSawCarry = true;
                return s == KH_FrisbeeCatch.StateDrop || (_patSawCarry && !carrying);
            }
            return false;
        }

        // ================================================================ what the flows read (me, dogs, discs only)

        /// <summary>The dog's disc is in the air or being chased (a throw has happened): my own flight (FlightLocal, at once on my client) or the
        /// synced state before the catch.</summary>
        bool PatThrown(int dog)
        {
            KH_Frisbee d = DiscOf(dog);
            if (d != null && d.FlightLocal) return true;
            KH_FrisbeeCatch c = CatcherOf(dog);
            if (c == null) return false;
            int s = c.State;
            return s == KH_FrisbeeCatch.StateChase || s == KH_FrisbeeCatch.StateJump || s == KH_FrisbeeCatch.StateLand
                || s == KH_FrisbeeCatch.StateRetrieve || s == KH_FrisbeeCatch.StateWaterChase;
        }

        /// <summary>A disc I threw is flying right now (any active dog).</summary>
        bool PatMyFlightAny()
        {
            if (discs == null) return false;
            for (int i = 0; i < discs.Length; i++)
                if (discs[i] != null && DogValid(i) && discs[i].FlightLocal) return true;   // the same dogs PatScene looks at (no per-frame step that can never switch)
            return false;
        }

        /// <summary>The frisbee flows' dog: the disc I hold (any dog first), else the one I threw (flying, then the dog engaged with my throw). Kept until another.</summary>
        void PatUpdateThrowDog()
        {
            if (dogRoots == null) return;
            int me = SelfId();
            int found = -1;
            // Codex review (3): holding wins over a flight on ANY dog (dog 0's disc in the air, dog 1's in my hand → dog 1), so the hold search
            // finishes over all the dogs before the flight is looked at
            for (int i = 0; i < dogRoots.Length && found < 0; i++)
                if (DogValid(i) && SelfHoldsDisc(i)) found = i;
            for (int i = 0; i < dogRoots.Length && found < 0; i++)
            {
                if (!DogValid(i)) continue;
                KH_Frisbee d = DiscOf(i);
                if (d != null && d.FlightLocal) found = i;
            }
            for (int i = 0; i < dogRoots.Length && found < 0; i++)
            {
                if (!DogValid(i)) continue;
                KH_FrisbeeCatch c = CatcherOf(i);
                if (c != null && me >= 0 && c.IsEngaged && c.ThrowerId == me) found = i;
            }
            if (found >= 0) _patThrowDog = found;
        }

        /// <summary>おまかせ's scene (design §6.2 order; ふれあい left out): フリスビー (I hold a disc, threw one, or the board dog plays fetch) /
        /// 洗う (a station I called has the dog on it, or one within patWashNear does) / 散歩 (a dog on my leash) / ふだん.</summary>
        int PatScene()
        {
            int me = SelfId();
            if (dogRoots != null)
                for (int i = 0; i < dogRoots.Length; i++)
                {
                    if (!DogValid(i)) continue;
                    KH_Frisbee d = DiscOf(i);
                    if (d != null && (d.FlightLocal || SelfHoldsDisc(i))) return SceneFrisbee;
                    KH_FrisbeeCatch c = CatcherOf(i);
                    if (c != null && c.IsEngaged && ((me >= 0 && c.ThrowerId == me) || i == _patDog)) return SceneFrisbee;
                }
            if (PatWashStation() >= 0) return SceneWash;
            if (manager != null && dogRoots != null)
                for (int i = 0; i < dogRoots.Length; i++)
                    if (DogValid(i) && manager._IsMine(i)) return SceneWalk;
            return SceneIdle;
        }

        /// <summary>A wash station with a dog on it that I called, else the nearest one with a dog on it within patWashNear of me; -1 = none.</summary>
        int PatWashStation()
        {
            if (washCalls == null) return -1;
            int me = SelfId();
            bool have = SelfHead();
            int best = -1; float bd = 1e9f;
            for (int k = 0; k < washCalls.Length; k++)
            {
                KH_WashStationCall w = washCalls[k];
                if (w == null || !w._Arrived()) continue;
                if (me >= 0 && w._CallerId() == me) return k;
                if (!have) continue;
                float d = (w._AnchorPosition() - _selfFeet).magnitude;
                if (d <= patWashNear && d < bd) { bd = d; best = k; }
            }
            return best;
        }

        /// <summary>The wash spot for おまかせ: the spot of the station I called (PatWashStation), else of the nearest station within patWashSpotMax
        /// of me (with or without a dog); -1 = none.</summary>
        int PatWashSpot()
        {
            if (spotStations == null || spotPositions == null || washCalls == null) return -1;
            int st = PatWashStation();
            if (st < 0 && SelfHead())
            {
                float bd = 1e9f;
                for (int k = 0; k < washCalls.Length; k++)
                {
                    if (washCalls[k] == null) continue;
                    float d = (washCalls[k]._AnchorPosition() - _selfFeet).magnitude;
                    if (d <= patWashSpotMax && d < bd) { bd = d; st = k; }
                }
            }
            if (st < 0) return -1;
            for (int k = 0; k < spotStations.Length && k < spotPositions.Length; k++)
                if (spotStations[k] == st) return k;
            return -1;
        }

        // ================================================================ the board

        /// <summary>Lights the running pattern (colour + underline) and writes the state line. Called from DrawPanel; the text is rebuilt only
        /// when the pattern, the row, the shot or the scene changed.</summary>
        void DrawPattern()
        {
            int key = _patRun ? ((((_patIdx * 64 + (_patRow + 1)) * 16 + _mode) * 8 + (_patScene + 1)) * 8 + (_mode == ModePath ? _pathMove : _mode == ModeOrbit ? _orbitKind : 0)) : -1;
            if (key == _patShownKey) return;
            _patShownKey = key;
            if (patternImages != null)
                for (int i = 0; i < patternImages.Length; i++)
                    if (patternImages[i] != null) patternImages[i].color = _patRun && i == _patIdx ? modeOnColor : modeOffColor;
            if (patternLines != null)
                for (int i = 0; i < patternLines.Length; i++)
                    if (patternLines[i] != null) patternLines[i].SetActive(_patRun && i == _patIdx);
            if (patternLabel == null) return;
            if (!_patRun) { patternLabel.text = Tx(KeyPatternIdle, WordPatternIdle); return; }
            string line = Tx(PatternNameKey(_patIdx), PatternName(_patIdx));
            if (_patIdx == PatternOmakase) line += " (" + Tx(SceneKey(_patScene), SceneWord(_patScene)) + ")";
            if (_patRow < 0) line += "  " + ModeText(ModeSelfFollow) + " … " + Tx(KeyPatternRetry, WordPatternRetry);
            else
            {
                line += "  " + (_patRow + 1).ToString() + "/" + _psLen[_patSeq].ToString() + " " + ModeText(_mode);
                if (_mode == ModePath) line += " " + PathMoveText(_pathMove);
                else if (_mode == ModeOrbit) line += " " + OrbitKindText(_orbitKind);
            }
            patternLabel.text = line;
        }
    }
}
