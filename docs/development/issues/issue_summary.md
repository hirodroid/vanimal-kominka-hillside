# Issue サマリ

> 生成ファイル。手で編集しない。運用は [README.md](README.md)。
> 更新は `node scripts/check-issues.js --write` (状態・概要・次は各 Issue の frontmatter が正本)。

50 件 — 対応中 15 / 確認待ち 11 / 未着手 12 / 保留 3 / 完了 9

## 実行キュー

| ID | 重要度 | 概要 | 状態 | 次 |
|----|--------|------|------|----|
| [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md) | High | 非マスターの投球で犬が追わない。原因はコード上の経路で確認 (投球通知が Pickup の解放同期より先に届き、古い保持状態で即取消)。犬 Owner 側の 1 秒保留と (送信者, 世代, 発生時刻) 照合で修正、ClientSim netorder 10/10。2 人 PCVR の再検証待ち | wip |  |
| [DES-DOG-001](DES-DOG-001-leash-walking.md) | High | 集合・目的地継承・拠点回収の契約を実装済み (Editor)。Play 後に停滞検知・端末別入力・購入権の双方向を詰める | wip |  |
| [IMP-DOG-001](IMP-DOG-001-port-dog-system.md) | High | 古民家ドッグランの犬ギミックを移植する (タグ `export/kominka-hillside-v1` + 移植記録) | wip |  |
| [IMP-DOG-003](IMP-DOG-003-walking-polish.md) | High | 自由な散歩の試作は PCVR で「かなり良かった」。残り: 犬同士の遊び、Desktop/Touch、複数人。看板の本制作・分かりやすさは DES-UI-004 へ分離 | wip |  |
| [IMP-DOG-009](IMP-DOG-009-walk-speed-tiers.md) | High | 散歩の犬の速さを 4 段階に (最速は現状の速歩の 3 倍)。人が犬を引っ張る状態をなくす。Run クリップの再生速度・減速も合わせる。9/22 PCVR 1 人 1 匹で OK・採用、3〜6 匹・2 人以上・Desktop は未確認 | wip |  |
| [BUG-DOG-002](BUG-DOG-002-leash-handle-orientation.md) | Medium | リードの輪と紐の接続部が横向き。肘→手の延長上・手の前側へ向くよう左右とも調整する (9/23 要望具体化) | wip |  |
| [DES-ENV-002](DES-ENV-002-longer-course.md) | Medium | 約2.41kmの川沿い周回を別シーンへ実装中。地形・経路・景観・容量とEditor描画を確認し、Quest 3・iPhone 15 Proの実歩行と16人性能で最終距離/予算を判断する | wip | 接続済みの436点経路・手元メニューで犬の周回を検証。LODと遮蔽の実機表示、犬連れの歩行時間と16人性能から最終距離/予算を確定する |
| [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) | Medium | 9/23 ユーザー回答: 初回付与200pt・獲得は滞在のみ・撫でFX採用・タブ構成A・おじいさんは門・犬目線/かけっこは対象外・距離フェードとおもちゃ柄は採用。正本へ反映済み。モック OK・計画書あり。IMP-UI-001 は段階 0-b まで完了 (9/24 PCVR OK)、段階 2・3 は 9/24 実装済み・PCVR 待ち | wip |  |
| [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) | Medium | フリスビー通常版はPCVRでひとまずOK。項目別・2人の検証を保持。バリエーションは008、6匹展開は012 | wip |  |
| [IMP-DOG-007](IMP-DOG-007-leash-pull-haptics.md) | Medium | リードを引っ張られたら、接続している側の手に振動を伝える (Haptics)。9/22 に VR 限定で実装 (伸び比率 + 止まっている時の引き、手ごとに最大)、PCVR の体感確認が残る | wip |  |
| [IMP-DOG-010](IMP-DOG-010-walk-stopovers.md) | Medium | 寄り道が分かりにくい (嗅ぐ姿勢が無い・ずれが小さい・短い) ので作り直し、道沿いの立ち寄り場所と夜の散歩の違いを足す。9/22 PCVR 1 人 1 匹で OK・採用 (Sniff クリップは保留)、3〜6 匹・2 人以上・Desktop は未確認 | wip |  |
| [IMP-DOG-012](IMP-DOG-012-frisbee-six-dogs.md) | Medium | フリスビー (先回り型 + 6 プリセット) を 6 匹へ展開する。2026-09-24 実装済み (ff76602): 犬種別クリップ・facts、犬別ディスク 6 枚、犬種別の跳躍上限 (小型犬 0.45 m)、Verify・縮小ハーネス・6 匹同時の負荷 PASS。残り: Shiba / Samoyed の縮小表の取り直し・全 36 投げ (AI)、PCVR (ユーザー) | wip |  |
| [IMP-DOG-013](IMP-DOG-013-frisbee-pet-after-return.md) | Medium | フリスビーを持ち帰った犬をよしよしできる。くわえて待っている間は撫でを再開し (撫でている間はディスクを落とさない)、受け取っても 4 秒 / 2.5 m の猶予はその場で待つ。9/22 実装・Editor 検査済み、PCVR 確認が残る | wip |  |
| [IMP-DOG-020](IMP-DOG-020-river-wading-and-swim.md) | Medium | 川の水遊びを初期公開にすべて入れる (2026-09-27 2 回目の決定)。前提・水遊び場・浅瀬歩き・小型犬の泳ぎ (ClientSim 済み) に、水遊び場の拡張と目印・近くにいれば入るきっかけ、浮くボールの回収、積み残し (フリスビー・ディスク・押し合い・揺れ・水音) を加え、公開前の検証まで行う | wip | 段階 5・6 の Codex レビュー (2026-09-27) の反映を実装済み。統括がコンパイル → Course/76 → 27 → 77 → 78 → 79 → 88 を実行して結果を記録する |
| [IMP-ENV-004](IMP-ENV-004-river-loop-course.md) | Medium | 既存の登り道を保全し、奥広場・川岸・小橋を経て門へ戻る周回コースを軽量に制作する。約2.41kmの地形・436点の周回経路・手元の6目的地を別シーンへ実装 | wip | ユーザー了承済みの周回版を本番へ採用。細部を調整し、Quest 3/iPhone 15 Pro・16人性能の実機検証へ |
| [IMP-DOG-004](IMP-DOG-004-leash-models-release.md) | High | 首輪・リードの本制作。PCVR 1 人は合格。2 人・Quest/iOS・座位/木陰の確認待ち | review | PCVR 1 人は合格 (2026-09-20)。残り: 2 人・Quest/iOS・座位/木陰。持ち手の向きは BUG-DOG-002 |
| [IMP-DOG-017](IMP-DOG-017-frisbee-intercept-solver.md) | High | 小型犬 (Pome / Dachs / Corgi) と Shiba がフリスビーの下に回り込み、振り向いて跳ばずに取るのがほとんど。プランナーは軌道を知っているが、0.1 秒刻みの候補を決まった近づき方で合否判定し、落ちたら次の案へ回すため、跳べる時間帯が短い犬ほど立ち取りに落ちる。投げた瞬間に「どこで何秒後に跳ぶか・どの速さで走るか」を解く方式へ見直す | review | PCVR 3 回目: 大型犬の走り (再生倍率 1.7 で足の滑りが気にならないか)、横曲がり 0.07 の曲がり具合、退役プリセットがメニューから消えているか |
| [IMP-DOG-018](IMP-DOG-018-frisbee-dog-face.md) | High | フリスビーが誰の (どの犬の) ものか分からない (体験会 2026-09-26)。ディスクの上下面に犬の顔 (シーン内モデルのスクリーンショット 256px) を印字。2026-09-26 実装・撮影・Build・Verify PASS、PCVR / 実機の見え方とユーザーの権利確認待ち | review | PCVR で 3 m 離れて誰のディスクか分かるか、持ったときに顔が消えて離すと戻るか。撮った 6 枚 (Textures/Frisbee) の見た目をユーザーが確認 (首輪の輪が頭の上に写っている)。iOS / Quest は VER-WLD-001 |
| [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) | High | 台帳・おじいさん (門)・支度用ポイント (初回 200pt 一回・滞在 1分1pt)・購入と CE 判定。2026-09-24 実装済み・Verify 全 PASS (e4f5906、名前空間 KH_Commerce_v1)。T-007 / T-008・復元の実機確認待ち | review | 200pt が一回だけ入る (T-007)、滞在加算と再入室の復元、購入の保存と CE の所持判定、残高 0 で基本機能が使える (T-008)。**AI 側の残作業**: 台帳用の ClientSim ハーネスが未作成で、付与・購入・復元の固定手順が未実行 (ユーザーの PCVR 確認とは別) |
| [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) | High | 手元メニューを古民家の器 (KD_TreatPouch + Editor ビルダー移植) に置き換え、設定面を作る。段階 0-a / 0-b / 3 は実装済み・Verify 全 PASS、0-b は 2026-09-24 PCVR OK。段階 3 (距離フェード・許可初期 ON・ようこそ等) の実機確認待ち。クレジット URL 3 件はユーザー記入待ち | review | 段階 0-b は PCVR OK (2026-09-24)。残り: 退行・取り違えの確認、段階 3 の距離フェード (T-011)・ナイトモードのスライダー・許可トグル 2 種・状態表示の規律。クレジット URL 3 件はユーザー記入。Quest/iOS は VER-WLD-001 |
| [DES-DOG-003](DES-DOG-003-plaza-leash-station.md) | Medium | 体験会 (2026-09-26) で「広場のリード置き場が遠い」。同日夜に決定: 広場の拠点を広場の縁に戻す (3 拠点 × 左右 1 本)。Build が初回だけ縁に置き、位置はユーザーが手で微調整する。実装・Verify 済み、位置の手調整と PCVR 確認待ち | review | ユーザーが Unity で KH_Station_Plaza の位置を広場の縁で微調整 (中心から 6〜12.5 m の帯内。Build は Crossbar があれば位置を触らない) → PCVR で遠くないか確認。OK なら done |
| [IMP-DOG-014](IMP-DOG-014-treats-port.md) | Medium | おやつ 4 種を古民家から移植 (メニューの [買う] 含む)。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。出す/あげる/しまう・補充・T-009 (枠 3) の Play・実機確認待ち。接続犬の反応は IMP-DOG-006 | review | 出す / あげる / しまう が 1 人で成立、在庫の増減と復元、[買う] の補充、4 人で枠 3 (T-009)、6 匹の食べ・お礼。**AI 側の残作業**: おやつ用の ClientSim ハーネスが未作成で、出す → あげる → しまう → 在庫確認の固定手順が未実行 (ユーザーの PCVR 確認とは別) |
| [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) | Medium | 飾り 14 商品と撫でFX 11 種を移植。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。首輪干渉は 2026-09-24 案 C で決定・適用 (5 件は (b) で下げ、3 件は犬ごとに除外、Verify 6f PASS。049b173)。装着・同期・見た目の PCVR 確認待ち | review | 6 匹への装着と他人への表示、購入・試着と台帳の一致、撫でFX の配布、60 秒ルールと退室後の残存 (2 人)、首まわりの見た目 (案 C 適用後) |
| [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md) | Medium | 呼ぶ・ポーズを移植し、リード接続中の除外と斜面 (接地・法線) の判定を足す。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。法線 0.9 は仮採用。Play・T-010・斜面の PCVR 確認待ち | review | 6 匹で平地の流れが成立、リード中の除外と逆方向 (T-010)、斜面の赤マーカーと法線 0.9 の確定、4 動作の課金ゲート、許可トグル、2 人の予約の取り合い |
| [IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md) | Medium | 同じ犬に対してボールとフリスビーはどちらか片方しか使えない (ユーザー決定 2026-09-26)。相手が使用中なら手元メニューの呼び出しを拒み、現地でも拾えない。同日夜の追加決定で出ているおもちゃは犬ごとに 1 つ (初期はディスクだけ、ボールは広場の下の置き場。出したら相手が置き場へ消える)。夜の追加分はコードのみ、Unity の Build / Verify と Play / PCVR 確認待ち | review | 統括がコンパイル → Walking/3 Build → Walking/5 Verify → 保存。PCVR で: 入室時に広場にディスク 6 枚だけ、[ボール] で目の前にボールが出て同じ犬のディスクが消える、[ディスク] で戻る、相手が使用中なら出せない。2 人で消えた側が相手の画面でも消えている・途中参加者にも同じ |
| [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md) | Medium | 地図の副画面、川辺・奥広場の 2 地点ワープ、インスタンス内のプレイヤーへの合流、接続犬の同行、暗転を、DES-WLD-003 の確定設計 (2026-09-27) どおりに実装する | review | PCVR チェックリスト (設計書末尾「実機チェックリスト (2026-09-27)」、表示名の字の |
| [BUG-DOG-004](BUG-DOG-004-destination-per-player.md) | High | 体験会 (2026-09-26、15 人) で「行き先が犬ごとに決まっていて、プレイヤーごとのローカル選択になっていない」と報告。コードは自分の選択を自分の接続犬に写す設計なので、何がどう見えたかを 2 人で再現して切り分ける | todo | 2 人で別の行き先を選び、自分の犬だけが従うか・メニューの点灯が自分の選択を示すかを確認。NG なら Manager Owner 側の ReqDestination / ReqAttach の経路を疑う |
| [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md) | High | 散歩の目的地・位置と案内／説明ボードの内容・配置・役割を検討する (仮看板は 2026-09-26 に撤去済み)。体験会 (2026-09-26、15 人) で「リードの付け方が分からない、簡単な説明ボードが要る」と報告があり、説明ボードを先に決める | todo | 入口 (スポーン付近) に置く「リードの付け方」の説明ボード 1 枚の文言・位置案を出してユーザー確認。目的地の案内板は後 |
| [BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) | Medium | 体験会 (2026-09-26) で iOS 参加者から「フリスビーの色が全部同じ」。ディスクは Standard Lite + _Color で犬別 6 色。iOS 実機で再現し、材質・ビルドのどこで色が落ちるかを特定する | todo | iOS 実機で 6 枚の色を見る。同じなら iOS ビルドの材質 (Standard Lite の _Color、持ち中の Transparent/Diffuse) と Android ビルドとの差を疑う |
| [DES-DOG-002](DES-DOG-002-toy-behaviour.md) | Medium | フリスビーは 1 匹で採用内容を確定 (IMP-DOG-008 完了)。複数ディスク・犬の割当は犬別 6 枚で開発側決定 (2026-09-24、ユーザー確認待ち)。残り: 他のおもちゃの挙動、ボールの柄違いの持たせ方、公開範囲 | todo |  |
| [DES-LIV-001](DES-LIV-001-hillside-live.md) | Medium | 古民家の有料ライブ購入者の利用と、裏山ライブの開催・再生・競合・復帰を設計する。演目企画は DES-LIV-002 | todo |  |
| [DES-LIV-002](DES-LIV-002-live-lineup-and-parade.md) | Medium | ハロウィン・Candy Step・ライブパックの裏山向け演目を企画する。散歩道を犬たちがパレードする専用ライブ 1 本を含め、枠・本数・構成を整理 | todo |  |
| [DES-UI-002](DES-UI-002-frisbee-settings.md) | Medium | フリスビーの設定 (待つ場所・曲げ・取り方) の選ぶ / 自動の切り分けと操作権、開発用切替の撤去時期を設計する。[試作] 入口は 9/23 の KH_HandMenu 退役で消え、代替はユーザー判断 | todo |  |
| [DES-WLD-001](DES-WLD-001-round-trip.md) | Medium | 古民家からの出発・帰還と状態の扱いを設計する | todo |  |
| [DES-WLD-002](DES-WLD-002-sound.md) | Medium | 裏山の音 (BGM・環境音) の方針・音源・ライブとの切替を設計する | todo |  |
| [IMP-DOG-006](IMP-DOG-006-leashed-treat-approach.md) | Medium | おやつで自分の接続犬の最寄り1匹を呼び、少し待つ間に撫でる/首輪を外す→次の1匹。9/23 操作方針了承、詳細設計・実装は未 | todo |  |
| [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) | Medium | 負荷の初回測定後、道・木陰・見晴らし・休憩場所を磨く | todo |  |
| [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) | Medium | Quest 単体・iOS の実機で全機能 (散歩・メニュー・空/夜・フリスビー・首輪/リード・負荷・境界) を確認する。各 Issue の Quest/iOS 未確認をここに集約 | todo |  |
| [IMP-DOG-011](IMP-DOG-011-muchio-greeting.md) | Medium | 裏山で空いている犬が、自由行動の一つとしてたまにムチォの匂いを嗅ぎに行く。2026-09-22 保留 (他者のアバターギミック依存・集客寄りの動機)。理由と再検討条件は本文末尾の保留の記録 | hold | 再検討: 「これがあると散歩・犬との体験そのものが良くなる」と Play で感じたとき。集客目的だけでは解除しない |
| [VER-DOG-001](VER-DOG-001-avatar-pet-contact-research.md) | Medium | 来訪者の無改変が条件。ムチォは既存Senderで対応候補、犬っこは通常時の対象外。静的調査まで完了、IMP-DOG-011 と共に保留 | hold | 再検討: IMP-DOG-011 の保留解除時。静的調査結果は [調査結果](../../plans/2026-09-21-avatar-pet-contact-research.md) に残す |
| [IMP-ENV-003](IMP-ENV-003-spawn-and-dropped-portals.md) | Low | 参加者が落としたポータルがスポーン地点の真後ろに置かれ、間違って入りそうだった (体験会 2026-09-26)。ユーザーがスポーン周りを手で調整する (2026-09-26 決定)。AI 側の作業は無し | hold | 再検討: ユーザーの手動調整後の体験会でまた真後ろにポータルが置かれたとき。それまで AI は触らない |

## 完了

| ID | 重要度 | 概要 | 日付 |
|----|--------|------|------|
| [DES-DOG-004](DES-DOG-004-river-play-and-floating-fetch.md) | Medium | 小川で犬が入水・浮遊・泳ぐ動作と、浮くボールを泳いで回収する遊びを設計する | 2026-09-27 |
| [DES-WLD-003](DES-WLD-003-map-and-travel.md) | Medium | 広い周回コースの地図、地点ワープ、プレイヤーへの合流と接続犬の同行を設計する | 2026-09-27 |
| [DES-ENV-001](DES-ENV-001-terrain-vs-blender.md) | High | 地形を Unity Terrain で作るか Blender で作るか → **Terrain 採用** | 2026-09-22 |
| [DES-UI-001](DES-UI-001-menu-and-leash-assist.md) | High | 古民家と揃えた手元メニュー (さんぽ / あそぶ / おもちゃ)・接続補助・目的地選択・集合・ボール個別回収 | 2026-09-22 |
| [IMP-DOG-002](IMP-DOG-002-leash-walking.md) | High | 先端 Pickup → 首輪接続、片手 3 匹・合計 6 匹、接続中の追従、Fetch 競合抑止、退出時解除、途中参加復元 | 2026-09-22 |
| [IMP-DOG-008](IMP-DOG-008-frisbee-variations.md) | Medium | ラブ 1 匹でフリスビーの走り込み・取り方・飛び方を試し、先回り型 + 動きの層に作り替え、6 プリセット全部採用 | 2026-09-22 |
| [IMP-ENV-002](IMP-ENV-002-skybox-and-night-mode.md) | Medium | 空を古民家と同じキューブマップにし、手元メニューのヘッダーに OFF / ON のナイトモード (RBS 不使用、乗算オーバーレイ + 空の露出) | 2026-09-22 |
| [VER-ENV-001](VER-ENV-001-prototype-view-and-edges.md) | High | 試作: 眺め・移動・端の見え方・実機負荷を確認する (代表シーン検証を兼ねる) | 2026-09-22 |
| [BUG-DOG-001](BUG-DOG-001-ball-spawn-slope.md) | High | ボール 6 個を坂から上の広場の平坦部へ | 2026-09-20 |
