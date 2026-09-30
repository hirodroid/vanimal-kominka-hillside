# Issue サマリ

> 生成ファイル。手で編集しない。運用は [README.md](README.md)。
> 更新は `node scripts/check-issues.js --write` (状態・概要・次は各 Issue の frontmatter が正本)。

71 件 — 対応中 18 / 確認待ち 23 / 未着手 17 / 保留 3 / 完了 10

## 実行キュー

| ID | 重要度 | 概要 | 状態 | 次 |
|----|--------|------|------|----|
| [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md) | High | 非マスターの投球で犬が追わない。原因はコード上の経路で確認 (投球通知が Pickup の解放同期より先に届き、古い保持状態で即取消)。犬 Owner 側の 1 秒保留と (送信者, 世代, 発生時刻) 照合で修正、ClientSim netorder 10/10。2 人 PCVR の再検証待ち | wip |  |
| [DES-DOG-001](DES-DOG-001-leash-walking.md) | High | 集合・目的地継承・拠点回収の契約を実装済み (Editor)。Play 後に停滞検知・端末別入力・購入権の双方向を詰める | wip |  |
| [DES-DOG-006](DES-DOG-006-wash-method.md) | High | 散歩帰りのお手入れの方式 (汚れ・泡・濡れの見た目、判定、2 人協力の同期、犬の行動との切替) を決める | wip | Codex への相談の結果を設計書へ反映し、賭け 3 つを IMP-DOG-025 の試作で確かめる |
| [IMP-DOG-001](IMP-DOG-001-port-dog-system.md) | High | 古民家ドッグランの犬ギミックを移植する (タグ `export/kominka-hillside-v1` + 移植記録) | wip |  |
| [IMP-DOG-003](IMP-DOG-003-walking-polish.md) | High | 自由な散歩の試作は PCVR で「かなり良かった」。残り: 犬同士の遊び、Desktop/Touch、複数人。看板の本制作・分かりやすさは DES-UI-004 へ分離 | wip |  |
| [IMP-DOG-009](IMP-DOG-009-walk-speed-tiers.md) | High | 散歩の犬の速さを 4 段階に (最速は現状の速歩の 3 倍)。人が犬を引っ張る状態をなくす。Run クリップの再生速度・減速も合わせる。9/22 PCVR 1 人 1 匹で OK・採用、3〜6 匹・2 人以上・Desktop は未確認 | wip |  |
| [IMP-DOG-024](IMP-DOG-024-walk-and-wash-experience.md) | High | Animal Wash以上のASMR的な洗浄の気持ちよさを最優先に、散歩帰りのお手入れと複数洗い場を作る大型Issue | wip | IMP-DOG-025 で代表 1 匹 (柴) の一周を作り、賭け 3 つを確かめる |
| [IMP-DOG-025](IMP-DOG-025-wash-prototype-one-dog.md) | High | 代表 1 匹 (柴) で洗浄の一周を細く通し、設計書の賭け 3 つを確かめる試作 | wip | 2026-09-30 の PCVR の指摘 (泡の層の切れ目、犬が向きすぎる、座りすぎる、汚れは足元から、汚れと濡れの区別) を直す |
| [IMP-DOG-028](IMP-DOG-028-wash-sound-and-reactions.md) | High | 洗浄の音と犬の反応の仕上げ (音素材の制作と記録、個人音量、タオル、ブルブル、満足の合図) | wip | ハーネス (W-shake / satisfied / paw / no-repeat) の結果、ユーザーの Play (反応の見え方、音の試聴)、試聴の後の音素材の作り直しが残り |
| [DES-ENV-002](DES-ENV-002-longer-course.md) | Medium | 約2.41kmの川沿い周回を実装し、2026-09-27に本番シーン (VRCDefaultWorldScene) へ採用済み。Quest 3・iPhone 15 Proの実歩行と16人性能で最終距離/予算を判断する | wip | 犬連れの実歩行時間、LODと遮蔽の実機表示、Quest 3・iPhone 15 Pro・16人の性能から最終距離/予算を確定する (ユーザーの実機。VER-WLD-001) |
| [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) | Medium | 9/23 ユーザー回答: 初回付与200pt・獲得は滞在のみ・撫でFX採用・タブ構成A・おじいさんは門・犬目線/かけっこは対象外・距離フェードとおもちゃ柄は採用。正本へ反映済み。モック OK・計画書あり。IMP-UI-001 は段階 0-b まで完了 (9/24 PCVR OK)、段階 2・3 は 9/24 実装済み・PCVR 待ち | wip |  |
| [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) | Medium | フリスビー通常版はPCVRでひとまずOK。項目別・2人の検証を保持。バリエーションは008、6匹展開は012 | wip |  |
| [IMP-DOG-007](IMP-DOG-007-leash-pull-haptics.md) | Medium | リードを引っ張られたら、接続している側の手に振動を伝える (Haptics)。9/22 に VR 限定で実装 (伸び比率 + 止まっている時の引き、手ごとに最大)、PCVR の体感確認が残る | wip |  |
| [IMP-DOG-010](IMP-DOG-010-walk-stopovers.md) | Medium | 寄り道が分かりにくい (嗅ぐ姿勢が無い・ずれが小さい・短い) ので作り直し、道沿いの立ち寄り場所と夜の散歩の違いを足す。9/22 PCVR 1 人 1 匹で OK・採用 (Sniff クリップは保留)、3〜6 匹・2 人以上・Desktop は未確認 | wip |  |
| [IMP-DOG-012](IMP-DOG-012-frisbee-six-dogs.md) | Medium | フリスビー (先回り型 + 6 プリセット) を 6 匹へ展開する。2026-09-24 実装済み (ff76602): 犬種別クリップ・facts、犬別ディスク 6 枚、犬種別の跳躍上限 (小型犬 0.45 m)、Verify・縮小ハーネス・6 匹同時の負荷 PASS。2026-09-29 に現行の既定表 26 投げ × 6 犬種を取り直して全 PASS。残り: 本シーンでの負荷 (AI / 実機)、PCVR (ユーザー) | wip |  |
| [IMP-DOG-013](IMP-DOG-013-frisbee-pet-after-return.md) | Medium | フリスビーを持ち帰った犬をよしよしできる。くわえて待っている間は撫でを再開し (撫でている間はディスクを落とさない)、受け取っても 4 秒 / 2.5 m の猶予はその場で待つ。9/22 実装・Editor 検査済み、PCVR 確認が残る | wip |  |
| [IMP-ENV-004](IMP-ENV-004-river-loop-course.md) | Medium | 既存の登り道を保全し、奥広場・川岸・小橋を経て門へ戻る周回コースを軽量に制作する。約2.41kmの地形・436点の周回経路・手元の6目的地を別シーンへ実装 | wip | ユーザー了承済みの周回版を本番へ採用。細部を調整し、Quest 3/iPhone 15 Pro・16人性能の実機検証へ |
| [IMP-WLD-002](IMP-WLD-002-call-player.md) | Medium | 在室の 1 人を呼ぶ ([来てもらう]。呼ばれた人が [行く] を押したときだけ自分で呼んだ人の近くへ移動し、接続犬も同行する) を、合流の受付箱の交渉を流用して実装する | wip | 実装と ClientSim は済み (82c 29/29、82・82b の回帰なし、3c307f8)。C7-wet は 82c に足したが現行の地形では未実施 (濡れ判定に入る NavMesh 上の点が 0、19db81d)。残りは PCVR 2〜3 人 (設計書 §F.7 |
| [IMP-DOG-004](IMP-DOG-004-leash-models-release.md) | High | 首輪・リードの本制作。PCVR 1 人は合格。2 人・Quest/iOS・座位/木陰の確認待ち | review | PCVR 1 人は合格 (2026-09-20)。残り: 2 人・Quest/iOS・座位/木陰。持ち手の向きは BUG-DOG-002 |
| [IMP-DOG-017](IMP-DOG-017-frisbee-intercept-solver.md) | High | 小型犬 (Pome / Dachs / Corgi) と Shiba がフリスビーの下に回り込み、振り向いて跳ばずに取るのがほとんど。プランナーは軌道を知っているが、0.1 秒刻みの候補を決まった近づき方で合否判定し、落ちたら次の案へ回すため、跳べる時間帯が短い犬ほど立ち取りに落ちる。投げた瞬間に「どこで何秒後に跳ぶか・どの速さで走るか」を解く方式へ見直す | review | PCVR 3 回目: 大型犬の走り (再生倍率 1.7 で足の滑りが気にならないか)、横曲がり 0.07 の曲がり具合、退役プリセットがメニューから消えているか |
| [IMP-DOG-018](IMP-DOG-018-frisbee-dog-face.md) | High | フリスビーが誰の (どの犬の) ものか分からない (体験会 2026-09-26)。ディスクの上下面に犬の顔 (シーン内モデルのスクリーンショット 256px) を印字。2026-09-26 実装・撮影・Build・Verify PASS、権利と 6 枚の見た目はユーザー確認済み (2026-09-29、微調整は後で)。PCVR / 実機の見え方待ち | review | PCVR で 3 m 離れて誰のディスクか分かるか、持ったときに顔が消えて離すと戻るか。6 枚の見た目の微調整はユーザーが後で (首輪の輪が頭の上に写っている)。iOS / Quest は VER-WLD-001 |
| [IMP-DOG-021](IMP-DOG-021-river-wide-fetch-and-recall.md) | High | 川のどこへ投げたボールでも犬が取りに行く (川全体で浮かせ、安全な岸から入って取る)。水中の犬のボール・ディスクも、口に無ければ手元へ呼べる | review | ユーザーの PCVR (設計書末尾のチェックリスト 29〜36: 遠投の走りと入水、上陸の場所、大型犬が流路で取る見え方、サモエドの口の余裕、橋の近くの岸戻し)。2 人の実機で所有権の往復 (非 Owner の投げ、Owner 退出) |
| [IMP-DOG-026](IMP-DOG-026-wash-state-and-sync.md) | High | 洗浄の状態と同期の仕上げ (セルの正本、清潔の判定、再洗浄、途中参加、Owner 交代、2 人の同時操作) | review | ユーザーの PCVR 2 人 (同時操作、途中参加、Owner 交代) と、送信量の実測が残り |
| [IMP-DOG-027](IMP-DOG-027-wash-behavior-switching.md) | High | 洗われ中の犬と既存行動 (リード・Fetch・フリスビー・水遊び・集合・呼び戻し・ワープ) の切替 | review | ユーザーの PCVR (リード接続中の停止と解除、他人のストロークで止まらないこと、Fetch・集合との切替) が残り |
| [IMP-ENV-007](IMP-ENV-007-main-wash-station.md) | High | 門の外の主洗い場の整備 (洗い台、道具の棚と戻し先、蛇口・ホース・桶、犬を呼ぶ方法) | review | ユーザーの Play (棚の位置と高さ、道具の取りやすさ、札の日本語の記入、手動配置との干渉) が残り |
| [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) | High | 台帳・おじいさん (門)・支度用ポイント (初回 200pt 一回・滞在 1分1pt)・購入と CE 判定。2026-09-24 実装済み・Verify 全 PASS (e4f5906、名前空間 KH_Commerce_v1)。T-007 / T-008・復元の実機確認待ち | review | 200pt が一回だけ入る (T-007)、滞在加算と再入室の復元、購入の保存と CE の所持判定、残高 0 で基本機能が使える (T-008)。AI 側の ClientSim ハーネスは済み (Course/96 7/7、96b 4/4、2026-09-29 ccd3ff1)。サーバー保存・実際の再入室・CE 判定は対象外で PCVR / 実機に残る |
| [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) | High | 手元メニューを古民家の器 (KD_TreatPouch + Editor ビルダー移植) に置き換え、設定面を作る。段階 0-a / 0-b / 3 は実装済み・Verify 全 PASS、0-b は 2026-09-24 PCVR OK。段階 3 (距離フェード・許可初期 ON・ようこそ等) の実機確認待ち。クレジット URL 3 件はユーザー記入待ち | review | 段階 0-b は PCVR OK (2026-09-24)。残り: 退行・取り違えの確認、段階 3 の距離フェード (T-011)・ナイトモードのスライダー・許可トグル 2 種・状態表示の規律。クレジット URL 3 件はユーザー記入。Quest/iOS は VER-WLD-001 |
| [BUG-DOG-002](BUG-DOG-002-leash-handle-orientation.md) | Medium | リードの輪と紐の接続部が横向き。肘→手の延長上・手の前側へ向くよう左右とも調整する (9/23 要望具体化)。2026-09-29 に輪の面を指の付け根の線と前腕の軸から決める形へ作り直し (57bcf2e、Production 3 PASS)、PCVR の見た目待ち | review | PCVR で右手・左手とも輪の後ろの帯が指の付け根に沿い、輪と紐の接続部が前腕の延長で拳の前へ出るか。握りが拳に食い込む・浮く場合は HandleGripOffset、面の回転は HandleRollDegrees (定数を直して Walking/Production 7)。2 人目から見て左右が同じか、手首を深く曲げて跳ばないか |
| [BUG-DOG-006](BUG-DOG-006-steal-held-pickup.md) | Medium | 実機 (2026-09-28 夜、7 人) で VRChat の「Attempted to steal ownership of a held pickup.」が 15 件 (22:57:07 に 2 件、22:57:21 に 13 件)。ログとコードでは、その時刻にこちらの端末のフリスビー側が SetOwner を呼んだ経路は見つからない (3 匹とも Ready・持ち直し直後で、SetOwner する状態遷移が無い)。同じ文言は他ワールドのログ 60 本にも出る。犬のディスクの SetOwner を AskDisc 1 か所にまとめて持たれている間は呼ばないようにし、全ての SetOwner を送り元の Owner 付きでログに出す | review | コンパイルと ClientSim の回帰は済み (7178d1f)。次の複数人実機でエラーの時刻に `setowner` の行がこちらに出ているかを見る (出ていなければ他の端末の要求)。相手のログも同じ時刻に同じ行があるかを比べる |
| [DES-DOG-003](DES-DOG-003-plaza-leash-station.md) | Medium | 体験会 (2026-09-26) で「広場のリード置き場が遠い」。同日夜に決定: 広場の拠点を広場の縁に戻す (3 拠点 × 左右 1 本)。Build が初回だけ縁に置き、位置はユーザーが手で微調整する。実装・Verify 済み、位置はユーザーが手調整済み (2026-09-29)。PCVR 確認待ち | review | PCVR で広場の拠点が遠くないか確認 (位置は 2026-09-29 にユーザーが手調整済み)。OK なら done |
| [DES-DOG-005](DES-DOG-005-frisbee-anywhere.md) | Medium | フリスビーを広場以外 (奥広場・川辺・道) でも投げて取れるようにする。段階 1 は取り出しを投げられるエリアに限る、段階 2 は平らな場所を追加、段階 3 は地面の高さを基準にした滑空・キャッチでどこでも | review | 段階 3 は IMP-DOG-023 M1〜M4 で実装・ClientSim 検証済み (2026-09-28)。残りはユーザーの PCVR 37〜48 と VER-WLD-001 項目 12。確かめたら done |
| [IMP-DOG-014](IMP-DOG-014-treats-port.md) | Medium | おやつ 4 種を古民家から移植 (メニューの [買う] 含む)。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。出す/あげる/しまう・補充・T-009 (枠 3) の Play・実機確認待ち。接続犬の反応は IMP-DOG-006 | review | 出す / あげる / しまう が 1 人で成立、在庫の増減と復元、[買う] の補充、4 人で枠 3 (T-009)、6 匹の食べ・お礼。AI 側の ClientSim ハーネスは済み (Course/97 17/17、2026-09-29 ccd3ff1)。4 人の枠の取り合いは模擬で、実通信は PCVR に残る |
| [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) | Medium | 飾り 14 商品と撫でFX 11 種を移植。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。首輪干渉は 2026-09-24 案 C で決定・適用 (5 件は (b) で下げ、3 件は犬ごとに除外、Verify 6f PASS。049b173)。装着・同期・見た目の PCVR 確認待ち | review | 6 匹への装着と他人への表示、購入・試着と台帳の一致、撫でFX の配布、60 秒ルールと退室後の残存 (2 人)、首まわりの見た目 (案 C 適用後) |
| [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md) | Medium | 呼ぶ・ポーズを移植し、リード接続中の除外と斜面 (接地・法線) の判定を足す。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。法線 0.9 は仮採用。Play・T-010・斜面の PCVR 確認待ち | review | 6 匹で平地の流れが成立、リード中の除外と逆方向 (T-010)、斜面の赤マーカーと法線 0.9 の確定、4 動作の課金ゲート、許可トグル、2 人の予約の取り合い |
| [IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md) | Medium | 同じ犬に対してボールとフリスビーはどちらか片方しか使えない (ユーザー決定 2026-09-26)。相手が使用中なら手元メニューの呼び出しを拒み、現地でも拾えない。同日夜の追加決定で出ているおもちゃは犬ごとに 1 つ (初期はディスクだけ、ボールは広場の下の置き場。出したら相手が置き場へ消える)。夜の追加分も Build / Verify 済み (d360f2b)、Play / PCVR 確認待ち | review | PCVR で: 入室時に広場にディスク 6 枚だけ、[ボール] で目の前にボールが出て同じ犬のディスクが消える、[ディスク] で戻る、相手が使用中なら出せない。2 人で消えた側が相手の画面でも消えている・途中参加者にも同じ |
| [IMP-DOG-020](IMP-DOG-020-river-wading-and-swim.md) | Medium | 川の水遊びを初期公開にすべて入れる (2026-09-27 2 回目の決定)。前提・水遊び場・浅瀬歩き・小型犬の泳ぎ (ClientSim 済み) に、水遊び場の拡張と目印・近くにいれば入るきっかけ、浮くボールの回収、積み残し (フリスビー・ディスク・押し合い・揺れ・水音) を加え、公開前の検証まで行う | review | ユーザーの PCVR (設計書末尾の実機チェックリスト、段階 5〜7 の項目を含む)。2 人 (S1〜S5b と監査の実機確認 4 件)、Quest/iOS・16 人は VER-WLD-001。破壊監査 (2026-09-28) は反映済み、記録は docs/_sessions/destructive-audit-2026-09-28/ |
| [IMP-DOG-023](IMP-DOG-023-frisbee-anywhere.md) | Medium | フリスビーをどこでも (DES-DOG-005 段階 3)。M1 陸の地面基準ジャンプと到達判定、M2 川のディスクの浮き、M3 岸からのドボンと水中回収、M4 取り出しの門の置き換え | review | AI 側の検証は M1〜M4 とも済み (ClientSim、2026-09-28)。残りはユーザーの PCVR 37〜48 (実機 09-28 夜はドボン 0 件で、岸からの跳び込み 40/41 が未確認)、VER-WLD-001 項目 12 |
| [IMP-DOG-029](IMP-DOG-029-dirt-accumulation.md) | Medium | 散歩・土や泥・川で犬が汚れる条件と、洗った後の清潔の余韻 | review | ハーネス (W-dirt-*) の結果と、ユーザーの Play (散歩の後に足が汚れて見えるか、洗い場の近くで汚れ直さないか) が残り。本番では initialDirt を 0 にする |
| [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md) | Medium | 地図の副画面、川辺・奥広場の 2 地点ワープ、インスタンス内のプレイヤーへの合流、接続犬の同行、暗転を、DES-WLD-003 の確定設計 (2026-09-27) どおりに実装する | review | PCVR チェックリスト (設計書末尾「実機チェックリスト (2026-09-27)」、表示名の字の |
| [IMP-WLD-003](IMP-WLD-003-bgm-and-volume-settings.md) | Medium | 専用BGM「木漏れ日の帰り道」003を導入し、古民家と同じ設定メニューで個人のON/OFF・音量調整を可能にする | review | 実装と Edit モードの検査は済み (Course/98 Build・99 Verify BGM_OK、Menu/16a PASS、f32cd77 / a06178c)。残り: ユーザー判断 3 件 (ON/OFF ボタンを残すか、WAV 62 MB を git へ入れるか、基準音量 0.35 と圧縮品質の確定)、ライブ連動と実際の出音の確認 (ClientSim / PCVR)、Android / iOS の容量の実測 |
| [BUG-DOG-007](BUG-DOG-007-shiba-throw-implausible.md) | Low | 実機 (2026-09-28 夜) で柴のディスクの投げが 22:53〜22:59 に 5 回 `implausible` で断られた。原因は 200 m の距離の上限で、柴は投げ手から約 430〜516 m 離れた場所に残っていた (周回コース 600×720 m では正当に起こる)。拒否理由を条件ごとに分けた。統括の決定で、Ready と投げの受理を犬から 40 m 以内に限り (`readyMaxDistance`、理由 `far`)、照準は × と「犬が遠くにいます」を出す。200 m の `implausible-far` は二重の安全として残す | review | コンパイル・Course/27・92・93・試作の既定は済み (7178d1f)。実機で 40 m の外から持つ・投げると犬が動かず、照準に「犬が遠くにいます」が出ることを確かめる |
| [BUG-DOG-004](BUG-DOG-004-destination-per-player.md) | High | 体験会 (2026-09-26、15 人) で「行き先が犬ごとに決まっていて、プレイヤーごとのローカル選択になっていない」と報告。コードは自分の選択を自分の接続犬に写す設計なので、何がどう見えたかを 2 人で再現して切り分ける | todo | 2 人で別の行き先を選び、自分の犬だけが従うか・メニューの点灯が自分の選択を示すかを確認。NG なら Manager Owner 側の ReqDestination / ReqAttach の経路を疑う |
| [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md) | High | 散歩の目的地・位置と案内／説明ボードの内容・配置・役割を検討する (仮看板は 2026-09-26 に撤去済み)。体験会 (2026-09-26、15 人) で「リードの付け方が分からない、簡単な説明ボードが要る」と報告があり、説明ボードを先に決める | todo | 入口 (スポーン付近) に置く「リードの付け方」の説明ボード 1 枚の文言・位置案を出してユーザー確認。目的地の案内板は後 |
| [IMP-DOG-030](IMP-DOG-030-wash-six-dogs.md) | High | 洗浄を 6 匹へ展開する (位置マップ・部位・体格・毛色・動く部位) | todo | IMP-DOG-025 の評価で方式が成立したのを確かめてから、犬種ごとの位置マップと部位を焼く |
| [VER-DOG-002](VER-DOG-002-wash-experience-and-devices.md) | High | 洗浄の体験の評価 (ASMR、先行作との比較、2 人、入力機器) と対象端末の実機確認を、各段の到達条件として行う | todo | IMP-DOG-025 の試作が動いた時点で、先行 Animal Wash の比較対象の版と場面を選び、対象端末の最小の描画の確認を依頼する |
| [BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) | Medium | 体験会 (2026-09-26) で iOS 参加者から「フリスビーの色が全部同じ」。ディスクは Standard Lite + _Color で犬別 6 色。iOS 実機で再現し、材質・ビルドのどこで色が落ちるかを特定する | todo | iOS 実機で 6 枚の色を見る。同じなら iOS ビルドの材質 (Standard Lite の _Color、持ち中の Transparent/Diffuse) と Android ビルドとの差を疑う |
| [DES-DOG-002](DES-DOG-002-toy-behaviour.md) | Medium | フリスビーは 1 匹で採用内容を確定 (IMP-DOG-008 完了)。複数ディスク・犬の割当は犬別 6 枚 (2026-09-24 開発側で決め、2026-09-29 ユーザー了承)。残り: 他のおもちゃの挙動、ボールの柄違いの持たせ方、公開範囲 | todo |  |
| [DES-LIV-001](DES-LIV-001-hillside-live.md) | Medium | 古民家の有料ライブ購入者の利用と、裏山ライブの開催・再生・競合・復帰を設計する。演目企画は DES-LIV-002 | todo |  |
| [DES-LIV-002](DES-LIV-002-live-lineup-and-parade.md) | Medium | ハロウィン・Candy Step・ライブパックの裏山向け演目を企画する。散歩道を犬たちがパレードする専用ライブ 1 本を含め、枠・本数・構成を整理 | todo |  |
| [DES-UI-002](DES-UI-002-frisbee-settings.md) | Medium | フリスビーの設定 (待つ場所・曲げ・取り方) の選ぶ / 自動の切り分けと操作権、開発用切替の撤去時期を設計する。[試作] 入口の代替は不要 (2026-09-29 ユーザー: おもちゃタブで呼び出せる) | todo |  |
| [DES-WLD-001](DES-WLD-001-round-trip.md) | Medium | 古民家からの出発・帰還と状態の扱いを設計する | todo |  |
| [DES-WLD-002](DES-WLD-002-sound.md) | Medium | BGM003の導入・個人音量設定はIMP-WLD-003へ。環境音・場所別構成と残る音の方針を整理する | todo | BGM採用決定とIMP-WLD-003を前提に、環境音・場所別構成の残件をgame-designへ整理する |
| [IMP-DOG-006](IMP-DOG-006-leashed-treat-approach.md) | Medium | おやつで自分の接続犬の最寄り1匹を呼び、少し待つ間に撫でる/首輪を外す→次の1匹。9/23 操作方針了承、詳細設計・実装は未 | todo |  |
| [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) | Medium | 負荷の初回測定後、道・木陰・見晴らし・休憩場所を磨く | todo |  |
| [IMP-ENV-005](IMP-ENV-005-auto-night-mode.md) | Medium | ナイトモードに時間経過で暗くなるグローバルのオートと、個人で調整するローカルのマニュアルを追加する | todo | 時間の起点・暗くなる所要時間・到達後の挙動・モードの初期値と保存方針を決める |
| [IMP-ENV-006](IMP-ENV-006-river-ambience.md) | Medium | 制作中の川のせせらぎ音を専用ディレクトリへ格納し、本番シーンの川沿いに環境音として設定する | todo | Audio/Ambience/River に完成音源を受け取り、川沿いの配置・ループ・距離減衰・音量を調整する |
| [IMP-ENV-008](IMP-ENV-008-wash-stations-along-course.md) | Medium | 主洗い場と同じ洗浄処理を、川辺・広場・裏山の採用地点へ展開する | todo | IMP-ENV-007 の主洗い場が成立してから、道中の採用地点の数と位置を導線・負荷・制作量から決める |
| [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) | Medium | Quest 単体・iOS の実機で全機能 (散歩・メニュー・空/夜・フリスビー・首輪/リード・負荷・境界) を確認する。各 Issue の Quest/iOS 未確認をここに集約 | todo |  |
| [IMP-DOG-011](IMP-DOG-011-muchio-greeting.md) | Medium | 裏山で空いている犬が、自由行動の一つとしてたまにムチォの匂いを嗅ぎに行く。2026-09-22 保留 (他者のアバターギミック依存・集客寄りの動機)。理由と再検討条件は本文末尾の保留の記録 | hold | 再検討: 「これがあると散歩・犬との体験そのものが良くなる」と Play で感じたとき。集客目的だけでは解除しない |
| [VER-DOG-001](VER-DOG-001-avatar-pet-contact-research.md) | Medium | 来訪者の無改変が条件。ムチォは既存Senderで対応候補、犬っこは通常時の対象外。静的調査まで完了、IMP-DOG-011 と共に保留 | hold | 再検討: IMP-DOG-011 の保留解除時。静的調査結果は [調査結果](../../plans/2026-09-21-avatar-pet-contact-research.md) に残す |
| [IMP-ENV-003](IMP-ENV-003-spawn-and-dropped-portals.md) | Low | 参加者が落としたポータルがスポーン地点の真後ろに置かれ、間違って入りそうだった (体験会 2026-09-26)。ユーザーがスポーン周りを手で調整する (2026-09-26 決定)。AI 側の作業は無し | hold | 再検討: ユーザーの手動調整後の体験会でまた真後ろにポータルが置かれたとき。それまで AI は触らない |

## 完了

| ID | 重要度 | 概要 | 日付 |
|----|--------|------|------|
| [DES-WLD-003](DES-WLD-003-map-and-travel.md) | Medium | 広い周回コースの地図、地点ワープ、プレイヤーへの合流と接続犬の同行を設計する | 2026-09-29 |
| [IMP-DOG-022](IMP-DOG-022-frisbee-takeout-area.md) | Medium | (IMP-DOG-023 M4 で置換済み) ディスクの手元への取り出し (おもちゃタブ) を、投げられる広場の近く (円 + 5 m) に限る。外では「広場の近くで取り出せます」と案内する (DES-DOG-005 段階 1) | 2026-09-29 |
| [DES-DOG-004](DES-DOG-004-river-play-and-floating-fetch.md) | Medium | 小川で犬が入水・浮遊・泳ぐ動作と、浮くボールを泳いで回収する遊びを設計する | 2026-09-27 |
| [DES-ENV-001](DES-ENV-001-terrain-vs-blender.md) | High | 地形を Unity Terrain で作るか Blender で作るか → **Terrain 採用** | 2026-09-22 |
| [DES-UI-001](DES-UI-001-menu-and-leash-assist.md) | High | 古民家と揃えた手元メニュー (さんぽ / あそぶ / おもちゃ)・接続補助・目的地選択・集合・ボール個別回収 | 2026-09-22 |
| [IMP-DOG-002](IMP-DOG-002-leash-walking.md) | High | 先端 Pickup → 首輪接続、片手 3 匹・合計 6 匹、接続中の追従、Fetch 競合抑止、退出時解除、途中参加復元 | 2026-09-22 |
| [IMP-DOG-008](IMP-DOG-008-frisbee-variations.md) | Medium | ラブ 1 匹でフリスビーの走り込み・取り方・飛び方を試し、先回り型 + 動きの層に作り替え、6 プリセット全部採用 | 2026-09-22 |
| [IMP-ENV-002](IMP-ENV-002-skybox-and-night-mode.md) | Medium | 空を古民家と同じキューブマップにし、手元メニューのヘッダーに OFF / ON のナイトモード (RBS 不使用、乗算オーバーレイ + 空の露出) | 2026-09-22 |
| [VER-ENV-001](VER-ENV-001-prototype-view-and-edges.md) | High | 試作: 眺め・移動・端の見え方・実機負荷を確認する (代表シーン検証を兼ねる) | 2026-09-22 |
| [BUG-DOG-001](BUG-DOG-001-ball-spawn-slope.md) | High | ボール 6 個を坂から上の広場の平坦部へ | 2026-09-20 |
