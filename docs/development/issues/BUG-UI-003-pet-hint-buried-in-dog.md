---
id: BUG-UI-003
type: BUG
system: UI
status: review
priority: Low
summary: 犬のそばで出る撫で方ヒント (hint.pet) が犬の根元 + 0.12 m に置かれ、板 (0.65 × 0.11 m) が犬の体に埋まっていた。その犬の頭アンカーの高さ + 0.2 m に変え、犬の上に出す (ユーザー報告 2026-10-16)
next: "ユーザーの Play: 入室 90 秒後に犬のそば (2 m 以内) で撫でずにいると、ヒントが犬の頭の上に出る (6 匹とも体に埋まらない)。歩いている犬で上下の揺れが気にならないか"
updated: 2026-10-16
decided: 2026-10-16
---

# BUG-UI-003 撫で方ヒントが犬の体に埋まる

ユーザー報告 2026-10-16「犬の撫でる通知が犬の身体に埋まっているので、犬の上あたりに出るようにして」。

## 症状

`KD_HintDirector` の撫で方ヒント (`hint.pet`「頭・ほっぺ・あごをなでてみて / 両手だと大よろこび」。入室 90 秒後、投げても撫でてもいない人の 2 m 以内の犬に出す) は、犬の根元 (`KD_DogPack.dogRoots`、地面の高さ) + 0.12 m に置かれていた。板は 650 × 112 (0.001 倍 = 0.65 × 0.11 m、中央基準) なので 0.06〜0.18 m の帯になり、どの犬でも体に埋まる (頭アンカーは根元から ポメ 0.21 / ダックス 0.28 / コーギー 0.28 / 柴 0.44 / サモエド 0.56 / ラブ 0.62 m。Edit モードの実測)。

## 対応 (2026-10-16、統括 Fable)

- `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_HintDirector.cs` (裏山改変): `PetHintHeight(dog)` を足し、その犬の `KD_DogPetController.headAnchor` の高さ − 根元の高さ + 0.2 m を根元からの高さにする。`pet[]` は `dogs[]` と並びが違う (Hillside の Menu/11b は `FindObjectsOfTypeAll` の順。実測: pet = ラブ・ダックス・柴・サモエド・ポメ・コーギー、dogs = コーギー・ポメ・ラブ・ダックス・柴・サモエド) ので `dogRoot == dog` で引く。見つからなければ 0.6 m。定数 (`PetHintAboveHead` / `PetHintFallbackHeight`) で、シーンの値は無い。
- 高さは 0.25 秒の tick ごとに読み直す (ヒントは毎 tick 置き直される)。頭が歩きで上下すると板も数 cm 動く。

## 検証

- C# 変更なし → U# コンパイル ok (`unity-compile.sh --udon`、`scriptCompilationFailed = False`、UASM に `PetHintHeight`)。`KD_HintDirector.asset` の再シリアライズ無し。
- Play はユーザー (frontmatter の next)。

## 記録

- 2026-10-16: 起票と対応 (同日)。決定: 2026-10-16
