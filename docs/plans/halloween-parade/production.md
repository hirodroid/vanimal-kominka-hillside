# かぼちゃ灯りの坂道行進 — Unity統合と検収

2026-10-14。新規制作はこの1本。制作Issueは [IMP-LIV-001](../../development/issues/IMP-LIV-001-halloween-parade.md)。制作物の存在、構造検査、実機での合格を分けて記録する。

## 2026-10-15 実機・マルチプラットフォーム確認と整理

ユーザーから「実機確認、マルチプラットフォームでの確認もOKでした」と報告を受領。ライブ009・専用CE接続後のパレードについて、ユーザーによる実機・複数プラットフォームの総合確認OKを記録する。端末名・人数・個別ケースの結果・実購入/ギフト取引の内訳は今回の報告には無いため、各チェック欄や計測値を推定して埋めない。以前の各版に書かれた「実機未確認」は当時の記録。Storefront運用の個別照合は引き続きCE台帳を参照。

不採用AIサムネイル、旧横長文字入り画像と制作スクリプト、縮小プレビュー、動画化済み600枚の連番、生成ツールの旧showplan履歴7本を、ローカルの `Logs/parade-cleanup-20261015/` へ相対パスを保って退避。計620ファイル・69.24MiB。削除ではなく復元可能な保管で、Gitには含めない。現行演出から旧historyのGUID参照が無いことを確認。採用CE画像・元写真・再出力スクリプト、採用演出/衣装/音源の参照、比較動画と検証記録は保持する。購入アセット本体と派生mesh/materialは既存ignoreを維持し、再生成用の`.meta`とコードを残す。

コミット範囲の独立レビュー担当は `parade_commit_review`（開発・保守観点、読み取りのみ）。パレード専用コードと連携差分、翻訳、コンパイル生成物を確認。別作業のUI案とテクスチャimport設定は除外する。本シーンには既にコミットされたIMP-UI-010の保存反映も含む（`18641ec`でパレードの区切りに保存差分を含める旨が記録済み）。

## 2026-10-15 専用CE ID接続 — 調整010の販売準備中を解除

ユーザーからListing `prod_a628b66e-0526-4cb7-b720-a559bdb40465`、Product `prod_1f3a7a1e-3fc0-45b3-b868-c2cf00839a83`を受領。`KD_ProductDefinitions`と[CE台帳D-1](../../development/design/ce-listings.md)へ反映し、既存の55で専用UdonProduct作成と購入/ギフト/所持開催を配線。56と5で検証・保存し、再読込後の56もPASS。商品backingの`ceListingId`と`ceProduct.ID`も受領値に一致。C#コンパイル0エラー。ランタイムコードは変更していない。証拠は[evidence/commerce-v10/ids-connected.txt](evidence/commerce-v10/ids-connected.txt)。

CE画像はユーザー了承済みの[1024×1024 PNG](storefront/halloween-parade-ce-thumbnail-1024.png)。実際のUnity演出画像、VAnimal配色、古民家ドッグラン同様の正方形構成。Storefront側の価格/期限/商品包含・実購入・ギフト受取・実VM開催は未確認、公開アップロードは未実施。以下のID未提供は接続前の履歴。

## 2026-10-15 調整010 — ライブ採用と通常販売メニューへの復帰

ユーザー「よし、ライブはOKです」により、統合009／音楽005／外観008／Timeline004の演出を採用。これは端末・通信・販売の検証完了とは分ける。追加指示は古民家ドッグランと同じ購入／ギフト／所持後開催、通知案内、広場の常設パネル撤去。以下の古い無料確認手順は廃止した。

- 「ライブ」に専用商品「かぼちゃ灯りの坂道行進」を最初から表示。未所持は「くわしく」→詳細→「買う」→既存購入確認→Store、所持済みは「開催」。古民家と同様、所持者の「ギフト」も既存の確認カードから同じListingを開く。ギフト受取後はCE所有通知を使う。
- 通知帯はこの商品の詳細へ。300 C、販売期限10/31を案内。時刻は古民家と同じ2026-10-31 23:59 JSTの境界を採用。以後は購入・ギフトを閉じ、購入済みの開催権は残す。Storefrontの実際の販売終了設定はID発行後に照合する。
- `KH_HalloweenParade/ParadeBoard`と`HalloweenPreviewRow`を撤去。犬利用者の許可Surfaceは維持。開催中、所持者は商品行の「進行を見る」で確認・取消へ戻れる。追従ボタンは作らない。
- `live.hillside_halloween_parade`を商品表の末尾へ追加。古民家Pumpkin商品と分離。管理側Beginも認証済みcallerの`OwnedBy`を再判定し、インスタンス内の他人の所持では開催できない。`developmentEnabled`は互換フィールドのみとなり、trueでも無料開催を許可しない。
- **接続前の履歴：専用Product ID／Listing IDは未提供だった**。両方空の専用予約行だけを許容し、空のUdonProductは作らない。商品はCE扱いで無料所持化せず、詳細は「販売準備中」、購入・開催は無効。ID発行後は[CE台帳](../../development/design/ce-listings.md)と`KD_ProductDefinitions.HillsideParadeProduct/Listing`を揃えて再適用する。

再適用: `Parade/55 - Apply Paid Menu` → `56 - Verify Paid Menu` → `5 - Save Verified Scene`。新規Udonを作った場合は`Menu/19b Assign Network IDs`も行う。`14/15`の無料確認メニューは廃止。`13`の手動入口もパネルを開くのみで、CE所持が必要。旧局所ハーネスはID配線と所有済みClientSim fixtureが前提となり、未設定ならPlay前に止める。

独立レビューは`parade_choreography_consult`（D/M/X、読み取りのみ）。進行画面への再入場、一般Setupの専用Panel配線・商品mapping検査を指摘され修正、限定再確認で必須指摘なし。C#/Udonコンパイル0エラー、専用商品のproxy/backing・catalog/ledger・イベント配線を確認。NetworkIDは既存390件を維持して専用商品1件（451）を追加。163件の契約検査はPASS。旧検査に残っていた001の挨拶式とC# float表記の変換漏れを、採用済み振付へ更新した。

証拠は [commerce-v10](evidence/commerce-v10/verification.txt)、[一覧](evidence/commerce-v10/ja-live-entry.png)、[販売準備中の詳細](evidence/commerce-v10/ja-live-detail-pending.png)、[13言語の配置](evidence/commerce-v10/layout.txt)。静止画は実UI/フォントのコピーによるEdit確認。左の既存演目と残高は保存時の文字で、所有状態を再現していない。実購入・ギフト・実VM所有開催・複数人・各端末の合格は未確認。今回アップロードはしていない。

## 2026-10-15 調整009 — 上空のカボチャと短調の大きな終幕

ユーザーは008を「大分いい感じになった」と評価し、問題の「パポ、パポ」は犬の挨拶ではなく、最後に上部へ大量のカボチャが出る山場と訂正。曲調を一気に変えるか、ハロウィンのおどろおどろしさで盛り上がる場面を求めた。音楽005で180–205秒を改作し、既存220秒時計のカボチャ185秒・締め200–205秒へ合わせた。

- 180–185秒: 刻みを減らし、C#dim7→A7(b9)の不穏な保持和音。最後の半拍に呼吸。
- 185–195秒: 木管とマレットの短い交互応答を休ませ、弦が2拍単位で歌うD minorの主題へ。弦の内声4＋旋律、オルガン4声、長いブラスの柱、低音とピアノの1/3拍で大きい足取りを作る。
- 195–205秒: 高い変奏で二段目の山、200秒の属和音から202.5秒にD minorへ着地。205秒の既存Bb→C→Fコーダで明るさを戻す。打楽器は4拍/8分の基盤を維持し、32分の短い連打を使わない。

[音楽005の構成](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/005/composition.md)・[根拠](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/005/musical-basis.md)・[独立譜面レビュー/技術検査](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/005/review.md)。全10パートの180秒未満と205秒以降MIDIは004と一致。独立担当parade_musicは和声/持越し/タイ/旋律/境界に追加必須なし。10実音源の発音/MIDI検査errors0/warnings0、220秒48kHz stereo24bit、10ステム、handoff4874イベント、保存再読込を確認。AIの実聴・005のユーザー試聴は未実施。弦上声の聞こえ方と「最大の山」としての聴感は確認を残す。

[試聴用175–215秒](evidence/grand-finale-v9/audio/grand-finale.m4a)。現行は **音楽005／統合009／外観・表示008／Timeline004**。Unity既存master.wavを.meta/GUID保持で置換。[import/hash記録](evidence/grand-finale-v9/audio-import.json)と[配線確認](evidence/grand-finale-v9/unity-verification.txt)。このターンの開始時Scene/.metaは不変更、Edit/dirty=false。Runtime/同期/UI/外観は変更していないので008の画像や局所VMを再実行しない。008のPASSを005の音響/実機合格へ読み替えない。

再生成は --audio C:/work/unity/vrc-music-agent/songs/KH_HalloweenParade/versions/005/output/master.wav を指定する。その後の灯り/広場非表示の再配線は008の手順。[Mobile Caps](evidence/grand-finale-v9/mobile-caps.md)変更0/不足0。販売ID・全編/端末/通信・最終採用は継続確認。公開アップロードはしていない。

[Android代理生成物](evidence/grand-finale-v9/bundle-sizes.txt)はdownload84.44MB、非圧縮254.68MB。最新rawの[BuildReport](evidence/grand-finale-v9/build-report.txt)はSucceeded/errors0/warnings2。圧縮個別reportは未保持のため、その成功statusを推定しない。最終[保存manifest](evidence/grand-finale-v9/integration-manifest.json)でも、このターンの開始時Scene/.metaに変更なし。

## 2026-10-15 調整008 — 挨拶の拍と、広場の案内・洗い場を非表示

007へのユーザー評価は「大分いい感じ」。犬が順番に挨拶する2:30–3:05の短音が流れを崩すとの指定を受け、音楽004で150–185秒だけ編曲を修正した。61–66小節はマレットを拍頭へ、67–74小節は木管を0/1/2拍、ブラスを0/2拍の保持和音へ。返事は低いマレット1音、打楽器の32分連打を外して節目の8分フィルに整理。前半150秒と185秒以降は全10パートMIDIを003から維持。[作曲・検証記録](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/004/review.md)。AIの実聴・004のユーザー試聴は未実施。[挨拶部分の試聴用145–190秒](evidence/greetings-v8/audio/greetings.m4a)。

広場のアイテム案内と洗い場を、パレードPlaying中だけローカル非表示にする。対象は KH_ItemGuideBoards/KH_ItemGuideBoard_Hillside と KH_WashStations/Hillside。洗い場root・ObjectSync・同期処理を停止せず、Renderer.forceRenderingOff、CanvasGroup、UI raycaster/形状Collider、Pickup受付だけを抑制する。終了・取消・Playback無効化で復元。選択工具は開始前の値へ巻き戻さず、現在slotからpickupableを戻す。持っている道具と使用中台は描画を維持する設計（held中も新規Pickup受付はfalse）。[Dレビューと確認限界](evidence/greetings-v8/review-d.md)。実際のheld/drop、使用中台、非表示中の通信選択変更は未検証。

現在の組合せは **音楽004／演出表示008／購入モデル007／Timeline004**。既存Unity音源のGUIDを保って置換した。[hash・import設定](evidence/greetings-v8/audio-import.json)。再生成時は --audio C:/work/unity/vrc-music-agent/songs/KH_HalloweenParade/versions/004/output/master.wav を指定し、既存の灯り適用後に Parade/52 で広場を配線、53・9で検査して5で保存する。アイテム案内または洗い場を再構築した場合も52→53が必要。

### 008の検証

C#・Udonコンパイル新規エラー0、[配線検査](evidence/greetings-v8/plaza-verification.txt)、16対象backing照合を確認。[限定実VM試験](evidence/greetings-v8/plaza-lifecycle.json)でPlaying非表示→Disable復元→再有効化で非表示→Cancel復元、現在slotに従うPickup受付、他3洗い場不変がPASS。Scene/source/PlayerData/settingsも復旧。初回はテストの主洗い場path誤認で開始前[INVALID_SETUP](evidence/greetings-v8/plaza-invalid-setup.json)、修正後の結果と分ける。通常220秒終端・保持工具・使用中台・実通信の合格ではない。

[8視点の比較用画像](evidence/greetings-v8/views/report.json)を記録。Edit単眼、静止犬meshを配置したコピー。実音同期/犬の実動作/VRの検証ではない。描画抑制をfinallyで戻し、シーンは撮影前後ともdirty=false。[Mobile Caps](evidence/greetings-v8/mobile-caps.md)変更0/不足0。

[E画像レビュー](evidence/greetings-v8/review-e.md)は今回変更による必須問題なし。案内/洗い場の除去と犬・灯りを維持した範囲だけ採用可。[Android代理生成物](evidence/greetings-v8/bundle-sizes.txt)はdownload84.49MB、非圧縮254.73MB。最新rawの[外側BuildReport](evidence/greetings-v8/build-report.txt)はSucceeded/errors0/warnings2。圧縮個別reportは未保持のため生成物のサイズのみを記録。実機性能・SDK公開の合格ではない。[一般ユーザーのLive入口](evidence/greetings-v8/public-preview.txt)を維持、Scene hashは局所VM後も同一。[保存manifest](evidence/greetings-v8/integration-manifest.json)。最終Edit/dirty=false/StandaloneWindows64。アップロードは未実施。音の聴感・全編実機・端末/通信・販売IDは継続確認事項。

## 2026-10-15 調整007 — 先導灯・前向きの街灯・広場のフィナーレ

006に対しユーザー「めちゃいい感じ」と評価。追加指定は先頭かぼちゃのEmission、かぼちゃ街灯を進行方向へ、広場に着いてから曲にもう一段の盛り上がり。006の「同行中は後ろの観客側へ向ける」という判断をこの指定で置き換える。

- 先頭guide-journeyだけ専用LeaderPumpkin材質へ分離し、テクスチャの色を残す表面発光を追加。他6灯は従来値。共有shaderの追加プロパティは既定0で、既存描画・深度と薄暮の扱いを維持する。Eの画像レビューで顔の明暗差不足が出たため、表面Emissionを1.25から0.6へ抑え、先頭専用meshに小さな内部灯を結合した。原本atlasの灯具部分を参照し、殻に遮蔽される球の黄白色が目・口の穴からだけ見える。既存の先導PS1個の中に収め、別の追従処理・Realtime Lightは追加しない。
- かぼちゃ街灯とろうそくの逆向き補正を外し、モデルの前面−Zを一座の歩行正面へ合わせる。95/100/110/120秒の経路微分との角度差は0.001度未満。広場への整列・回頭は従来Timelineを使う。
- 曲を音楽003へ差し替え。150秒から犬ごとのブラス挨拶と上昇する木管応答、170秒から和声の上昇、185秒で新しい高音主題と四声ブラス・五声弦の最後のサビ、200秒からF終止。205秒以降の写真時間は002を維持する。全体220秒・96 BPM、前半150秒の10パートMIDIも維持。単に全体ゲインを上げる変更ではない。

音楽の[制作記録](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/003/composition.md)・[根拠](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/003/musical-basis.md)・[レビュー](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/003/review.md)。独立譜面レビューのF/A低音指摘を修正後、10パート実音プローブ・全曲/10ステム・handoff・保存再読込を確認。クリップ0。AIの実聴や003のユーザー試聴は未実施。

### 再生成と現在の組合せ

Timeline/showplanは004、外観markerはFinale007、音楽は003。過去版004のshowplanを履歴のまま保持するため、そこに書かれたaudioFileは002である。再import時は **--audio C:/work/unity/vrc-music-agent/songs/KH_HalloweenParade/versions/003/output/master.wav** を明示し、その後Parade/46で灯りを再適用、47・9で検査し5で保存する。Unityの既存master.wavは.meta/GUIDを維持して差し替えた。[導入音源のhashとmeta](evidence/finale-v7/audio-import.json)。購入原本は[223ファイル変更0](evidence/finale-v7/source-verification.json)。

### 007の統合・局所確認

本シーンへ適用・保存。C#コンパイル新規エラー0、Runtime変更はなくUdonの再試験は省略。[構造検査](evidence/finale-v7/verify.txt)と[18視点](evidence/finale-v7/views/report.json)、[3本の確認映像](evidence/finale-v7/motion/README.md)を保存。[Eレビュー](evidence/finale-v7/review-e.md)は顔のコントラスト指摘を内部灯で解消し追加必須なし。[Dレビュー](evidence/finale-v7/review-d.md)は再適用/GUID/他粒への波及に重大欠陥なし。画像はEdit単眼、映像は静止犬meshを目標位置へ置いたコピー。曲は後付けで実音同期の検証ではない。[広場の145–220秒を切り出した試聴用](evidence/finale-v7/audio/plaza-finale.m4a)も保存した。

[Mobile Caps](evidence/finale-v7/mobile-caps.md)は83対象changed=0/missing=0。[Android代理生成物](evidence/finale-v7/bundle-sizes.txt)はdownload 84.44MB、非圧縮254.66MB。内側scene-buildのログがFailureと出たため独立Dへ限定相談し、最新の[外側BuildReport](evidence/finale-v7/build-report.txt)を読取。今回のraw出力と時刻が一致しSucceeded/errors=0/warnings=2を確認した。圧縮版の個別reportは保持されていないため、そちらの成功statusへは拡張せず、新規出力ファイルのサイズを記録する。再ビルドはしない。実機メモリ/描画性能/SDK公開の証拠ではない。

[一般ユーザーのLive開催入口](evidence/finale-v7/public-preview.txt)・16対象backing・先頭mesh割当1個（3148tri）を再確認。本シーンはEdit/dirty=false/StandaloneWindows64。診断用一時Editorスクリプトを削除しC#コンパイルエラー0。保存成果の[hash](evidence/finale-v7/integration-manifest.json)を記録。公開アップロードはしていない。

技術検証と実機の採用を分ける。今回Runtimeコード・同期・犬の振付は変更しておらず、006の限定VM試験を007の全編合格とは呼ばない。販売ID・端末/通信・全編の最終検収は引き続き未完。

## 2026-10-15 灯り006 — 薄暮の灯りの一座

ユーザーは005の改善を評価し、外灯の足・自作の木を撤去、購入ろうそく/かぼちゃ外灯の増員、開演中の薄暗さとEmission、犬飾りの発光を指定。方針はcore-conceptの犬主役とビジュアル方針。道の中央と犬の顔を空け、橙金の灯りを外周へ置く。ホラーの暗闇ではなく表情の読める夕暮れを狙う。005以前の木/足踏みの指定はこの更新で置換。

- 旧木2体の経路をろうそく隊へ（大小3本ずつ＋かぼちゃ外灯各1本）。既存外灯2体は足を取り除き、蝙蝠外灯各1本と小ろうそく各2本を追加。合計10ろうそく・6外灯、帽子2/蝙蝠12/灯りPS7/犬6は保持。内部TreeL/RはTimeline経路の互換名のみ残す。
- 材料: 同じIKA購入パックのCandles/Candles_B/Candle_Pumpkin、Outside Lamp01/04追加。原本/権利条件は005と同じ。派生mesh/materialはIKA2022へ、元フォルダは不変更。サブメッシュで分かれた炎/灯具だけ後段発光、少量の柔らかいhalo。犬リボンは金色模様を発光し、全面白色化しない。Realtime LightやBloomと同一視しない。
- 夜: NightModeへ専用paradeDarknessを追加、max(Auto/Manual,live,parade)。最大.72、共有Elapsedから開始4秒/終端5秒でフェード。Cancel/Disable/参照不足は0。手動保存値・自動時刻・他liveを変更しない。通常Emissionまで暗くなるoverlay4999のため、深度判定付き発光だけ5000へ。
- E事前相談: 木太鼓の動作をろうそくへそのまま流用せず、緩い浮遊と左右の光の返事へ。D相談:保存する手動night APIを使わず、専有入力を追加し現在の基準へ戻す。

代表確認は道中の返事50–65秒、同行100秒、祝福190秒と犬飾り近景、開演/終演。Edit単眼画像の判定をVR/実音同期/実機合格と分ける。

### 006の統合・確認

本シーンへ適用・保存。C#/Udon compileはいずれも新規エラー0。[構造検査](evidence/lantern-v6/verify.txt)で10ろうそく/6外灯/6リボン、木/足のrenderer撤去、Night参照とbacking、粒の後段描画、NetworkID欠損なしを確認。元の音楽002/220秒と犬の振付・一般ユーザーのLive開催入口、005の追従UI撤去は保持。購入原本は[223ファイル変更0](evidence/lantern-v6/source-verification.json)。

Eの初回指摘は、かぼちゃが暗い置物に見える、帽子/蝙蝠が空に埋もれること。かぼちゃ内部に共有meshの小さな発光体を入れ、元の殻で遮蔽して目・口だけ光らせた。同行中は後ろの観客側、145–155秒で広場正面へ向ける。背面の光のはみ出しを近景で縮小調整。帽子は紫の帯だけ、蝙蝠は控えめな翼の明度差を足し、リボンの金模様と犬の顔を維持。[39視点](evidence/lantern-v6/views/report.json)と[20詳細視点](evidence/lantern-v6/details/report.json)、[確認映像と制限](evidence/lantern-v6/motion/README.md)を保存。Eの静止標本再確認は必須2件解消、追加必須修正なし。

[Dレビュー](evidence/lantern-v6/review-d.md)では、入力の破壊前検査、marker確定順、PS材質の再適用パスを修正。実VMの最初のDisable試験は暗さが残りFAIL。診断でOnDisable直後は0だが、無効中にUpdateが再開していたためenabled/activeInHierarchyの関門を追加。同じ限定[夜ライフサイクル試験](evidence/lantern-v6/night-lifecycle.json)がPASS。Disable直後/0.3秒後とも停止・暗さ0、reenableで経過5.238秒へ復帰、Cancelで6匹の利用制限と撫で抑制を解除。手動値/Auto選択とScene/PlayerData/settings/sourceの復旧を確認。このrunの復帰基準は0で、非0基準や通常220秒終端、実通信途中参加の試験へ拡張しない。フェード端点はEditorの純関数標本で別確認。

[Eの時系列再確認](evidence/lantern-v6/review-e.md)は15標本（返事5/方向転換5/同行3/祝福2）で追加必須修正なし、ユーザー確認へ進めてよいとの判断。連続再生/実音は未確認。905フレームを5本の確認映像へ保存したが、犬Animator/実音同期の証拠にはしていない。

[Android代理Bundle](evidence/lantern-v6/bundle-sizes.txt)はdownload 84.43MB、非圧縮254.65MB、100MB上限まで15.57MB。[Mobile Caps](evidence/lantern-v6/mobile-caps.md)は83対象changed=0/missing=0。購入原本は変更せず、既存表の範囲での適用/検証。描画負荷や実機メモリの測定ではない。ビルド後もScene SHA256はNight試験時と同一、Edit/dirty=false/StandaloneWindows64。[Live入口](evidence/lantern-v6/public-preview.txt)と16対象backing・構造検査を再確認。

VRChatへのアップロード・端末/通信/全編の最終検収は未実施。この版では販売ID未発行だったが、2026-10-15に接続済み（冒頭参照）。


## 2026-10-15 外観005 — IKA購入モデルへの差し替え

ユーザーは004を「めちゃくちゃいい感じ！」と評価し、追従する「パレードへ戻る」を不要と指定。導入済みのHalloween Decoration_2022へハロウィン系モデルを差し替える。公演時刻・曲002・6匹の動作は保持。これは全端末・通信・販売の完了判定ではない。

方針はcore-concept §3、犬が主役。帽子・外灯・かぼちゃ・蝙蝠を購入モデルへ。木の太鼓・葉・犬リボンは対応品がないため保持。Hatは壁飾りなので立体のHat-shaped shelvesを司会役に採用。Outside Lamp_02/03を左右の照明役、Batを翼の動く伝令、Ornamental Pumpkin_02を7灯へ。肩・灯具の重みを付け、既存Timelineの関節を使用。家具や料理を無目的に追加しない。

### 素材・利用範囲

出所はユーザー導入済み Assets/IKA 3DCG art studio/Halloween Decoration_2022、作者IKAmeshi/IKA 3DCG art studio。[公式商品ページ](https://booth.pm/ja/items/4223874)の2025-01-15 VCC更新版表記と規約を2026-10-15確認。VRChat利用、作品・ゲームの販売、色/形状改変と改変物のゲーム組込み可。著作権表示・報告は必須ではない。モデルそのものや改変モデルの配布/販売は禁止。今回はローカルでワールドへ組込み、生成AIへ素材を入力しない。購入原本と派生mesh/material本体はGit対象外、派生.metaはGUID維持のため管理対象。原本を編集しない。元ファイルhashはevidence/ika-v5/source-hashes.json。

派生meshは Data/HalloweenParade/IKA2022。Parade/41で再生成し、既存meshはCopySerializedでGUID維持、初回/別checkoutでは生成後にシーンを再配線する。選択記録 IKA2022Selection.json がある場合、StoryCast再構築/Show再Bindでも差替を適用する。

### UI

閉じた後の小型再表示面と追従処理を撤去。既存Live一覧または広場のパレード板から再び開ける。許可を閉じても同意にはならず、未回答期限と洗浄開始者の同意条件は保持。

### 005の統合・確認範囲

ローカル本シーンへ適用・保存。追従resumeのGameObject、proxy/backing参照を除去し、[一般ユーザーのLive開催入口](evidence/ika-v5/public-preview.txt)は維持。C#とUI変更後のUdon compileはエラー0。[構造検査](evidence/ika-v5/verify.txt)で2帽子・14SkinnedRenderer・7かぼちゃPS、材質/関節とNetworkIDを確認。購入原本223ファイルの[hash変更0](evidence/ika-v5/source-verification.json)。

購入meshのFloat32頂点色がPSで黒い斑点になる問題を比較診断し、Color32へ統一して解消。既存派生meshのGUIDを維持してGPUデータも更新。[Dレビュー](evidence/ika-v5/review-d.md)は主要指摘解消、新しい重大欠陥なし。

[39静止視点](evidence/ika-v5/views/report.json)、[道の門](evidence/ika-v5/motion/gate.mp4)、[一座](evidence/ika-v5/motion/ensemble.mp4)、[祝福](evidence/ika-v5/motion/blessing.mp4)を保存。[映像の制限](evidence/ika-v5/motion/README.md)参照。Editコピー603フレーム、240 root姿勢とactivation境界を確認。犬のAnimator・実音同期・通常VMの実行映像ではない。005のVR実機確認とアップロードは未実施。

[Android代理Bundle](evidence/ika-v5/bundle-sizes.txt)はdownload 84.71MB、非圧縮252.44MB、100MB上限まで15.29MB。Apply Mobile Capsは83対象changed=0、Verify missing=0。今回の購入テクスチャはその既存cap表の対象外で、原本を保持して計測。端末性能やVRAMの測定ではない。保存済みEdit状態へ戻り、activeBuildTarget=StandaloneWindows64、dirty=falseを確認。

[Eレビュー](evidence/ika-v5/review-e.md)では犬6匹の視認と外周構成に重大な悪化なし。暗い帽子の応答は、本番60–65秒の前/最深/復帰を2視点12枚で追加確認し、左右ともお辞儀として読めるため保留解除、追加修正指摘なし。横向きの輪郭の弱さは残るが、本番の応答方向を変更する必要はない。

## 2026-10-14 演出004 — 仲間が目覚めて行進する

ユーザー評価: 003で「大分良くなった」「曲とワイワイ感は出た」。一方、紙吹雪が塊で動く・ちゃちな見え方を不採用。各粒のモチーフ/時刻/場所/動きに意味を持たせ、道沿いアーチ・外向き・雨、木・街灯・帽子等の同行を求めた。以下の003物量方針より今回の演出の意味を優先する。

現行制作は[演出004](../../../../../vrc-live-generator/shows/kh-halloween-parade/versions/004/production.md)。曲002と6匹の振付は保持。木は太鼓、街灯は照明役、帽子は司会、蝙蝠は伝令として、目覚め→道の門→掛け合い→同行→外周楽団→祝福→写真→おやすみへ展開する。粒数だけで完成を判定しない。

今回のBlender新作: 木/街灯/帽子/翼の分離した蝙蝠、13部品。素材と原本、面/UV/FBX往復検査は[companion-source](../../../../../vrc-live-generator/shows/kh-halloween-parade/versions/004/companion-source/)。専用の新規Imagegen atlasを使用、入力参照画像なし。3Dの厚み、根/足/灯具/枝/翼の別関節をTimelineへ焼く。18役者・共通1clip+18表示track、Particle復元へ混ぜず新Udon常時loopは追加しない。

旧5秒周期festival()の一括散布を外し、道の固定された左右門から時差アーチ、犬Hop後の外向き扇、木から持ち上がる葉、最後のサビで個別軌跡の降下へ。209の単粒、モデル同時最大70粒。豪華さは3Dの一座と軌道の構成で作る。描画call/実機性能は粒数から推定しない。

E `/root/parade_entertainment` とD `/root/parade_choreography_consult` の実装前相談を実施。D指摘の蝙蝠activation境界に正確な時刻キーを追加し、非表示時の地中座標を撤廃。再Buildの入力/地面検査と一時clip作成を、旧成果の置換より前へ移した。画像/動画・保存後検査の結果はこの節へ追記する。

### 004の統合・確認範囲

- ローカルの本シーンへ209単粒と18体の一座を反映、保存。音楽002のSHA256一致。C#コンパイル成功、16対象backing参照、一般ユーザーのLive開催入口、mesh/texture/material欠損0を確認。[import](evidence/story-v4/import.json)、[構造](evidence/story-v4/verify.json)、[入口](evidence/story-v4/public-preview.txt)。セクションをまたぐ粒のtailは意図した継続で、曲/公演は220秒。
- 木の枝冠・太鼓のマレット、街灯の両足、蝙蝠の独立した2軸の羽ばたき、帽子のお辞儀を制作。Blender13部品は合計22,868tri（重複配置前）、ゼロ面/ゼロUV面/非多様体各0、FBX往復一致。新規画像の生成文・hashは原本のcompanion-source/imagegen-provenance.jsonへ保存。
- 小型犬を前へ、終盤隊列を5.75m幅へ調整。36clip配線と6本のNavMesh到達、目標速度最大0.999m/s、最小root間隔1.154mを確認。実際の犬の体や衣装の接触判定ではない。
- [cast配線](evidence/story-v4/cast-wiring.txt)と[240姿勢](evidence/story-v4/motion-wiring.txt)はEditコピーで確認。最大位置差約0.000015m。足の最低点は地面から-6.07〜+5.05cmで、片足の浮きや自然な体重移動の合格にはしない。D独立レビューは旧P1解消・新しい重大欠陥なし、[制限を併記](evidence/story-v4/review-d.md)。
- [39視点](evidence/story-v4/views/report.json)、[道の門](evidence/story-v4/motion/gate.mp4)、[一座の行進](evidence/story-v4/motion/ensemble.mp4)、[祝福](evidence/story-v4/motion/blessing.mp4)を保存。映像の犬は静止meshを目標へ配置、飾りと粒をTimelineで評価。音楽は該当時刻を後付け。[証拠の範囲](evidence/story-v4/motion/README.md)を参照。犬ダンス・実音同期・通常VM/VRの映像ではない。

Eレビューでは蝙蝠の輪郭、帽子の大きさ、6匹の顔の分離、門の形成が改善。祝福で葉と蝙蝠の動きが似る指摘を受け、蝙蝠だけが8秒で左右へ6.5m抜けるよう修正。修正後は最新静止画1枚と185/188/190/193/195秒の5枚をEが再確認し、この必須指摘は解消、追加指摘なし。[Eレビュー記録](evidence/story-v4/review-e.md)。004のユーザー採用、220秒全編の実行、通信・端末・負荷は未確認。VRChatへのアップロードは今回行っていない。

## 2026-10-14 演出003の履歴 — ポップ素材・厚い音楽・大量の祝祭演出

003時点の指示は「リアルな細密描写ではなく、遠くから分かるポップな大柄」「かぼちゃの縦縞を明瞭に」「曲が寂しいので3〜5音の和音と多彩な音」「パーティクルを100倍ぐらい賑やかに」。以下の002履歴にある控えめな素材・粒の方針を上書きする。

演出版003の[制作票](../../../../../vrc-live-generator/shows/kh-halloween-parade/versions/003/production.md)、音楽版002の[編曲仕様](../../../../../vrc-music-agent/songs/KH_HalloweenParade/versions/002/composition.md)を使用した版の履歴。既存の6匹・専用ダンス・洗うボタン押下者の同意・一般ユーザー開催は維持する。

- **素材**: 新規Imagegenアトラス2枚をBlenderで貼り直し。かぼちゃの太い縦縞、葉の太い葉脈、リボンの橙紫の縞と大きい星。布目や細かい擦れを主役にしない。かぼちゃ2344／葉248／リボン668tri、FBX往復とUV・面の検査を実施。
- **音楽**: 10パート、冒頭から5声弦・4声伴奏・3声ブラス。犬の掛け合いと75秒Hopへの返事、130秒の入場変奏、185秒の重奏回帰。220秒／96BPMは保持。全10パートの実発音、音割れなし、REAPER再読込一致を確認。独立譜面レビューのベル上昇句を修正。AI実聴とユーザー002採用は未確認。
- **粒**: 左右の星噴水、頭上の紙吹雪、キャンディ・こうもり・大きな星・上下するかぼちゃ。588インスタンス、モデル上最大32稼働system／3374粒。002の最大14粒から拡張。粒数を楽しさ・性能の合格根拠にはしない。
- **復元**: 固定seedのLocal単発burstを複数粒へ拡張。World履歴を要する重力・collision・trail等は使わず、可変刻みの連続進行と一括seekをEditで比較。実通信・通常フレームのPlay復帰・VR性能の合格へ読み替えない。

E担当 `/root/parade_entertainment` とD担当 `/root/parade_choreography_consult` の独立相談、音楽譜面 `/root/parade_music` の独立レビューを実施。各担当は未実聴を明記しており、エンタメの完成認定ではない。003の統合確認結果は `evidence/festival-v3/` へ保存する。旧002の動画を003の映像として見せない。

### 003の今回確認できた範囲

- Unityへ588演出と音楽002を導入し、保存済み。C#／Udon compile成功、16対象のbacking参照一致、一般ユーザー用Live開催入口の検査PASS。[構造検査](evidence/festival-v3/verify.json)は欠損prefab・材質・texture・mesh・error shader各0。[入口](evidence/festival-v3/public-preview.txt)。音源SHA256は`27a6b94146a0f2c52f96041c4aeb8665f2aecff212a468e92ee3e9f33b6ed5da`。
- 80の星噴水を細かく広く上へ散らし、4枚の2D材質を専用unlit shaderへ変更。粒数は維持し、旧撮影で犬を隠した不透明な星の帯と濁った黄橙を改善。[75.8秒の道脇](evidence/festival-v3/views/075.8-roadside.png)、[190.8秒の広場](evidence/festival-v3/views/190.8-plaza-middle.png)、[衣装](evidence/festival-v3/views/218.0-ribbon-0.png)。[全25視点の記録](evidence/festival-v3/views/report.json)はEditコピーの静止画で、ダンスの実行映像ではない。
- E `/root/parade_entertainment` が5画像を独立実見。「ポップ模様・賑やかさを維持し星による遮蔽が改善、静止画修正は採用してよい」。広角では小型犬が小さく、踊りの可読性・動く粒の遮蔽・曲との一体感は未確認。追加の静止画修正を必須とする指摘なし。
- 実PSのEdit集計では190.8秒に32稼働system・3374粒。実機fps・メモリの証拠ではない。軽減設定は572 detail renderersを非表示にするが、粒子simulation自体の削減ではない。
- **seekの厳密同値は未達**。[比較100件](evidence/festival-v3/banks.json)の10件NG。最大位置差2.381cm・寿命差約30ms・回転差1.65度、寿命末3msでは連続側に粒が残り一括側は0。閾値を緩めてPASSにはしていない。旧fixed-step失敗も[別記録](evidence/festival-v3/banks-fixed-step-failed.json)に保存し、runtimeは従来の`Simulate(age,false,true,false)`を維持。Unity内部刻みが原因とは断定しない。
- D相談により、固定1/60の大量Udon呼出しへ変更する前に[別の視覚比較](evidence/festival-v3/seek-visual/report.json)を追加。実素材・裏山背景、1m/3m、差の出た時点±50msを48枚取得。寿命末3msの残存alpha最大1/255、50ms後は両側0。Dは1mの星2.5秒／蝙蝠3秒の4枚を独立実見し「微差はあるが大きな構図変化や群れ全体の欠落なし」と確認。全48枚の独立確認ではない。静止画での限定確認であり、通常Playのseek直前直後や通信遅延の合格とはしない。

音楽の実聴、003の220秒を通した演技と実音同期、複数人／途中参加、VRでの負荷は未確認。構造検査・静止画レビューをエンタメ完成やユーザー採用へ読み替えない。ローカルUnityには確認用として反映済みで、今回VRChatへのアップロードはしていない。

## 2026-10-14 実見後の不採用と改作（002の履歴）

ユーザーが全体を確認し、「犬が歩いているだけ」「派手さ・パレード感・踊りがない」「テクスチャの描き込みが少ない」と演目を不採用にした。従来の「未確認」はこの演出品質の判定には適用しない。下記001の構造検査・静止画・復旧検査は、楽しさの合格を意味しない。

独立した **E（VRライブ／パレードのエンターテインメント専門）担当**を追加した。設計相談担当 `/root/parade_entertainment`、リグ技術相談担当 `/root/parade_choreography_consult`。両者は読み取りのみ。Eは実VMの時系列PNGとログを確認したが、動画連続再生・実音の試聴は未実施。Eの指摘「犬が演じる設計がない」を採用し、旧方針「歩行中に大きな芸を入れない」「185秒以降だけが山」を撤回する。

改作002は、足の接地を伴う専用四足ダンス、ペアの呼び掛けと返事、全員の決め、観客に顔が見える配置を中心に作る。かぼちゃ・葉・衣装の描き込みも増やす。6匹・新曲1本・同意と後始末・一般ユーザー用の開催入口は維持する。代表25秒の連続映像で足運びと掛け合いを先に評価し、静止画だけで演目を合格にしない。ユーザーの再評価前に完成扱いにしない。

制作中の専用クリップは `Parade/20 - Build Dance Clips`、検査記録は `evidence/dance-v2/`。素材改作のBlender原本は Live Generator `shows/kh-halloween-parade/versions/002/ribbon-source/`。新しいアトラスは built-in Imagegen による今回の新規生成（2026-10-14）、かぼちゃ表皮／金色の紙／茎／秋の葉の4領域。参照画像は使用していない。生成画像を単色パレット用の三角形ごとのUVへそのまま貼らず、連続UVへ改作する。

### 改作002の変更と今回の証拠

- 専用Generic動作はGroove / Reply / Bow / LookLeft / LookRight / Hopの6語彙×6犬種、36クリップ。既存の通常歩行と首かしげだけで構成する方針を変更した。36種類の芸という意味ではない。
- 55–80秒で行進を止め、前後順を保った斜めの3ペアへ開く。同行・道脇へ身体を向け、通常の呼び掛け→返事→ポメだけ速い返事と前足保持→コーギーのお辞儀→全員ステップ→全員Hop→列へ戻る。準備旋回だけ120度/秒、位置・速度・角度が落ち着くまでダンスへ入らない。
- 55–70秒は小さい3灯を保持し、音符ごとの粒を止める。70/75秒で葉が広がる。全体178インスタンス、モデル上の同時粒子最大14、リアルタイムライト0。軽減設定は新しい扇の葉にも接続した。
- 終盤は共通の終点へ集まる方式をやめ、最後の経路角から各持ち場へ分岐。Editで6本のNavMesh直線到達、全対象時刻の目標速度最大0.999m/s・根の最小間隔1.295mを確認。runtimeの移動は既存SnapToGroundを利用する。胴や衣装の非接触、終盤の実行時到達をこの値だけで合格にしない。
- かぼちゃ・葉・布を描き込んだ1774×887アトラス2枚へ変更し、Blenderの連続UVへ貼り直した。布の織り・縫い・月と葉の刺繍、葉脈、かぼちゃの表皮と茎を追加。

[道脇からの25秒](evidence/dance-v2/runtime/roadside.mp4)が主映像。[同行位置の補助映像](evidence/dance-v2/runtime/following.mp4)は下端で大型犬が欠け、手前の灯りが大きく見える。全6匹が見える完成構図として扱わない。[スタジオ部品動画](evidence/dance-v2/studio-vocabulary.mp4)は別の配置で6語彙を確認するEditサンプルであり、本シーンの実行結果ではない。

最新の限定実VMは `20261014-110733-975-390cd5`。正規開催→0–82秒の自然進行→中止→6匹復帰・抑制解除・元のdevelopmentEnabled=true維持・Scene/source/PlayerData保全を確認。[報告](evidence/dance-v2/runtime/report.json)、[出番の観測](evidence/dance-v2/runtime/cues.csv)、[各犬の状態](evidence/dance-v2/runtime/dogs.csv)。6匹×6出番を固定時刻の+0.25〜0.9秒の観測窓で検査し、全36件で実state・パラメータ・遷移終了を確認。初回観測は+0.278〜0.526秒。最短到達時間ではなく、音楽的に遅れが気にならないという判定でもない。ポメの高速返事と保持はCallTimeも照合した。

収録は55.023〜79.981秒の333枚、平均13.30fps、最大間隔0.199秒。固定24fpsへの読み替えをせず[取得時刻](evidence/dance-v2/runtime/frames.csv)の差分で動画化し、元WAVの55.023秒からを後付けした。実音出力・実音同期・滑らかさの合格証拠ではない。生画像は `Logs/kh-halloween-parade-harness/<runId>/dance/` に保存。

動画のHop付近に出る小さな橙色の矩形は、肉球用のstraight-alpha画像を既定のPremultiplyシェーダーで描いていた表示不具合。収録後に肉球専用で`Legacy Shaders/Particles/Alpha Blended`を明示し、再生成・再インポートした。[修正後の実材質描画](evidence/dance-v2/paw-transparency.png)で肉球形と背景の透過を確認。動画は修正前の記録として保持し、加工して消していない。犬のruntimeや時刻はこの修正で変えていない。

初回の収録は「向き直りで先手の出番が欠落」、次の収録は「単発assertが切替途中を失敗と判定」で終了。前者を演出・旋回修正、後者を固定観測窓へ変更し、今回の結果と区別した。全220秒・複数人・端末の確認は行っていない。

Eは新スタジオ33枚・実景道脇16枚・同行10枚とログを独立確認し、重なりと背中・飾りの強弱の改善、Hopの輪郭差を認め、「55–80秒の改作候補としてユーザーの再評価へ出せる」と判断。MP4連続再生と実聴は未実施。ポメの出番が説明なしで伝わるか、Grooveの賑やかさ、全編の楽しさは未合格。静止画の改善を演目全体の採用へ読み替えない。

## 初稿001の制作履歴と生成元

| 対象 | 原本・再生成元 | 現在の確認範囲 |
|---|---|---|
| 新曲001 | [Music Agentの曲入口](../../../../../vrc-music-agent/songs/KH_HalloweenParade/README.md) | 220秒、96 BPM、88小節。WAV/MIDI/REAPER保存・再読込検査。実聴・音色採用は未確認 |
| 演出001 | [制作票](../../../../../vrc-live-generator/shows/kh-halloween-parade/versions/001/production.md) | 実音源hashを照合した127層。音符対応、専用Blenderかぼちゃ/葉、途中参加用データ生成 |
| 犬のリボン | [制作票とFBX](../../../Assets/KominkaHillside/Data/HalloweenParade/Ribbon/README.md) | 今回専用Blenderモデル。単体検査済み、6犬種fitはUnityで別検査 |
| ルート | [production-route.json](production-route.json) | 現行シーンのNavMesh経路。節13→14の設備除外域を迂回。約79.92m、先頭開始弧長14.2013m |
| 粒子の時刻復元 | [runtime-restoration.json](runtime-restoration.json) | 演出生成器が出すデータの保存コピー。showplan hashとの一致を確認して取り込む |

[A詳細](plan-a.html)と`route-snapshot.json`は企画・経緯の図。Unityへ配線する座標は`production-route.json`。実測点数が同じでも座標が違えば配線検証を失敗させる。犬配列はCorgi/Pome/Lab/Dachs/Shiba/Samoyed、行進順位は`[0,1,4,2,3,5]`。半円は行進順位でlocal x=`[3.5,2.1,.7,-.7,-2.1,-3.5]`、z=`[0,1.2,2,2,1.2,0]`。観客側はlocal −Z。挨拶は150秒から2.5秒間隔、首の動作2秒。

## 再現・更新

Unity操作はプロジェクトの `scripts/unity-api.sh` 経由。メニューは `Tools/KominkaHillside/Parade/`。保存済みの本シーン、Edit modeで行い、他作業者の未保存変更を勝手に保存・破棄しない。

1. `1 - Survey Route and Views`で現行NavMeshと代表視点を採取する。レポートは`Temp/halloween-parade/survey/<UTC>/report.json`。現時点の経路は20261014-061339の実測corner列を使う。新経路へ変更するときは点・開始弧長・累積距離・stage座標を一組で更新し、演出生成器も再生成する。
2. C#コンパイル→`2 - Create Program Assets`→UdonSharpコンパイル。`3 - Apply Runtime and Panel`は初回のみ。6匹の既存Manual stateへadapterを追加し、既存Ownerを移譲しない。
3. `4 - Verify Runtime Wiring`→`5 - Save Verified Scene`。適用の戻り値だけで成功にしない。初回適用・リボン配置はUndo group内で実行し、途中例外ではそのgroupを戻す。例外後はdirty/実体を確認し、部分状態を保存しない。
4. 演出正本をbyte同一で `vrc-live-generator/out/kh-halloween-parade/kh-halloween-parade.showplan.json`へコピーし、`tools/unity/vclive.sh import ... --audio <master.wav> --effects effects.kh-halloween.json --no-scene`。`VCLIVE_UNITY_PROJECT`を本プロジェクトへ明示する。Importerは親フォルダ名をIDにするため、`versions/001`から直接取り込まない。
5. `6 - Attach Authored Ribbons`、`7 - Bind Authored Show`を各々検証・保存。`9 - Verify Show and Backing Values`でTimeline/AudioSource/復元配列とUdon backing値を照合する。AudioTrackだけが再生し、別AudioSource.Playは呼ばない。
6. 経路/翻訳の同形更新は `8 - Update Route and Words`。犬数・経路トポロジー・舞台配置を変える場合は既存配置を見て対象を限定した移行を行う。自動的に犬や他公演を再構築しない。
7. `10 - Capture Edit Views`は使い捨ての描画用コピーによる静止画確認。実行時歩行・同期・音との一致・端末負荷の証拠にはしない。
8. `11 - Refine Ribbon Fits`は六犬種の専用飾りの位置・向きを調整する。対象は今回のmountだけ。描画確認後に保存する。初期位置を適合済みと扱わない。
9. `12 - Capture UI Layouts`は実Surface/フォント/ボタンの使い捨てコピーで日本語/英語/ドイツ語×4状態、`12b - Capture Remaining UI Languages`は残り10言語×4状態を確認する。全35語と犬名のEditorフォント収録も測る。イベントは実行せず、動作検証には数えない。

### 手動で曲と全編を確認した旧入口（010で廃止）

**以下は2026-10-14の履歴。現在の開催・再適用手順は冒頭010を使う。無料確認スイッチは無効化済みで、以下の14/15メニューは存在しない。**

**2026-10-14追加指定により、確認用に一般ユーザーの開催を有効化した。** 手元メニューの「ライブ」→「ハロウィンパレード」の「開催する」で、開催許可の確認画面を開く。購入権・トレーナー・管理者権限は不要。保存シーンの`developmentEnabled=true`を使用し、未回答/拒否/6匹未配置の開始禁止は維持する。既存の有料演目の購入権は変更しない。VRChatで確認する際は変更後のBuild & Testまたはアップロードが必要。

再適用は`14 - Enable Public Preview in Live Tab`、独立検証は`15 - Verify Public Preview Entry`、保存は`5 - Save Verified Scene`。この入口は有料カタログの行数を増やさず、既存Liveのgridに専用行を足す。全体メニュー再構築後は入口の存在を再検証する。正式販売へ切り替える際は確認用開催を無効にし、専用Productの権利判定へ接続する。

追加分の検証：Editor C#コンパイル0エラー、保存後の16対象backing照合PASS、[入口の配線](evidence/live-entry/wiring.txt)PASS。[一覧の静止画](evidence/live-entry/ja-live-entry.png)で右側に入口を確認し、[13言語の配置測定](evidence/live-entry/layout.txt)は枠溢れ・最小文字サイズ張り付き0（開催ボタンは全言語20pt）。左側の既存演目と残高はEdit時の保存文言で、実行時の購入状態を模擬していない。Editor欠字と実機フォールバックの留保は下記と同じ。今回ランタイムの状態遷移コードは変更せず、Playを追加実行していない。限定ハーネスは元の`developmentEnabled`を保存・復元する方式へ改修済み。現在の確認用シーンでも元のtrueを維持する。

以下の`13`は従来のClientSim専用補助入口。通常の確認にはライブ画面のボタンを使える。

初回の回答は「まだ未確認」だったが、その後ユーザーが実見し001の演出を不採用とした。以下は再確認にも使う操作手順であり、002の合格記録ではない。

1. 本シーンで手動でPlayに入り、ClientSimのローカルプレイヤーの準備を待つ。
2. `Tools/KominkaHillside/Parade/13 - Open Manual ClientSim Preview`を選ぶ。実行時だけ開発用開催を有効にしてパネルを開く。メニュー自体は公演を始めず、プレイヤーを移動しない。
3. パネルで開催を依頼し、犬に同行するか広場で220秒を見る。音は距離14mまで通常、28mで無音なので、公演から離れたまま無音と判定しない。既存BGM個人音量とパレードのミュートも確認する。
4. 各犬の挨拶、205〜215秒の撮影時間、終演後の犬・衣装・音の復帰を確認し、気になる箇所は秒数と犬種を記録する。
5. Playを停止する。実行時だけの変更は破棄される。追加指定後の保存シーンは一般ユーザーの確認用に`developmentEnabled=true`。

入口のC#コンパイルは0エラー。ユーザーは001を実見済み。002の220秒全編の採用評価は未完。ClientSimの確認をVRChat実機・複数人・購入権の合格へ読み替えない。

## 今回の仕様訂正

洗浄の許可対象は「洗う」ボタンの押下者だけ。既存`KH_WashStationCall._CallerId()`を使い、共同洗浄者台帳は追加しない。許可待ちは遊びを継続、全対象者の許可と6匹確保の確認後だけ清潔化・終了・配置へ進む。拒否や未回答は許可ではない。

復帰を確認できないときは30秒後に失敗を明示し、未復旧犬だけ受付を止めて、正常な犬の遊びを再開する。復旧失敗を通常終了と報告せず、そのインスタンスでの全犬公演再開は止める。清潔化済みの犬を再び汚したり、終端後に古い清掃/ワープを実行したりしない。

## 独立レビューと改善

外部Claudeへの読取レビューは非公開情報の送信先未承認として自動審査が拒否。再試行せず、この作業環境内の実装未関与担当で独立レビューを行う。セルフ点検と区別する。

| 担当 | 主な指摘 | 対応・状態 |
|---|---|---|
| D 開発者 | Restore永久gate、配置前失敗の撫で抑制残り | 復旧失敗状態/犬別隔離と抑制markerを追加。独立再確認済み。終端時の再評価を1回に制限 |
| M 保守者 | 途中適用失敗、同じ長さの古い配線がPASS、原本入口不足 | Undoによる巻戻し、座標/文言比較と更新メニュー、本票の原本リンクを追加 |
| P PM | 挨拶間隔2秒/2.5秒の不一致、制作と公開条件の混同 | 2.5秒へ統一。制作候補/実機確認/販売設定を区別 |
| X UX / Sora | 中止理由、閉じた同意への復帰、復旧失敗犬の表示、設定の文字状態 | 修正後12画像を独立確認。残った全犬復旧失敗の断定表現とドイツ語の枠溢れを修正し、最終81 textのoverflow 0・画像実見で確認 |
| U 利用者 / Mai・Kana | 個人音量未配線、公演中に設定へ戻れない、回答期限不足 | 既存BGM個人音量への接続・公演中の再表示・期限と復帰中の案内を独立再確認。準備期限切れを回答期限と誤表示しないよう結果コードも分離。音楽は構成資料だけで実聴未実施 |
| V デザイナー / Ren・Misaki | Labの確認用描画崩れ、リボン埋没、かぼちゃの顔欠落、7mで小型犬が小さい | 修正後17画像を独立再確認し必須3件解消。3mは個体接近、集合写真は5〜7m。終端ディザの網目感は実機へ残す |

実装者によるソース抽出検査163ケースPASSは、通信順序・ClientSim・実機の代替ではない。

## 統合検査の記録

- C#/UdonSharp：2026-10-14、配線・終端復旧・UI修正時点で各0エラー。後続の利用者レビュー修正は再コンパイルして別記する。
- 配線：`9 - Verify Show and Backing Values`で16対象の新規所有field/backing一致を確認。既存behaviorの本件外の同期初期値は比較対象から除外。
- 演出：初回import `20261014-155141-1187`。verify `20261014-155650-1747` は127 prefab、254 ParticleSystem、maxParticles合計127、null材質/mesh/未結線0、Realtime Light0、issuesなし。ピーク描画負荷や実機fpsの測定値ではない。
- 描画不具合：`20261014-065450`の巨大犬画像は撮影処理の誤りで棄却。`20261014-070015`の15枚は独立Vが全件確認したが、Labの毛伸長・専用リボン識別不可・かぼちゃ顔欠落により視覚検収不合格。問題を隠して完成画像として使わない。
- かぼちゃ修正：透過材質が後方の内球を外皮上へ描いていたため、今回の7 presetだけ専用ZWrite/dither材質へ変更。再import `20261014-160543-525` はAPI期限超過したが、新しい`Logs/vclive/result.json`の完了とEditor.logの`import ok=True`を照合。重複実行していない。再取込後はstage参照を再配線する。
- 限定Playは独立Dとの相談で正常開始/取消と1匹ACK欠落の2ケースだけを対象とする。通常15秒、欠落55秒の上限。実VMを観測するが、複数クライアント同期・端末・全220秒の合格へ広げない。

### 最新の結果（2026-10-14）

| 検査 | 結果と証拠 |
|---|---|
| C#/UdonSharp | 最終ランタイム修正後ともに0エラー。Play前の16対象backing照合もPASS |
| 限定実VM | [記録](evidence/bounded-vm.json)、run `20261014-071938-674-0fab9f`。Play1回・2ケース。正常開始/取消3.20秒、ACK欠落48.43秒でPASS。実ACK63→配置→開始を確認。欠落時はmask32だけ隔離、5匹の撫で抑制と受付を復帰、再有効化後3秒の古い清掃/warpなし |
| 後片付け | 上記runのscene/source hash前後一致、PlayerData・設定・実行時override復元、Edit復帰、runtime error0 |
| 演出構造 | [最終verify](evidence/show-verify.json)、run `20261014-162453-1068`。127 prefab / 254 PS / maxParticles合計127 / 材質3 / Light0、欠損・未結線・errorShader0、issuesなし |
| 実景静止画 | [17画像の記録](evidence/edit-views/report.json)。全景・同行・犬別装飾を独立V確認。[集合](evidence/edit-views/210.0-plaza-low.png)、[ポメの装飾](evidence/edit-views/218.0-ribbon-1.png)、[サモエドの装飾](evidence/edit-views/218.0-ribbon-5.png) |
| UI静止画 | [12画像の記録](evidence/ui-layouts/report.json)。3言語×同意/復帰/全犬復旧失敗/公演中、81 textのoverflow0、元データhash一致・dirty false。[同意](evidence/ui-layouts/ja-consent.png)、[全犬復旧失敗](evidence/ui-layouts/ja-recoveryfail.png)、[ドイツ語軽減](evidence/ui-layouts/de-playing.png) |
| 追加10言語UI | [40画像の記録](evidence/ui-layouts-remaining/report.json)。初回11件の枠溢れを短い設定文言へ修正し、再測定overflow0、元データhash一致・dirty false。全13言語の計52画像はEditor上の配置検査であり、欠字を含む言語の最終的な字幅・可読性は未合格 |
| Mobile Caps | [Verify](evidence/mobile-caps.md)、対象83 texturesの未適用0。音源の品質を容量対策で下げていない |
| Android代理容量 | [結果](evidence/android-bundle.txt)、圧縮79,037,952 bytes（75.38 MiB）、非圧縮226,165,904 bytes（215.69 MiB）。100 MiB枠まで24.62 MiB。保存シーン全体の現状値で、今回の追加分だけの差分ではない。実機RAM・fps・SDKアップロードの合格ではない |

画像report内の`Temp/`は撮影時パス。保存コピーは同じreportと同じフォルダのファイル名で参照できる。静止画はEdit mode/昼/片眼、犬は現在姿勢を意図位置へ配置した描画用コピー。歩行・首振り・動作中の装飾追従を証明しない。

全35語と犬名のEditorフォント収録検査では、未収録の異なり文字数はja=3（完・揃・浄）、ru=9、uk=10、ko=155、zh-cn=83、pl=7、zh-hk=66、en/de/es/fr/pt-br/it=0。詳しい文字集合は両UI reportの`fontCoverage`に保存した。欠字は画像でも確認した。これはVRChatクライアントのフォールバックを含まないため、実機の欠字とは断定しない。既定のフォント追加禁止方針を守り、新規フォントは同梱せず、対象端末で字形・改行・枠内収まりを確認する。日本語/英語/ドイツ語の画像は同じ文言・配置のまま文字収録検査追加版（07:49 UTC）へ更新した。後続の配線検査も16対象PASS。

途中の音量配線で、古民家由来のSleepAudioが本シーンにもあると誤認した適用は失敗しUndoで戻った（dirty false、旧30語390件のままを独立読取）。本ワールドの実体はBgmPlayer1、SleepAudio0と確認して修正済み。既存BGM個人音量を読むだけで、BGM ON/OFFとパレードミュートは別々。購入素材・犬本体材質を変えて視覚問題を隠していない。

### 実機で残る確認

| 項目 | 確認方法 | 現在 |
|---|---|---|
| 曲と全編 | 220秒を実聴し、行進・各犬の挨拶・205〜215秒撮影時間と音の一致を見る | ライブ009はユーザー採用。端末別・全条件の技術検証とは分ける |
| 装飾と灯り | 6匹の歩行/座位/首振りで埋没・貫通、昼/夜、終端ディザのちらつきを見る | 静止画のみ確認 |
| 許可の実通信 | Aが洗浄中に許可→Bが拒否、Aの洗浄継続。洗う押下者1名だけ対象、退出・古い回答も確認 | ソース契約のみ確認 |
| 公演の同期 | 途中参加・Owner交代・開催者退出・正常終演で犬/音/装飾が戻る | 一部の単一ClientSimのみ確認 |
| 端末 | PCVR/Desktop/Quest単体/iOSで入力・13言語の文字とフォールバック後の枠内収まり・通常/軽減・負荷をそれぞれ記録 | 未確認。Editor未収録文字は上記記録参照 |

## 検収の段階

- 今回制作・統合：専用音源/モデル/演出を保存し、6匹へ配線。C#/Udon/構造/代表画像/限定ロジック検査を行い、独立レビューの必須指摘を解消する。
- 実機確認待ち：実音を聴く、220秒の歩行と接地、6匹の装飾fit、昼/ローカル夜・立位/低視点、複数人同意/途中参加/Owner交代、PCVR/Desktop/Quest単体/iOS。未実施を合格にしない。
- 販売・公開条件（2026-10-15更新）：300CE、2026-10-31 23:59 JSTまで販売、購入済み開催権は期限後も維持。専用Product/Listingを[商品台帳 D-1](../../development/design/ce-listings.md)へ記入し、通常メニューの購入・ギフト・所持後開催へ接続済み。無料確認開催は廃止。保存再読込・backing照合はPASS。Storefront側設定・実購入/ギフトと公開アップロードは未実施であり、課金公開完了とは扱わない。
