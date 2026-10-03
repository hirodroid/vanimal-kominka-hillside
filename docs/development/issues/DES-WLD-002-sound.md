---
id: DES-WLD-002
type: DES
system: WLD
status: done
priority: Medium
summary: BGM003の導入・個人音量設定はIMP-WLD-003へ。環境音・場所別構成と残る音の方針を整理する
next: 方針は game-design §4「音」に記録済み。聞こえ方・容量の実測は IMP-WLD-003 / IMP-ENV-006 / IMP-DOG-028、ライブ側は DES-LIV-001 で扱う
updated: 2026-10-03
---

# DES-WLD-002 裏山の音 (BGM・環境音) を設計する

2026-09-20 ユーザー: 「BGM をどうするか」も次の流れに含める。現在の裏山には犬の吠え・息以外の音がない。古民家ドッグランの音の扱い (BGM の有無・環境音・ライブとの切替) を先に確認し、裏山で何を鳴らすかを決める。

## 設計すること

### 2026-09-29 BGMの採用・実装範囲の決定

ユーザーが専用BGM **KH_Komorebi 003「木漏れ日の帰り道 — きみと、この場所で」** の組み込みを指定。古民家ドッグランと同じ設定メニューから本人のBGMをON/OFFし、音量を調整できるところまでを [IMP-WLD-003](IMP-WLD-003-bgm-and-volume-settings.md) にまとめた。対象音源・素材記録・モバイル容量の確認・ライブとの連携・完了条件は同Issueに記録。本Issue全体の完了ではなく、環境音と場所別構成などは未決定のまま残す。

### 残る検討と当初の観点

2026-09-29: 川のせせらぎ音の導入を [IMP-ENV-006](IMP-ENV-006-river-ambience.md) に分けた。ユーザーが音源を制作中で、格納先を用意済み。川への配置・ループ・距離減衰・音量の調整は同Issueで扱い、BGMや他の環境音の設計は本Issueに残す。

- 常時 BGM を置くか、風・鳥・葉音などの環境音だけにするか、場所 (門前・道・見晴らし・広場) で変えるか。散歩の静けさと会話を邪魔しない音量・ループ。
- 音源の出所: 購入/既存素材、自作 (vrc-music-agent / REAPER)、古民家からの流用。権利と容量 (Quest/iOS の 100MB 上限、`Decompress On Load` の常駐メモリ)。
- 専用ライブ (DES-LIV-001) 中の BGM/環境音の扱い、複数人での同期要否 (環境音はローカルで足りる想定)。
- 犬の吠え・息・撫での音との音量バランス。

## 完了条件

- [x] BGM/環境音の採用方針と場所ごとの構成が game-design に書かれている
- [x] 音源の出所・権利・容量の見積もりがある (実測は各実装 Issue。game-design の音の表に未実測と明記)
- [x] ライブとの切替と同期要否が決まり、実装 Issue がある (BGM は IMP-WLD-003 でライブ連動を配線済み。ライブ側の実装は 2026-10-03 のユーザー決定で最後に作るため DES-LIV-001 / 002 が持つ)

## 2026-10-03 完了の記録

音の方針を [game-design §4「音 (BGM・環境音・効果音)」](../../game-design.md) に書いた。この Issue の仕事 (方針の整理) は終わりとして閉じる。

- BGM: [IMP-WLD-003](IMP-WLD-003-bgm-and-volume-settings.md) (003 を本人の ON/OFF・音量で。基準音量 0.35 を 2026-10-03 に採用)。
- 川の環境音: [IMP-ENV-006](IMP-ENV-006-river-ambience.md) (20 音源、権利は [制作票](../../plans/2026-10-02-river-ambience-production.md) に記録、公開保留は 2026-10-03 に解除)。
- 水遊び: [制作票](../../plans/2026-09-27-water-se-production.md)。洗う音: [IMP-DOG-028](IMP-DOG-028-wash-sound-and-reactions.md)、[制作票](../../plans/2026-09-30-wash-se-production.md)。犬の声の音量: [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)。
- ライブ: 2026-10-03 のユーザー決定で最後に作る。ライブ中の音は [DES-LIV-001](DES-LIV-001-hillside-live.md) / [DES-LIV-002](DES-LIV-002-live-lineup-and-parade.md) で決める。
- 同期: 環境音・BGM はローカルで同期しない (IMP-ENV-006 は Udon・同期なし、BGM は SyncMode None)。
- 残る実測 (Android/iOS の圧縮後サイズ、実際の聞こえ方・ループの継ぎ目) は各実装 Issue と VER-WLD-001 が持つ。方針を止める条件ではない。
