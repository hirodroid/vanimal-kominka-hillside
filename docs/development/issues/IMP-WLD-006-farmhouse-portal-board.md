---
id: IMP-WLD-006
type: IMP
system: WLD
status: wip
priority: High
summary: 門にあるポータルを古民家ドッグランの案内ボードにし、Interact すると隣にポータルが出るようにする (古民家は触らない)
next: "Unity が使えるようになったら Compile を通し、Guide/1 を再実行 (物理で塞がらない位置を探す。まだ一度も走っていない) → シーンを開き直して Guide/2 が GUIDE_BOARDS_OK になるか確かめる → Play (ClientSim) で Interact による PortalAnchor の出し入れと 60 秒の自動しまいを確認。ポータルを後から有効にして実際に入れるかは VRChat のクライアントでしか分からない (ユーザー。2 人で、押した人にだけ見えることも。A0-14・A0-15、B58)"
updated: 2026-10-03
---

# IMP-WLD-006 門の案内ボード: 古民家ドッグランへのポータルを Interact で出す

## ユーザーの依頼 (2026-10-03、原文)

「往復導線ってなに？古民家ドックランは触らない。VRCの設置型ポータルで移動するだけなので特に何もすることは無い。→ いまポータルおいてあるけど、門のところに。そこを古民家ドックランの説明案内ボードにして、インタラクトしたら隣にポータルが出るように修正が必要。」

設計は [DES-WLD-001](DES-WLD-001-round-trip.md) (完了)。初期公開に入れる (2026-10-02 決定、優先度 High)。

## 範囲

- 門の、いまワールド側のポータルが置いてある場所を、**古民家ドッグランの説明案内ボード**にする (何のワールドか、ここから行けること、状態は引き継がれないこと)
- ボードを Interact すると、**ボードの隣にポータルが出る** (出る前は隠れている)。ポータルの行き先は古民家ドッグラン
- 既存のポータルは作り直さず使い回す (行き先の設定はそのまま)
- ボードの様式・文言は他の看板 ([DES-UI-004](DES-UI-004-walk-destinations-and-boards.md)) に揃える

## 範囲外

- 状態 (犬の名前・飾り・リード・ポイント・所持品) の引き継ぎ
- 古民家ドッグラン (別リポジトリ) の改変
- ポータル以外の帰還手段 (裏山から戻る手段は、このポータルだけ)

## 完了条件

- [ ] 散歩道 (門の前) から、ボードの文言が読める (Desktop と、スマホ・Quest でも)
- [ ] ワールドに入った直後、ポータルは隠れている
- [ ] ボードを Interact するとポータルが隣に現れる (Desktop とスマホ・Quest の両方で押せる)
- [ ] 現れたポータルに入ると、実機のクライアントで古民家ドッグランへ移動できる
- [ ] 途中参加者の画面でも、押されるまでポータルは隠れている (押した人がいる場合の見え方は実装時に方針を決めて記録する)
- [ ] ボード・ポータルが門の通行・犬・ほかの Interact (看板・メニュー・洗い場) を邪魔しない
- [ ] 古民家ドッグランのリポジトリに差分が無い

## 関連

- [IMP-ENV-003](IMP-ENV-003-spawn-and-dropped-portals.md) (hold): 参加者が落としたポータルがスポーンの真後ろに置かれる件。本 Issue はワールド側のポータルを門に置く話で、落とされるポータルの対策ではない。ユーザーがスポーン周りを手で調整する方針は変えない
- [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md): 案内ボードの配置・様式。門前のボードの配置と揃える

## 実装 (2026-10-03、未コンパイル・未検証)

- 追加: `Assets/KominkaHillside/Scripts/KH_PortalBoard.cs` (と `.asset`・`.meta`、いずれも未追跡) と、`KH_GuideBoardsEditor.cs` / `KH_GuideBoardsEditor.Verify.cs` の 3 枚目の板「Kominka」(紙面の題は「古民家ドッグラン」)。設計は [看板の設計書 §12](../../plans/2026-10-02-guide-boards.md)。
- ふるまい: ローカルのみ (`BehaviourSyncMode.None`、同期変数・Ownership・ネットワークイベントなし)。板の Trigger を Interact すると、自分の画面にだけ既存のポータルが出る。出ている間にもう一度 Interact するとしまう。出して 60 秒 (`showSeconds`) で自動でしまう。`PortalAnchor` は保存シーンで非アクティブ、`Start()` でも隠す。
- ポータルは作り直さず引き取った: シーンの `DogRunVRCPortalMarker` を `KH_GuideBoards/Kominka/PortalAnchor` の下へ付け替えるだけで、world id (`wrld_b03ddfbb-...`) は変えない (Guide/2 がコード内の写しと照合)。古民家ドッグランのリポジトリには触れていない。
- **最初の Build (2026-10-02T16:11Z、Guide/1) の結果**: 板とポータルの位置が、古民家の外壁 (`KH_World/KH_Places/KH_FarKominka/KH_KominkaWall/外壁`) の Collider と重なった状態で**保存された** (未コミットの `VRCDefaultWorldScene.unity` にその状態が入っている)。同時刻の Guide/2 は `GUIDE_BOARDS_FAIL` (FAIL 2: 「Kominka placement: no solid collider in the board's volume」「nothing solid where the portal appears (1 x 2 x 1 m)」、いずれも外壁。WARN 1 は A の足元の高低差 0.66 m。`Temp/kh-guide-boards/verify.json`)。
- その後ビルダーを直した (**未実行**): Guide/1 が、板の表の側 (塀から離れる向き) へ 0.1 m ずつ最大 3 m 動かし、板の体積とポータルの体積 (入る側の余裕を含む) が実際の Collider に当たらない最初の点を物理で探す (`FindKominkaPose`)。板が既にあっても、今の位置で塞がっていれば置き直す。3 m 以内で見つからなければ何も変えずに止まる。ずらした距離は `build.json` の `kominkaPlacement` に出る (値は Unity で Build するまで不明)。
- Unity の橋が切れていたため、Unity ではコンパイルも実行もしていない (`8f7ed6e` より後に書いた分は、オフラインの Roslyn コンパイルで全 C# がエラー 0 になっただけ。UdonSharp の制約と実行時の挙動は未確認)。コミットは未。

- 未確認: Build し直したあとに Guide/2 が通るか (今のシーンは FAIL 状態のまま)。実行時に有効にしたポータルが VRChat クライアントで正しく初期化されるか (SDK にソースが無く、ClientSim もポータルを再現しない。確度は中。だめなら設計書 §12-3 の代案: 保存シーンでは有効にして `Start()` の 1 フレーム後に隠す)。2 人で、片方が出したポータルがもう片方に見えないか、途中参加者に出ていないか。スマホ・Quest で板が押せるか。完了条件は上のとおり、どれも未確認。
