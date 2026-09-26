using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-005: 犬 1 匹分の定義。
    ///
    /// **導出できるものはここに書かない。** 骨格の構造 (2 段/1 段・耳 PhysBone の有無・
    /// 尻尾の書き先・体格) は <see cref="KD_DogRig"/> がプレハブから実測する。
    /// ここに置くのは「実測しても分からない = 人間が決めること」だけ:
    /// どのプレハブか / どのクリップを使うか / ボールの大きさと色 / 徘徊レールのずらし。
    ///
    /// 定位置 (犬・ボール・リセットボタン) は**持たない** — 庭のどこへ置くかは
    /// 人間の作業で、コードが持つと Setup 再実行のたびに潰す。
    /// Setup は「既存があれば触らない / 無ければ仮置き」を守る。
    ///
    /// 2 匹目を足す手順: <see cref="All"/> に 1 行足して各 Setup を実行する。
    /// </summary>
    public struct KD_ClipRef
    {
        public string label;   // ログ用
        public string guid;
        public float length;  // m_StopTime (取り違え検出)

        public KD_ClipRef(string label, string guid, float length)
        {
            this.label = label; this.guid = guid; this.length = length;
        }

        public bool IsEmpty { get { return string.IsNullOrEmpty(guid); } }
    }

    /// <summary>IMP-INF-003: 「この名前のオブジェクトはここへ置く」の 1 行。
    /// <see cref="KD_DogDefs.LayoutTable"/> を参照。</summary>
    public struct KD_LayoutRow
    {
        /// <summary>オブジェクト名 (シーン内で一意)</summary>
        public string name;
        /// <summary>親コンテナのパス (`====KominkaDogRun====/KD_Props` など)</summary>
        public string containerPath;
        /// <summary>移行前の名前。空でなければ `Scene Layout/2 - Apply` が
        /// 見つけたときに <see cref="name"/> へ改名する。
        /// **fileID は変わらないので NetworkIDs も Inspector 参照も無傷。**</summary>
        public string legacyName;
        /// <summary>付け外しされる物 (開発専用の計器など)。
        /// 在れば置き場所を検査するが、**無くても missing にしない**
        /// (公開前に外すのが正しい状態なので、外した後の Verify を赤くしない)。</summary>
        public bool optional;

        public KD_LayoutRow(string name, string containerPath, string legacyName = null,
            bool optional = false)
        {
            this.name = name;
            this.containerPath = containerPath;
            this.legacyName = legacyName;
            this.optional = optional;
        }
    }

    /// <summary>IMP-PRG-005: ParticleLive の作品 1 本。**配列の添字 = liveId**
    /// (= `KD_LiveSystem.stageDirectors` / `KD_TreatPouch.liveItems` の添字)。
    /// 商品は別表 <see cref="KD_LiveProduct"/> — Pack は複数の作品が同じ商品を指す。</summary>
    public struct KD_LiveShow
    {
        /// <summary>ASCII。生成器の `shows/&lt;slug&gt;/`、シーンの `KD_LiveStage_&lt;slug&gt;`。</summary>
        public string slug;
        /// <summary>メニューの行に出す曲名 (英語。2026-09-06 ユーザー決定)。</summary>
        public string title;
        /// <summary>この曲を開催する権利を持つ商品の恒久 ID (<see cref="KD_LiveProduct.productKey"/>)。</summary>
        public string productKey;
        /// <summary>夜の暗さ (0 = 昼のまま / 1 = 真っ暗)。Play で詰める。</summary>
        public float darkness;

        public KD_LiveShow(string slug, string title, string productKey, float darkness)
        {
            this.slug = slug; this.title = title;
            this.productKey = productKey; this.darkness = darkness;
        }
    }

    /// <summary>IMP-PRG-005: ParticleLive の商品 (Point の買い切り)。
    /// `KD_ShopItem_Live_&lt;productKey&gt;` としてカタログへ並ぶ。</summary>
    public struct KD_LiveProduct
    {
        public string productKey;
        /// <summary>確認帯に出す商品名。</summary>
        public string displayName;
        /// <summary>値段 (pt)。台帳の外なので Play で動かしてよい。</summary>
        public int price;

        public KD_LiveProduct(string productKey, string displayName)
        {
            this.productKey = productKey; this.displayName = displayName; this.price = KD_ProductDefinitions.Get(productKey).price;
        }
    }

    public struct KD_DogDef
    {
        /// <summary>オブジェクト名のサフィックス。**ASCII 固定** (日本語はツール連携で化ける)</summary>
        public string key;
        /// <summary>ログ・Interact 文言用</summary>
        public string displayName;
        /// <summary>_MA プレハブの GUID</summary>
        public string prefabGuid;
        /// <summary>徘徊レールのずらし。犬ごとに別の値にする (0 = 従来のコーギー)</summary>
        public int seedSalt;
        /// <summary>ボール直径 (m)。体格に合わせる — DES-DOG-002 の識別層 1</summary>
        public float ballDiameter;
        /// <summary>ボールの VRCPickup.proximity (m)。IMP-FET-013。
        ///
        /// **到達距離そのものではなく、体格由来の基本距離 (≒1.25m) への足し込み** なので
        /// 0.1 でも Desktop で床のボールへ届く。小型犬ほど小さくして、口から取る操作
        /// (IMP-FET-014) が「口元へ手を伸ばした」になるようにする。小 0.1 / 中 0.3 / 大 1.0。
        /// 既存 Prefab・シーンへは Toy Physics/5 の明示 Apply で書く (Setup は既存値を触らない)。</summary>
        public float pickupProximity;
        /// <summary>ボール色。首輪の色とそろえる — 識別層 2</summary>
        public Color ballColor;

        /// <summary>吠え声の GUID (IMP-FET-005 — 識別層 5)。
        ///
        /// **体格クラス (小 / 中 / 大) はここが正本。実行時の実測から導出しない。**
        /// ダックスは体幹がコーギーの 1.012 倍で**実測上は同サイズ**だが、犬種は
        /// ミニチュアダックス = 小型犬。胴の長さが体幹長に出るだけで声とは無関係で、
        /// `bodyScale` から導くと**ダックスがコーギーの声で鳴く**
        /// (設計書 §10 決定 6)。触り心地・耳 PB と同じく「犬の性質」なのでここに書く。</summary>
        public string barkClipGuid;

        /// <summary>IMP-DOG-015: はぁはぁの息 (運動後)。**2026-09-04 に定数からここへ降ろした。**
        ///
        /// 2026-08-23 は「全犬種同じ音源で OK」で定数 1 本にしてあり、そのコメントに
        /// 「犬種で変えたくなったら、そのときフィールドへ降ろせばよい」と書いていた。
        /// Play で 4 匹が同じ息をするのが聞き分けられ、**そのときが来た**
        /// (2026-09-04 ユーザー要望「4 匹とも同じ音でバリエーションが無い」)。
        /// 前例は <see cref="barkClipGuid"/> — 同じく犬定義ごとのフィールド。
        ///
        /// **体格クラスの正本は <see cref="barkClipGuid"/> 側**で、こちらはそれに従う
        /// (声と息で犬の大きさが割れると、同じ犬から矛盾した体格が聞こえる)。
        ///
        /// ---- 割り当ての根拠 (2026-09-04 に 3 本の wav を実測。全 48kHz / 2ch / 3s 前後) ----
        ///
        /// 「重さ」はスペクトルの中央周波数 (エネルギーの半分がここより下) で測った —
        /// 体が大きいほど気道と共鳴腔が長く、息の帯域が下へ寄る。
        /// 併せて包絡線から 1 秒あたりの息の回数を数えた (小型犬ほど速く浅く喘ぐ):
        ///
        /// | wav | 中央周波数 | 8kHz 超の割合 | 息の回数 | → |
        /// |-----|-----------|--------------|---------|---|
        /// | `運動後_#3`         | **1330 Hz** (最低) | 5.1% | 0.33/s (最も長く深い) | 大 |
        /// | `運動後_variation4` | 1582 Hz | 10.9% | 1.67/s | 中 |
        /// | `はぁはぁ_variation1` | **2373 Hz** (最高) | 20.4% (最も掠れる) | 1.00/s | 小 |
        ///
        /// **2 つの指標が同じ順番を出した**ので、中央周波数の順にそのまま体格へ当てた。
        ///
        /// **3 本しか無いので 1 本は 2 犬種で共有する。** 共有先はポメとダックス —
        /// <see cref="barkClipGuid"/> が既に**両方とも小型犬**に分類している
        /// (ダックスは体幹がコーギー並みだが犬種で決める、が設計書 §10 決定 6)。
        /// 体格クラスが同じ 2 匹を重ねるのが、識別層をいちばん削らない重ね方になる。
        /// 4 本目が手に入ったらダックスへ割り当てる (共有はそこで解消する)。</summary>
        public string breathClipGuid;

        /// <summary>IMP-DOG-019 Phase 4 追補: **寝息** (2026-09-04)。
        ///
        /// 運動後の息 (<see cref="breathClipGuid"/>) と違い**定数 1 本**にしてある。
        /// 理由は 2026-08-23 に呼吸音を定数にしたときと同じで、**音源が 1 本しか無い**から —
        /// 先に 4 つ並べても同じ GUID が 4 回書かれるだけで嘘が増える。
        /// 犬種ぶんの寝息が手に入ったら、そのときフィールドへ降ろせばよい。
        ///
        /// 2026-09-08 IMP-DOG-023: ユーザー追加のゆっくりした呼吸音へ差し替え。</summary>
        public const string SleepBreathClipGuid = "440811c598c82214486ac0343c32f7df";
        /// <summary>IMP-DOG-023: 各犬の従来音源と2択にする共通の追加候補。</summary>
        public const string AlternateBreathClipGuid = "5cfefd8c43a8e2846b15a73fe219d8ca";

        // ---- 触り心地 (IMP-PET-004 / IMP-PET-005 で体感を詰めた PhysBone 値) ----
        //
        // **ここが正本。揺れ具合を変えるときはシーンではなくこの定義を直す。**
        //
        // 慣行の「Setup は人間の調整を潰さない」と衝突するが、意図的にこちらを採った
        // (2026-08-13 ユーザー決定)。理由: 実測したところ、これらの値は**シーンの中にしか
        // 存在していなかった** — Animal Wash へコードを持っていっても付いてこず、
        // 2 匹目にも自動適用されず、犬をプレハブから作り直すと消える。
        // 「Setup が犬ユニットを組み立てられること」を移植資産にするなら、
        // **犬の気持ちよさも Setup が持っていなければ意味がない。**
        //
        // 体格が違えば適値も違う見込み。006/007 では犬種ごとに実機で詰めてここへ書く。

        /// <summary>
        /// **耳 PhysBone を持たない犬種へ Setup が追加してよいか** (IMP-DOG-006)。
        ///
        /// 購入パックの中でも耳 PhysBone があるのはコーギーだけで、ポメ・ラブには無い
        /// (2026-08-13 実測)。無い犬では「撫でると耳が揺れる」が機構ごと成立しない。
        ///
        /// **自動で足さない。** 足すべきかは犬種ごとに違う判断で、耳ボーンの長さに
        /// 強く依存する (コーギー 9.07cm = headScale 比 1.25 / ポメ 2.05cm = 0.31)。
        /// 短い骨に PB を付けても揺れは見えない可能性がある。
        /// **true にするときは必ず理由をコメントに書く。**
        ///
        /// 耳 PB が既にある犬 (コーギー) では無視される。
        /// </summary>
        public bool addEarPhysBone;

        /// <summary>耳 PhysBone の当たり半径。小さいと手が当たらず「触れて揺れる」が成立しない</summary>
        public float earPbRadius;
        /// <summary>耳の戻りの速さ (小さいほどよく揺れる = 触り心地)</summary>
        public float earPbPull;
        /// <summary>
        /// IMP-DOG-020: **尻尾 PhysBone を持たない犬種へ Setup が足してよいか。**
        ///
        /// 柴 (`shibaRemake_MA`) とサモエド (`samoedo_MA2`) の購入プレハブは PhysBone が
        /// **0 本** (2026-09-05 実測。4 犬種は全部尻尾 PB を持っていた)。尻尾は撫での返事
        /// (IMP-PET-004 の振り) と催促 (IMP-FET-005) の書き先なので、無いと犬が成立しない。
        /// 耳 (<see cref="addEarPhysBone"/>) と同じく黙って足さず、犬定義で明示する。
        /// **足すときの設定は Primary (コーギー) のシーン上の尻尾 PB を複製する**
        /// (値をここから複製する犬 = <see cref="KD_DogDefs.Primary"/> の役目そのもの)。
        /// pull / spring だけは <see cref="tailPbPull"/> / <see cref="tailPbSpring"/> が正本。
        /// </summary>
        public bool addTailPhysBone;

        /// <summary>尻尾の戻りの速さ</summary>
        public float tailPbPull;
        /// <summary>尻尾のばね (振れ戻り)</summary>
        public float tailPbSpring;

        /// <summary>
        /// **例外の明示。** シーンの犬がプレハブインスタンスでなく Unpack 済みであることを許す。
        ///
        /// 既定は false = 厳密。**「Setup は Unpack を要求してはならない」を不変条件にする**ため
        /// (この仕組みは Animal Wash へ横展開する前提。移植できるのはシーンの成果物ではなく
        /// 「購入プレハブから犬ユニットを組み立てられること」なので、パイプラインの再現性が要)。
        ///
        /// 黙認せずフラグにしてあるのは、将来 Setup が誰かの犬を Unpack しても
        /// 気づけるようにするため。true にするときは**必ず理由をコメントに書く**。
        /// </summary>
        public bool allowUnpacked;

        // ---- クリップ (役割ごと。GUID + 長さで取り違えを検出する) ----
        public KD_ClipRef idle;      // 待機の主
        public KD_ClipRef idleVar1;  // 待機バリエーション (省略可)
        public KD_ClipRef idleVar2;
        public KD_ClipRef idleVar3;
        public KD_ClipRef sit;
        public KD_ClipRef lieDown;

        /// <summary>
        /// BUG-DOG-002: **伏せクリップの「体を下げる位置カーブ」が、ルート以外のボーンに打たれているとき**の直し方。
        ///
        /// この犬パックの体の高さは <c>アーマチュア/Bone</c> の位置カーブだけが決める
        /// (2 段構えではメッシュ側ルートが PositionConstraint で追従し、他 27 本は回転のみ)。
        /// ところが <c>huse (shiba_anim)</c> は沈み (local z -0.127 = world で 12.7cm 下) を
        /// **<c>hojo_born/Bone</c> に打っていて、<c>アーマチュア/Bone</c> に位置カーブが無い**
        /// (2026-09-05 実測 `Logs/imp-fet-001-rootmotion.txt`)。<c>hojo_born/Bone</c> は
        /// <c>inu</c> の ParentConstraint (購入ギミック。src = 追従GIMMICK) に遮られて骨格へ届かないので、
        /// 柴は**立位と同じ高さで脚だけ折れて浮く**。
        ///
        /// **購入クリップは触らない。** ここに「位置カーブを持っている間違ったボーンのパス」を書くと、
        /// `Dog Controller/0 - Generate Pickup Pose` が <see cref="lieDown"/> を複製し、そのパスの位置カーブを
        /// <c>アーマチュア/Bone</c> のパスへ移した**派生クリップ** (<see cref="LieDownDerivedPath"/>) を作る。
        /// Build / Verify は派生クリップを LieDown として使う (<see cref="LieDownIsDerived"/>)。
        /// 空なら購入クリップをそのまま使う (他 5 匹)。
        /// 派生クリップの世界座標での沈みは 0.127 (2026-09-06 に `SampleAnimation` で実測。原本の意図した量)。
        /// </summary>
        public string lieDownRootCurveSourcePath;

        /// <summary>BUG-DOG-002: LieDown に購入クリップではなく派生クリップを使う犬か</summary>
        public bool LieDownIsDerived { get { return !string.IsNullOrEmpty(lieDownRootCurveSourcePath); } }
        /// <summary>BUG-DOG-002: 派生した伏せクリップの置き場所 (生成物。`KD_PickupPose_{key}` と同じ作法)</summary>
        public string LieDownDerivedPath
        {
            get { return "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_LieDown_" + key + ".anim"; }
        }

        public KD_ClipRef walk;
        public KD_ClipRef run;
        public KD_ClipRef lookAt;
        public KD_ClipRef pet;       // 撫でブレンドツリーの [0]

        /// <summary>IMP-DOG-019 Phase 1: **そばで寝る本編** (`寝なでなで`。横倒しのループ)。
        /// <see cref="lieDown"/> (`huse` = 伏せ) とは別物で、伏せの挙動は一切変えない。</summary>
        public KD_ClipRef sleepLoop;

        /// <summary>IMP-DOG-019 Phase 1: **横になる出入り** (`寝なで_ONOFF`。0.58s)。
        ///
        /// **DES-DOG-003 §3.1 は「伏せ ↔ 横倒しの出入り」と書いているが、実測は違う** —
        /// 先頭フレームは**伏せ (`huse`) ではなく待機 (`taiki`) の先頭と一致する**
        /// (4 犬種で照合。コーギー/ポメ/ラブは差 0.00°、ダックスのみ最大 4.4°)。
        /// つまり中身は「**立っている姿勢 → 横倒し**」。
        ///
        /// 末尾フレームは <see cref="sleepLoop"/> の先頭フレームと**完全に一致する**
        /// (4 犬種とも最大差 0.0000) ので、`SleepIn → Sleep` はブレンド 0 で繋がる。</summary>
        public KD_ClipRef sleepInOut;

        // ---- 命名 (規則はここ 1 箇所で決める) ----
        public string DogName { get { return "KD_Dog_" + key; } }
        public string FetchSystemName { get { return "KD_FetchSystem_" + key; } }
        public string PetSystemName { get { return "KD_PetSystem_" + key; } }
        public string WanderSystemName { get { return "KD_WanderSystem_" + key; } }
        /// <summary>IMP-NET-003: 停止アンカー。**犬の子ではなく犬ユニットの中の兄弟**
        /// (dogRoot は購入アセットのプレハブインスタンスなので子を足さない)。
        ///
        /// IMP-INF-003 以前は「シーン直下の兄弟」だった。**守りたかったのは「シーン直下」
        /// ではなく「購入プレハブへ子を足さないこと」**なので、階層化しても意味は変わらない。</summary>
        public string StopAnchorName { get { return "KD_StopAnchor_" + key; } }
        public string BallName { get { return "KD_Ball_" + key; } }

        /// <summary>**廃止した物理リセットボタン (2026-09-03。IMP-FET-011)。**
        ///
        /// 名前だけ残してあるのは**消すため** — `Archive/Fetch System/4 - Remove Reset Button` が
        /// これでシーンを引いて片付け、`5 - Verify Ball Recovery` が 0 個を数える。
        /// **復帰の口は手元メニューの「おもちゃ」タブ**へ移った
        /// (押すと初期位置ではなく**呼んだ人の目の前**へ来る)。</summary>
        public string LegacyResetButtonName { get { return "KD_BallResetButton_" + key; } }

        /// <summary>IMP-INF-003: この犬 1 匹ぶんのまとまり。**「犬ユニット」の実体。**
        ///
        /// 中身は 犬本体 / Fetch / Pet / Wander / StopAnchor の 5 個。
        /// **ボール (<see cref="BallName"/>) は入れない**
        /// — <c>VRCObjectSync</c> の Respawn 先はシーン開始時の world 位置で、
        /// 「犬ごとまとめて動かす」ことが起きうるユニットの下に置くと**親を少し動かしただけで
        /// ボールの復帰位置が黙ってずれる**。ボールの Owner も犬ではなく投げた人なので、
        /// DES-NET-002 の Owner まとめにも寄与しない。犬とボールの結びつき (色・大きさ) は
        /// 既にこの定義が持っているので、階層で重ねて表現する必要が無い。→ <c>KD_Props</c></summary>
        public string UnitName { get { return "KD_DogUnit_" + key; } }
        public string UnitPath { get { return KD_DogDefs.DogsPath + "/" + UnitName; } }

        public string ControllerPath
        {
            get { return "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_" + key + ".controller"; }
        }
        public string PickupPosePath
        {
            get { return "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_PickupPose_" + key + ".anim"; }
        }
        public string BallPrefabPath
        {
            get { return "Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/KD_Ball_" + key + ".prefab"; }
        }
        public string BallMaterialPath
        {
            get { return "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Ball_" + key + ".mat"; }
        }
    }

    public static class KD_DogDefs
    {
        // ================================================================
        // 置き場所 (IMP-INF-003)。**名前と同じくここが正本**
        // ================================================================
        //
        // 階層は Editor ツールからは見えない — `KD_SceneObjects.Find` は
        // **名前でシーン全体を引く**ので、置き場所を変えても全ツールが素通りする。
        // ここが効くのは「新しく作るときにどこへ入れるか」と
        // `Scene Layout` の是正・検査だけ。
        //
        // 分け方は「Transform に意味があるか」:
        //   KD_Systems … 台帳・名簿。位置は意味を持たない
        //   KD_Dogs    … 犬 1 匹 = 1 フォルダ (犬ユニット)
        //   KD_Places  … 庭や家に据えた「場」。位置に意味がある
        //   KD_Props   … 可搬物と備品 (VRCObjectSync を持つものはここ)
        //   KD_Debug   … 開発専用。**公開前にここが空か非アクティブであることを確認する**

        /// <summary>自作物の唯一のルート。`====` は購入シーンの区切りの多数派に合わせた
        /// (`==非CC0アセット用==` だけ `==` だが、**購入シーンの命名なので直さない** —
        /// 直すと自作でないオブジェクトに差分が出て「出荷時のまま」と言えなくなる)。</summary>
        public const string WorldRootName = "====KominkaDogRun====";
        public const string SystemsPath = WorldRootName + "/KD_Systems";
        public const string DogsPath = WorldRootName + "/KD_Dogs";
        public const string PlacesPath = WorldRootName + "/KD_Places";
        public const string PropsPath = WorldRootName + "/KD_Props";
        public const string DebugPath = WorldRootName + "/KD_Debug";

        // ---- 単発オブジェクトの名前 (IMP-INF-003 でここへ集約) ----
        //
        // "KD_PhotoSystem" は KD_PhotoSetupEditor / KD_WanderSetupEditor /
        // KD_CreditSetupEditor の 3 箇所、"KD_DogPack" は 2 箇所にリテラルで散っていた。
        // LayoutTable を作るのに一覧が要るので、**4 箇所目を作らずここへ寄せる。**

        public const string DogPackName = "KD_DogPack";
        public const string PhotoSystemName = "KD_PhotoSystem";
        public const string ProgressSystemName = "KD_ProgressSystem";

        /// <summary>IMP-TRT-001 Phase 1 の進行の正本。**Phase 4b Step 6 で畳んだ** —
        /// 枠 (<see cref="TreatSlotNameOf"/>) と <see cref="TreatBoxName"/> が
        /// 役目を全部引き取ったので、シーンからもコードからも消えている。
        ///
        /// **名前だけ残してあるのは「無いこと」を機械検査するため** —
        /// `Treat System/3 - Verify` がシーン内 0 個を数える。
        /// 消すと、古いシーンを開いた人が気づかないまま
        /// **進行の正本が 2 つある状態** (Step 4 の罠) へ戻る。</summary>
        public const string LegacyTreatSystemName = "KD_TreatSystem";

        /// <summary>IMP-TRT-001 Phase 4b: おやつのルールの正本。
        /// **Manual 同期なので専用 GameObject** で、かつ**マスター固定**
        /// (`SetOwner` を呼ばない — 犬と同じ規律)。位置に意味が無いので `KD_Systems`。</summary>
        public const string TreatBoxName = "KD_TreatBox";

        /// <summary>IMP-TRT-001 Phase 4b Step 5: **手元メニュー (おやつポーチ)。**
        ///
        /// **`KD_Systems` に置く。** 腰の左脇へ付いて回るので Transform は毎フレーム
        /// 上書きされ、**シーン上の位置には何の意味も無い** — 「据えた場」ではないので
        /// `KD_Places` ではないし、掴めも置けもしないので `KD_Props` でもない。
        /// (`KD_Systems` の「Transform に意味が無い」がそのまま当てはまる唯一の理由)。
        ///
        /// シーンに 1 個で **SyncMode None・同期変数 0 本** — 各クライアントが
        /// **自分のローカルプレイヤー**へ追従させるので、他人には見えない
        /// (KanjiFind `KF_CrownMarker` と同型)。</summary>
        public const string TreatPouchName = "KD_TreatPouch";

        /// <summary>IMP-TRT-001 Phase 4b Step 7: **食べ終わりの絵と音 (4 種)。**
        ///
        /// **`KD_Systems` に置く。** 子の ParticleSystem は撃つ直前に
        /// 食べた場所へ移されるので (Simulation Space = World)、
        /// **シーン上の位置には何の意味も無い**。
        ///
        /// シーンに 1 個で **SyncMode None・同期変数 0 本** —
        /// 発火は `dogEatMs + eatDuration` から全クライアントが導出する。</summary>
        public const string TreatFxName = "KD_TreatFx";

        /// <summary>IMP-TRT-001 Phase 4b: **枠の数 = 同時に出せる人数。**
        /// 3 なのは「4 人目が待つ」が体験として成立する下限だから (設計 1)。
        /// 増やすとこの表・器・Verify が一斉に追随する。</summary>
        public const int TreatSlotCount = 3;

        /// <summary>枠 i の実体 (`VRCObjectSync` を持つので `KD_Props`)。</summary>
        public static string TreatNameOf(int i) { return "KD_Treat_" + i; }

        /// <summary>枠 i の定位置 (`Respawn` 先の目印)。**位置は人間が決める。**
        /// Phase 4b で見た目を落とした — 共有の皿は廃止されたので、
        /// ここに見える皿があると「みんなの皿」という消えた設計が絵として残る。</summary>
        public static string TreatHomeNameOf(int i) { return "KD_TreatHome_" + i; }

        /// <summary>枠 i の状態 (Manual 同期なので**専用 GameObject**。位置に意味が無いので
        /// `KD_Systems`)。</summary>
        public static string TreatSlotNameOf(int i) { return "KD_TreatSlot_" + i; }

        /// <summary>枠 0 の実体・定位置の**移行前の名前** (Phase 1 の 1 枠版)。
        /// `Scene Layout/2 - Apply` が改名する — **fileID は変わらないので
        /// NetworkID も Inspector 参照も無傷**で、Phase 1 の配線がそのまま生き残る。</summary>
        public const string LegacyTreatName = "KD_Treat";
        public const string LegacyTreatHomeName = "KD_TreatHome";

        public const string CreditBoardName = "KD_CreditBoard";

        /// <summary>IMP-PRG-002: 棚 (カタログ)。**選択の正本なのでインスタンスに 1 個だけ。**
        /// 位置に意味が無いので `KD_Systems`
        /// (陳列は <see cref="ShopShelfName"/> が別に持つ)。</summary>
        public const string ShopCatalogName = "KD_ShopCatalog";

        /// <summary>IMP-PRG-002: おもちゃの消費側。**Manual 同期なので専用 GameObject。**
        /// U# は同一 GameObject で同期方式を混在できないので、SyncMode None の
        /// カタログとは同居できない (`vrc-udonsharp` §2)。</summary>
        public const string ToyBoxName = "KD_ToyBox";

        /// <summary>IMP-PET-006: 撫でエフェクトの消費側。**個人装備なので同期変数 0 本** —
        /// おもちゃ (共有物・Manual) と同期の性格が正反対。位置に意味が無いので KD_Systems。</summary>
        public const string PetFxBoxName = "KD_PetFxBox";

        /// <summary>IMP-PET-006 Phase 3: プレイヤーごとに複製される札の雛形。
        /// **VRCPlayerObject を付けてあるので入室ごとに複製され、本人が Owner になる。**</summary>
        public const string PlayerFxTagName = "KD_PlayerFxTag";

        /// <summary>IMP-DOG-019: そばで寝るモードの台帳。**Manual 同期なので専用 GameObject。**
        /// 位置に意味が無いので `KD_Systems`。
        ///
        /// **マスター固定ではない** — この台帳の `SetOwner` を呼ぶのは権限者が開放フラグを書くときだけで、
        /// その人へ移して戻さない (犬 / おやつ台帳の固定 Owner とは別)。**犬の Ownership は絶対に取らない** (IMP-NET-005 の不変条件 3)。
        /// `CanControl` (オーナー優先、不在時だけ Master) は部屋全体の管理者判定の正本 (IMP-NET-007)。
        /// 犬 1 匹ぶんの占有は <see cref="SleepSlotNameOf"/> の枠が持つ。</summary>
        public const string SleepBoardName = "KD_SleepBoard";

        /// <summary>IMP-DOG-019: 寝る枠 (犬 1 匹に 1 つ)。**台帳の子**に置く
        /// (Issue §3 の図)。`KD_TreatSlot_{i}` と同じ枠型の同期だが、
        /// 添字が枠番号ではなく**犬の番号** (`dogIndex`) なので名前は犬 key で引く。</summary>
        public static string SleepSlotNameOf(string key) { return "KD_SleepSlot_" + key; }

        /// <summary>寝る枠のコンテナ (= 台帳)。`Scene Layout` が掘る先。</summary>
        public static string SleepSlotContainerPath
        {
            get { return SystemsPath + "/" + SleepBoardName; }
        }

        /// <summary>IMP-PRG-004: ParticleLive の台帳 (liveId / startMs / hostId)。
        /// **Manual 同期なので専用 GameObject。** 位置に意味が無いので `KD_Systems`。
        /// Ownership は開催するときだけ動く (KD_SleepBoard と同じ)。</summary>
        public const string LiveSystemName = "KD_LiveSystem";

        /// <summary>IMP-PRG-004 Phase 1: Live 中の夜 (世界の `_Color` / 空 / 木と塀 / 覆い)。
        /// **同期変数 0 本** — 台帳の同期値から各自が描く。位置に意味が無いので `KD_Systems`。</summary>
        public const string LiveNightName = "KD_LiveNight";

        /// <summary>DES-DOG-004: 飾りの台帳 (KD_AccessoryBox)。
        /// **犬 x 装着枠 -> 品目の同期変数を持つ専用 GameObject。**
        /// Transform に意味は無い (KD_Systems は名簿)。</summary>
        public const string AccessoryBoxName = "KD_AccessorySystem";

        /// <summary>「アバターも暗くする」の箱。<see cref="LiveNightName"/> の子。
        /// 実行時に頭へ追従するので**シーン上の位置に意味は無い**。</summary>
        public const string LiveNightOverlayName = "KD_LiveNightOverlay";

        // ---- IMP-PRG-005: ParticleLive の品ぞろえ (作品 × 商品) ----
        //
        // Products use fixed keys; metadata belongs to KD_ProductDefinitions.
        // **暗さは作品ごと** (0 = 昼のまま / 1 = 真っ暗。2026-09-06 ユーザー決定)。
        // 星・提灯系 1.0、昼寄りの曲は 0.6〜0.8 を初期値にし Play で調整。

        /// <summary>作品の表。**添字 = liveId** (並べ替えると開催される曲がすり替わる)。
        /// 生成器 (vrc-live-generator) の `shows/&lt;slug&gt;/` と 1:1。</summary>
        public static readonly KD_LiveShow[] LiveShows =
        {
            // 裏山改変 (DES-LIV-001 / 002、2026-09-26 統括決定 D1): 裏山専用ライブ 1 本だけ。古民家の 9 曲はここから外した
            // (古民家の商品 7 行は KD_ProductDefinitions に台帳の行として残る)。暗さ上限 1.0 (夜で終わる)。
            // 暗くなり方 (曲の頭から山へ向けて上限へ) は KH_LiveNight が liveId ごとの到達秒で持つ (D2 (b))
            new KD_LiveShow(HillsideDuskSlug, "紅葉が星になる", "live.hillside_dusk", 1.0f),
        };

        /// <summary>裏山専用ライブの演出 slug (生成器の `shows/&lt;slug&gt;/`、シーンの `KD_LiveStage_&lt;slug&gt;`)。</summary>
        public const string HillsideDuskSlug = "kh-hillside-dusk";

        /// <summary>商品の表。購入方法・未設定CEの状態は KD_ProductDefinitions が持つ。</summary>
        public static readonly KD_LiveProduct[] LiveProducts =
        {
            // 裏山改変: 裏山専用ライブの 1 商品だけ (Point 30pt)
            new KD_LiveProduct("live.hillside_dusk", "紅葉が星になる"),
        };

        /// <summary>ステージの置き場所 (`KD_Places` 直下の local)。**全曲同じ** —
        /// 犬の集合向きがステージの回転から写されるので、曲ごとに変えない。
        /// `Live System/1 - Adopt Imported Stages` が毎回書く (設計が持つ値。Inspector で詰めない)。</summary>
        public static readonly Vector3 LiveStagePosition = new Vector3(-5f, -0.39f, -1f);
        // Lotus was reviewed around the world's lanterns in this stage frame.
        public static Vector3 LiveStagePositionForName(string stageName)
        {
            return stageName == LiveStageNameOf("kd-gardenlights-004-lotus-r003")
                || stageName == "LiveStage_kd-gardenlights-004-lotus-r003"
                ? new Vector3(-3f, 0f, .5f) : LiveStagePosition;
        }
        public const float LiveStageEulerY = 270f;

        /// <summary>シーン上のステージ名。**位置に意味がある** (庭のどこで流れるか) ので `KD_Places`。
        /// 生成器は root に <see cref="LegacyLiveStageNameOf"/> で作るので、
        /// `Live System/1 - Adopt Imported Stages` が改名して `KD_Places` へ移す
        /// (再 import は root に新しい `LiveStage_*` を作り、改名済みの旧は残る → Adopt が旧を消して新を採用する)。</summary>
        public const string LiveStagePrefix = "KD_LiveStage_";
        public const string LegacyLiveStagePrefix = "LiveStage_";
        public static string LiveStageNameOf(string slug) { return LiveStagePrefix + slug; }
        public static string LegacyLiveStageNameOf(string slug) { return LegacyLiveStagePrefix + slug; }

        /// <summary>IMP-PRG-004 Phase 2: 犬が集まって伏せて見る場所 (ユーザーが仮置きした
        /// `DogsParticlePlace`)。**位置に意味がある**ので `KD_Places`。</summary>
        public const string LiveDogSpotName = "KD_LiveDogSpot";
        public const string LegacyLiveDogSpotName = "DogsParticlePlace";


        /// <summary>IMP-PRG-002: 陳列。**位置に意味がある**ので `KD_Places`。
        /// 商品 (`KD_ShopItem`) はこの子。
        /// 縦切りの間は庭・ボール置き場の近くへ仮置きする (2026-08-23 ユーザー決定) —
        /// 「買う → 選ぶ → その場で投げる」が切れないので、体験判定が距離のノイズで濁らない。
        /// **見た目は Phase 5 で作り直す前提。**</summary>
        public const string ShopShelfName = "KD_ShopShelf";

        /// <summary>IMP-TRT-001 Phase 4b: **おやつ棚 (2 台目の陳列)。**
        ///
        /// **カタログは 1 個のまま** — 2 台目もこの同じ <see cref="ShopCatalogName"/> を指す
        /// (shop-foundation §1「カタログは 1 個。陳列は何個あってもよい」)。
        /// 棚ごとにカタログを置くと**選択の正本が 2 つ**になり、
        /// どちらの棚で選んだかで結果が変わる。
        ///
        /// 分けたのは**買う場所と使う場所が違うから**ではなく、
        /// おもちゃとおやつで**商品の型が違う** (買い切り / 消耗品) ため —
        /// 同じ面に混ぜると「あと N pt」と「のこり N 個」が同居して読めなくなる。
        /// 位置に意味があるので `KD_Places`。**仮置き。場所は Play で決める。**</summary>
        public const string ShopShelfTreatName = "KD_ShopShelf_Treat";

        /// <summary>IMP-ENV-004: おじいさん NPC (場の旗印 + 説明・案内役)。
        /// **位置に意味がある**ので `KD_Places`。
        /// man_farm (購入アセット) のインスタンスは**この子へ人間が置く** —
        /// Setup は検出して是正するだけで、Instantiate も位置決めもしない。</summary>
        public const string ShopKeeperName = "KD_ShopKeeper";

        /// <summary>IMP-ENV-009: BGM の音源。**2D (spatialBlend 0) なので Transform に意味が無い**
        /// ので `KD_Systems`。据えた「場」ではないし、掴めも置けもしない。</summary>
        public const string BgmName = "KD_Bgm";
        public const string SleepAudioName = "KD_SleepAudio";

        public const string PlayDebugPanelName = "KD_PlayDebugPanel";
        public const string ProgressDebugPanelName = "KD_ProgressDebugPanel";
        public const string PetDebugTextName = "KD_PetDebugText";

        /// <summary>IMP-INF-003: 案内板。購入シーンの `====オブジェクト====` の中に
        /// `Naigation` (typo) として置かれていた自作物を、名前ごとここへ移す。</summary>
        public const string NavigationName = "KD_Navigation";
        /// <summary>移行前の名前。`Scene Layout/2 - Apply` が見つけたら改名する。</summary>
        public const string LegacyNavigationName = "Naigation";

        /// <summary>IMP-DOG-014: 水場。**位置に意味がある**ので `KD_Places`。
        /// 犬が必ず来る場所になるので、撮影の構図にも効く。</summary>
        public const string WaterPlaceName = "KD_WaterPlace";
        /// <summary>IMP-FET-015: 壁の外へ飛んだボールの受け皿 (見えないトリガーの帯)。
        /// 外壁の外側 <see cref="BallCatchMargin"/> m を囲む 4 枚の BoxCollider (isTrigger)。
        /// ボールの Owner が帯に入った瞬間に水平速度を殺し、その場に落とす → 犬が壁をすり抜けて拾いに行く。
        /// 空中で跳ね返らない (物理の壁ではない)。**位置に意味がある**ので `KD_Places`。
        /// 物理の壁にしないのは、ボールの物理材質が bounceCombine=Maximum で反発 0 の壁でも 0.6 で跳ね返り、
        /// 庭へ戻ってくる経路が残るため (2026-09-16 実測)。</summary>
        public const string BallCatchName = KD_BallController.CatchNamePrefix;
        /// <summary>外壁の当たりの外側から帯の内面までの距離 (m)。犬が壁の向こうで見えない時間 (往復 + くわえ 1s) が 3〜4 秒になる値。</summary>
        public const float BallCatchMargin = 3f;
        /// <summary>帯の厚み (m)。1 物理ステップ (20ms) で 15m/s のボールが進む 0.3m より十分厚い。</summary>
        public const float BallCatchThickness = 1f;
        /// <summary>帯の高さ (m)。VR の強投 15m/s の最高点 (≈11.5m + 手の高さ) を覆う。</summary>
        public const float BallCatchHeight = 14f;
        /// <summary>帯の下端 (地面より下へ。外の地面は -0.39〜0.0)。</summary>
        public const float BallCatchBottom = -1f;
        /// <summary>帯の層。Ignore Raycast = ポインタ・接地レイに当たらず、Pickup(13) との衝突判定は残る (Verify が見る)。</summary>
        public const int BallCatchLayer = 2;

        /// <summary>移行前の名前。FBX をシーンへドラッグすると
        /// **モデルのファイル名そのままのインスタンス**ができる。
        /// `Scene Layout/2 - Apply` が見つけたら改名する
        /// (fileID は変わらないので配置も NetworkIDs も無傷)。</summary>
        public const string LegacyWaterPlaceName =
            "tripo_convert_083d3748-8f85-4faf-8a56-ca097883c605";

        /// <summary>IMP-INF-003: 「この名前はここへ置く」の全表。
        ///
        /// **`Scene Layout` の 3 メニュー (Survey / Apply / Verify) だけがこれを読む。**
        /// Setup は `KD_SceneObjects.Ensure` の引数としてしか触らない。
        /// **新しい常設物を足したらここへ 1 行足す** (水場・おもちゃ箱・おやつ枠・店…)。
        ///
        /// 並び順がそのまま Hierarchy の兄弟順になる。</summary>
        public static System.Collections.Generic.List<KD_LayoutRow> LayoutTable
        {
            get
            {
                var t = new System.Collections.Generic.List<KD_LayoutRow>();

                // 台帳・名簿 (位置に意味が無い)
                t.Add(new KD_LayoutRow(DogPackName, SystemsPath));
                t.Add(new KD_LayoutRow("KD_Language", SystemsPath));
                t.Add(new KD_LayoutRow("KD_MenuSettings", SystemsPath));
                t.Add(new KD_LayoutRow("KD_LiveAccess", SystemsPath));
                t.Add(new KD_LayoutRow(KD_DistanceFadeSettingsEditor.SystemName, SystemsPath));
                t.Add(new KD_LayoutRow(KD_SleepPlacementEditor.SystemName, SystemsPath));
                t.Add(new KD_LayoutRow(ProgressSystemName, SystemsPath));
                // KD_TreatSystem (Phase 1 の進行の正本) は Step 6 で畳んだので行ごと落とした
                t.Add(new KD_LayoutRow(TreatBoxName, SystemsPath));
                // 手元メニュー。**位置に意味が無い** (毎フレーム腰へ追従して上書きされる)
                t.Add(new KD_LayoutRow(TreatPouchName, SystemsPath));
                // 食後エフェクト。**位置に意味が無い** (撃つ直前に食べた場所へ移す)
                t.Add(new KD_LayoutRow(TreatFxName, SystemsPath));
                for (int i = 0; i < TreatSlotCount; i++)
                    t.Add(new KD_LayoutRow(TreatSlotNameOf(i), SystemsPath));
                t.Add(new KD_LayoutRow(ShopCatalogName, SystemsPath));
                t.Add(new KD_LayoutRow(ToyBoxName, SystemsPath));
                t.Add(new KD_LayoutRow(PetFxBoxName, SystemsPath));
                t.Add(new KD_LayoutRow(PlayerFxTagName, SystemsPath));
                t.Add(new KD_LayoutRow(KD_DogBookEditor.TagName, SystemsPath));
                t.Add(new KD_LayoutRow(KD_DogBookEditor.BoardName, PlacesPath));

                // IMP-DOG-019: そばで寝るモード。**枠は台帳の子** (Issue §3 の図)。
                // 台帳が「どの犬を誰が占有しているか」の窓口なので、階層でもそれを表す —
                // 枠だけ見つけて台帳を通さない読み手が生まれると、
                // null / 範囲外の扱いが読み手ごとに分かれる
                t.Add(new KD_LayoutRow(SleepBoardName, SystemsPath));
                t.Add(new KD_LayoutRow("KD_CallPose", SystemsPath));
                foreach (var d in All)
                    t.Add(new KD_LayoutRow(SleepSlotNameOf(d.key), SleepSlotContainerPath));

                // IMP-PRG-004: ParticleLive の台帳と夜。どちらも位置に意味が無い
                t.Add(new KD_LayoutRow(LiveSystemName, SystemsPath));
                t.Add(new KD_LayoutRow("KD_Activities", SystemsPath));
                // IMP-PRG-010: ギフトコードの面 (手元メニューの中に描く。位置に意味が無い)
                t.Add(new KD_LayoutRow(KD_GiftCodeSetupEditor.SystemName, SystemsPath));
                t.Add(new KD_LayoutRow("KD_DogPovCamera", SystemsPath));
                t.Add(new KD_LayoutRow("KD_DogPovMonitor", PropsPath));
                t.Add(new KD_LayoutRow("KD_FormationPreview", PlacesPath));
                t.Add(new KD_LayoutRow("KD_ShopItem_Formation", SystemsPath));
                t.Add(new KD_LayoutRow("KD_ShopItem_HostingPlay", SystemsPath));
                t.Add(new KD_LayoutRow("KD_ShopItem_DogPov", SystemsPath));
                t.Add(new KD_LayoutRow(AccessoryBoxName, SystemsPath));
                t.Add(new KD_LayoutRow(LiveNightName, SystemsPath));

                // IMP-ENV-009: BGM。2D なので位置に意味が無い
                t.Add(new KD_LayoutRow(BgmName, SystemsPath));

                // 犬ユニット。**犬 1 匹ぶんが連続して並ぶ**ように犬ごとにまとめる
                foreach (var d in All)
                {
                    t.Add(new KD_LayoutRow(d.DogName, d.UnitPath));
                    t.Add(new KD_LayoutRow(d.FetchSystemName, d.UnitPath));
                    t.Add(new KD_LayoutRow(d.PetSystemName, d.UnitPath));
                    t.Add(new KD_LayoutRow(d.WanderSystemName, d.UnitPath));
                    t.Add(new KD_LayoutRow(d.StopAnchorName, d.UnitPath));
                }

                // 据えた場
                // IMP-ENV-016: purchased BroadleafTreeB01, project-owned seasonal material override.
                t.Add(new KD_LayoutRow("KD_SeasonTree", PlacesPath, "BroadleafTreeB01"));
                t.Add(new KD_LayoutRow("KD_SeasonView", PlacesPath, "ViewSet01"));
                t.Add(new KD_LayoutRow(PhotoSystemName, PlacesPath));
                t.Add(new KD_LayoutRow("KD_BedroomPhoto", PlacesPath));
                t.Add(new KD_LayoutRow("KD_CatUnit", PlacesPath)); // IMP-CAT-001
                t.Add(new KD_LayoutRow("KD_SleepGuide", PlacesPath));
                t.Add(new KD_LayoutRow("KD_HintDirector", SystemsPath));
                // IMP-INF-020: 初回に手元メニューを庭へ開いた状態で置く位置。位置に意味がある
                t.Add(new KD_LayoutRow(KD_WelcomeGuideEditor.SpotName, PlacesPath));
                t.Add(new KD_LayoutRow(SleepAudioName, PlacesPath));
                t.Add(new KD_LayoutRow(NavigationName, PlacesPath, LegacyNavigationName));
                t.Add(new KD_LayoutRow(WaterPlaceName, PlacesPath, LegacyWaterPlaceName));
                t.Add(new KD_LayoutRow(BallCatchName, PlacesPath)); // IMP-FET-015
                t.Add(new KD_LayoutRow(ShopShelfName, PlacesPath));
                t.Add(new KD_LayoutRow("KD_PlushDisplayBox", PlacesPath));
                t.Add(new KD_LayoutRow(ShopShelfTreatName, PlacesPath));
                t.Add(new KD_LayoutRow(ShopKeeperName, PlacesPath));
                // IMP-PRG-004 / 005: Live のステージ (生成物。作品ごとに 1 行) と犬の集合地点。
                // どちらも庭の位置に意味がある。**legacyName は持たせない** —
                // 改名は `Live System/1 - Adopt Imported Stages` の仕事 (旧を消して新を採用する工程が要るため。
                // Layout Apply は新名が既にあると改名をスキップするので差し替えに使えない)
                foreach (var s in LiveShows)
                    t.Add(new KD_LayoutRow(LiveStageNameOf(s.slug), PlacesPath));
                t.Add(new KD_LayoutRow(LiveDogSpotName, PlacesPath, LegacyLiveDogSpotName));

                // 可搬物と備品。**VRCObjectSync を持つものは必ずここ** (動かない親の下)
                foreach (var d in All)
                {
                    t.Add(new KD_LayoutRow(d.BallName, PropsPath));
                    // **リセットボタンの行は 2026-09-03 に落とした** (IMP-FET-011)。
                    // 置き場所の正本がこの表なので、**残すと Layout が「消したはずの物を
                    // 毎回 KD_Props へ作り直す」**ことになる (Layout は動かすだけだが、
                    // コンテナの整合を見る Verify がこの表を読む)
                }
                // おやつは**枠 3 つぶん**。0 番だけ Phase 1 の 1 枠版からの改名
                // (`legacyName` を使うので fileID が変わらず、参照も NetworkID も無傷)
                for (int i = 0; i < TreatSlotCount; i++)
                {
                    t.Add(new KD_LayoutRow(TreatNameOf(i), PropsPath,
                        i == 0 ? LegacyTreatName : null));
                    t.Add(new KD_LayoutRow(TreatHomeNameOf(i), PropsPath,
                        i == 0 ? LegacyTreatHomeName : null));
                }

                // 開発専用。optional — Attach/Detach で付け外しされ、
                // 公開前は「無い」のが正しい状態
                t.Add(new KD_LayoutRow(PlayDebugPanelName, DebugPath, null, true));
                t.Add(new KD_LayoutRow(ProgressDebugPanelName, DebugPath, null, true));
                t.Add(new KD_LayoutRow(PetDebugTextName, DebugPath, null, true));

                return t;
            }
        }

        /// <summary>コンテナのパス一覧 (`Scene Layout/3 - Verify` が
        /// 「Transform が単位元か」を検査する対象)。</summary>
        public static string[] ContainerPaths
        {
            get
            {
                var list = new System.Collections.Generic.List<string>
                {
                    WorldRootName, SystemsPath, DogsPath, PlacesPath, PropsPath, DebugPath,
                    // IMP-DOG-019: 台帳は**枠 4 つの親でもある**ので、コンテナとして
                    // Transform の単位元を検査する (寝る枠は位置を持たないが、
                    // 台帳が動くと配下ごと動いたことに気づけない)
                    SleepSlotContainerPath,
                };
                foreach (var d in All) list.Add(d.UnitPath);
                return list.ToArray();
            }
        }

        /// <summary>
        /// コーギー。**クリップ GUID は IMP-DOG-005 以前の AC_KD_Dog と同一**
        /// (2026-08-13 に Verify の出力から転記)。同一にしてあることが機能等価の根拠。
        ///
        /// 待機バリエーションが ookami / 犬_テンプレ 由来なのは、ボーンパス一致率 100% を
        /// 実測したうえでコーギーに合うものを選んだ結果 (IMP-DOG-002 / 設計書 §5)。
        /// **他犬種でそのまま使えるとは限らない** — ルート位置カーブは体格そのものなので、
        /// 犬種ごとに 8 - Diagnose Idle Root Motion で照合してから採用する。
        /// </summary>
        public static readonly KD_DogDef Corgi = new KD_DogDef
        {
            key = "Corgi",
            displayName = "コーギー",
            prefabGuid = "de3f682e15a9ad74093cb1c4970f6520", // コーギー_MA.prefab
            seedSalt = 0,                                    // 0 = 従来と同一の徘徊レール
            ballDiameter = 0.085f,                           // コーギーの口に合うテニスボール弱
            pickupProximity = 0.3f,                            // 中型 (IMP-FET-013)
            barkClipGuid = "6a60e2ccece00a64f927ec382deb5bac", // 中型犬 (体幹 0.204)
            // 運動後の息: `運動後_variation4` = 中央 1582Hz (3 本の真ん中)。中型犬の枠
            breathClipGuid = "9400d40e8f391cf4eb5801c34a0cd5d0",
            ballColor = new Color(0.85f, 0.20f, 0.15f),      // 赤 (芝生・畳の上で見つけやすい)

            // 触り心地 (2026-08-13 に実機調整済みのシーンから実測して転記。
            // 素の購入プレハブは 耳 radius=0.01 / 尻尾 pull=0.05 spring=0 — つまり
            // **この 4 値がコーギーの「気持ちよさ」の実体**)
            earPbRadius = 0.03f,
            earPbPull = 0.2f,
            tailPbPull = 0.3f,
            tailPbSpring = 0.2f,

            // 2026-08-13: **例外は解消した。**
            // かつてコーギーは Unpack 済みだった (IMP-PET-005 の耳 v2 撤去がボーンの
            // 再親子付けを要求したため)。5 - Diff で「置き直すと何を失うか」を実測したところ、
            // 失うのは触り心地の 4 値だけ = それを犬定義へ移せば無損失と分かったので、
            // 6 - Relink でプレハブインスタンスへ戻した。
            // **全犬種で allowUnpacked = false (既定) が保てている。**
            allowUnpacked = false,

            idle = new KD_ClipRef("taiki 6 (ko-gi-)", "900d7453d14409b49957f6210fa1020a", 12.0f),
            idleVar1 = new KD_ClipRef("taiki 1 (ookami)", "8f744ef50316a994eabed38db0d42837", 15.0f),
            idleVar2 = new KD_ClipRef("taiki 2 (ookami)", "c4762410fbe7b0d459e88ce2b9c34e13", 15.0f),
            idleVar3 = new KD_ClipRef("taiki 5 (犬_テンプレ)", "f668ea4736bcc7f489f647e128543985", 12.0f),
            sit = new KD_ClipRef("osuwari (ko-gi-)", "5e448255d0bd8574894ae68f76a0272d", 17.0f),
            lieDown = new KD_ClipRef("huse (ko-gi-)", "d7e080edd7f7e6d4aa904a37130372f9", 12.016666f),
            walk = new KD_ClipRef("aruku (ko-gi-)", "eab42ac2032e9ec45bd44c5df7ff6dc6", 5.0f),
            run = new KD_ClipRef("hasiru1 (ko-gi-)", "14117a4128efa284a952f3af4809efea", 0.5f),
            lookAt = new KD_ClipRef("taimen 2 (ko-gi-)", "5fe63cc983111d044b71f8b8158e09d5", 8.933333f),
            pet = new KD_ClipRef("なで1 (ookami)", "a9dd07fab9b40b347afebd13e2d00f6f", 10.216666f),

            // IMP-DOG-019 Phase 1。**どちらもコーギー自身のフォルダ** (借り物ではない)。
            // ルート (アーマチュア/Bone) は Y も X も**レストからのずれ 0.0000** で、
            // 動くのは Z だけ (huse -0.0437 → 寝 -0.0810 = 後ろへ 3.7cm)
            sleepLoop = new KD_ClipRef("寝なでなで (ko-gi-)", "02ee37b15a3651245be5aaf6f488273a", 25.016666f),
            sleepInOut = new KD_ClipRef("寝なで_ONOFF (ko-gi-)", "b9ddebc667b9d5b4b9cd79134dc386fe", 0.5833333f),
        };

        /// <summary>
        /// ポメラニアン (もちぽめ)。**IMP-DOG-006 で投入した 2 匹目。**
        ///
        /// コーギーと構造が違うところ (2026-08-13 実測。詳細は設計書 §0):
        /// - **1 段構え** (`inu/アーマチュア` のみ / RotationConstraint 0)
        ///   → 追従の書き先はボーン直。`KD_DogRig.WriteBone` が自動で切り替える
        /// - **耳 PhysBone が無い** → 「撫でると耳が揺れる」は成立しない (下記 <see cref="earPbRadius"/>)
        /// - 尻尾 PB ルートが `Bone.019` (コーギーは `Bone.020`)。解決器が構造で引くので定義には書かない
        /// - **アーマチュア/Bone のレスト位置が (0, 0.1527, 0.1890)** (コーギーは原点)。
        ///   ルート位置カーブの検査はここからのずれで見る (KD_DogAnimatorBuilderEditor)
        ///
        /// クリップは**すべてポメ自身のフォルダ** (`gimmick/pome/anim 1`) から採った。
        /// コーギーが待機バリエーションを ookami / 犬_テンプレ から借りているのは
        /// ko-gi- フォルダに `taiki 1/2` が無いためで、ポメは自前で持っている。
        /// **借りる必要がないなら借りない** — ルート位置カーブは体格そのものなので。
        /// </summary>
        public static readonly KD_DogDef Pome = new KD_DogDef
        {
            key = "Pome",
            displayName = "ポメラニアン",
            prefabGuid = "e9a4544f2eda9254f965d555060479e8", // pome_MA.prefab
            seedSalt = 1,                                    // コーギー (0) と別のレールにする

            // 口に合う大きさ。頭の代表寸法比は 0.0660/0.0723 = 0.913 (→ 0.078)、
            // 口の幅に近い頬幅比は 0.0462/0.0538 = 0.859 (→ 0.073)。
            // **くわえられることが要件**なので狭い方の比へ寄せる。
            //
            // ただし体格比だけでは決まらない — **DES-DOG-002 の識別層 1
            // 「大きさの違いで対応が非言語に伝わる」には犬種間 1.2 倍が要る**
            // (Fetch Verify がこれを見ている)。0.073 だとコーギーの 0.085 に対し
            // 1.16 倍で読めない。1.2 倍を満たす 0.0708 まで下げても頬幅比から
            // 4% しか外れないので、**両方を満たす側を採る**。実機で詰める対象
            ballDiameter = 0.070f,
            pickupProximity = 0.1f,                            // 小型 (IMP-FET-013)
            barkClipGuid = "9f31abb395098b748833f8743a2c093f", // 小型犬 (体幹 0.117 — 最小)
            // 運動後の息: `はぁはぁ_variation1` = 中央 2373Hz (最高) / 8kHz 超 20.4% (最も掠れる)。
            // **ダックスと共有する 1 本**。3 本しか無く、小型犬 2 匹が体格クラスで同じだから
            breathClipGuid = "ca92bc23a9df4cf4ba60baa683086294",
            ballColor = new Color(0.20f, 0.45f, 0.85f),      // 青 (コーギーの赤と取り違えないこと = 識別層 2)

            // ---- 触り心地 ----
            // **コーギーの値は流用しない** (体格も尻尾の形も違う)。出発点は下記のとおり。
            //
            // 耳: **購入プレハブに耳 PhysBone が無い** (コーギーだけが持つ)。
            // IMP-DOG-005 からの持ち越し事項で、**2026-08-13 ユーザー決定で「Setup が足す」**。
            //
            // 承知のうえのリスク: ポメの耳ボーンは **2.05cm** しかない
            // (コーギー 9.07cm の 1/4.4。headScale 比で 0.31 対 1.25) ので、
            // ふさふさの耳の見た目に対して**動く骨がほとんど無い**。
            // 揺れが体感として出るかは Play でしか分からない。
            // 出なければ「犬種の個性」へ倒す (addEarPhysBone を false に戻すだけ)。
            addEarPhysBone = true,
            earPbRadius = 0.027f,                            // = 0.03 × (0.0660/0.0723)
            earPbPull = 0.2f,                                // 無次元なので体格換算しない
            // 尻尾: **購入プレハブの素の値がそのまま出発点** (pull 0.6 / spring 0.3)。
            // コーギーとは事情が逆で、素の値が壊れていない —
            // コーギーは素が pull 0.05 / spring 0 (垂れっぱなし) で、0.3 / 0.2 へ
            // **pull を上げる**方向に詰めた。ポメの素はその着地点よりさらに上にあるので、
            // 学習した方向と矛盾しない。加えて尻尾の形が違う
            // (コーギー: gravity 0.3 で垂れる / ポメ: gravity 0 で背に巻く)。
            // **実機で詰める対象。振れが足りなければ pull を下げる**
            tailPbPull = 0.6f,
            tailPbSpring = 0.3f,

            // **素の購入プレハブから Setup を通す初のケース。**
            // コーギーは Unpack 済みからの復旧だったので、この不変条件が本当に
            // 成り立つかはここで初めて実証される。既定の false のままにしてある
            allowUnpacked = false,

            idle = new KD_ClipRef("taiki (pome)", "77e44dc8ea4dd084aa298322244e39e7", 13.75f),
            idleVar1 = new KD_ClipRef("taiki 1 (pome)", "69764e290e1e1fa4197e512bc9ee4262", 15.05f),
            idleVar2 = new KD_ClipRef("taiki 2 (pome)", "4b4fcfe359ce5f94ba70230f4f899015", 15.2f),
            // 待機は 3 本。コーギーの 4 本目 (犬_テンプレ taiki 5) に相当するものが
            // ポメのフォルダに無い。**他犬種から埋めない** (借りる理由が無い)
            idleVar3 = default(KD_ClipRef),
            sit = new KD_ClipRef("osuwari (pome)", "8ba19b0a510eee64cb326ab7f1c164e1", 17.033333f),
            lieDown = new KD_ClipRef("huse (pome)", "2d756dc47f0cf4e4384317d26dd9d87d", 12.0f),
            walk = new KD_ClipRef("aruku (pome)", "858b557e3d755cb44882ae73e35c44fc", 3.0f),
            run = new KD_ClipRef("hasiru1 (pome)", "a8adf6f5d7d053a418f8c9b5fdc8b404", 1.4f),
            lookAt = new KD_ClipRef("taimen (pome)", "f4639b5e476bc8640a4b74d7292e5fc4", 5.0f),
            pet = new KD_ClipRef("なで1 (pome)", "f96ad11b3d2956746b5c550f69cfef64", 10.183333f),

            // IMP-DOG-019 Phase 1。ポメ自身のフォルダから。ルートのずれは Z だけ
            // (huse -0.0420 → 寝 -0.0465 = 後ろへ 0.5cm)。Y は 0.0000。
            //
            // ⚠ `寝なで_ONOFF` は **SHIBA/anim の同名ファイルと md5 が完全一致する**
            // (ラブの hasiru = ookami / ダックスの aruku = samoedo に続く 3 例目の混入)。
            // **ただしこれは無害** — SHIBA(anim) はポメと同じ family B (レスト Y 0.1527) で、
            // ポメのリグ上でルートのずれを実測しても Y 0.0000 / Z -0.0465 に収まる
            // (ボーンパスも huse と 100% 一致)。**混入だから危険なのではなく、
            // 別 family だから危険**だという検出器の原則どおりの扱い
            sleepLoop = new KD_ClipRef("寝なでなで (pome)", "3bdbd35a36eb6f94984259e497af84eb", 25.016666f),
            sleepInOut = new KD_ClipRef("寝なで_ONOFF (pome)", "3b67a645e8c94004c97de58efbefa82b", 0.5833333f),
        };

        /// <summary>
        /// ラブラドール。**IMP-DOG-007 で投入した 3 匹目。**
        ///
        /// 骨格はコーギーと同型 (2 段構え / `アーマチュア/Bone` レストが原点) なので、
        /// ポメで問題になった「レスト位置が違う」問題は起きない。
        /// 違うのは**大きさ** — 体幹 0.6657 (コーギーの 1.63 倍 / ポメの 2.84 倍)。
        ///
        /// **クリップに購入アセット側の混入がある。下記 <see cref="run"/> を必ず読むこと。**
        /// </summary>
        public static readonly KD_DogDef Labrador = new KD_DogDef
        {
            key = "Lab",
            displayName = "ラブラドール",
            prefabGuid = "5736813bd5bbfe04ab60ae0f5cbeb6fd", // ラブラドール_MA.prefab
            seedSalt = 2,                                    // コーギー 0 / ポメ 1 と別のレール

            // 口の幅 (頬幅) 比 0.0892/0.0538 = 1.658 → 0.085 × 1.658 = 0.141。
            // 識別層 1 の「犬種間 1.2 倍」も満たす (0.085 → 0.140 で 1.65 倍)。
            // ボール径の階段: ポメ 0.070 → コーギー 0.085 → ラブ 0.140
            ballDiameter = 0.140f,
            pickupProximity = 1.0f,                            // 大型 (IMP-FET-013)
            barkClipGuid = "2200b5fb728bd2d4391398a08146a0d6", // 大型犬 (体幹 0.333 — 最大)
            // 運動後の息: `運動後_#3` = 中央 1330Hz (最低) / 息の回数 0.33/s (最も長く深い)。
            // **2 つの指標が両方とも「いちばん重い」を指した唯一の 1 本**なので大型犬へ。
            // ⚠ 3 秒のうち鳴っているのは前半だけで後半はほぼ無音 (包絡線の実測)。
            // loop 再生なので「深く吸って、間があって、また吸う」に聞こえる想定だが、
            // **間延びして聞こえないかは Play で要確認**
            breathClipGuid = "977bac520a4cb404daf0996a1efb2c7d",
            ballColor = new Color(0.95f, 0.80f, 0.15f),      // 黄 (赤・青と混ざらない。芝でも畳でも目立つ)

            // ---- 触り心地 ----
            // 耳: **ラブは足す価値が高い。** ポメと違い耳ボーンが長い —
            // ラブ 12.92cm (コーギー 9.07cm より長い / headScale 比 1.28 でコーギーの 1.25 と同等)、
            // 耳に主に付く頂点 132/3485 = 3.79% (コーギー 3.68% とほぼ同じ)。
            // **構造的にコーギーと同じ「垂れ耳」**なので、揺れは素直に出る見込み。
            addEarPhysBone = true,
            earPbRadius = 0.042f,                            // = 0.03 × (0.1010/0.0723)
            earPbPull = 0.2f,                                // 無次元なので体格換算しない

            // 尻尾: **素の購入値がコーギーと完全に同じ** (pull 0.05 / spring 0 / gravity 0.3)。
            // つまりポメとは事情が逆で、**コーギーで「垂れっぱなし」と判定した値そのもの**。
            // 尻尾の形も同型 (gravity 0.3 で垂れる) なので、
            // コーギーで詰めた着地点をそのまま出発点にする。実機で詰める対象
            tailPbPull = 0.3f,
            tailPbSpring = 0.2f,

            allowUnpacked = false,

            idle = new KD_ClipRef("taiki 6 (lab)", "38be610ea9b3e8e4195dd266009e825b", 12.0f),
            // 待機バリエーションはラブのフォルダに無い (コーギーと同じ事情)。
            // **借りるのは「ルートを触らないクリップ」だけ**にした —
            // ookami taiki 1/2 は位置カーブを 1 本も持たず、犬_テンプレ taiki 5 は
            // ルート Y が 0 (= ラブと同じ family)。3 本ともラブのリグへ 100% バインドし、
            // ルート Y のずれ 0.000 を実測済み (2026-08-14)
            idleVar1 = new KD_ClipRef("taiki 1 (ookami)", "8f744ef50316a994eabed38db0d42837", 15.0f),
            idleVar2 = new KD_ClipRef("taiki 2 (ookami)", "c4762410fbe7b0d459e88ce2b9c34e13", 15.0f),
            idleVar3 = new KD_ClipRef("taiki 5 (犬_テンプレ)", "f668ea4736bcc7f489f647e128543985", 12.0f),
            sit = new KD_ClipRef("osuwari (lab)", "342fe931678d28540a84116236f11e31", 17.0f),
            lieDown = new KD_ClipRef("huse (lab)", "894398544439a6a45a3b70e50f4b445e", 12.0f),
            walk = new KD_ClipRef("aruku (lab)", "e0ad0a71cb6f1fe43a6a55eaa9d979c8", 5.0f),

            // **ラブの走りはラブのフォルダから採れない。**
            //
            // `ラブラドール_anim/anim 1/hasiru1.anim` と `hasiru2.anim` は
            // **`ookami/anim/` の同名ファイルと md5 が完全一致する** (2026-08-14 実測)。
            // 購入アセット側でクリップが混入している。実際、ラブのリグへ当てると
            // ルート Y が **0.277** 浮く — 2026-08-11 に「犬が浮く」で除外した
            // ookami taiki と同じ値。**フォルダ名は当てにならない。**
            //
            // 代替を全 11 本の hasiru から実測して選んだ (ラブのリグ上でルート Y を測定):
            //   samoedo 0.270 / SHIBA(anim 1) 0.153 / ookami・ラブ 0.277 → いずれも浮く
            //   **犬_テンプレ 0.000 / ko-gi- 0.000** → 使える
            // 犬_テンプレ を採ったのは、コーギーの短い脚の歩様より
            // **中庸なテンプレートの方が大型犬に載りやすい**という判断
            // (犬_テンプレ taiki 5 はコーギーで実績あり)。
            // **見た目は Play で要確認。** 合わなければ ko-gi- hasiru1 か、
            // aruku を Run にも当てて速度で見せる案がある
            run = new KD_ClipRef("hasiru1 (犬_テンプレ)", "3ce58a3f9e2f290498064b4368f4a24c", 0.6666667f),

            lookAt = new KD_ClipRef("taimen 2 (lab)", "5a0bf753b85784746acd48393f3bca8b", 8.933333f),
            // ラブ自身の撫でクリップ。ルート位置カーブを持たない 3 本のうち最も長い
            // (なで5 15.0 / なで_4 8.0 / nade5 16.98)。**選定は Play の目視で**
            // (コーギーの なで1 は候補 10 本から目視で選んだ — 設計書 §5)
            pet = new KD_ClipRef("nade5 (lab)", "8c1753de68e07cc4285ae7a3f89f43cd", 16.983334f),

            // IMP-DOG-019 Phase 1。**ラブ自身のフォルダのものが使える** (hasiru と違い
            // ookami の混入ではない — md5 はパック内で一意)。ルートのずれは Z だけで、
            // **4 犬種で最大** (huse -0.1610 → 寝 -0.2760 = 後ろへ 11.5cm)。
            // 体格 1.63 倍ぶんそのまま大きい。Y は 0.0000
            sleepLoop = new KD_ClipRef("寝なでなで (lab)", "9e794cea31fa2554f9715b5b280dd6b1", 25.016666f),
            sleepInOut = new KD_ClipRef("寝なで_ONOFF (lab)", "abf713bf0b38aa341b861e07e9653637", 0.5833333f),
        };

        /// <summary>
        /// ダックスフンド。**IMP-DOG-010 で投入した 4 匹目。**
        ///
        /// 実測 (2026-08-14。詳細は設計書 §0):
        /// - **1 段構え** (RotationConstraint 0)。ポメと同じで、追従の書き先はボーン直
        /// - **耳 PhysBone を最初から持つ** — コーギー以外では初。しかも素の radius が
        ///   0.07 と大きい (コーギーの素は 0.01 = 手が当たらない値だった)。
        ///   **「素の値が壊れている」は犬種の性質ではなくコーギー個体の事情**だった
        /// - **`アーマチュア/Bone` のレストが (0, 0.1660, 0.2161)** — ポメ (0.1527) にも
        ///   コーギー/ラブ (0) にも一致しない**第 4 の family**。
        ///   これがルート位置検出器の穴を開けた (下記 <see cref="walk"/> と設計書 §0)
        /// - **体格はコーギーとほぼ同じ** (体幹 0.4138 / 肩高 0.1586 に対し
        ///   コーギー 0.4088 / 0.1630)。**胴長短足なので「大きさ」で見分けられない** —
        ///   ボール径の決め方が 3 匹目までと変わる (下記 <see cref="ballDiameter"/>)
        ///
        /// クリップは**ダックス自身のフォルダ** (`gimmick/dakkusu/anim 2`) から採った。
        /// **ただし 1 本だけ混入がある** — <see cref="walk"/> を読むこと。
        /// </summary>
        public static readonly KD_DogDef Dachshund = new KD_DogDef
        {
            key = "Dachs",
            displayName = "ダックスフンド",
            prefabGuid = "dd94fbd924e81d944a726405a15238d7", // Dachshund_MA.prefab
            seedSalt = 3,                                    // 0/1/2 は使用済み

            // **識別層 1 (大きさ) がほとんど効かない初めての犬。**
            // 体幹 0.4138 (コーギー比 1.012) / 肩高 0.1586 (同 0.973) —
            // ダックスとコーギーは**体格がほぼ同じ**で、胴の長さだけが違う。
            //
            // 3 匹目までは「口の幅 (頬幅) 比へ寄せる」で決めていたが、それだと
            // 0.058/0.0538 × 0.085 = **0.092** で、コーギーの 0.085 に対し 1.08 倍。
            // Fetch Verify が要求する**犬種間 1.2 倍**を満たせない。
            //
            // 一方この犬は**頭が長い** (headScale 0.0922 = コーギー比 1.28。
            // 頬幅比 1.08 との差がそのまま「細長い顔」)。**頭の長さ比**で採ると
            // 0.085 × 1.28 = **0.108** で、
            //   コーギー 0.085 → 0.108 (1.271 倍) / 0.108 → ラブ 0.140 (1.296 倍)
            // と両隣に均等な余裕が残る。**マズルは長いので口には入る。**
            //
            // つまり**この犬だけ頬幅ではなく頭長で決めた**。頬幅基準からは 18% 大きく、
            // 見た目に大きすぎないかは **Play で要確認**
            // (窮屈なら 0.102 まで下げられる = 1.2 倍の下限)
            ballDiameter = 0.108f,
            pickupProximity = 0.1f,                            // 小型 (IMP-FET-013)
            barkClipGuid = "e000cdf21890ff346a5b03d13bab010d", // 小型犬 (ミニチュアダックス。**体幹はコーギー並みだが犬種で決める**)
            // 運動後の息: **ポメと同じ 1 本** (`はぁはぁ_variation1`)。音源が 3 本しか無いので
            // 1 本は重なる。重ねる相手をポメにしたのは、`barkClipGuid` が両方とも
            // 小型犬に分類しているから — 体格クラスが同じ 2 匹なら、重なっても
            // 「大きさが音で分かる」(識別層) を削らない。
            // **この犬だけ吠えはポメと別クリップ**なので、息が同じでも声で見分けは付く。
            // 4 本目が手に入ったらここを差し替える (共有はこの 1 行で解消する)
            breathClipGuid = "ca92bc23a9df4cf4ba60baa683086294",
            // 紫。赤 (コーギー) / 青 (ポメ) / 黄 (ラブ) のどれとも混ざらない。
            // **緑は避けた** — 芝の上で沈む。
            // **体格で見分けられないぶん、この犬では色が識別の主役**になる
            ballColor = new Color(0.60f, 0.30f, 0.85f),

            // ---- 触り心地 ----
            // 耳: **購入プレハブが最初から耳 PhysBone を持っている** (コーギー以外で初)。
            // 耳ボーン 12.13cm (headScale 比 1.32 = コーギー 1.25 / ラブ 1.28 と同等) の
            // 立派な垂れ耳で、構造的にはコーギー・ラブと同じ。
            addEarPhysBone = false,                          // 既にあるので足す必要が無い
            // 素の radius は **0.07**。コーギーで詰めた 0.03 を頭寸法比で換算すると
            // 0.03 × (0.0922/0.0723) = 0.038 なので、素は換算値の 1.8 倍にあたる。
            // **0.040 を採った** — コーギーで「手が当たる」と確認できた比率をそろえる方を
            // 優先した。素の 0.07 は耳ボーン長 0.121 の半分以上あり、頭を包む太さになる
            // (手が耳から離れていても押せてしまう)。**触れて揺れるかは Play で確認し、
            // 当たらなければ素の 0.07 へ戻す**
            earPbRadius = 0.040f,
            earPbPull = 0.2f,                                // 無次元なので体格換算しない (素も 0.2)
            // 尻尾: **素が pull 0.2 / spring 0.2 / gravity 0.5 / radius 0.1** で、
            // コーギー・ラブが共有する素のテンプレ (0.05 / 0 / 0.3 / 0.03) とは別物 =
            // **この犬のために作られた値**。ポメと同じ事情なので**素をそのまま出発点にする**。
            // コーギーの着地点 (0.3 / 0.2) とも近い。実機で詰める対象
            tailPbPull = 0.2f,
            tailPbSpring = 0.2f,

            allowUnpacked = false,

            // 待機は 3 本 (ポメと同じ。`taiki 6` に相当するものが無い)。
            // `idle.anim` は長さ 0・パス 1 本の切り替え用でモーションではない (設計書 §5)
            idle = new KD_ClipRef("taiki (dakkusu)", "2fae1eee984f1444b80b0ccae4213e46", 7.0f),
            idleVar1 = new KD_ClipRef("taiki 1 (dakkusu)", "41f333647fdce104e9b60403f82ca383", 12.5f),
            idleVar2 = new KD_ClipRef("taiki 2 (dakkusu)", "14fdc2d40b6bf1248b15eff64ddbdb6f", 8.95f),
            idleVar3 = default(KD_ClipRef),
            sit = new KD_ClipRef("osuwari (dakkusu)", "6a4786e4120b83645a4d64306b1c2d38", 17.183333f),
            lieDown = new KD_ClipRef("huse (dakkusu)", "258851a2ef70da34cae703471b43c104", 12.0f),

            // **ダックスの歩きはダックスのフォルダの `aruku` から採れない。**
            //
            // `dakkusu/anim 2/aruku.anim` は **`samoedo/anim 2/aruku.anim` と md5 が
            // 完全一致する** (2026-08-14 実測)。ラブの `hasiru1/2` = ookami に続く
            // **2 例目の混入**。当てるとルートが 0.103 上がって犬が浮く。
            //
            // 同フォルダの **`aruku 1` (0.75s) は無事** (ルート変位 z 0.002 のみ) なので
            // そちらを採った。コーギー/ラブの `aruku` (5.0s) より 1 周期が短く、
            // **歩きが速く見えないかは Play で要確認**。
            // 代替は `aruku` (犬_テンプレ / ko-gi- / ラブ。いずれも 5.0s) —
            // **これらはルート位置カーブを 1 本も持たない**ので family を問わず当てられる
            // (2026-08-14 に全 aruku を実測。Logs/imp-dog-010-survey.txt §C)
            walk = new KD_ClipRef("aruku 1 (dakkusu)", "c0c8b1bedc48a9c4ca465bcb2666344d", 0.75f),
            run = new KD_ClipRef("hasiru1 (dakkusu)", "1aba286a2ba715a49ba8fd9f53b5cd79", 0.4f),

            // **ダックス自身の `taimen` は使えない。** 2.0s / **ループしない** / パス 12 本で、
            // Verify の「LookAt のクリップはループする」を満たさない
            // (LookAt は Bool が立っている間ずっと再生される State なので、
            //  ループしないと最終フレームで固まる)。
            //
            // パック内の taimen 全 12 本をダックスのリグで実測し (Logs/imp-dog-010-survey.txt §C)、
            // **「ループする」かつ「ルート位置が安全」を両方満たすのは 2 本だけ**だった:
            //   **ポメ taimen 5.0s (沈み 0.013)** ← 採用 / tiwawa taimen 7.5s (沈み 0.073)
            // 他はすべて family A (ルート Y 0) 由来でダックスを 0.22〜0.30 沈める。
            // ポメを採ったのは**ずれが小さい方**だから (ラブで 犬_テンプレ hasiru1 を
            // 選んだのと同じ基準)。ポメとダックスは「レストが浮いている」同系統。
            // **見た目は Play で要確認** — 合わなければ tiwawa (7.5s。コーギーの 8.93s に近い)
            lookAt = new KD_ClipRef("taimen (pome)", "f4639b5e476bc8640a4b74d7292e5fc4", 5.0f),

            // ダックス自身の撫でクリップ。ルート位置カーブを持たない 4 本
            // (なで1 10.25 / なで2 15.0 / なで3 8.03 / nade4 17.02) のうち最も長いものを採った
            // (ラブと同じ機械選定)。**選定は Play の目視で**
            pet = new KD_ClipRef("nade4 (dakkusu)", "acb5e679271cfc94191ef6e6688ec8ee", 17.016666f),

            // IMP-DOG-019 Phase 1。ダックス自身のフォルダから (aruku と違い混入なし)。
            // **本編だけ 19.52s と短い** (他 3 犬種は 25.02s) が、ループするので問題にならない。
            // ルートのずれは Z だけ (huse -0.0491 → 寝 -0.0751 = 後ろへ 2.6cm)。Y は 0.0000
            sleepLoop = new KD_ClipRef("寝なでなで (dakkusu)", "5c324e23c14d005428a1051044003e56", 19.516666f),
            sleepInOut = new KD_ClipRef("寝なで_ONOFF (dakkusu)", "b93db3ccbd5a88e4f9dc883cdbf24682", 0.5833333f),
        };

        /// <summary>
        /// 柴犬 (5 匹目。IMP-DOG-020 / 2026-09-05 実測 `Logs/imp-dog-020-survey.txt`)。
        ///
        /// - **2 段構え** (mesh=shiba / RotationConstraint 28) = コーギーと同じ構造
        /// - **`アーマチュア/Bone` のレストは原点 = family A** (コーギー・ラブと同じ)。
        ///   設計書 §0 が「SHIBA (`anim 1`) = family B」と書いていたのは**旧 SHIBA フォルダのクリップ**の話で、
        ///   remake のプレハブ自体は A。したがって借りられるのは A のクリップ (`shiba_anim/anim 1` = remake 自前) で、
        ///   `SHIBA/anim` (= pome と md5 一致 = B) / `SHIBA/anim 1` (= tiwawa と md5 一致) は
        ///   ルート位置カーブを持つものはすべて 0.137〜0.216 浮く。**位置カーブ 0 本の taiki だけ**借りている
        /// - headScale 0.1046 (コーギー比 1.45) / 体幹 0.4642 (1.14) / 肩高 0.3132 / 頬幅 0.0631 (1.17)
        /// - **PhysBone 0 本** → 尻尾は Setup が足す (<see cref="KD_DogDef.addTailPhysBone"/>)。
        ///   耳も Setup が足す (IMP-DOG-021。耳ボーン 0.0723 = headScale 比 0.69)
        /// - **伏せ `huse` の沈みが `hojo_born/Bone` に打たれている** → 派生クリップで直す
        ///   (BUG-DOG-002。<see cref="KD_DogDef.lieDownRootCurveSourcePath"/>)
        /// </summary>
        public static readonly KD_DogDef Shiba = new KD_DogDef
        {
            key = "Shiba",
            displayName = "柴犬",
            prefabGuid = "03f419105d1319b42bd3a1eb8b1b301f", // 柴犬_remake/shibaRemake_MA.prefab
            seedSalt = 4,                                    // 0〜3 は使用済み
            // **コーギーと同径** (IMP-DOG-020 §5 決定 1 / 2026-09-05 ユーザー決定)。
            // 頬幅比なら 0.0997 だが梯子 (0.070 / 0.085 / 0.108 / 0.140) に空きが無い。
            // 識別は色 (白) と拾う演技 (IMP-FET-005) に寄せる。Fetch Verify の 1.2 倍検査は
            // この組だけ同径を許す (KD_DogDefs.BallDiameterSharePermitted)
            ballDiameter = 0.085f,
            pickupProximity = 0.3f,                            // 中型 (IMP-FET-013)
            // 中型犬 = **コーギーと共有** (IMP-DOG-020 §5。音源が 4 本しか無い。調達したら差し替える。
            // Wander Verify の一意検査は KD_DogDefs.BarkSharePermitted でこの組だけ許す)
            barkClipGuid = "6a60e2ccece00a64f927ec382deb5bac",
            breathClipGuid = "9400d40e8f391cf4eb5801c34a0cd5d0", // 運動後_variation4 (中) — コーギーと共有
            ballColor = new Color(0.95f, 0.95f, 0.90f),      // 白 (空いていた色。柴の赤茶に対して映える)

            // ---- 触り心地 ----
            // 耳: 立ち耳。耳ボーン 0.0723m (headScale 比 0.69)。ポメ (0.31) は足していないが
            // ラブ (1.28) は足した。IMP-DOG-020 では「立ち耳を揺らすと犬種の印象が崩れる」として
            // 足さなかった (AI 判断) が、**IMP-DOG-021 (2026-09-05 ユーザー決定) で「他の犬と同じように」足す**。
            // 印象が崩れるなら pull / radius で押さえる (戻す判断もユーザー)
            addEarPhysBone = true,
            earPbRadius = 0.043f,                            // = 0.03 × (0.1046/0.0723)
            earPbPull = 0.2f,
            // 尻尾: **素は PB 0 本**。Primary (コーギー) の尻尾 PB を複製して足し、pull / spring はコーギーの着地点
            addTailPhysBone = true,
            tailPbPull = 0.3f,
            tailPbSpring = 0.2f,
            allowUnpacked = false,

            // ---- クリップ (全部 remake 自前 `shiba_anim/anim 1` = family A。待機の変化だけ位置カーブ 0 本の SHIBA/anim) ----
            idle = new KD_ClipRef("taiki 6 (shiba_anim)", "1867f7eb5eae9a642b1bdc085a7ffd35", 12.0f),
            idleVar1 = new KD_ClipRef("taiki (SHIBA/anim)", "8601ab456405ff64d8568398e57698b4", 13.75f),
            idleVar2 = new KD_ClipRef("taiki 1 (SHIBA/anim)", "6b1b20a0a875aae4d8e1886fab13ae1e", 15.05f),
            idleVar3 = new KD_ClipRef("taiki 2 (SHIBA/anim)", "e5e853d4287250f4aa0a442dab82690f", 15.2f),
            sit = new KD_ClipRef("osuwari (shiba_anim)", "eef8ba6f262de2d4a904a83c23b852f0", 17.0f),
            // BUG-DOG-002: 原本は沈み (z -0.127) を hojo_born/Bone に打っていて アーマチュア/Bone に位置カーブが無い
            // (6 匹 × 全採用クリップでこの 1 本だけ)。Controller には派生クリップ KD_LieDown_Shiba を当てる。
            // 寝クリップ (寝なでなで / 寝なで_ONOFF) は アーマチュア/Bone に -0.196 が正しく打たれている (同罠なし)
            lieDown = new KD_ClipRef("huse (shiba_anim)", "aaaf58ec27f4d0a40aa5c784deb4cf41", 12.016666f),
            lieDownRootCurveSourcePath = "hojo_born/Bone",
            walk = new KD_ClipRef("aruku (shiba_anim)", "3e1367a710ae72a45a7b8c9b8636dcc7", 5.0f),
            run = new KD_ClipRef("hasiru1 (shiba_anim)", "5692b1ee19f6f904f92308ea18ded38c", 0.5f),
            // taimen 2: ループする かつ 沈み 0.056 (許容 0.12) — 自前で足りた (ダックスのように借りなくてよい)
            lookAt = new KD_ClipRef("taimen 2 (shiba_anim)", "80fd3c844e53eb84c9a1c7c425b35529", 8.933333f),
            // 撫で: なで4 / なで5 (各 8.0s。位置カーブ 0) のうち暫定で なで4。目視で決める (Play)
            pet = new KD_ClipRef("なで4 (shiba_anim)", "38dec9911a7dab54ea4f9fbd597700f3", 8.0f),
            sleepLoop = new KD_ClipRef("寝なでなで (shiba_anim)", "71a8c81b4915f434182216e58332d84d", 25.016666f),
            sleepInOut = new KD_ClipRef("寝なで_ONOFF (shiba_anim)", "cfd6f10f507053449ae5cfd9f9e3402f", 0.5833333f),
        };

        /// <summary>
        /// サモエド (6 匹目。IMP-DOG-020 / 2026-09-05 実測)。
        ///
        /// - **1 段構え** (RotationConstraint 0) = ポメ・ダックスと同じ構造
        /// - `アーマチュア/Bone` レスト (0, 0.2695, 0.3857) = **family C** (ookami と同じ)。
        ///   自前 `samoedo/anim 2` で全役が埋まった。借りるなら family C が第一候補だが、**可否は family ではなく実測**で決める
        ///   (ポメ `taimen` = B は沈み 0.117 で許容 0.157 の内側 — 自己テストに ok として入れてある)
        /// - プレハブは `samoedo_MA1` / `MA2` / `samoedo/samoedo_v1 Variant` の 3 つ。
        ///   **MA1 と MA2 は材質まで同一で、差は MA1 に RotationConstraint が 1 個残っていることだけ**
        ///   (資料 §0 の「材質 1 本の差」は誤り)。Variant は `inu` が無く解決できない。→ **MA2**
        /// - headScale 0.0927 (1.28) / 体幹 0.6409 (1.57。ラブ 1.63 より少し小さい) / 肩高 0.3703 / 頬幅 0.0739 (1.37)
        /// - BlendShape 16 (4 犬種は 14。metozi 6 / egao 13 の index は同じ)
        /// - **PhysBone 0 本** → 尻尾も耳も Setup が足す (耳は IMP-DOG-021)
        /// </summary>
        public static readonly KD_DogDef Samoyed = new KD_DogDef
        {
            key = "Samoyed",
            displayName = "サモエド",
            prefabGuid = "743ffbf56e83bb44a9c3d18337f9262c", // サモエド/samoedo_MA2.prefab
            seedSalt = 5,
            // **≥ 0.168 = ラブ 0.140 × 1.2** (IMP-DOG-020 §5 決定 1)。頬幅比なら 0.117 で梯子に入らないので、
            // 6 匹目は上に段を足して層 1 を保つ。口に対して大きめ (頬幅比 2.27。ラブ 1.57) —
            // **くわえて不自然でないかは Play** (直せるのは色と演技で分ける方向)
            ballDiameter = 0.168f,
            pickupProximity = 1.0f,                            // 大型 (IMP-FET-013)
            // 大型犬 = **ラブと共有** (IMP-DOG-020 §5。KD_DogDefs.BarkSharePermitted)
            barkClipGuid = "2200b5fb728bd2d4391398a08146a0d6",
            breathClipGuid = "977bac520a4cb404daf0996a1efb2c7d", // 運動後_#3 (大) — ラブと共有
            ballColor = new Color(0.95f, 0.55f, 0.15f),      // 橙 (白い犬に白いボールは同化する)

            // ---- 触り心地 ----
            // 耳: 立ち耳。耳ボーン 0.0777m (headScale 比 0.84)。柴と同じく IMP-DOG-021 (ユーザー決定) で足す
            addEarPhysBone = true,
            earPbRadius = 0.038f,                            // = 0.03 × (0.0927/0.0723)
            earPbPull = 0.2f,
            addTailPhysBone = true,
            tailPbPull = 0.3f,
            tailPbSpring = 0.2f,
            allowUnpacked = false,

            // ---- クリップ (全部自前 `samoedo/anim 2`) ----
            idle = new KD_ClipRef("taiki (samoedo)", "fe10d22c21213b14d8d40d9b888d6031", 7.0f),
            idleVar1 = new KD_ClipRef("taiki 1 (samoedo)", "4da7269f16c52c44e81f63b662a07d0c", 12.5f),
            idleVar2 = new KD_ClipRef("taiki 2 (samoedo)", "62e27b1a52227864c8049a417cd24daf", 8.916666f),
            idleVar3 = default(KD_ClipRef),
            sit = new KD_ClipRef("osuwari (samoedo)", "5bbfb1b892c55884c85e40c1a72cfa4a", 17.033333f),
            lieDown = new KD_ClipRef("huse (samoedo)", "294e4e263bb731543a994d62726e6651", 12.0f),
            // aruku: ダックスの `aruku` と md5 一致の既知の混入 (IMP-DOG-010) — **原本はこちら** (family C)。
            // サモエドのリグでは面内 0.024 で安全。`aruku 1` (位置カーブ無し) は予備
            walk = new KD_ClipRef("aruku (samoedo)", "74afe7973b9e0cb4bb33477a5d485c85", 2.5f),
            run = new KD_ClipRef("hasiru1 (samoedo)", "962f8c2d6e8ed4c4292877d0f5dc6b7c", 1.75f),
            lookAt = new KD_ClipRef("taimen (samoedo)", "c6c64fca745a50d4ca733e7c75a6fba8", 9.633333f),
            pet = new KD_ClipRef("nade4 (samoedo)", "8cc1c9518c130c142bb7b3d19d9f66f1", 17.016666f),
            sleepLoop = new KD_ClipRef("寝なでなで (samoedo)", "90a641a279df1d4499f1cf05fd336630", 19.45f),
            sleepInOut = new KD_ClipRef("寝なで_ONOFF (samoedo)", "b212a630628f2aa4d9220ba21cc18f52", 0.5833333f),
        };

        /// <summary>ワールドに置く犬。**次の 1 匹はここへ 1 行足す。** (6 匹 = IMP-DOG-020。2026-09-05)</summary>
        public static KD_DogDef[] All
        {
            get { return new[] { Corgi, Pome, Labrador, Dachshund, Shiba, Samoyed }; }
        }

        // ================================================================
        // IMP-DOG-020 の例外 (2026-09-05 ユーザー決定)。**検査が名指しで許す組はここが正本**
        // ================================================================

        /// <summary>識別層 1 (ボール径 1.2 倍の梯子) の例外: **柴 ↔ コーギーの同径だけ**。
        /// 5 匹目で梯子に空きが無くなった。色 (層 2) と拾う演技 (層 4) で分ける。</summary>
        public static bool BallDiameterSharePermitted(string keyA, string keyB)
        {
            return SamePair(keyA, keyB, "Corgi", "Shiba");
        }

        /// <summary>識別層 5 (吠え声の全犬一意) の例外: **柴 ↔ コーギー (中型) / サモエド ↔ ラブ (大型)**。
        /// 吠えの音源が 4 本しか無い。**音源を調達したらここの組を消す** = 検査が元の厳しさに戻る。</summary>
        public static bool BarkSharePermitted(string keyA, string keyB)
        {
            return SamePair(keyA, keyB, "Corgi", "Shiba") || SamePair(keyA, keyB, "Lab", "Samoyed");
        }

        static bool SamePair(string a, string b, string x, string y)
        {
            return (a == x && b == y) || (a == y && b == x);
        }

        /// <summary>毛の距離フェードの正本 = コーギーの `ke_Uzumore.mat` (IMP-DOG-018 / runbook §1.1)。
        /// **`.gitignore` の内側**なので環境を作り直したら runbook の表から入れ直す。</summary>
        public const string FurReferenceMaterialGuid = "3a95ef7d2895a454590aa47c6c9ba7dc";
        /// <summary>同上のパス。手で作り直すと GUID が変わるので、GUID で引けないときの予備 (KD_FurMaterialEditor)</summary>
        public const string FurReferenceMaterialPath = "Assets/TORINOSUKE/犬ギミック/コーギー/material/ke_Uzumore.mat";
        /// <summary>購入パックの毛の素のシェーダ (lilToon Fur TwoPass)。これを見つけたら Uzumore へ差し替える</summary>
        public const string StockFurShaderGuid = "54bc8b41278802d4a81b27fe402994e2";

        /// <summary>
        /// **「全犬でそろっていなければならない値」の正本にする犬。**
        ///
        /// IMP-CAM-002 (2026-08-16) で「撮影が当面の相手にする犬」という役目は消えた —
        /// 撮影は 4 匹とも撮るようになり、Setup も Verify も Primary を特別扱いしなくなった。
        /// **それでも消さない**のは、別の役目が残っているため:
        /// - じゃれ合いの予定表 (KD_DogPackSetupEditor。全犬一致が成立条件なので正本が要る)
        /// - 撫でハートの ParticleSystem の複製元 (KD_PetHeartsSetupEditor)
        ///
        /// つまり今の意味は「単一システムが相手にする犬」ではなく
        /// **「値をここから複製する犬」**。新しい用途を足すときはどちらの意味かを確かめること。
        /// </summary>
        public static KD_DogDef Primary { get { return Corgi; } }

        public static bool TryGet(string key, out KD_DogDef def)
        {
            foreach (var d in All)
            {
                if (d.key != key) continue;
                def = d;
                return true;
            }
            def = default(KD_DogDef);
            return false;
        }

        // ---- 移行 (IMP-DOG-005 Phase 2)。犬種サフィックスが付く前の名前 ----

        /// <summary>旧名 → 新名。コーギーだけが対象 (1 匹版の遺産)。</summary>
        public static string[,] LegacySceneNames
        {
            get
            {
                var c = Corgi;
                return new[,]
                {
                    { "KD_Dog", c.DogName },
                    { "KD_FetchSystem", c.FetchSystemName },
                    { "KD_PetSystem", c.PetSystemName },
                    { "KD_WanderSystem", c.WanderSystemName },
                    { "KD_Ball", c.BallName },
                    // 旧名の改名先も**消す対象のまま**にしておく (IMP-FET-011)。
                    // ここを落とすと、1 匹版の遺産 `KD_BallResetButton` が
                    // 改名されずに残り、`4 - Remove` の検索名 (犬キー付き) に掛からない
                    { "KD_BallResetButton", c.LegacyResetButtonName },
                };
            }
        }

        public static string[,] LegacyAssetPaths
        {
            get
            {
                var c = Corgi;
                return new[,]
                {
                    { "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog.controller", c.ControllerPath },
                    { "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_PickupPose.anim", c.PickupPosePath },
                    { "Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/KD_Ball.prefab", c.BallPrefabPath },
                    { "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Ball.mat", c.BallMaterialPath },
                };
            }
        }
    }
}
