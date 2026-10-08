# Play チェックリスト 2026-10-02 (まとめ版)

review (確認待ち) と wip (ユーザー確認が残る) の Issue の Play / PCVR / 2 人 / 端末の確認を、セッション単位に 1 本へまとめたもの。Issue ごとではなく、コースを歩く順に並べた。結果は **○ / × / 気づいた点** を結果欄へ書く。終わったら AI が各 Issue へ転記する (Issue 側の項目番号は「元」の列の括弧)。

- 作成時点のコミット: `f9ed660 (IMP-DOG-025 すすぎ改善)`。2026-10-03 に、その後の追加分 (`ea4baf4`・`2c9c794`・`8f7ed6e`) を A-0 と B54〜B57・C13〜C15・D20〜D22・E18〜E20 へ足した (既存の項目は変えていない)。さらに同日、未コンパイル・未検証の実装 (門の古民家ドッグランの板とポータル・全地点ワープ・洗い場ボードの可否表示と呼べる範囲・フリスビーの取り方の連打・おじいさんの案内文) の確認を A0-14〜A0-20・B58〜B61・C16〜C18・D23〜D25・E21〜E23 へ足した。これらは Unity でコンパイル・Build・ClientSim を通したあとに行う (`8faf572` で通った。次)。**2026-10-03 の `8faf572`** で本番シーンへ入った 11 件 (ワープ全地点・洗い場の呼べる表示と範囲・門の案内ボードとポータル・さんぽタブ 2 列・おやつで接続犬を呼ぶ・フリスビーの曲げと取り方・ボールの柄・自動写真・場所を決めて呼ぶ (手の高さ)・看板の文字サイズ・おじいさんの文言) は、Unity でコンパイル・Build・ClientSim (1 人) を通した。その確認に A0-21〜A0-27・B62〜B66・C19〜C21・D26〜D27・E24〜E25 を足し、A89 に撤回を注記した。Play・2 人・端末は未実施
- 公開目標 (開発完了 2026-10-03〜04、公開 2026-10-10〜11) は [開発全体像](../development/overview.md)。確認済みの項目 (日付つき) は載せていない。
- 合否は AI が決めない。1 人の総合評価を、項目別・端末別の合格に読み替えない。疑似入力 (ClientSim) の結果は、ここの合格にならない。
- 順序は A (PCVR 1 人) → B (PCVR 2 人以上) → C (Desktop) → D (Quest 3) → E (iOS)。A の 1 周を終えてから B 以降へ進むと、同じ場所を何度も往復しない。

## 始める前に

- シーン保存済み・Console エラーなしで **Build & Test をし直す** (古い動きが出たら最初に疑う)。
- ユーザー作業 (Play ではない): クレジット頁の URL 3 件を記入 ([IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md))。洗い場の棚の札 `KH_WashStation/SignText` と、リセット札の文字 (今は仮の英語) を日本語にする ([IMP-ENV-007](../development/issues/IMP-ENV-007-main-wash-station.md))。
- 犬の初期の汚れは 0 (本番値)。汚れを流す確認は、散歩のあと (A-5) に洗い場へ戻って行う。
- 項目の元になった詳細チェック表: [川の設計書](../plans/2026-09-27-river-play-and-floating-fetch.md) (川 #1〜#36)、[フリスビーどこでも](../plans/2026-09-28-frisbee-anywhere.md) (川設計書 続き #37〜#48)、[地図の設計書](../plans/2026-09-27-map-and-travel.md) (地図設計書 #1〜#9、呼ぶ F.7 #10〜#15)、[メニュー計画](../plans/2026-09-23-menu-rebuild.md)、[散歩計画](../plans/2026-09-20-free-walk.md)、[集合計画](../plans/2026-09-20-menu-gather-stations.md)、[洗い場の呼び出しと高さ](../plans/2026-10-01-wash-call-and-height.md)、[6 匹フリスビー](../plans/2026-09-24-frisbee-six-dogs.md)。

## A. PCVR 1 人 (193 項目)

> **2026-10-02 ユーザー確認 (総合判断)**: 「PCVR 1 人 138 — これはもう基本全部見たからOKだよ」。A-2〜A-10 は、この総合判断により確認済みとして扱う (項目別の結果の記録はない。下の結果欄は埋めない)。**A-1 (すすぎ改善、`f9ed660`) は今日の実装のため、この発言に含まれない (ユーザー回答 2026-10-02、未 Play)。未確認のまま残す。** この判断を 2 人・Desktop・Quest 3・iOS (B〜E) の合格に読み替えない。

1 人で入り、スポーン → 主洗い場 → 門 → 散歩 → 展望・広場 → 奥広場・川 → 地図 → 設定の順に 1 周する。最初に Build & Test をし直す。犬の初期の汚れは 0 なので、泡の確認は汚れのない犬で行い、汚れを流す確認は A-4 の散歩のあとに戻って行う。

### A-0 2026-10-02〜03 の追加分 (看板・洗い場の出入り・首輪・川の音・オートナイトモード)

> A-1 以降の総合判断 (「基本全部見たから OK」) の**後**に実装した分なので、この発言に含まれない。未確認。ビルドは `8f7ed6e` 以降の版で入る。

| # | やること・見ること | 元 | 結果 |
| A0-14 | 門の西側の塀沿いの板「古民家ドッグラン」(スポーンの方を向く) に気づき、文言が読めるか。板や柱が古民家の外壁にめり込んでいないか、門の通行・おじいさん・洗い場・リード台の邪魔にならないか。入室した直後、ポータルが見えていないか。 | [IMP-WLD-006](../development/issues/IMP-WLD-006-farmhouse-portal-board.md) | |
| A0-15 | 板を Interact (VR はトリガー) すると、板のとなりにポータルが現れるか。もう一度押すとしまい、押さなくても約 1 分で自動でしまうか。現れたポータルに入ると古民家ドッグランへ移動できるか (名前・サムネイルが付くか)。 | [IMP-WLD-006](../development/issues/IMP-WLD-006-farmhouse-portal-board.md) (実機でしか分からない) | |
| A0-16 | 手元メニュー [地図と移動] で 1 古民家側の入口・2 広場・3 奥の広場・4 上流の橋・5 川辺の 5 か所へワープできるか (犬 1 匹と 6 匹をつないだ状態で)。着地点が洗い場・看板・ポータルに近すぎず、6 匹が並べて着くか。6 下流の橋は選べない (○ の目印のまま) ことの確認。一覧 (2 列 × 3 段) と地図の番号が合っているか。川辺の着地点が以前と少し変わっても不自然でないか。 | [IMP-WLD-004](../development/issues/IMP-WLD-004-warp-all-five-points.md) | |
| A0-17 | 洗い場のボードで、犬ごとに「呼べます」または呼べない理由が、色だけでなく文字で読めるか。呼べない犬 (ほかの人と散歩中・遊んでいる等) のボタンを押すと、その場で理由が出て犬が動かないか。ボードの文字の大きさ・高さ (6 cm 高くなった) は押しやすいか。 | [IMP-DOG-037](../development/issues/IMP-DOG-037-wash-call-availability-and-range.md) | |
| A0-18 | 洗い場から呼べる範囲 (主 25 m・川辺 28 m・奥広場 25 m・展望のベンチ 25 m) が自然か。4 か所で範囲の内・境目・外の犬を呼び、外の犬には「遠すぎます」が出て犬が来ないか。川辺の洗い場から、古民家側の犬を呼べないか。 | [IMP-DOG-037](../development/issues/IMP-DOG-037-wash-call-availability-and-range.md) | |
| A0-19 | トレーナーで、手元メニューの [あそぶ] の「フリスビーの取り方」を、返事が届く前に (1.5 秒以内に) 続けて 2 回押す。通常 → 並走 → ひねり と 2 つ進むか (同じ番号を 2 回頼み直さないか)。トレーナーでない状態では変えられないか。1 人では修正前でも進むので、本命は B60。 | [BUG-DOG-008](../development/issues/BUG-DOG-008-frisbee-style-double-press.md)<br>[BUG-DOG-009](../development/issues/BUG-DOG-009-frisbee-style-receiver-check.md) | |
| A0-20 | おじいさんに話しかけ [さんぽ] の案内文を読む。行き先が 6 つで、手元メニューの呼び名と合っているか。枠に収まるか (英語・ドイツ語など長い言語も)。 | [BUG-UI-001](../development/issues/BUG-UI-001-shopkeeper-walk-info-destinations.md) | |
| A0-21 | 手元メニューの [さんぽ] が 2 列でスクロール無しか。左の [左につなぐ] [右につなぐ] (128×52) をレーザーで狙えるか。つないだ犬の行が [はずす] だけに変わり、行の地が点灯するか。**左右を素早く 2 度押したときに、つないだ直後に意図せず外れないか** (Owner の確定は数百 ms かかる)。行き先 6 タイルの 1 回押しと「いまの行き先」、[地図と移動] を押した後の 4 秒の結果、保留中に [地図と移動] が半幅になり [申し込み 1 件] が並ぶこと。木陰・夜の可読性。 | [DES-UI-006](../development/issues/DES-UI-006-walk-menu-html-layout.md) | |
| A0-22 | おやつを手に出すと、自分の接続犬の最寄り 1 匹が寄ってくるか (他人の犬・未接続の犬は来ない)。食べたあと止まって待ち (8 秒、撫でている間は最大 20 秒)、次のおやつでは別の接続犬が来るか。6 m ほど先へ投げたとき、リード長の 1.25 倍で止まって見つめるか。接続犬が全員来られない状態 (遊び中・洗い場・移動の切符) で出そうとすると、理由が出て在庫が減らないか。手が頭と首輪に届く距離か (撫でる・首輪の Interact)。 | [IMP-DOG-006](../development/issues/IMP-DOG-006-leashed-treat-approach.md) | |
| A0-23 | おもちゃタブ上段の [まっすぐ] [曲げる] で、照準の線と実際のフリスビーの飛び方が一致するか。選んでいる方が点灯し下線が出るか。曲げた投げを犬が取る見た目に違和感がないか。[あそぶ] の「フリスビーの取り方」の副文が「全部の犬: ○○」と出るか (A0-19 の続き)。 | [DES-UI-002](../development/issues/DES-UI-002-frisbee-settings.md) | |
| A0-24 | おもちゃタブ上段の右半分「ボールの柄」で ◀ ▶ で選べ、持っていない柄は共通の確認カードで買えるか (価格は古民家と同じ。基本は無料)。6 匹 (犬種ごと) で柄ごとの大きさ・くわえた向き・犬色の見え方が自然か。文字が枠に収まるか。 | [DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md) | |
| A0-25 | 自動写真: 撫でる・ボールを持ってくる・フリスビーを取る・集合などで写真が撮れ、門の看板 A の隣のボードと、ヘッダーの [写真] の副画面に新しい順で出るか (8 枚、いちばん古い写真が置き換わるか)。ボードの位置・文字の大きさが読めるか。設定の [写真: ON / OFF] で撮影が止まり、戻るか。撮影の一瞬の引っかかりがないか。ボードに VRChat カメラを近づけて撮れるか。 | [IMP-WLD-005](../development/issues/IMP-WLD-005-auto-photo-and-gallery.md) | |
| A0-26 | 場所を決めて呼ぶ: VR でマーカーが手の高さに追従し、膝・胸の上に置くと犬がそこへ浮いて寝るか。斜面でも確定でき、赤い印や「平らな場所が必要です」が出ないか。呼び終わった (起こした) 犬が地面へ戻るか。高く置いて寝ている犬に別の犬が重なっても、置いた高さから落ちないか。(A89 の読み替え) | [IMP-DOG-032](../development/issues/IMP-DOG-032-call-placement-free-height.md) | |
| A0-27 | 上流の橋の入口の急な角 (周回の正方向) をリードの犬を連れて通る。犬が角の先で止まり、飼い主が角を回る間に手元へ走って戻る動きが見えるか (見えたら、何匹・どの向き・どの速さで)。下流の橋の角でも起きるか。1 匹と 6 匹で。 | [IMP-DOG-038](../development/issues/IMP-DOG-038-leash-wait-at-bridge-corner.md) (ClientSim の Course/83d で既知。直し方は未選択) | |
| A0-28 | 手洗いの図解: 主洗い場のボードの上の 3 コマ (①泡をつける ②手でわしゃわしゃ ③シャワーで流す) だけを見て、説明されずに「最初にボトルを持つ」「次に空いた手で犬に触る」が分かるか。絵の手・泡・ボトルが読めるか、高さ (上端 1.64 m) は見上げすぎないか。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
| A0-29 | ボトル: 棚の左手前のピンクのボトルを持ち、口を犬へ向けて Use を 1 回 → 当たった所に泡のかたまりがすぐ見えるか。押したままで 0.4 秒ごとに足されるか。口の向き・届く距離 (0.6 m)・泡の大きさ・手ごたえ (振動・音・粒) は自然か。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
| A0-30 | 手で泡立て: ボトルの泡を、空いた手 (道具を持っていない手) でこすると泡が増えて周りへ広がるか。片手で成立するか、両手 (ボトルを棚へ置く) でも洗えるか。広がる速さ・手の当たり判定 (手を毛に入れた時 / 少し浮かせた時)・振動は気持ちよいか。スポンジと比べて「自分の手で世話している」感じがあるか。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
| A0-31 | 混ざらないこと: 泡の無い所を触っても泡が出ない (頭は今までどおり撫でになる)、泡の上で手を止めているだけでは増えない、シャワーで流した所は触っても泡が戻らない (泡の縁からは少し入る)、道具を持った手では泡立たない。頭の泡をこすると泡立ちと撫での反応が両方出てよいか (既定は両方)。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
| A0-32 | 道中の 3 か所の洗い場にもボトルと図解があり、同じように洗えるか。ボトルを落とす・[RESET] で棚へ戻るか。洗い場に留めていない犬 (散歩中・道中) には素手が効かないこと (道具は今までどおり効く)。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
| A0-33 | 洗い場の図解 (4 コマ): ①泡をつける (すきな所に) ②ボトルをはなす ③両手でわしゃわしゃ ④たりなければ泡を足す、と下の行 (シャワーとタオル) が、背中限定・片手で洗うと誤解させずに伝わるか。4 か所とも。 | [IMP-UI-003](../development/issues/IMP-UI-003-wash-guide-bottle-release-both-hands.md) | |
| A0-34 | 散歩の帯: 入口・展望・広場のリード台の上の 4 コマ (①リードの先を取る ②首輪に近づける ③行き先をえらぶ ④はずす) が読めるか。高さ (下端 1.30 m)・向き・柱と背板の見た目、説明どおりに操作できるか (先が緑になる・つないだら先が台へ戻る・首輪を押して外す)。 | [IMP-UI-002](../development/issues/IMP-UI-002-guide-board-capture-instructions.md) | |
| A0-35 | 看板「あそびかた」: フリスビー (4 手順と注意 2 つ)・ボールの写真と枠・矢印・番号で、文字を読まなくても手順が追えるか。写真の中のメニューの文字 (おもちゃ・ディスク・ボール・犬の名前) が読めるか。上の帯の「お散歩はリード台で、洗うのは洗い場で、道は地図の看板で」で現地へ行けるか。 | [IMP-UI-002](../development/issues/IMP-UI-002-guide-board-capture-instructions.md) | |
| A0-36 | 写真 24 枚: メニューの [写真] と門のボードの ◀ ▶ でめくれるか (9 枚以上で出る)。撫でる 1 回で 4 枚撮れても、散歩や遊びの写真が押し出されないか。ボードは 60 秒で 1 ページ目へ戻るか。 | [IMP-WLD-007](../development/issues/IMP-WLD-007-photo-capacity-walk-and-menu-snap.md) | |
| A0-37 | 散歩中の写真: リードでつないで歩くと、立ち寄り・展望のベンチ (古民家が背景)・上流の橋・川辺・しばらく歩いた時に撮れるか。犬目線 (犬の向く先) の構図はよいか、地面や壁ばかりにならないか。自撮り風に自分の顔と犬が入るか (犬が横か後ろに居る時だけ)。撮られる頻度は多すぎないか。 | [IMP-WLD-007](../development/issues/IMP-WLD-007-photo-capacity-walk-and-menu-snap.md) | |
| A0-38 | メニューの拡大写真の吸着: まず手元メニューを開いて VRChat のカメラで撮り、メニューが写真に写るか (写らなければ吸着は別レイヤーへ変える)。写るなら、拡大した写真にカメラを 0.6 m 以内へ近づけて画面いっぱいになるか (Desktop で届くか)。 | [IMP-WLD-007](../development/issues/IMP-WLD-007-photo-capacity-walk-and-menu-snap.md) | |
| A0-39 | 動画カメラ (PC だけ。小さな版): 手元メニュー [写真] → [撮影カメラ]。目の前にモニターが出て犬が動いて映るか。[じぶん] で自分が映るか。◀ ▶ で犬を替えられるか。追従のなめらかさ・揺れ・距離はどうか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| A0-40 | 動画カメラの 5 分テスト (歩いても録画が続くか): (1) VRChat のカメラを出し、モニターから 20 m 離れてカメラを別の方向へ向ける → 映像が画面いっぱいに出たままか。その場で 1 回転しても消えないか。(2) カメラを Stream に切り替え、デスクトップの画面に映像が出るか。録画ソフトで 30 秒録り、ワールドの音・犬の声・自分の声が入るか。(3) [VRChatカメラに映す] を OFF → 普通の景色に戻る、ON → 映像に戻る。Desktop モードでも (1)(3) を 1 回。(4) 録画にモニターや映像の入れ子が写っていないか。鏡の前で鏡が全画面にならないか。× で閉じた後、VRChat のカメラに古い映像が残らないか。(1) で全画面にならなければ知らせる (板のレイヤーを変えて再確認)。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| A0-41 | 動画カメラの負荷: 開いている時と閉じている時の fps の差 (PC)。開閉を繰り返して重くならないか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| A0-42 | 動画カメラの再確認 (10-04 の修正後): OVR Advanced Settings でプレイスペースを動かして地面に座り、[じぶん] で、動画の中の自分が通常の見え方と同じ位置 (地面に座っている) に写るか。立って歩いても足が地面に付いているか。OVR を使わない時も合っているか。モニターを裏から見ると、裏返った文字ではなく暗い板だけが見えるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | OK (2026-10-04 ユーザー: 1〜3 とも OK) |
| A0-43 | 動画カメラの 10 種類 (10-08 の入れ替え後。距離・高さ・揺れ・被写体が入るか。「近い / 遠い / 高い」などを撮り方ごとに): **犬追従** (つないだ犬と歩く・止まる・曲がる) / **じぶん** (犬が足元でも顔と犬が入るか) / **背中ごし** (後ろ・右肩から自分の見ている方が写るか。歩く・その場で振り向く・止まるで画が振り回されないか。自分が画面の真ん中に来すぎないか) / **犬目線** (犬の横に立つと見上げる構図、7 m 以上離れると犬の向く景色) / **定点** (◀ ▶ で洗い場 4・川 2。草や壁の陰でないか) / **ディスク** (投げる。ディスクと追う犬が一緒に入り、映像が回らないか) / **キャッチ待ち** (投げた瞬間にキャッチ地点の横で待ち、飛んでくる所とキャッチが入るか) / **一周** (犬と座る。60 秒でゆっくり 1 周、木や斜面に当たらないか。◀ ▶ の 近づく / 離れる で 1 周かけて寄る / 引くか (近づくは最後に上半身の寄り)。[←] [→] で速さが変わるか (2〜24 °/s)) / **通り過ぎ** (橋・展望・川辺の近くを歩く。先回りして近づく・通る・遠ざかるを撮るか) / **みち** (◀ ▶ で 5 つ。散歩道の上と道の外の両方で: 待って寄る = 12 m 先で待ち、7 m まで来ると前へ寄ってくる / 後ずさり = 前を下がりながら / 横ならび = 右を一緒に / 追いこし = 後ろから抜いて前で待つ / 寄って引く = 前から離れていく。Vlog が撮りやすいか、カーブで道から外れすぎないか)。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md)、[IMP-WLD-012](../development/issues/IMP-WLD-012-video-camera-tps-and-path-moves.md) | |
|---|---|---|---|
| A0-1 | スポーンから入口の看板 A「あそびかた」に気づくか。お散歩 / フリスビー / 洗う / ポイントとトレーナーの 4 枚が読む順に分かり、文字の大きさ・距離が足りるか。昼とナイトモード、立位と座位で読めるか。下半分の余白・本文の小ささが気になるかも書く。 | [DES-UI-004](../development/issues/DES-UI-004-walk-destinations-and-boards.md)<br>[DES-UI-005](../development/issues/DES-UI-005-trainer-and-paid-items-purchase-flow.md) (4 枚目) | |
| A0-2 | 登り口の看板 B「裏山の地図」が手元の地図と合った絵で、現在地・洗い場・ワープの仕方が分かるか。看板の柱が通行や犬の邪魔にならず、すり抜けた時の違和感がないか。入口の計測プローブの箱が看板の近くに見える (公開前に撤去する開発用) ことは気にしなくてよい。 | [DES-UI-004](../development/issues/DES-UI-004-walk-destinations-and-boards.md) | |
| A0-3 | 洗い場で犬を呼び、洗ってから「おわり」を押す。犬が台の上からすぐに (満足の座りを待たずに) 小走りで出ていくか。向き・足の動きが不自然でないか。台を上げていた時は台と一緒に下りて出るか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) (追加要望 10-02) | |
| A0-4 | 主洗い場のそばを歩く空いている犬 (呼んでいない犬) が、台の周り約 4 m に入ってこず、台の上で休み始めないか。6 匹を放して少し待ち、うろつき方が不自然でないか (縁を回り込む・急に向きを変えるのが目立たないか)。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) (追加要望 10-02) | |
| A0-5 | 洗い場の中でリードを外してから、洗い始める (または別の犬を呼ぶ) までの猶予 20 秒は足りるか。洗っている間は追い出されないか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) (追加要望 10-02) | |
| A0-6 | 主洗い場の近くのリード台・おやつ置き場が、犬が寄らない範囲 (約 4 m) のせいで使いにくくなっていないか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) (未確認) | |
| A0-7 | 散歩していない犬の首まわりを撫でる・洗う。首輪の「外す」インタラクト表示が出ず、手の操作を取られないか。 | [IMP-DOG-031](../development/issues/IMP-DOG-031-collar-release-interact-only-own-walk.md) | |
| A0-8 | 自分が散歩中 (リード接続中) の犬の首輪には「外す」が出て、押すと外れるか。外したあと (または他の犬) には出ないか。 | [IMP-DOG-031](../development/issues/IMP-DOG-031-collar-release-interact-only-own-walk.md) | |
| A0-9 | 川沿いを歩く。川へ近づくとせせらぎが聞こえ、離れると消えるか。道が川から離れた所は静かか。音量が BGM・犬の声・会話・洗い場の音を邪魔しないか。 | [IMP-ENV-006](../development/issues/IMP-ENV-006-river-ambience.md) | |
| A0-10 | せせらぎのループの継ぎ目・クリックが気にならないか。川の曲がり角など音源が重なる所で音が大きくなりすぎないか。設定に音量の行が無い (BGM の音量にも連動しない) ことが不便か。ライブ中に川の音をどうしたいかも書く。 | [IMP-ENV-006](../development/issues/IMP-ENV-006-river-ambience.md) (音量・ライブ中の扱いは聞いてから決める) | |
| A0-11 | オートナイトモード: 入室して時間が経つと自然に暗くなるか (昼約 10 分 → 夕暮れ約 30 分 → 暗さ 0.8 で止まる)。暗さ 0.8 でも道・犬・メニュー・看板が見やすいか。暗くなる速さは適切か。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md) | |
| A0-12 | 設定のナイトモード行: [オート][マニュアル] が切り替わり、オート中にスライダーを触るとマニュアルへ切り替わるか。マニュアルは自分だけの暗さで、オートへ戻すと共通の暗さへ 3 秒ほどで寄るか。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md) | |
| A0-13 | 入り直すと、モード (オート / マニュアル) とマニュアルの暗さが戻るか。言語を英語・ドイツ語などに変えても、[オート][マニュアル] の文字が枠に収まるか (11 言語は端末の字形)。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md) | |

### A-1 主洗い場 (スポーン近く): すすぎ改善 (10-02 の最新分。最初にここ)

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A1 | スポンジでゴシゴシこする。泡が気持ちよく増えて広がるか。速すぎ・広すぎないか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認1) | |
| A2 | シャワーを当てる。泡がさっと流れ、帯状にきれいな毛が現れる感じが残っているか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認2) | |
| A3 | 肩・脇・足の付け根・関節に、狙い続けないと落ちない泡が残らないか。柴のあと、ポメ・コーギー・サモエドでも見る。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認3)<br>[IMP-DOG-030](../development/issues/IMP-DOG-030-wash-six-dogs.md) (10-02 要望) | |
| A4 | 片側を流しているとき、反対側の足や体の裏の泡まで消えて不自然に見えないか (シャワーは距離だけで効く)。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認4) | |
| A5 | 流した所の隣に残った小さな泡が、水を止めて 1〜2 秒で続いて消えるか。消え方は自然か。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認5) | |
| A6 | 泡を付け直して繰り返し洗っても楽しいか。全身 1 匹にかかる時間 (目標 1〜2 分) を計って書く。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認6・表 15)<br>[IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (完了条件) | |
| A7 | 水から始める / 泡の途中で水を当てる / きれいにしたあとにもう一度泡を付ける、のどれもできるか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 12)<br>[IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (完了条件) | |

### A-2 主洗い場: 洗い心地 (道具・音・犬の反応)

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A8 | シャワーは Use を押すたびにオン・オフが切り替わるか (押し続けない)。空中に出している時と犬へ当てている時で、見た目と音が変わるか。水が当たった位置に飛沫が出るか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 5・6)<br>[IMP-ENV-007](../development/issues/IMP-ENV-007-main-wash-station.md) (決定 09-30) | |
| A9 | 音: 噴射と接触を聞き分けられるか。泡に当てた時と毛に当てた時で変わるか。止めると自然に消えるか。耳障りな成分やループの継ぎ目がないか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 8)<br>[IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md) | |
| A10 | 足・尻尾・頭・首を洗えるか。届かない・当たらない部位はないか。足を動かしても泡が足についてくるか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 10・11)<br>[IMP-DOG-030](../development/issues/IMP-DOG-030-wash-six-dogs.md) | |
| A11 | 泡の殻が毛の上に自然に乗り、横腹・脇・足・顔で毛と前後しないか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (賭け 2) | |
| A12 | 洗っている間、犬がその場に止まるか。部位を変えると首をかしげるか。洗うのをやめて 3 秒ほどで歩き出すか。(体を回さない点は 09-30 に確認済み) | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 9)<br>[IMP-DOG-027](../development/issues/IMP-DOG-027-wash-behavior-switching.md) | |
| A13 | コミカルな反応 (首かしげ・足上げ・ブルブル) に気づけて楽しいか。洗う対象と音を追えなくならないか。すすぎ前に泡を全部落として見せ場を奪わないか。同じ反応の連発が気にならないか。 | [IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md) | |
| A14 | きれいになった時の満足の合図 (座る・尻尾・笑顔・声) で「もう十分きれい」と分かるか。続けて洗えるか。 | [IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md) | |
| A15 | 日向・木陰・ナイトモードで、泡が光って浮いたり暗く沈んだりしないか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 18) | |
| A16 | 洗い場の近くでも、首輪への Interact、リード接続、撫で、ボール・ディスクをつかむ操作が今までどおりできるか。尻尾と耳が今までどおり揺れるか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 13・14) | |
| A17 | 先行の Animal Wash と比べて、良くなった点と違和感を書く。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 19)<br>[IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md)<br>[VER-DOG-002](../development/issues/VER-DOG-002-wash-experience-and-devices.md) | |

### A-3 主洗い場: 棚・犬を呼ぶ・高さ・6 匹

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A18 | スポーンから洗い場が見えるか。初見でも道具を取ってすぐ洗い始められるか。リードから手を空ける動作で迷わないか。 | [IMP-ENV-007](../development/issues/IMP-ENV-007-main-wash-station.md) | |
| A19 | 棚の位置と高さは取りやすいか。犬の横へ回れる余白があり、集合・リードの取り出し・散歩への出発を妨げないか。 | [IMP-ENV-007](../development/issues/IMP-ENV-007-main-wash-station.md) | |
| A20 | 道具は手放した所に残り、棚のリセットボタンで全部定位置へ戻るか。戻し先が見て分かるか。 | [IMP-ENV-007](../development/issues/IMP-ENV-007-main-wash-station.md) (決定 09-30) | |
| A21 | 手で置いたもの (おやつ置き場・リード台・待機の輪・おじいさん) と干渉していないか。 | [IMP-ENV-007](../development/issues/IMP-ENV-007-main-wash-station.md) | |
| A22 | ボードで犬を選んで呼ぶ。犬が来て台に立ち、立つ向きで両側から洗えるか。(ボードの押しやすさは 10-01 に確認済み) | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) | |
| A23 | 呼んだ犬が洗い終わるまで留まり、「おわり」で台を下りて普段の行動へ戻るか。別の犬を呼ぶと交代するか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) (記録 10-01) | |
| A24 | 台の ▲▼ で犬が上がる。立ったまま・座ったままで、犬の背中と足が洗えるか。 | [IMP-ENV-011](../development/issues/IMP-ENV-011-wash-height-adjust.md) | |
| A25 | 自分の ▲▼ で自分が浮く。犬を上から洗えるか。上げ下げで酔わない・落ちない・台や棚に引っかからないか。 | [IMP-ENV-011](../development/issues/IMP-ENV-011-wash-height-adjust.md) | |
| A26 | ボードの文字 (日本語) が読めるか。 | [IMP-ENV-011](../development/issues/IMP-ENV-011-wash-height-adjust.md) | |
| A27 | リードを付けたまま洗う。リードに余裕がある時に犬が止まり、歩き出したら直ちに追従へ戻るか。 | [IMP-DOG-027](../development/issues/IMP-DOG-027-wash-behavior-switching.md) | |
| A28 | 洗い終えたあと、犬が普段の遊び (歩く・撫でられる・ボール) へ自然に戻るか。 | [IMP-DOG-027](../development/issues/IMP-DOG-027-wash-behavior-switching.md) (親 024 また過ごす) | |
| A29 | 6 匹それぞれで、こする → すすぐ → 仕上げ → 普段の遊びへ戻れるか。 | [IMP-DOG-030](../development/issues/IMP-DOG-030-wash-six-dogs.md) | |
| A30 | 白・黒・茶の毛で泡と汚れが見え、顔や表情を隠しすぎないか (特にサモエドの白い毛と小型犬)。落ちない部位・過剰な反応がないか。 | [IMP-DOG-030](../development/issues/IMP-DOG-030-wash-six-dogs.md) | |
| A31 | 小型犬の足・大型犬の背中に無理なく届くか。座ったまま・片手・ゆっくりした操作でも洗えるか。 | [IMP-DOG-030](../development/issues/IMP-DOG-030-wash-six-dogs.md)<br>[IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (完了条件) | |

### A-4 門: おじいさん・ラック・リード

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A32 | おじいさんが門のそばに立ち、地面から浮かず埋まらず、待機アニメが動くか。話しかけるまで板が出ず、4 m 離れると閉じるか。ナビ 5 項目が押せ、本文が枠に収まるか。 | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) (メニュー計画 段階 1) | |
| A33 | 初回の会話で「200pt」が出て残高が +200 になるか。連打しても、入り直しても 2 回目は入らないか。(受け取り済みなら「入り直しても入らない」だけ見る) | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) (T-007) | |
| A34 | 入室直後は「読み込み中...」、復元後に数値が出るか。滞在 1 分ごとに 1 増えるか。入り直すと残高が戻るか。 | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) | |
| A35 | ヘッダーの残高を押すと「いま N pt」と入手経路 2 行と [戻る] が出るか。受け取る前は準備中の行に「おじいさんから受け取れます」の案内が出て、受け取ると消えるか。 | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) | |
| A36 | 初めて入った時だけ、入口の前にようこその板が出るか。[あとで] / [メニューを見る] が効き、ラックやおじいさんへの視界を遮らないか。入り直すと出ないか。フッタ 2 行が枠に収まるか。 | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md)<br>[IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (段階 3) | |
| A37 | 残高 0 のままで、散歩・リード解除・ボール・フリスビー・撫で・集合が使え、ポイントを求める表示が出ないか。 | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) (T-008) | |
| A38 | 通しで 1 周: 入室 → おじいさんに話しかけて 200pt → かざりで 1 品買って犬に着ける → さんぽでつないで歩く。案内と画面が食い違わず、途中で詰まらないか。 | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) (メニュー計画 段階 2 の一周)<br>[IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) | |
| A39 | リードの先を取り出して犬の首輪へ付ける。右手・左手どちらでも、輪の後ろの帯が指の付け根に沿い、輪と紐の接続部が前腕の延長で拳の前へ出るか。握りが拳に食い込まない・浮かないか。 | [BUG-DOG-002](../development/issues/BUG-DOG-002-leash-handle-orientation.md) | |
| A40 | 腕を前へ出す・下げる・左右へ向ける・手首を回しても、握り位置と紐の接続がずれないか。手首を深く曲げても持ち手が跳ばないか。片手 3 匹でも持ち手は手ごとに 1 個か。 | [BUG-DOG-002](../development/issues/BUG-DOG-002-leash-handle-orientation.md) | |
| A41 | 首輪・リードを明所と木陰、立位・座位・低い視点で見て、動作中のずれや見え方に問題がないか。 | [IMP-DOG-004](../development/issues/IMP-DOG-004-leash-models-release.md) (残り 座位/木陰) | |
| A42 | リードが張って引っ張られると、持っている側の手が震えるか (張るほど強くなるか)。立ち止まっているのに犬が離れていく時に「グイッ」と来るか。 | [IMP-DOG-007](../development/issues/IMP-DOG-007-leash-pull-haptics.md) | |
| A43 | 紐が緩む・犬が待つ・追いついた時に鳴り続けないか。リスポーン・拠点への瞬間移動・解除のあとに鳴らないか。 | [IMP-DOG-007](../development/issues/IMP-DOG-007-leash-pull-haptics.md) | |
| A44 | 片手 3 匹 (両手 6 匹) で振動がうるさい・強すぎないか。左右が正しい手に来るか。 | [IMP-DOG-007](../development/issues/IMP-DOG-007-leash-pull-haptics.md) | |

### A-5 散歩 (門 → 登り道 → 展望)

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A45 | 犬 1 匹: つなぐと自分から道を登り始め、リード端で止まってこちらを向いて待つか。近づくと再び先へ行き、歩き続けると犬が前を歩く時間が長いか (後ろに隠れない)。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §0) | |
| A46 | 立ち止まって 8〜10 秒: 犬が戻って 1 回吠え、こちらを見てまた先へ行くか。1 分止まると誘いは 3 回で止まり、そばで伏せ、歩き出すと起きてついてくるか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §0) | |
| A47 | 広場に着くと、まわりをうろうろして落ち着くか (先へ行こうとし続けない)。道から外れて草地を横切ると手のまわりについてくるか。引き返すと目的地を変えずについてくるか。首だけ回す・手だけ振っても進路が反転しないか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §0) | |
| A48 | 途中 (坂の途中・展望) で外して付け直すと、すぐ散歩を再開するか。看板 (GATE / PLAZA) を押すと犬が向かうか (他人の犬・未接続の犬は動かない)。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §0) | |
| A49 | 3 匹・6 匹: 同じ点に固まらず、待つ位置・寄り道のタイミングがばらけるか。全員が同時に戻って吠えず、誘いは 1 匹ずつか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §1)<br>[IMP-DOG-010](../development/issues/IMP-DOG-010-walk-stopovers.md) (3〜6 匹) | |
| A50 | 3 匹・6 匹: 同じ立ち寄り場所に 2 匹以上が同時に止まらず、「また待つのか」にならないか。往復の所要時間と寄り道・立ち寄りの合計回数を控える。目的地への到着が遅れすぎないか。 | [IMP-DOG-010](../development/issues/IMP-DOG-010-walk-stopovers.md) (3〜6 匹) | |
| A51 | 最速の段階で 6 匹が走っても重ならず、紐の見え方が破綻しないか。カクつき・足滑りが許容範囲か。 | [IMP-DOG-009](../development/issues/IMP-DOG-009-walk-speed-tiers.md) (3〜6 匹)<br>[IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §1) | |
| A52 | 直線ロープへの切替 (フレームが落ちた時) と、同じ持ち手の犬同士のめり込み具合が気にならないか。 | [IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) (034 未確認分) | |
| A53 | リードの紐の追従タイミング、持ち手での接続、撫でたときの犬の視線、犬同士の挨拶・じゃれ合い、ボールのフェイントが違和感なく動くか。 | [IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) (036 未確認分) | |
| A54 | 犬が川に入った時の手元の通知 (ポーチ) と、おじいさんの Interact が動くか。 | [IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) (036 未確認分) | |
| A55 | 集合: [あそぶ] (トレーナー所持) で隊形を選び [ここに集合]。足元に印 6 個 (斜面でも地表)、犬が歩いて集まり、つないでいる犬は来ないか。集合中に [つなぐ] を押すと「集合中は待ってください」か。1 分で解散して犬がその場でうろつくか。木や柵の向こうの犬がすり抜けて来るか。 | [DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) (集合計画 §0) | |
| A56 | 拠点 (入口・展望・広場) のラックに左右 1 本ずつ道具があり、首輪につなぐと道具が最寄りの拠点へ戻る (地面に残らない) か。山上で外して山上の拠点から付け直せるか。 | [DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) (集合計画 §0) | |
| A57 | 展望のベンチの洗い場 (ベンチから道に沿って約 5 m): 位置と導線、開始と道具の戻し先が初見で分かるか。散歩の流れを止めず、洗うか通り過ぎるか選べるか。 | [IMP-ENV-008](../development/issues/IMP-ENV-008-wash-stations-along-course.md) | |
| A58 | 散歩のあと足先から汚れが付くか (足元ほど濃く、上ほど薄い)。洗い場へ戻って洗い、洗い場の近くで汚れ直さないか。洗ったのに散歩ですぐ汚れないか (汚れる距離 900 m・泥 25 秒・猶予 300 秒の体感)。汚れを罰や義務に感じないか。 | [IMP-DOG-029](../development/issues/IMP-DOG-029-dirt-accumulation.md)<br>[IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (汚れる理由と余韻) | |

### A-6 広場: フリスビー・ボール・おやつ・かざり・呼ぶ・ポーズ

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A59 | 広場の拠点 (リード置き場) が遠くないか。(位置は 09-29 にユーザーが手調整済み) | [DES-DOG-003](../development/issues/DES-DOG-003-plaza-leash-station.md) | |
| A60 | 入室時に広場にはディスク 6 枚だけが見え、ボールは見えないか。 | [IMP-DOG-019](../development/issues/IMP-DOG-019-ball-and-disc-placement.md) (夜の追加) | |
| A61 | [ボール] で目の前にボールが出て同じ犬のディスクが消えるか。[ディスク] で逆に戻るか。 | [IMP-DOG-019](../development/issues/IMP-DOG-019-ball-and-disc-placement.md) (夜の追加) | |
| A62 | ボール追跡中は [ディスク] が拒否され、現地でも拾えないか (逆も同じ)。持っている手から落ちないか。他の犬に影響しないか。 | [IMP-DOG-019](../development/issues/IMP-DOG-019-ball-and-disc-placement.md) | |
| A63 | 6 匹とも自分の色のディスクを投げると跳んで取り、持ち帰って置くか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) (§0) | |
| A64 | 犬種ごとの姿勢: ため・跳躍・着地で脚が地面を突き抜けない、腹が床に付かない、胴や尾が折れないか (特にコーギー・ダックスの胴)。口がディスクに届いて見えるか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) (§0) | |
| A65 | 高い投げ (lob) で跳躍の高さが体高に対して自然か (小型犬の 0.45 m は体高の約 2.9 倍。跳びすぎ・低すぎの犬種を記録)。ひねりの投げで 6 犬種とも 90° ひねって取り、着地で前を向くか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) (§0) | |
| A66 | 胴体追従を切った 5 匹 (コーギー・ポメ・ダックス・柴・サモエド) の歩き・向き変えが硬く見えないか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) (§0) | |
| A67 | メニュー「おもちゃ」の 6 行すべてで [ディスク] が押せ、犬の手元へ来るか。2〜3 枚を続けて投げると各犬が自分のディスクだけを追うか。6 枚を順に投げて犬同士がぶつかる・重なる見え方が気にならないか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) | |
| A68 | あそぶタブ「フリスビーの取り方」を切り替えると、ディスクを持つ犬全部が同じ設定になるか。退役した設定 (走り込みの横・遠く) がメニューから消えているか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md)<br>[IMP-DOG-017](../development/issues/IMP-DOG-017-frisbee-intercept-solver.md) (PCVR 3 回目) | |
| A69 | リード中・集合中の犬は自分のディスクを投げても追わず、外すと再び遊べるか。追走中に別の犬をつなぐ・集合させても、追走中の犬は影響を受けないか。6 匹同時に遊んで重くならないか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) | |
| A70 | 大型犬 (ラブ・サモエド) の走りで足の滑りが気にならないか (再生倍率 1.7)。横曲がり (0.07) の曲がり具合は適切か。 | [IMP-DOG-017](../development/issues/IMP-DOG-017-frisbee-intercept-solver.md) (PCVR 3 回目) | |
| A71 | 3 m 離れて、どの犬のディスクか顔の印字で分かるか。持つと顔が消え、離すと戻るか。 | [IMP-DOG-018](../development/issues/IMP-DOG-018-frisbee-dog-face.md) | |
| A72 | 持ち帰って目の前で待つ犬を撫でられる (ハート・表情・尻尾) か。撫でている間はディスクを落とさず、手を離すと残り時間で落とすか。 | [IMP-DOG-013](../development/issues/IMP-DOG-013-frisbee-pet-after-return.md) | |
| A73 | ディスクを受け取っても 2 秒または 2.5 m 離れるまでその場で待つか。撫で続けている間は走り出さず、離れると 8 m 先へ走って構えるか。猶予中に投げれば即座に追うか。 | [IMP-DOG-013](../development/issues/IMP-DOG-013-frisbee-pet-after-return.md) | |
| A74 | ボールを投げると Fetch が勝ち、その間は撫でられないか。リード接続中・集合中はフリスビーが始まらず、途中で取られたら犬がそちらへ行くか。待っている間の撫でで口のディスクが外れたり暴れたりしないか。 | [IMP-DOG-013](../development/issues/IMP-DOG-013-frisbee-pet-after-return.md) | |
| A75 | ジャンプ 4 段階 (ため → 口が届く瞬間 → 着地の沈み込み) が自然につながって見えるか。 | [IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md) | |
| A76 | 取れない投球 (高すぎ・遠すぎ・壁・人がつかむ・場外) で、中止・着地・場外復帰が破綻しないか。 | [IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md) | |
| A77 | 犬から 40 m より遠い所で持つ・投げると犬が動かず、照準に × と「犬が遠くにいます」が出るか。 | [BUG-DOG-007](../development/issues/BUG-DOG-007-shiba-throw-implausible.md) | |
| A78 | 道の斜面で上り・下りへ投げる。跳ぶ・拾うの切り替えが自然か。0.3〜0.5 m 低い所で跳んで取ると、宙で着地せず地面に刺さらないか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #37・38)<br>[DES-DOG-005](../development/issues/DES-DOG-005-frisbee-anywhere.md) | |
| A79 | 崖・木立・岩の方へ構えて投げると × と一言が出るか。跳ぶ弧が岩や枝を通り抜けないか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #39)<br>[DES-DOG-005](../development/issues/DES-DOG-005-frisbee-anywhere.md) | |
| A80 | おやつ: 出す → あげる → しまう が 1 人で成立するか (吸着 → 犬が来る → 食べる → お礼)。6 匹それぞれで食べる動きとお礼の効果の位置がおかしくないか。 | [IMP-DOG-014](../development/issues/IMP-DOG-014-treats-port.md) | |
| A81 | 定位置のおやつが地面に沈まず、浮きすぎないか。 | [IMP-DOG-014](../development/issues/IMP-DOG-014-treats-port.md) (メニュー計画 段階 2) | |
| A82 | [買う]: ヒントが枠に収まり、確認カードが 上限 / 不足 / 購入可 の 3 態で正しく出るか。[やめる] で「購入をやめました」、買うと在庫が 5 増え、入り直すと在庫が戻るか。 | [IMP-DOG-014](../development/issues/IMP-DOG-014-treats-port.md) | |
| A83 | かざり 14 商品が 6 匹に着けられるか。購入・所持・試着が台帳 (残高) と一致し、未所持でも試着へ進めて犬の行から購入確認へ行けるか。 | [IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) | |
| A84 | 首輪と首元の飾り: 下げた 5 件が胸の毛に沈んで見えないか、除外した 3 件の行が暗く理由が出るか、前へずらした 9 件が毛から浮いて見えないか。 | [IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) (案 C) | |
| A85 | リードの線と首元の飾りの交差 (ダックスは留め具まで約 68 mm) が不自然でないか。試着のミニチュアの首輪が実際の犬と同じに見えるか。 | [IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) | |
| A86 | 撫でエフェクト: 行がかざり一覧の最初の階層にだけ出て、買う → つけるが効くか。入り直すと自分の選択が戻るか。 | [IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) | |
| A87 | 呼ぶ・ポーズ: 6 匹で、犬 → ポーズ → 場所の流れが平地で成立するか (予約・配置・到着・終了)。4 動作 (へそ天・あご乗せ・お手・伸び+あくび) は買うまで価格が出て、買うと使えるか。 | [IMP-DOG-016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) | |
| A88 | リード中の犬の行は暗く「リード中。さんぽで外してから」が出て、外すと呼べるか。呼ばれて待機中の犬はさんぽでつなげず (理由が出る)、集合中の犬も呼べないか。 | [IMP-DOG-016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) (T-010) | |
| A89 | 斜面で赤いマーカーと「この姿勢には平らな場所が必要です」が出て確定できず、平地では成立するか。地面法線 0.9 前後に置いた犬が沈まず浮かないか (0.9 を採用してよいか)。 | [IMP-DOG-016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) | 決定: 2026-10-03 (ユーザー) 法線 0.9 を採用。**撤回: 法線 0.9 は 032 により撤回。斜面・手の高さに置けることを見る** (同日のユーザー回答「推奨で」。この行の「赤マーカー・確定不可」は期待しない。確認は A0-26) |
| A90 | NavMesh が途切れる場所へ呼んでも、犬が直進でたどり着き固まらないか。場所決めのマーカーの光線が小物に当たって変な高さに出ないか。 | [IMP-DOG-016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) (メニュー計画 段階 2) | |

### A-7 奥広場・川

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A91 | 奥広場の洗い場: 位置と導線、開始と道具の戻し先が初見で分かるか。 | [IMP-ENV-008](../development/issues/IMP-ENV-008-wash-stations-along-course.md) | |
| A92 | 川辺の洗い場: 位置と導線、看板・石との干渉がないか。開始と戻し先が初見で分かるか。 | [IMP-ENV-008](../development/issues/IMP-ENV-008-wash-stations-along-course.md) | |
| A93 | 洗い場を移っても、汚れと道具の扱いが自然に保たれるか。 | [IMP-ENV-008](../development/issues/IMP-ENV-008-wash-stations-along-course.md) | |
| A94 | 川辺の立ち寄り点から、看板「犬の水あそび」と石 3 群、濃い踏み分け道が見えるか。着いたら入水点とそこへ続く道が見えるか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #19・#1) | |
| A95 | 入水点の近くでリードを外す。5 秒以内に犬が水へ入るか。手元に「ここで放すと水遊びします。上がったらまたつなげます」が 1 回出るか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #2) | |
| A96 | 犬を連れずに川辺へ行く。25 m 以内の犬が自分から入水し、手元に「(犬名) が水遊びに行きました」が出るか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #20) | |
| A97 | 浅瀬で足元のしぶきと波紋が出るか。量と大きさがうるさくないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #3) | |
| A98 | 小型犬 (ポメ・コーギー・ダックス) と柴が深い所で浮いて犬かきに見えるか。口と鼻先が水面から出ているか。止まって見えないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #4) | |
| A99 | 泳ぐ犬の上下の揺れ (±1 cm) を、揺れありの犬 (コーギー・柴) と揺れなしの犬 (ポメ・ダックス) で見比べ、要るか判断する。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #5・#25) | 決定: 2026-10-03 (ユーザー) 「そのままでOK」= 現状の揺れ ±1 cm のまま採用 |
| A100 | 柴を泳がせるか (泳がせる / 浅瀬だけ) を決める。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #6) | 決定: 2026-10-03 (ユーザー) 「そのままでOK」= 現状 (柴は泳ぐ犬種の表に入っている) のまま |
| A101 | 大型犬 (サモエド・ラブ) は浅瀬をチャプチャプ歩き、泳がず、深い方へ行きすぎないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #7) | |
| A102 | 水の中の犬にリード接続・おもちゃの呼び出しを試す。拒否の一言が出て、犬が要求した人を向いてから上がるか。上がった後はつながるか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #8・#9) | |
| A103 | 水の中の犬を「呼ぶ (そばで寝る)」・おやつ・陸へボールを投げる。犬が上がってから来る・取りに行くか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #10・#11) | |
| A104 | 6 匹を入水点の近くで放す。少しずつ時間と場所をずらして入り、重なり続けないか。見た目が騒がしくないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #12・#24) | |
| A105 | 水遊びをナイトモードで見る。水面・犬・粒子が見分けられるか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #13) | |
| A106 | 水音 (浅瀬の「ぱしゃ」、入水の「ばしゃっ」、犬かき) が犬の位置から聞こえ、20 m で消えるか。採用 / 差し替え / 無音を決める。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #14・#26) | 決定: 2026-10-03 (ユーザー) 「そのままでOK」= 現在の合成の水音 3 本のまま採用。犬の位置から聞こえる・20 m で消えるの確認は別 |
| A107 | 浅瀬で犬どうしが押し合う場面 (集合など) で、深い側へ押されず、重なったまま止まらないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #28) | |
| A108 | 小型犬がいる所でボールを川へ投げる。浮いて下流へ流れ、犬が泳いで取り、上陸して身震いし、手元へ持ち帰るか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #21) | |
| A109 | 大型犬で同じことをする。浅い所のボールは歩いて取り、深い所のボールは追わず下流端で止まるか。対岸からボールを川へ投げると、犬は取って東岸の復帰点で置くか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #22・#23) | |
| A110 | 道から離れた川 (z 150 付近・z 400 付近) で川の真ん中へボールを投げる。ボールが浮いて下流へ流れ、犬が岸の決まった所まで走ってから水に入るか。 | [IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md) (川 #29) | |
| A111 | 上のボールを、小型犬は泳いで、大型犬は流路を歩いて取り、投げた人の側の岸へ上がって手元へ持ち帰るか。サモエドの口とくわえたボールが水面より上に見えるか。 | [IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md) (川 #30・#31) | |
| A112 | 犬を対岸に置いて自分の岸から投げる。犬は自分の岸から入り、こちらの岸へ上がって持ってくるか。 | [IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md) (川 #32) | |
| A113 | 橋 (z 190 / 490) の近くの川へ投げる。ボールは浮かず 10 秒以内に投げた人の側の岸へ戻り、犬は川に入らないか。橋の上ではフリスビーを拾うだけで、橋の帯では岸へ戻るか。 | [IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md) (川 #33)<br>[IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #43) | |
| A114 | 水遊び中の犬のボールを手元メニューで呼ぶ。ボールが来て犬は水遊びを続けるか。犬が水中のボールを追っている最中に呼ぶと、ボールが来て犬は諦めて岸へ上がるか。犬がくわえて泳いでいる最中は断られ、そのまま持ち帰るか。 | [IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md) (川 #34〜#36) | |
| A115 | 小型犬で、岸から川の上へフリスビーを投げる。ドボン → 泳いで投げた人の側の岸へ → 手元へ来るか。ラブ・サモエドは、浅いとドボンして歩き、深いと跳ばないか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #40・#41)<br>[DES-DOG-005](../development/issues/DES-DOG-005-frisbee-anywhere.md) | |
| A116 | 川へ低く投げて取り損ねさせる。浮いて流れ、犬が取りに行くか。浮いているディスクを手で取って陸で放す・投げると、取った瞬間から重力があり陸で転がるか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #42・#48)<br>[DES-DOG-005](../development/issues/DES-DOG-005-frisbee-anywhere.md) | |
| A117 | 川で遊んでいる犬を、洗い場のボードで呼ぶ。岸へ上がってから洗い場へ来て立つか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) (記録 10-01) | |

### A-8 地図・ワープ

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A118 | 手元メニュー [地図と移動] → 奥の広場 → [ここへワープ]、次に川辺へ (犬 0 / 1 / 3 / 6 匹、昼・ナイトモード・ライブ中)。画面が黒くなってから移動し、両眼とも黒く、メニューも覆われて、明るく戻るか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #1a) | |
| A119 | 着いた向きが道の正方向か (壁・崖を向かない)。メニューが目の前に置き直されるか (移動 1 回につき 1 回)。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #1b) | |
| A120 | 到着後に [さんぽへもどる] で歩く。犬がそばにいて一緒に歩き出すか。ワープしたあとに歩き出したくなるか (ワープの役割の評価)。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #1c) | |
| A121 | [ここへワープ] を押して暗転中に [取り消す]。取り消せて、明るく戻りその場に残るか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #1d) | |
| A122 | 地図を開いたまま歩く・向きを変える。数字 1〜6、● (ワープできる 2 地点)、○ (目印) が読めるか。自分の点と向きの棒が追従するか。周回路・川・橋の線が潰れないか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #1e) | |

### A-9 手元メニュー・設定・BGM

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A123 | 結果表示が取り違わないか: 連打する / 結果が出る前にタブを変える / 閉じて開き直す / 拒否される / 4 秒を超える。それぞれで別の操作の結果が出たり前の結果が残ったりせず、4 秒超は「結果を確認できませんでした」になるか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §0) | |
| A124 | さんぽ・おもちゃ・あそぶが以前と同じに動くか (つなぐ / 外す / 行き先 / 目の前へ / 隊形と集合)。[はずす] がつないでいる犬の行にだけ出るか。おもちゃの理由の行が枠に収まるか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §0) | |
| A125 | ナイトモード: 設定のスライダーで夜の明るさが連続的に変わり、フェードが不自然でないか。入り直すと値が戻るか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §0・§4) | |
| A126 | 距離フェード (PC): 変えて [確定して保存] すると犬の毛の見え方が変わるか。入り直すと戻るか。範囲外の値は保存されず理由が出るか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (T-011) | |
| A127 | 犬の声: 吠え声・息が鳴るようになったか。音量 50%・頻度がうるさくないか。音量頁の犬の声スライダーで変わるか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §4) | |
| A128 | 設定ホームの 3 群 (自分の見え方・聞こえ方 / この部屋のみんな / 読む) で迷わないか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §4) | |
| A129 | クレジット頁の 7 行が読めるか。インフォメーションの 4 頁 (はじめに / 生成 AI / VAnimal / トレーナー) が開けて本文が読めるか。(クレジットの URL 3 件はユーザー記入後に確認) | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §4) | |
| A130 | BGM: 設定 → 音量に BGM の行があるか。0%・中間・100% で実際の音量が変わり、0% で無音になるか。OFF → ON、OFF 中の音量変更、メニューの開閉で表示と出音が一致するか。入り直すと ON・50% に戻るか。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |
| A131 | BGM の [ON/OFF] ボタンを残すか決める (古民家の現行画面には無く、0% で無音に統一している)。基準音量 0.35 が犬や会話を邪魔しないか。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) (判断待ち) | 決定: 2026-10-03 (ユーザー) ON/OFF ボタンは残す。基準音量 0.35 は採用 (「残す。OK」) |
| A132 | BGM の余韻・複数周回の継ぎ目が気にならないか (圧縮品質 PC 0.7 の聞こえ)。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |
| A133 | BGM・洗浄音・犬の声を重ねても、自分の操作の音と会話が聞き取れるか (主洗い場で)。 | [IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (音)<br>[IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md)<br>[IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |
| A134 | ライブ中に BGM が滑らかに下がり、終了後に自分の設定へ戻るか。ライブ中の設定変更・途中入室でも勝手に ON や以前の音量へ戻らないか。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |

### A-10 周回 (最後に。長い)

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| A135 | 犬を連れて一周 (門 → 広場 → 奥広場 → 川 → 小橋 → 門) を実際に歩き、所要時間を書く (目標は休憩を除いて約 20 分、寄り道込みで 30〜40 分)。 | [IMP-ENV-004](../development/issues/IMP-ENV-004-river-loop-course.md)<br>[DES-ENV-002](../development/issues/DES-ENV-002-longer-course.md) | |
| A136 | 既存の道・広場・見晴らし・古民家の視認性が保たれ、新旧の道に穴・段差・遠景の端がないか。 | [IMP-ENV-004](../development/issues/IMP-ENV-004-river-loop-course.md) | |
| A137 | 曲がり角・橋・写真を撮る視点・両眼で、景色が不意に消えないか (LOD・遮蔽)。 | [IMP-ENV-004](../development/issues/IMP-ENV-004-river-loop-course.md)<br>[DES-ENV-002](../development/issues/DES-ENV-002-longer-course.md) | |
| A138 | 1 匹・3 匹・6 匹で、橋・両岸・正逆周回・途中開始・帰還が成立し、反対岸の経路へ誤接続しないか。 | [IMP-ENV-004](../development/issues/IMP-ENV-004-river-loop-course.md) | |

### A-11 2026-10-04 の追加分 (仕上げ: おもちゃ・フリスビーの色・ギャラリー・洗いの写真)

> 2026-10-04 に実装し、Edit モードの検査と ClientSim 1 人のハーネスまで済んだ分。実機では未確認。値はどれも試作値なので、「近い / 遠い / 高い / 早い」などをそのまま書いてよい。

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| A0-44 | おもちゃ: 手元メニューでボール・フリスビーを呼ぶと胸の前に浮いて出て、かがまずに取れるか (位置は 前 0.45 m・下 0.35 m。近い / 遠い / 高い / 低い)。平地・登り坂・下り坂・川辺で落ちない・転がらないか。浮いているおもちゃに犬が反応しないか。 | [IMP-DOG-040](../development/issues/IMP-DOG-040-toy-recall-hover-in-reach.md) | |
| A0-45 | おもちゃ: 20 秒取らずにいると真下へ下り、坂でも転がらないか。続けて 2 つ呼ぶと重ならず横に並ぶか。壁際・木の前・門の角で呼んでも壁の向こうに出ないか。同じ犬のもう片方を呼ぶと、浮いている方がしまわれるか。 | [IMP-DOG-040](../development/issues/IMP-DOG-040-toy-recall-hover-in-reach.md) | |
| A0-46 | フリスビーの 6 色 (赤・桃・黄・青・橙・白) と顔が、PC でも青みの無い色で見えるか。この見た目でよいか (10-04 に材質を直して PC の見た目も変わった)。狙いの線の水色と、行けない時の灰色が見分けられるか。 | [BUG-ENV-001](../development/issues/BUG-ENV-001-mirror-default-materials.md) | |
| A0-47 | 写真の一覧: 手元メニューの写真の面で、一覧のまま VRChat カメラを向けた 1 枚だけが全画面になるか。隣の写真へ誤って吸着しないか (境目付近を狙う)。0.3 m で届くか・狙いやすいか。大きい表示・説明を開いている間は後ろの縮小表示に吸着しないか。2・3 ページ目でも吸着するか。縮小表示を押すと今までどおり大きく出るか。 | [IMP-WLD-009](../development/issues/IMP-WLD-009-menu-gallery-snap-from-thumbnails.md) | |
| A0-48 | 写真の面を開いても、開いている間に撮れても、「新しい」の札が出ないか。 | [IMP-WLD-010](../development/issues/IMP-WLD-010-menu-gallery-remove-new-mark.md) | |
| A0-49 | 洗いの写真: 1 匹を洗い終えた後、泡・すすぎ・洗い上がりの 3 枚がギャラリーに残っているか。構図 (肩越し・俯瞰・正面) に犬と自分が入り、カメラが板や壁に埋まっていないか。台を上げた時に、写真の中で犬が浮いて見えないか。洗った後に撫でる・遊ぶ・散歩しても、洗いの写真が消えないか。 | [IMP-WLD-011](../development/issues/IMP-WLD-011-wash-photos-kept.md) | |
| A0-51 | 動画カメラの操作 (PC): [近く] [遠く]・[↑] [↓]・[←] [→]・[向き▲] [向き▼]・[戻す] で構図が変わり、端で止まるか。1 回の動く量 (1.25 倍・0.3 m・20°・5°) と範囲でよいか。定点・通り過ぎ・ディスク・キャッチ待ちでは [近く]〜[→] が暗いか。撮り方が 10 種類 (10-08 の入れ替え後: 背中ごし・みちがあり、見下ろす・手元が無い) か。みちでは [←] [→] が暗く、一周では [←] [→] が速さになるか。縦回転は「上下の首振り」でよいか (ズームが要るか)。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md)、[IMP-WLD-012](../development/issues/IMP-WLD-012-video-camera-tps-and-path-moves.md) | |
| A0-52 | 位置を示すドローン (PC): モニターを開くと、カメラの位置をドローンが飛ぶか。動画には写らないか。形・大きさ・色でよいか、3 m 離れて向きが分かるか。[見せない] で消えるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| A0-59 | 撮影パターン (PC、10-08): モニターの右の「パターン」の板で 6 つを選べるか (押したパターンが光って下線、下の行に「(名前) 2/5 背中ごし」など)。**フリスビー 1** = 投げる前は自分 → 投げた瞬間にキャッチ地点の横 → キャッチの後は戻る犬に追従 → 受け渡しで自分へ戻る。**フリスビー 2** = キャッチの 1.5 秒後に自分の側の低い位置から、走って戻る犬を正面に見る (おかえり)。**散歩 Vlog 1 / 2・犬とあそぶ** = 散歩しながら表の順にカットが変わるか (長さ・順番・つながりが Vlog として見られるか)。**おまかせ** = リードなしで立つと自分 → 一周、犬をつなぐと散歩の流れ、洗い場で洗うと定点、投げるとすぐフリスビーの流れになるか。撮り方のボタン・◀ ▶・[とめる]・× で止まるか。カットの長さ (10〜30 秒) と、パターンの種類・説明でよいか | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md)、[設計 §17](../plans/2026-10-03-video-camera.md#17-撮影パターン-imp-wld-0082026-10-08) | |
| A0-50 | 写真の枠: 散歩 10・撫で 4・遊び 4・洗う 6 の入れ替わりの早さでよいか。撫でたときの連続撮影が 2 枚でよいか (前は 4 枚)。洗っている間に撫での写真が撮られていないか。 | [IMP-WLD-011](../development/issues/IMP-WLD-011-wash-photos-kept.md) | |
| A0-53 | 門前の配置 (10-06): スポーンで出た向き (北) で、犬が正面・左にリード台と説明板と看板 B・右におじいさんと看板 A が見えるか。おじいさんが道を塞がないか、看板 A の左を隠し過ぎないか。主洗い場が塀ぎわの平地に乗っているか (すのこ・台が浮かない・沈まない)。手元メニューの初回の位置 (ようこそ板) が前に出るか。 | [IMP-UI-002](../development/issues/IMP-UI-002-guide-board-capture-instructions.md) | |

### A-12 2026-10-07 の破壊監査 Phase A (洗い場 × ワープ・場所指定・じゃれ合い・写真・ライブ)

> 2026-10-07 の破壊監査 ([triage](../audits/2026-10-07/triage.md) §8) で Tier 1 を同日に実装した分。C# / U# コンパイルと Live 26a/26b Verify、Codex の独立レビューまで。ClientSim の対象ケースと実機は未確認。

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| A0-54 | 洗い場のボードのそばで自分の犬を [呼ぶ] → すぐ地図でワープ (または逆の順)。着いた後に犬が洗い場へ引き戻されず、そばにいるか。切符の間は呼ぶが灰色 (文言なし) になるか | [triage RC-1](../audits/2026-10-07/triage.md) | |
| A0-55 | 呼ぶ → ポーズ → 場所決めで川面の上にマーカーを置く。犬は岸で止まり、約 60 秒で枠が解けてリード・おもちゃが使えるか。地上の届く点なら解けないか | [triage RC-2](../audits/2026-10-07/triage.md) | |
| A0-56 | 1 匹を遠く (30 m 以上、または川向こう) でリードから外して放置。残りの犬がその犬のホームへ走って行かないか。門前の犬同士のじゃれ合いは今までどおり起きるか | [triage RC-5](../audits/2026-10-07/triage.md) | |
| A0-57 | ワープの暗転中にフリスビーのキャッチや受け渡しが起きても、黒い写真がアルバムに入らないか | [triage RC-9](../audits/2026-10-07/triage.md) | |
| A0-58 | ライブを開催し、曲が終わる (124 s) と同時に暗転・BGM 消音・犬の拘束が解けるか (11.9 秒の無音が残らないか)。60 秒後にまた開催できるか | [triage RC-13](../audits/2026-10-07/triage.md) | |


### A-13 2026-10-08 犬紹介パネルと毛色

> DogBook/0〜5 で板と毛色を構築してから (未構築の版では板が無い)。選べる毛色は今は柴とサモエドだけ (ほかはテクスチャの持ち込み待ち)。

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| A0-60 | 看板「遊び方」の隣 (広場の側) に、遊び方と同じ屋根つきの立て看板「裏山の犬たち」があり、その中に犬紹介の板があるか (見出しは看板の 1 つだけ)。スポーンから気づけて、近づいて押せる高さか。**周回の戻り道と犬の待機の輪に近い見込み (設計書 §3.3)**: 散歩の人と犬が看板の脇を通れるか、犬が柱をすり抜けて見えないか。**場所でよいか・規則の例外にするか (ユーザーが決める)** | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計](../plans/2026-10-08-dog-book-and-fur-color.md) §3 | |
| A0-61 | 札を押す → 詳細で呼び名を付けて [呼び名を保存]・[お気に入りにする]。入り直しても残るか。状態の 1 行がリード中は「散歩に出かけています」、洗い場・水の中もそれらしく出るか。お気に入りにした犬が 1 回あいさつに来るか (リード中・洗い場の犬は来ない)。立ち寄りが多すぎないか | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計](../plans/2026-10-08-dog-book-and-fur-color.md) §4 | |
| A0-62 | (マスター 1 人) 詳細の下段の毛色 [◀] [▶]: 6 匹とも色が変わり (コーギー 5・ポメ 4・ラブ 3・ダックス 3・柴 4・サモエド 2 の全 21 色)、一周して最初の色 (コーギーなら「茶白」) へ戻るか。模様がずれていないか (特に柴の「うす茶」)。色名が入江と同じで見た目と合うか。距離フェード (PC) が効いたままか、洗うと泡・汚れが乗るか | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計](../plans/2026-10-08-dog-book-and-fur-color.md) §5・7 | |

## B. PCVR 2 人以上 (81 項目)

ユーザー (マスター) と相手 1 人 (必要な項目は 3〜4 人)。相手は別アカウントで、Build & Test をし直した同じ版に入る。役割は「A = マスター、B = 非 Owner」。

### B-1 主洗い場

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| B1 | 相手のすすぎと、続いて流れる泡が、自分の画面にも同じように見えるか。「きれい」の合図と見た目の泡が食い違わないか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (すすぎ確認7) | |
| B2 | 犬の Owner でない人が操作しても、その人の手元で即座に泡と接触が返り、もう 1 人にも帯が見えるか。同じ犬を同時に洗え、泡付けとすすぎを分担できるか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 17・賭け 3)<br>[IMP-DOG-026](../development/issues/IMP-DOG-026-wash-state-and-sync.md)<br>[VER-DOG-002](../development/issues/VER-DOG-002-wash-experience-and-devices.md) | |
| B3 | 途中参加の人に、洗った跡がおおまかに見えるか。犬の Owner が替わったあとも状態が保たれるか。2 人の表示が一時的に違った時の見え方が許容できるか。 | [IMP-DOG-026](../development/issues/IMP-DOG-026-wash-state-and-sync.md) | |
| B4 | 相手の洗う音が、相手の手の位置から聞こえるか。近くで 2 人が洗い、BGM と重ねても、自分の操作の音と会話が聞き取れるか。 | [IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md)<br>[IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (音) | |
| B5 | 洗われ中の犬の反応 (ブルブル・満足の合図) が相手の画面でも見えるか。 | [IMP-DOG-028](../development/issues/IMP-DOG-028-wash-sound-and-reactions.md) | |
| B6 | 他人が連れている犬をボードで呼べないか。呼んだ人以外も「おわり」で解放できるか。2 人での呼び出しと解放が成立するか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) | |
| B7 | 台の高さが全員に見えるか。自分の床 (自分だけ) の上げ下げが、他の人から見て不自然でないか。 | [IMP-ENV-011](../development/issues/IMP-ENV-011-wash-height-adjust.md) | |
| B8 | 他人が連れている犬を一方的に拘束しないか。他人のストロークで、リード中の犬が止まらないか。リード付きで洗っている犬の停止と解除が、他の人にも同じ姿勢で見えるか。 | [IMP-DOG-027](../development/issues/IMP-DOG-027-wash-behavior-switching.md) | |

### B-2 リード・散歩

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| B9 | 相手から見て自分の首輪とリードが正しく見えるか。持ち手の左右が同じ向きに見えるか。 | [IMP-DOG-004](../development/issues/IMP-DOG-004-leash-models-release.md) (2 人)<br>[BUG-DOG-002](../development/issues/BUG-DOG-002-leash-handle-orientation.md) | |
| B10 | 他人がつないでいる犬・他人が引っ張っている時に、自分の手が震えないか。 | [IMP-DOG-007](../development/issues/IMP-DOG-007-leash-pull-haptics.md) | |
| B11 | 2 人が別々の行き先を選ぶ (A が広場、B が入口) とそれぞれの犬だけが従うか。A が途中で変えても B の犬は変わらないか。B が外して A が同じ犬をつなぐと A の選択に従うか。メニューの点灯が自分の選択か。マスター退出後も同じか。 | [BUG-DOG-004](../development/issues/BUG-DOG-004-destination-per-player.md) (再現手順 1〜5)<br>[IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) | |
| B12 | 相手の画面でも、待っている犬がこちらを見て止まり、伏せた犬が伏せて見えるか。途中参加者にも犬の目的地の状態が復元されるか。接続者・Owner が退出しても残った人の散歩が続くか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §2) | |
| B13 | 他人の犬が全力で走る様子が飛んで見えないか。嗅ぐ・座る姿勢が自分の画面でも同じ場所で出るか。途中参加者にも止まっている犬の姿勢が出るか。 | [IMP-DOG-009](../development/issues/IMP-DOG-009-walk-speed-tiers.md) (2 人以上)<br>[IMP-DOG-010](../development/issues/IMP-DOG-010-walk-stopovers.md) (2 人以上) | |
| B14 | 他の人の紐の見え方と、途中参加した人の紐が違和感なく動くか。 | [IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) (036 未確認分) | |
| B15 | 集合: A が集合中に B がその犬をつなげないか。B の接続犬は A の集合に来ないか。途中参加者に残り秒・参加数・犬の位置が復元されるか。開催者が退出すると終了するか。2 人が同じ拠点から道具を取っても互いの道具が消えないか。 | [DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) (集合計画 §2) | |
| B16 | 購入権: 古民家で買った人が裏山で集合を開始できるか。裏山で買って古民家でも使えるか。(実課金はユーザー判断) | [DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) (集合計画 §2) | |
| B17 | 初めて触る人が、リードの接続と解除を迷わず理解できるか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (完了条件) | |

### B-3 広場: フリスビー・ボール・おやつ・かざり・呼ぶ

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| B18 | 非マスターがディスクを持つと犬が待機位置へ行き、投げると追走・キャッチ・持ち帰りをして、両者から見えるか。マスターの投げも引き続き成立するか。 | [BUG-DOG-003](../development/issues/BUG-DOG-003-frisbee-non-master-throw.md) (完了条件) | |
| B19 | A と B が交互に持って投げても、所有権が替わったあとで犬が反応しなくならないか。 | [BUG-DOG-003](../development/issues/BUG-DOG-003-frisbee-non-master-throw.md) (完了条件) | |
| B20 | 犬の Owner でない人が投げても犬が取るか。2 人が別の犬のディスクを同時に投げても、それぞれ取るか。途中参加者の画面で 6 匹の追走・跳躍が滑らかか。 | [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md) (2 人)<br>[IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md) (2 人以上) | |
| B21 | 飛行・キャッチの成否・くわえた状態・着地が 2 人の画面で矛盾せず、途中参加でも保たれるか。 | [IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md) (2 人以上) | |
| B22 | 撫でる人が投げ手と別、投げ手が犬の Owner と別でも、持ち帰り後の撫で・待ちが同じ挙動か。 | [IMP-DOG-013](../development/issues/IMP-DOG-013-frisbee-pet-after-return.md) (複数人) | |
| B23 | 同じ犬のボールとディスクの切り替えで、消えた側が相手の画面でも消えているか。途中参加者にも同じか。かけっこ・集合のあとに各犬のボールが拾えるか。相手が持っているおもちゃを奪わないか。 | [IMP-DOG-019](../development/issues/IMP-DOG-019-ball-and-disc-placement.md) (2 人) | |
| B24 | おやつ: 4 人が同時に出すと 4 人目に枠の理由が出て在庫が減らないか。枠が空けば出し直せるか。 | [IMP-DOG-014](../development/issues/IMP-DOG-014-treats-port.md) (T-009 (3〜4 人)) | |
| B25 | かざり: 着けた飾りが他人の画面にも見えるか (途中参加を含む)。相手が着けた犬の同じ枠は 60 秒待たされて理由が出るか。着けた人が退室しても飾りが残るか。 | [IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) (2 人) | |
| B26 | 撫でエフェクト: 選んだ効果が相手の画面でも撫でたときに出るか。 | [IMP-DOG-015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) (2 人) | |
| B27 | 呼ぶ: 同じ犬を同時に選ぶと先着だけが予約できるか。呼んでいる犬に別の人がリードを付けると、リードが勝って呼ぶが解けるか。「呼ぶを全員に許可」を OFF にすると権限者以外が呼べないか。寝ている犬は OFF にしても起きないか。 | [IMP-DOG-016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) | |

### B-4 川

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| B28 | 泳いでいる犬 (浅瀬と泳ぎの境目を含む) の最中に C が途中参加。C の画面で最初から浮いた高さ・Walk で見えるか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #15) | |
| B29 | 泳いでいる最中に犬の Owner (A) が退出。B の画面で犬が沈まずに上がり、自由行動へ戻るか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #16) | |
| B30 | 水中の犬に A と B が同時にリード接続・おもちゃの呼び出しを試す。両方拒否され、犬が最初に届いた人を向いて上がるか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (川 #17) | |
| B31 | A が川へ投げて浮かせ、漂っている間に C が途中参加。C にも最初から水面に浮いて見え (沈まない・跳ねない)、漂流が止まる位置も A と同じか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #46) | |
| B32 | 水中で運ぶ間に C が途中参加。浮いた高さ・Walk・口のディスクが最初から見えるか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #44) | |
| B33 | ドボン・水中運搬・手元へ持ち帰る間に犬の Owner が退出。ディスクが浮いて残り、犬は岸へ上がるか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #45) | |
| B34 | ディスクの Owner (投げた A) が、(a) 浮いている間 (b) 飛行中に退出。(a) 残った人の端末で浮いたまま漂い、橋の帯の手前で止まるか。(b) 飛行は止まり、川の上なら浮き、陸なら落ちるか。宙に止まらないか。 | [IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) (川設計書 続き #47) | |
| B35 | 非 Owner が川へ投げて、犬が水中から持ち帰るか (所有権の本物の移譲)。非 Owner の投げ・Owner の退出で、犬とボールの所有権が往復しても破綻しないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (S1)<br>[IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md) | |
| B36 | 川の実機確認 4 件: 水遊び場の外の川に犬が入る自然な経路があるか / 山なりの投げ・非 Owner の投げで水中からの直接追跡が打ち切られないか / 通常の退出で犬ルートと Fetch の Owner が割れたままにならないか / 呼び寄せたおもちゃが別端末で滑って見えないか。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) (破壊監査 RC-10・18・19・20) | |
| B37 | 川の 2 人のその他の場面 (境目の帯での途中参加・Owner 退出・水中の犬への同時要求 = S2・S3・S5a、段階 6 の S1・S4・S5b) を確かめる。 | [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) | |

### B-5 地図・ワープ・合流・呼ぶ

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| B38 | B が犬 3 匹を連れて奥の広場へワープ。A の画面で B の犬がコースを横切らず B と一緒に現れるか。A の犬は動かないか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #2) | |
| B39 | B が [人] で A を選び [合流をたのむ]。A は [いいよ] / [いまはだめ] / 放置 20 秒 / [しばらく受けない] を試す。A の目の前に通知板が出て音が 1 回鳴るか。歩くと 3 m で置き直されるか。B に結果ごとの文言が出るか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #3) | |
| B40 | A が橋の上・川の中・歩行中・コースの外のときに B が合流。B が安全な点に着き、理由の文言が出るか。コースの外なら移動しないか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #4) | |
| B41 | B の申し込み中に A が退出。承認の直後 (暗転中) に A が退出。前者は「相手が退室しました」で移動せず、後者は取りやめて明るく戻るか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #5) | |
| B42 | 3 人: C がマスター。B が [ここへワープ] を押した直後に C が退出。B の移動が完了または取りやめになり、犬が往復しないか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #6) | |
| B43 | 3 人: B の移動の 5 秒後に D が途中参加。D の画面で B の犬が B のそばにいるか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #7) | |
| B44 | [人] の一覧を開いたまま人が出入り。並びが変わらず、出た人の行が消え、入った人が末尾に足されるか。受付 OFF の人が暗く「申し込みを受けていません」、入室直後が「準備中」か。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #8) | |
| B45 | 表示名の字: 日本語・英語 (Zoë・Łukasz など)・記号 (★♪) の人が □ にならず読めるか。簡体字・ハングル・絵文字の人は □ で出るか (対象外。記録だけ)。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #8b) | |
| B46 | A が B を呼び、B が [行く] (犬 3 匹)。役割を入れ替えても行う。[今は行かない] / 放置 20 秒 / [しばらく受けない] も試す。B の通知板が「A さんが呼んでいます」、B が暗転して A のそばへ犬 3 匹も一緒に着き、A は動かないか。「着きました」が移動のあとに出るか。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md) (呼ぶ F.7 #10) | |
| B47 | B が水の中・橋の上・犬がディスクを追っている最中に [行く]。B が安全な点に着き、ディスク中は始まらず理由が出て、戻ってからもう一度押せるか。陸でつないだ犬が濡れてから [行く] を押した場合の同行も見る。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md) (呼ぶ F.7 #11・C7-wet) | |
| B48 | A が返事待ちで [取り消す]。B が [行く] を押した直後 (切符待ち・暗転中) に A が [取り消す]。前者は B の通知板が閉じ、後者は移動の前なら B が明るく戻って「A さんが取り消しました」、移動の後なら B が着くか。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md) (呼ぶ F.7 #12) | |
| B49 | B が合流を申し込み、取り消してすぐ呼ぶ。A と B が同時に互いを呼ぶ。通知板が片方にだけ出て、もう片方に「相手からの申し込みを先に表示します」が出るか。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md) (呼ぶ F.7 #13) | |
| B50 | 3 人: C がマスター。A が B を呼び、B が [行く] を押した直後に C が退出。別に B の移動の 5 秒後に D が途中参加。B の移動が完了か取りやめになり、A の表示が「来ます」のまま 20 秒を超えず、D の画面で B の犬が B のそばにいるか。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md) (呼ぶ F.7 #14) | |
| B51 | B が「合流・呼び出しを受ける」を OFF にすると、A の 2 つのボタンが暗く「申し込みを受けていません」になるか。ON に戻して呼ばれると手元メニューの印が「呼ばれています」になるか。長い言語 (de・ru・fr) でボタンの字が収まるか。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md) (呼ぶ F.7 #15) | |

### B-6 設定・ログ

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| B52 | 許可 2 種 (呼ぶ / ライブ開催を全員に許可) が初期 ON で、権限者 (インスタンスオーナー、不在時はマスター) 以外は切り替えられないか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §4) | |
| B53 | 遊び終わったあと、VRChat のログで「Attempted to steal ownership of a held pickup.」の有無と時刻を見る。あれば、その時刻に自分のログに `setowner` の行があるか、相手のログにも同じ時刻の行があるかを比べる。 | [BUG-DOG-006](../development/issues/BUG-DOG-006-steal-held-pickup.md) | |

### B-7 2026-10-02〜03 の追加分

| # | やること・見ること | 元 | 結果 |
| B58 | 2 人で、片方が板「古民家ドッグラン」を押してポータルを出す。もう片方の画面にはポータルが見えないか。あとから入った人の画面にポータルが出ていないか。ポータルに入ったのは入った本人だけが移動するか。 | [IMP-WLD-006](../development/issues/IMP-WLD-006-farmhouse-portal-board.md) | |
| B59 | 洗い場のボードの表示と受理が 2 人で一致するか。相手が連れている犬は「ほかの人と散歩中」と出て押しても来ないか。ボードを見たあとで相手が先にリードを付けた・呼んだ場合に、押した側へ理由が返るか。相手が洗っている犬は少し待ってから来るか。 | [IMP-DOG-037](../development/issues/IMP-DOG-037-wash-call-availability-and-range.md) | |
| B60 | 犬の Owner ではない側の端末のトレーナーが、「フリスビーの取り方」を 1.5 秒以内に 2 回押す。0 → 3 → 4 と 2 つ進むか。トレーナーでない人が押しても全員の犬の取り方が変わらないか。 | [BUG-DOG-008](../development/issues/BUG-DOG-008-frisbee-style-double-press.md)<br>[BUG-DOG-009](../development/issues/BUG-DOG-009-frisbee-style-receiver-check.md) | |
| B61 | 2 人が別々に地図から同じ地点 (例: 広場) へワープして、着地点が重ならず、犬も並べるか。ワープ先の洗い場・看板・ポータルの近くに着かないか。 | [IMP-WLD-004](../development/issues/IMP-WLD-004-warp-all-five-points.md) | |
| B62 | 2 人が同時におやつを出す。それぞれ自分の接続犬だけが来て、相手の犬は来ないか。待つ間にマスターが替わる・途中参加が入ったとき、待ちが消えるか。呼んだ順の記録が消えて次の 1 回だけ最寄りに戻っても不自然でないか。 | [IMP-DOG-006](../development/issues/IMP-DOG-006-leashed-treat-approach.md) | |
| B63 | 2 人で、他人の [曲げる] で自分の投げが曲がらないか (曲げは投げた人の投げだけ)。トレーナーが取り方を変えたとき、他人の画面の「全部の犬」の副文が追従するか。トレーナーでない人の取り方の変更 (偽装を含む) が実通信で弾かれるか。 | [DES-UI-002](../development/issues/DES-UI-002-frisbee-settings.md)<br>[BUG-DOG-009](../development/issues/BUG-DOG-009-frisbee-style-receiver-check.md) | |
| B64 | ボールの柄: 片方が柄を買って選ぶと、相手の画面でもボールの柄が変わるか。途中参加の人が同じ柄を見るか (Owner の所有が届くまでは基本ボール)。柄を選んだ人の退室、2 人がほぼ同時に違う柄を選んだときの最後の 1 つへのそろい方。 | [DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md) | |
| B65 | 自動写真: 集合写真が各自の端末で撮れるか。フリスビーのキャッチが他人の端末でも撮れるか (同期された state のエッジが届くか)。写真は各自の端末だけにあり、相手に共有されないか。 | [IMP-WLD-005](../development/issues/IMP-WLD-005-auto-photo-and-gallery.md) | |
| B66 | 場所を決めて呼ぶ: 片方がマーカーを手の高さに置いて犬を呼んだとき、相手の画面でも犬が同じ高さに浮いて寝るか。起こしたあと両画面で犬が地面へ戻るか。 | [IMP-DOG-032](../development/issues/IMP-DOG-032-call-placement-free-height.md) | |
| B67 | 手洗いの 2 人: A がボトルで泡をつけ、B が素手で広げる (逆も)。相手の画面でも泡が増えて見えるか、遅れ・食い違い (自分の画面だけ泡が濃い) がどの程度か。片方がシャワーですすいだ所を、もう片方が手でこすっても泡が戻らないか。B (非 Owner) の手洗いで犬が洗い場に留まり続け、B が離れると解放されるか。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
| B68 | 手洗いの途中参加と Owner 交代: 手洗い中に 3 人目が入室して泡が同じに見えるか。犬の Owner が退出して交代したあと、泡が大きく減ったり増えたりしないか (交代の瞬間の他人の泡立て 1 秒未満ぶんは落ちうる)。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
|---|---|---|---|
| B54 | 他人が散歩中の犬の首輪に、自分側で「外す」が出ないか。自分が散歩中の犬には出るか。リードの持ち主交代・退出・途中参加のあとも切り替わりが追従するか。 | [IMP-DOG-031](../development/issues/IMP-DOG-031-collar-release-interact-only-own-walk.md) | |
| B55 | オートナイトモード: 2 人が同じ暗さで進むか。途中参加の人が 3 秒ほどで現在の暗さへ合流するか。最初の人が抜けても夜が昼へ巻き戻らないか。オートの人とマニュアルの人が混ざっても互いに影響しないか。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md) | |
| B56 | 2 人で川沿いにいても、せせらぎが各自の位置で自然に聞こえ (相手の位置に引きずられず)、重なって大きくならないか。 | [IMP-ENV-006](../development/issues/IMP-ENV-006-river-ambience.md) | |
| B57 | 犬の Owner と洗い場の Owner が別の人のとき、「おわり」で犬が台から出ていく動きと、台が下りる見え方が両者の画面で同じか。呼ばれていない他人の犬が主洗い場に寄らないか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md) | |

### B-8 2026-10-04 の追加分 (おもちゃ・洗いの写真・他の人を撮る許可)

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| B69 | おもちゃ: 呼んだおもちゃが相手の画面でも同じ所に浮いて見えるか。途中参加の人にも同じか。呼んだ人が抜けたら地面へ落ちるか (宙に残らない)。浮いているおもちゃを相手が取れるか。犬の持ち主と球の持ち主が別の人のとき、犬が水中で追っている球を呼んでも、取る前に落ちないか。 | [IMP-DOG-040](../development/issues/IMP-DOG-040-toy-recall-hover-in-reach.md) | |
| B70 | 洗いの写真: 洗っている人の端末でだけ撮れ、見ているだけの人の端末では撮れないか。2 人で 1 匹を洗うと、それぞれの端末で撮れるか。 | [IMP-WLD-011](../development/issues/IMP-WLD-011-wash-photos-kept.md) | |
| B71 | 他の人を撮る (撮る人 = PC): モニターの人の一覧で相手を押すと「(相手) さんに撮影の許可を依頼しますか?」が出るか。[やめる] で相手に何も届かないか。[依頼する] で相手の目の前に撮影の通知が出るか。[いいよ] の前はモニターに相手の追従が出ず (犬が映る)、[いいよ] の後に相手を追って撮れるか。相手のさんぽタブに [撮影を許可中] が出て、開くと「撮影中」になるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| B72 | 他の人を撮る: 相手の [やめてもらう] で、すぐ犬の構図へ戻り、相手の映像がモニターと録画の板に残らないか (ワープの暗転中も)。[いまはだめ]・20 秒の時間切れ・× で閉じる・退室のそれぞれで止まるか。別の申し込みが保留中でも、[撮影を許可中] から止められるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| B73 | 他の人を撮る: 2 人から同時に申し込まれたとき、撮影と合流・呼ぶが行き違ったときに、どちらかが残って固まらないか。「撮影の申し込みを受ける」を OFF にすると断られ、入り直しても OFF のままか。通知の「この人は退室まで受けない」が、その人からの次の申し込みを止め、文言が枠に収まっているか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| B75 | ドローン (2 人): 相手を許可つきで撮ると、相手の画面にドローンが見えて、カメラに合わせて追ってくるか。第三者の画面には見えないか。撮る人が [見せない] にすると相手の画面からも消えるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| B76 | ドローン (2 人): 相手が [やめてもらう]・撮る人が × で閉じる・退室・対象を変える、のそれぞれで相手の画面から消えるか。カメラが相手の顔の 1 m 以内に来ると隠れるか (犬目線・手元)。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| B74 | 合流・呼ぶが今までどおり動くか (申し込み・[いいよ]・ワープ・取り消し・時間切れ)。撮影の許可を足したことで変わっていないか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |

### B-9 2026-10-07 の破壊監査 Phase A (2 人)

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| B77 | 相手が洗っている最中の犬がいる洗い場で、自分が別の犬を [呼ぶ]。相手の犬が追い出されず、「ほかの人が洗っています」で待たされるか (5 秒で断られるか) | [triage RC-8](../audits/2026-10-07/triage.md) | |
| B78 | 相手の手からスポンジを受け取ってすぐ犬をこする (棚から取ってすぐも)。最初のひとこすりが相手の画面にも出るか (自分の画面にだけ残らないか) | [IMP-DOG-026](../development/issues/IMP-DOG-026-wash-state-and-sync.md) | |
| B79 | 自分 (マスター) が洗い場の犬を汚れさせている最中に退室。残った相手の画面で汚れが途切れず、その後に入った 3 人目にも同じ汚れが見えるか | [IMP-DOG-026](../development/issues/IMP-DOG-026-wash-state-and-sync.md) | |
| B80 | 相手の犬を自分がリードでつないだまま、相手が洗い場のボードでその犬を呼ぶ → 拒否されるか。自分が切符を持っている間に相手が自分の犬を呼ぶ → 相手には呼べるか (呼ぶ人の切符だけを見る) | [triage RC-1](../audits/2026-10-07/triage.md) | |


### B-10 2026-10-08 毛色 (2 人以上)

> A = マスター (最初に入る人)、B = あとから入る人。上から順に (Codex の相談の結論: ハーネスは回さず、権限・交代・途中参加・見た目は 2 人の実機で見る)。

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| B81 | **単独開始 → 通常同期**: A が 1 人で何匹かの色を変える → B が入る。B は何もしないで同じ色か。A がもう一度変える・最初の色へ戻す → B に届くか。触っていない別の犬は変わらないか | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計 §9.3](../plans/2026-10-08-dog-book-and-fur-color.md#93-2-人で見る順番-チェックリスト-b81b85) | |
| B82 | **権限と交代**: B の板には [◀] [▶] が無く、色名と「毛色はインスタンスのマスターが決めます」だけか。A が退出 → 色はそのままで、B に [◀] [▶] が出て変えられるか。A が入り直しても A に [◀] [▶] が戻らないか | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計 §9.3](../plans/2026-10-08-dog-book-and-fur-color.md#93-2-人で見る順番-チェックリスト-b81b85) | |
| B83 | **未受信での交代**: B が入った直後 (読み込み中) に A が退出。初期の同期が終わるまで B の [◀] [▶] が出ず、受け取った後に出るか。触っていない犬がもとの色へ戻らないか | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計 §9.3](../plans/2026-10-08-dog-book-and-fur-color.md#93-2-人で見る順番-チェックリスト-b81b85) | |
| B84 | **描画の共存**: 距離フェードを別の設定 (深め・カスタム) にしてから色を変える / 色を変えてからフェードを変える (両方の順)。柴の模様がずれない、サモエドの目・口・歯が黒くならない、洗っている間に泥と泡が乗る、もとの色へ戻る | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計 §9.3](../plans/2026-10-08-dog-book-and-fur-color.md#93-2-人で見る順番-チェックリスト-b81b85) | |
| B85 | **Greeting と個人設定**: お気に入りの犬がリード中・洗い場に呼ばれている間はあいさつが始まらないか。あいさつの途中でリードを付ける・洗い場に呼ぶと止まるか。呼び名の保存・お気に入りの切り替えで毛色が変わらないか。呼び名・お気に入りは人ごとか (相手の板に自分の呼び名が出ない)。Android (Quest) なら、マスターのアプリをバックグラウンドへ移したときの交代 (色が残る・残った人が変えられる) も | [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)、[設計 §9.3](../plans/2026-10-08-dog-book-and-fur-color.md#93-2-人で見る順番-チェックリスト-b81b85) | |

## C. Desktop (24 項目)

Desktop (PC 画面、マウスとキーボード) で 1 人。座位・低い視点が関わる項目もここで見る。

### C-1 主洗い場・リード

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| C1 | スポンジは Use を押している間、画面の中心の先へ効くか。シャワーは向けた先へ当たるか。 | [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (表 16) | |
| C2 | ボードのボタン (犬を呼ぶ・おわり・台の ▲▼・自分の ▲▼) を押せるか。台と自分の高さで犬が洗いやすいか。ボードの文字が読めるか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md)<br>[IMP-ENV-011](../development/issues/IMP-ENV-011-wash-height-adjust.md) | |
| C3 | ラックの板・看板を Interact でき (距離 2.5 m)、ラベルが読めるか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (散歩計画 §3) | |
| C4 | リードの先を、接続位置 (犬の首輪) まで運んでつなげるか。新しい操作で既存の撫で・ボールを邪魔しないか。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (手順 7)<br>[DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) (端末別入力) | |
| C5 | 首輪・リードの見た目が Desktop で悪くなっていないか。 | [BUG-DOG-002](../development/issues/BUG-DOG-002-leash-handle-orientation.md) | |

### C-2 散歩・広場

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| C6 | 3 匹・6 匹で散歩し、犬が速くても自然についてきて、寄り道・立ち寄りが破綻しないか。 | [IMP-DOG-009](../development/issues/IMP-DOG-009-walk-speed-tiers.md) (Desktop)<br>[IMP-DOG-010](../development/issues/IMP-DOG-010-walk-stopovers.md) (Desktop) | |
| C7 | 手元メニュー: T で開閉し、マウスで押せるか。行き先・つなぐ・外すが動くか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §1)<br>[DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) (集合計画 §1) | |
| C8 | 場所を決めて呼ぶ: 斜面ではクリックしても確定できず、文言が頭の前で読めるか。平らな場所では成立するか。 | [IMP-DOG-016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) | |
| C9 | 立位・座位・低い視点で、犬の様子と操作のしやすさに差がないか (犬を追って姿勢を変え続ける必要がないか)。 | [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (完了条件) | |

### C-3 地図・設定

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| C10 | [地図と移動] から奥の広場・川辺へワープ。暗転が画面で黒くなり、地図が読めるか。 | [IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) (地図設計書 #1a) | |
| C11 | 距離フェード (PC) を変えて保存でき、入り直すと戻るか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (T-011) | |
| C12 | BGM の音量スライダー・ON/OFF が Desktop でも操作でき、表示と出音が一致するか。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |

### C-4 2026-10-02〜03 の追加分

| # | やること・見ること | 元 | 結果 |
| C16 | 門の西の板「古民家ドッグラン」をクリックでポータルを出し・しまえるか (遠くから押せる距離か)。文字が読めるか。 | [IMP-WLD-006](../development/issues/IMP-WLD-006-farmhouse-portal-board.md) | |
| C17 | 手元メニューの地図で、1〜5 の地点をクリックで選んでワープできるか。一覧の文字が収まり、6 の下流の橋は選べないか。 | [IMP-WLD-004](../development/issues/IMP-WLD-004-warp-all-five-points.md) | |
| C18 | 洗い場のボードで、犬ごとの「呼べます」と理由の文字が読めるか。呼べない犬をクリックすると理由が出るか。 | [IMP-DOG-037](../development/issues/IMP-DOG-037-wash-call-availability-and-range.md) | |
| C19 | さんぽタブが 2 列でスクロール無しか。左右の [左につなぐ] [右につなぐ]・[はずす]・行き先 6 タイル (2 列 × 3 段) をクリックで押せ、文字が収まるか。 | [DES-UI-006](../development/issues/DES-UI-006-walk-menu-html-layout.md) | |
| C20 | 場所を決めて呼ぶのマーカーが頭の前の距離にそのまま出て、斜面でも確定でき、赤い印が出ないか。 | [IMP-DOG-032](../development/issues/IMP-DOG-032-call-placement-free-height.md) | |
| C21 | ヘッダーの [写真] からギャラリーを開け、設定の [写真: ON / OFF] が押せるか。門の看板 A の隣の写真ボードをクリックで拡大できるか。 | [IMP-WLD-005](../development/issues/IMP-WLD-005-auto-photo-and-gallery.md) | |
| C22 | Desktop の手洗い: 図解の②が「スポンジでごしごし」の絵と文になっているか。ボトルは持って Use で視線の先 (2 m まで) に泡がつくか。スポンジで広げられるか (素手の泡立ては Desktop には無い)。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
|---|---|---|---|
| C13 | 散歩していない犬を撫でる・洗うとき、首輪の「外す」に操作を取られないか。散歩中の犬では首輪から外せるか。 | [IMP-DOG-031](../development/issues/IMP-DOG-031-collar-release-interact-only-own-walk.md) | |
| C14 | 設定のナイトモードの [オート][マニュアル] とスライダーをマウスで操作でき、文字が収まるか。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md) | |
| C15 | 入口の看板 A・B の文字が、Desktop の画面で読める距離と大きさか (立位・座位の低い視点、昼とナイトモード)。 | [DES-UI-004](../development/issues/DES-UI-004-walk-destinations-and-boards.md) | |

### C-5 2026-10-04 の追加分

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| C23 | おもちゃ: 呼ぶと画面の中の取れる位置 (前 0.8 m・少し下) に浮いて出て、そのまま取れるか。 | [IMP-DOG-040](../development/issues/IMP-DOG-040-toy-recall-hover-in-reach.md) | |
| C24 | 写真の一覧の吸着が Desktop のカメラで 0.3 m に届くか。撮影カメラの人の一覧・確認・[取り消す] がマウスで押せるか。 | [IMP-WLD-009](../development/issues/IMP-WLD-009-menu-gallery-snap-from-thumbnails.md)・[IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |

## D. Quest 3 単体 (33 項目)

Quest 3 単体で、PCVR と同じコースを 1 周する。結果欄には OS・VRChat の版・画質・アバター表示設定・発熱の状態も添える。fps は VRChat のデバッグ表示で読む。

### D-1 入室・負荷

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| D1 | 入室直後と 6 匹リード散歩のときの fps を書く。6 匹が同時に川へ入ったときも書く。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#6)<br>[IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) | |
| D2 | 地形が変に光らなくなったか (Lite の地形に差し替えた版で)。 | [IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) (完了条件) | |
| D3 | 地形の縁・遠景の継ぎ目 (南東・南西の隅)、柵・斜面の外への落下が、PCVR と同じか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#7) | |
| D4 | 1024 に落とした樹皮・古民家・空の見た目に問題がないか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#8) | |

### D-2 散歩・メニュー・空

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| D5 | リード散歩: 先端の取り出し・首輪への接続・解除、片手 3 匹・合計 6 匹、接続中の追従が成立するか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#1) | |
| D6 | リードで引っ張られると手が震えるか。 | [IMP-DOG-007](../development/issues/IMP-DOG-007-leash-pull-haptics.md) | |
| D7 | 首輪・リードを明所・木陰で見て、動作中のずれ・持ち手の向き・材質の破綻がないか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#5)<br>[IMP-DOG-004](../development/issues/IMP-DOG-004-leash-models-release.md) | |
| D8 | 手元メニューを開く操作が届くか。文字が読めるか。3 タブ (さんぽ・あそぶ・おもちゃ) が押せるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#2) | |
| D9 | 空とナイトモード: 夜の乗算オーバーレイが出るか。暗さが PCVR と同程度か。空の見え方に問題がないか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#3) | |

### D-3 洗浄

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| D10 | 洗浄でマスクへ描き続けられ、泡・汚れの内容が保たれるか。描けない・消える・負荷が高い場合は症状を書く。 | [VER-DOG-002](../development/issues/VER-DOG-002-wash-experience-and-devices.md)<br>[IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (賭け 3) | |
| D11 | 6 匹・複数の洗い場がある状態の fps と、洗浄音・BGM を重ねたときの聞き取りやすさ。 | [VER-DOG-002](../development/issues/VER-DOG-002-wash-experience-and-devices.md)<br>[IMP-DOG-024](../development/issues/IMP-DOG-024-walk-and-wash-experience.md) (負荷・音) | |

### D-4 広場・川

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| D12 | フリスビー: 照準線と着地マーカーが両眼で見えるか。持ち中の半透明のディスクが出るか。強さの右スティック上下が効くか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#4)<br>[IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md) | |
| D13 | 6 枚のディスクの色が見分けられるか。顔の印字が潰れていないか。 | [BUG-DOG-005](../development/issues/BUG-DOG-005-ios-disc-colors.md)<br>[IMP-DOG-018](../development/issues/IMP-DOG-018-frisbee-dog-face.md) | |
| D14 | 6 匹が道の斜面で同時にディスクを追うとき、水中を追う (ドボン・浮いたディスク) ときの fps。照準の × と「犬はここへ行けません」が両眼で見えるか。メニューの「ここでは不可」が読めるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#12)<br>[IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) | |
| D15 | 川: 不透明な水面の上で、泳ぐ小型犬 (頭だけが出る) と浅瀬の大型犬が、通常の目線と撫でる低い目線で見分けられるか。しぶき・波紋が両眼で出るか (ピンク・片眼欠落がない)。夜でも見分けられるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#10)<br>[IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) | |
| D16 | 6 匹が同時に水遊びしているときの fps。泳いでいる犬がいるときに途中参加した人の画面で、最初から泳ぎの姿勢で見えるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#11)<br>[IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) | |

### D-5 地図・BGM・周回

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| D17 | 地図・ワープ: 暗転の板が両眼で黒くなるか。通知板が読めるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#9)<br>[IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) | |
| D18 | BGM: 圧縮後の余韻・継ぎ目・会話や犬とのバランスに問題がないか。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |
| D19 | 周回の実歩行・景色が不意に消えないか・発熱。可能なら 16 人・犬 6 匹の条件で、60〜120 分続けた状態も記録する。 | [IMP-ENV-004](../development/issues/IMP-ENV-004-river-loop-course.md)<br>[DES-ENV-002](../development/issues/DES-ENV-002-longer-course.md)<br>[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) | |

### D-6 2026-10-02〜03 の追加分

| # | やること・見ること | 元 | 結果 |
| D23 | 門の西の板「古民家ドッグラン」を押してポータルが出し・しまえるか。文字が Quest 3 で読めるか。 | [IMP-WLD-006](../development/issues/IMP-WLD-006-farmhouse-portal-board.md) | |
| D24 | 地図の 1〜5 の地点をコントローラーで選んでワープできるか。ワープ後に fps が極端に落ちないか。 | [IMP-WLD-004](../development/issues/IMP-WLD-004-warp-all-five-points.md) | |
| D25 | 洗い場のボードの犬ごとの状態の文字が Quest 3 で読めるか。呼べない犬を押すと理由が出るか。 | [IMP-DOG-037](../development/issues/IMP-DOG-037-wash-call-availability-and-range.md) | |
| D26 | 自動写真: 撮影の一瞬の引っかかり (fps・発熱) が気にならないか。ヘッダーの [写真] とボード (640×360) が Quest 3 で読めるか。VRChat カメラを寄せた全画面化が動くか。 | [IMP-WLD-005](../development/issues/IMP-WLD-005-auto-photo-and-gallery.md)<br>[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) | |
| D27 | さんぽタブの 2 列 (左右の 128×52・行き先タイル) がコントローラーで狙えるか。おもちゃタブ上段の [まっすぐ] [曲げる] とボールの柄が読めて押せるか。 | [DES-UI-006](../development/issues/DES-UI-006-walk-menu-html-layout.md)<br>[DES-UI-002](../development/issues/DES-UI-002-frisbee-settings.md)<br>[DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md) | |
| D28 | Quest の手洗い: コントローラー / ハンドトラッキングの手で泡立てられるか。洗い場で「手洗い無し / 片手 / 両手」の fps の差 (平常 50・混雑 40 を下げないか)。図解の絵と文字が読めるか。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
|---|---|---|---|
| D20 | オートナイトモード: 暗くなる様子と暗さ 0.8 の見やすさが PCVR と同程度か。設定の [オート][マニュアル] が押せ、文字が収まるか。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md)<br>[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) | |
| D21 | 川のせせらぎが聞こえ、音量・継ぎ目が問題ないか。入れたことで fps・発熱が変わらないか (Android の容量は AI が測る)。 | [IMP-ENV-006](../development/issues/IMP-ENV-006-river-ambience.md) | |
| D22 | 入口の看板 A・B の文字が Quest 3 の解像度で読めるか (圧縮でにじまないか)。 | [DES-UI-004](../development/issues/DES-UI-004-walk-destinations-and-boards.md) | |

### D-7 2026-10-04 の追加分

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| D29 | フリスビーの 6 色と顔が見えるか (10-04 の報告は真っ黒)。狙いの線の色が出るか。首輪とリード本体 (別の材質で、10-04 は変えていない) が黒くないか。 | [BUG-ENV-001](../development/issues/BUG-ENV-001-mirror-default-materials.md) | |
| D30 | おもちゃ: 呼ぶと胸の前に浮いて、取れるか。 | [IMP-DOG-040](../development/issues/IMP-DOG-040-toy-recall-hover-in-reach.md) | |
| D31 | 写真の一覧のまま、VRChat カメラを向けた 1 枚が全画面になるか。 | [IMP-WLD-009](../development/issues/IMP-WLD-009-menu-gallery-snap-from-thumbnails.md) | |
| D32 | 撮られる側として: 撮影の通知に [いいよ] [いまはだめ] で答えられ、[撮影を許可中] から [やめてもらう] が押せるか。文言は日本語のまま出る (13 言語は未対応)。撮られている間、ドローンが見えるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |
| D33 | 負荷の計測 (計測用のボタンは 2026-10-06 に撤去。読むときは Mobile/20 で置き直す): スポーン右横の計測用のボタンで、NewGrass / NewBoards / NewOther / Props / DogUdon を 1 つずつ OFF にして、川沿いほかで fps を読む (手順と記入の表は Issue)。 | [VER-ENV-002](../development/issues/VER-ENV-002-scenery-performance.md) | |

## E. iOS (iPhone 15 Pro) (30 項目)

iPhone 15 Pro で、Touch 操作を中心に確認する。結果欄には iOS・VRChat の版・画質・発熱の状態も添える。

### E-1 入室・操作

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| E1 | 入室直後と 6 匹散歩のときの fps を書く。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#6) | |
| E2 | リード散歩: 先端の取り出し・接続・解除が Touch でできるか。接続位置まで先端を運べるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#1)<br>[IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) (手順 7) | |
| E3 | 手元メニュー: ランチャーの [メニュー] で開けるか (VR では出ない)。横画面で上端中央、縦画面で右中央に出て、画面を回しても崩れないか。3 タブが押せて読めるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#2)<br>[IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (メニュー計画 §1) | |
| E4 | 設定面の「距離フェード (PC のみ)」が見えて、押せないか。 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (T-011) | |
| E5 | 空とナイトモードの乗算オーバーレイが出るか。暗さが PCVR と同程度か。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#3) | |
| E6 | 首輪・リードの見え方と、動作中のずれに問題がないか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#5)<br>[IMP-DOG-004](../development/issues/IMP-DOG-004-leash-models-release.md) | |
| E7 | 地形の縁・遠景の継ぎ目、斜面の外への落下が PCVR と同じか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#7) | |

### E-2 洗浄

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| E8 | Touch でスポンジ・シャワーを狙った所へ当てられ、泡・汚れが変わるか。マスクへ描き続けられ、内容が保たれるか。 | [VER-DOG-002](../development/issues/VER-DOG-002-wash-experience-and-devices.md)<br>[IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md) (賭け 3) | |
| E9 | 洗い場のボード (犬を呼ぶ・おわり・台の ▲▼・自分の ▲▼) がスマホで押せるか。文字が読めるか。 | [IMP-DOG-035](../development/issues/IMP-DOG-035-wash-call-dog-to-station.md)<br>[IMP-ENV-011](../development/issues/IMP-ENV-011-wash-height-adjust.md) | |

### E-3 広場・川

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| E10 | フリスビー: 照準線と着地マーカーが見えるか。右側のバーチャルスティックが強さ (上下) として効くか (効かなければ、仰角だけ・強さ固定にする判断が要る)。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#4)<br>[IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md) | |
| E11 | 6 枚のディスクの色が iOS 実機でも見分けられるか (同じ色に見えないか)。顔の印字が潰れていないか。 | [BUG-DOG-005](../development/issues/BUG-DOG-005-ios-disc-colors.md)<br>[IMP-DOG-018](../development/issues/IMP-DOG-018-frisbee-dog-face.md) | |
| E12 | 照準の × と「犬はここへ行けません」、メニューの「ここでは不可」が見えて読めるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#12)<br>[IMP-DOG-023](../development/issues/IMP-DOG-023-frisbee-anywhere.md) | |
| E13 | 川: 泳ぐ小型犬と浅瀬の大型犬が見分けられるか。しぶき・波紋が出るか (ピンクがない)。夜でも見分けられるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#10)<br>[IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) | |
| E14 | タッチでリードを外して水遊びを始められ、案内の一言が読めるか。泳いでいる犬がいるときに途中参加した人の画面で最初から泳ぎの姿勢で見えるか。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#11)<br>[IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) | |

### E-4 地図・呼ぶ・BGM

| # | やること・見ること | 元 | 結果 |
|---|---|---|---|
| E15 | 地図・ワープ: [地図と移動]・一覧の行・[ここへワープ]・通知板の 4 つのボタンを押し分けられるか。通知板と Focus View が重ならないか。暗転が画面で黒くなるか。表示名の欠けた字 (□) が許容範囲か。 | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#9)<br>[IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) | |
| E16 | 呼ぶ・合流の 2 つのボタンと、通知板の [行く] [今は行かない] を押し分けられるか。 | [IMP-WLD-002](../development/issues/IMP-WLD-002-call-player.md)<br>[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) (#9) | |
| E17 | BGM の音量操作と聞こえに問題がないか。 | [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | |

### E-5 2026-10-02〜03 の追加分

| # | やること・見ること | 元 | 結果 |
| E21 | 門の西の板「古民家ドッグラン」をタップしてポータルが出し・しまえるか。文字が iPhone の画面で読めるか。 | [IMP-WLD-006](../development/issues/IMP-WLD-006-farmhouse-portal-board.md) | |
| E22 | 地図の 1〜5 の地点をタップで選んでワープできるか。一覧の文字が収まるか。 | [IMP-WLD-004](../development/issues/IMP-WLD-004-warp-all-five-points.md) | |
| E23 | 洗い場のボードの犬ごとの状態の文字が読め、呼べない犬をタップすると理由が出るか。 | [IMP-DOG-037](../development/issues/IMP-DOG-037-wash-call-availability-and-range.md) | |
| E24 | さんぽタブの 2 列 (スクロール無し) と行き先 6 タイル (2 列 × 3 段) をタップで押せるか。スクロールのドラッグが無くなったことで誤操作がないか。文字が収まるか。 | [DES-UI-006](../development/issues/DES-UI-006-walk-menu-html-layout.md) | |
| E25 | 自動写真: ヘッダーの [写真] とボードをタップで開け、全画面化が切れていて (BUG-CAM-003) 画面端の [メニュー] が隠れないか。写真が 640×360 で撮れるか。 | [IMP-WLD-005](../development/issues/IMP-WLD-005-auto-photo-and-gallery.md)<br>[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) | |
| E26 | スマホの手洗い: 図解の②がスポンジ版か。ボトルは持って Use で泡がつくか、スポンジで広げられるか。図解が画面で読めるか。 | [IMP-DOG-039](../development/issues/IMP-DOG-039-hand-wash-with-shampoo-bottle.md) | |
|---|---|---|---|
| E18 | 設定のナイトモードの [オート][マニュアル] とスライダーをタッチで押せ、文字が収まるか。暗くなる様子と見やすさが PCVR と同程度か。 | [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md)<br>[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) | |
| E19 | 川のせせらぎが iOS でも聞こえ、音量・継ぎ目が問題ないか。 | [IMP-ENV-006](../development/issues/IMP-ENV-006-river-ambience.md) | |
| E20 | 入口の看板 A・B の文字が iPhone の画面で読めるか。 | [DES-UI-004](../development/issues/DES-UI-004-walk-destinations-and-boards.md) | |

### E-6 2026-10-04 の追加分

| # | 見ること | Issue | 結果 |
|---|---|---|---|
| E27 | フリスビーの 6 色と顔が見えるか (9/26 の体験会では「色が全部同じ」)。 | [BUG-ENV-001](../development/issues/BUG-ENV-001-mirror-default-materials.md) | |
| E28 | おもちゃ: 呼ぶと画面の中に浮いて出て、タップで取れるか。 | [IMP-DOG-040](../development/issues/IMP-DOG-040-toy-recall-hover-in-reach.md) | |
| E29 | 写真の一覧・大きい表示のどちらでも吸着せず、黒くならないか。 | [IMP-WLD-009](../development/issues/IMP-WLD-009-menu-gallery-snap-from-thumbnails.md) | |
| E30 | 撮られる側として: 撮影の通知と [やめてもらう] がタップで押せるか。 | [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md) | |

## 具体的な確認に落とせなかった項目

Issue が「何を見ればよいか」を書いていないため、項目にしていない。ユーザーに期待値を決めてもらうか、AI 側で Issue を直す必要がある。

| Issue | 足りないこと |
|---|---|
| [IMP-DOG-001](../development/issues/IMP-DOG-001-port-dog-system.md) | 「6 匹の撫で・採用するおもちゃが 2 人で同期して動く」の見る点が無い (撫でとおもちゃは他の Issue の項目に分けて入れてある) |
| [DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md) | 集合の停滞検知 (到達不能時の救済) は、時間・速度の合格値が無い。「開始から 60 秒以内に移動と整列がどう見えるか」だけで、良し悪しの基準が書かれていない |
| [IMP-DOG-003](../development/issues/IMP-DOG-003-walking-polish.md) | 犬同士のじゃれ合い (手順 3) は未実装で、見る対象が無い。初期目的地・追加接続・再接続の採用仕様の確認も、観測する点が書かれていない |
| [IMP-WLD-003](../development/issues/IMP-WLD-003-bgm-and-volume-settings.md) | ライブ連動の確認は、ライブの開始手順 (どの操作で開催するか) が Issue に無い。ライブ中の BGM の確認 (A-9 の最後の項目) は、開催方法が分かってから行う |
| [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) | 「CE (王様・夏・キャンディ・パンプキン) の所持判定」は、CE 品を持つアカウントでの確認になるが、どのアカウントで見るかが無い (サーバー保存・実際の再入室は A-4 の項目で見る) |
| [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) | 「状態表示の規律がモック (DES-UI-003 版 3) と一致し差分を記録した」は、照合する項目がこの Issue に列挙されていない。メニュー計画 §0 の「準備中のタブは行 0 に『準備中』だけ」は、段階 1〜3 の実装で現状と合わない古い期待のため載せていない |
| [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) | 川 #27 (フリスビーを川へ投げると、犬が手前で諦めてディスクが岸へ戻る) は IMP-DOG-023 で動作が変わった (浮く・ドボンする) ため古い期待。川 #14 (水音が無いのが物足りないか) は #26 の水音の項目に統合した |
| [VER-DOG-002](../development/issues/VER-DOG-002-wash-experience-and-devices.md) | 先行 Animal Wash との比較は、比較対象の版と場面が未選定のまま。A-2 の最後の項目は、選ぶまでは「気づいた点」の記録だけになる |
| [DES-ENV-002](../development/issues/DES-ENV-002-longer-course.md) | 最終距離・予算を確定する判断基準が無い (実歩行時間は A-10 で記録するだけ)。夜の見え方と丘の陰の遮蔽の比較は「後段で確認」とあるだけで、見る対象が無い |
| [IMP-DOG-013](../development/issues/IMP-DOG-013-frisbee-pet-after-return.md) | 「Drop 中に口から取り上げる遊びを仕様に含めるか」は判断事項で、Play で見る点が無い |
| [IMP-DOG-018](../development/issues/IMP-DOG-018-frisbee-dog-face.md) | 6 枚の顔の見た目の微調整は「ユーザーが後で」とあり、今回の確認に含めていない |
| [IMP-ENV-010](../development/issues/IMP-ENV-010-quest-performance.md) | 地形の光りが消えない場合の切り分け (仮説 B) は、消えなかったときの作業のため項目にしていない。公開前の計測プローブの撤去は AI の作業 |

### この表の対象外 (AI 側の作業)

IMP-DOG-026 の送信量の実測、IMP-DOG-030 と IMP-DOG-012 の本シーンでの負荷の実測、IMP-WLD-003 の Android / iOS の容量の実測、IMP-DOG-018 のビルドサイズ、IMP-ENV-010 の計測プローブの撤去、IMP-ENV-006 の川の音のビルドサイズの実測と利用条件の記録 (ユーザーの回答待ち)、DES-UI-004 の看板の翻訳の校正、IMP-DOG-035 の全体ハーネスの C-keepout-wander の修正と再レビュー。DES-UI-003・DES-DOG-006 は、ユーザーの確認が他の Issue (IMP-UI-001・IMP-DOG-025) に出ているため、この表には載せていない。

<!-- 項目数: A. 151 (A0-1〜13 + A1〜A138) / B. 57 / C. 15 / D. 22 / E. 20 -->
