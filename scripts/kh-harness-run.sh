#!/usr/bin/env bash
# ClientSim ハーネスの実行を 1 本にまとめる (2026-09-28、IMP-DOG-023 の反省から)。
# 説明と使い分けの正本: docs/testing/kh-frisbee-harness.md「実行スクリプト」。
#
# Play を起動する入口 (proto / walk / segment / lap / photo) は次の 2 つが無ければ走らない (2026-10-03 ユーザー指定):
#   --reason  "<この Play でしか判別できないこと>"
#   --consult "<回す前の AI 相談 (Codex) の結論: 今回すべきか・どの群か>"
# 群の全体 (Course/42・100・126、絞り込みなしの proto default) と lap は、さらに --confirmed "<ユーザーの確定指示>" が要る。
#
#   bash scripts/kh-harness-run.sh proto <mode> [harness の追加引数] --reason ".." --consult ".."   # 試作シーン (kh-frisbee-harness.cjs)。例: proto terrain --shots=T-up04
#   bash scripts/kh-harness-run.sh walk  "<Course メニュー名>" --reason ".." --consult ".."          # 本シーン (Logs/kh-course-walk)。例: walk "Course/94b - Walk Harness Frisbee Anywhere (M3 water)"
#   bash scripts/kh-harness-run.sh segment bridge-190-choice-2-dogs-6 --reason ".." --consult ".."    # 通常改修: 橋か門の既存ケースを1件
#   bash scripts/kh-harness-run.sh lap forward --reason "確認する仮説" --consult ".." --confirmed ".." # 約20分の連続一周。通常改修では使わない
#   bash scripts/kh-harness-run.sh menu  "<メニュー名>"                 # Edit モードの静的検査 (Course/89・93、Frisbee/5 など)。Editor.log の増分から要約行だけ返す
#   bash scripts/kh-harness-run.sh compile                              # unity-compile.sh --udon (照合失敗は 1 回だけ待って再試行)
#   bash scripts/kh-harness-run.sh photo --reason ".." --consult ".."   # IMP-WLD-005 Photo/3 (自動写真、約 1 分。Photo/1・4 の後)
#
# 終了コード: 0 = PASS または歩行の SEGMENTS_OBSERVED / LAP_OBSERVED (範囲はreportどおり) / 1 = 失敗・未完了 / 2 = 待ち切れ・新しい run が出ない
#            3 = 実行前の照合失敗 (接続先違い、Play 中、コンパイル中。何もしない) / 64 = 使い方の誤り
#
# 守ること (実測 2026-09-28):
# - 実行前に「Unity が Edit モードで、コンパイル中でない」ことを確かめ、違えば exit 3 で何もしない。
#   (Play 中にメニューを叩くと "Expected scene in idle Edit mode" で空振りし、前の run の結果を新しい結果と取り違える)
# - 新しい runId が出て、終了状態 (RUNNING 以外) になり、Play を抜けるまで待つ。前の runId の結果は使わない。
# - 1 行で結果を返し、FAIL なら exit 1。連鎖させるときは `set -e` で最初の FAIL で止める (回帰の全体はユーザーが確定を指示した時に 1 回だけ)。
# - **ハーネス実行中はコードを編集しない。** Play 中の再コンパイルはフレームを止め (最初の計画が 0.2 s 遅れて stand になる等)、
#   途中の状態が混ざるとコンパイルエラーで run が壊れる。編集が要るなら先に stop する。
set -u
cd "$(dirname "$0")/.."
API="bash scripts/unity-api.sh"
EDITOR_LOG="${EDITOR_LOG:-${LOCALAPPDATA:-$HOME/AppData/Local}/Unity/Editor/Editor.log}"

# C# を 1 本評価する。$1 は JSON 文字列の中身としてそのまま埋め込む (ダブルクォートは \" で書く)。stderr (WARN) は捨てる
eval_cs() { $API execute "{\"tool\":\"unity_run_csharp\",\"params\":{\"code\":\"$1\"}}" 2>/dev/null; }
# 応答の result を取り出す。外側の result は内側 JSON の文字列 ({"success":..,"resultSet":..,"result":..}) のことも
# オブジェクトのこともある (unity-api.sh の data_path と同じ扱い)。取れなければ空
result_of() {
  node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{try{const line=s.split(/\r?\n/).filter(l=>l.trim().startsWith("{")).pop();const o=JSON.parse(line);let v=o.result;if(typeof v==="string"){try{v=JSON.parse(v)}catch(_){}}if(v&&typeof v==="object"&&"result" in v)v=v.result;process.stdout.write(v==null?"":String(v))}catch(e){}})'
}

guard() {
  local i
  for i in 1 2 3; do
    if $API check >/dev/null 2>&1; then break; fi
    [ "$i" = 3 ] && { echo "GUARD_FAIL: unity-api check (接続先の照合に失敗)"; exit 3; }
    sleep 10
  done
  local st
  st=$(eval_cs 'return UnityEditor.EditorApplication.isPlaying.ToString() + \" \" + UnityEditor.EditorApplication.isCompiling.ToString();' | result_of)
  case "$st" in
    "False False") ;;
    *) echo "GUARD_FAIL: Unity is not idle in Edit mode (isPlaying isCompiling = '$st')"; exit 3;;
  esac
}

is_playing() { eval_cs 'return UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode.ToString();' | result_of; }

# メニューを実行し、ExecuteMenuItem の戻り値 (True / False) を返す
run_menu() { eval_cs "return UnityEditor.EditorApplication.ExecuteMenuItem(\\\"Tools/KominkaHillside/$1\\\").ToString();" | result_of; }

log_size() { [ -f "$EDITOR_LOG" ] && wc -c < "$EDITOR_LOG" | tr -d ' ' || echo 0; }

cmd=${1:-}; shift || true
# Play を起動する入口は、目的と相談の結論が無ければ Unity に触れる前に止める (kh-harness-gate.cjs、2026-10-03)。
# 通ったら --reason / --consult / --confirmed を引数から外し、開始と終了 (終了コード・秒) を Logs/kh-harness-run/ledger.jsonl へ残す
case "$cmd" in proto|walk|segment|lap|photo)
  gate_out=$(node scripts/kh-harness-gate.cjs begin "$cmd" "$@") || exit 64
  mapfile -t gate_lines <<<"$gate_out"
  gate_id=${gate_lines[0]}
  set -- "${gate_lines[@]:1}"
  trap 'node scripts/kh-harness-gate.cjs end "$gate_id" "$?" "$SECONDS" >/dev/null 2>&1' EXIT;;
esac
case "$cmd" in
  compile)
    guard
    out=$(bash scripts/unity-compile.sh --udon 2>&1 | tail -1)
    case "$out" in *照合に失敗*) sleep 15; out=$(bash scripts/unity-compile.sh --udon 2>&1 | tail -1);; esac
    case "$out" in *'"udon":"timeout"'*) out=$(bash scripts/unity-compile.sh --no-refresh --udon 2>&1 | tail -1);; esac
    echo "compile $out"
    case "$out" in *'"csErrors":0'*'"udonErrors":0'*) exit 0;; *) exit 1;; esac;;
  menu)
    [ -n "${1:-}" ] || { echo "usage: menu <name>"; exit 64; }
    case "$1" in Course/53\ -*|Course/54\ -*) echo 'Use lap <forward|reverse> --reason "..." (continuous ~20 minute regression).'; exit 64;; esac
    # Play を起動するハーネスを menu から回すと門を通らない。停止・復旧のメニューは通す
    case "$1" in *Stop*|*Restore*) ;; *Harness*) echo 'GATE_REFUSED: Play を起動するハーネスは walk / proto / photo で回す (--reason と --consult が必要)。'; exit 64;; esac
    guard
    pos=$(log_size)
    ok=$(run_menu "$1")
    [ "$ok" = "True" ] || { echo "menu NOT_EXECUTED ($1): ExecuteMenuItem=$ok"; exit 2; }
    sleep 2
    # Debug.Log は API の応答に載らない (vrc-synaptic-unity)。Editor.log の増分から要約行を拾う
    lines=$(tail -c +"$((pos + 1))" "$EDITOR_LOG" 2>/dev/null | grep -E "_OK|_FAIL|_PASS|RESULT|TOTAL|BLOCKED|error CS|Exception" | head -5)
    echo "${lines:-menu NO_SUMMARY_LINE ($1): 結果ファイル (Temp/...) を直接読む}"
    case "$lines" in *_FAIL*|*BLOCKED*|*"error CS"*|*Exception*) exit 1;; "") exit 2;; *) exit 0;; esac;;
  proto)
    guard
    mode=${1:-default}; shift || true
    prev=$(node -e 'try{console.log(require("./Logs/kh-frisbee-harness/latest.json").runId)}catch(e){console.log("")}')
    if ! start_out=$(node scripts/kh-frisbee-harness.cjs start --mode="$mode" "$@" 2>&1); then
      echo "proto START_FAILED: $(printf '%s' "$start_out" | tail -3 | tr '\n' ' ')"; exit 2
    fi
    done_=0
    for i in $(seq 1 150); do
      sleep 10
      s=$(node -e 'try{const j=require("./Logs/kh-frisbee-harness/latest.json");console.log(j.runId+" "+j.status)}catch(e){console.log("none")}')
      case "$s" in none|"$prev "*|*" RUNNING") continue;; esac
      [ "$(is_playing)" = "False" ] || continue
      done_=1; break
    done
    [ "$done_" = 1 ] || { echo "proto TIMEOUT: no finished run after 1500 s (latest: $s)"; exit 2; }
    node -e 'const j=require("./Logs/kh-frisbee-harness/latest.json");const want=process.argv[1];const bad=(j.throwResults||[]).filter(x=>!x.ok).map(x=>(x.shot||"?")+": "+(x.terrainNote||x.expectNote||"")).join(" | ");const mm=(j.mode||"default")!==want?" MODE_MISMATCH(want "+want+")":"";console.log("proto",j.mode,j.runId,j.status+mm,j.failure||"",j.throwsRun+"/"+j.throwsPlanned,"ok",j.throwsOk,bad?"FAILS="+bad:"");process.exit(j.status==="PASS"&&!mm?0:1)' "$mode";;
  walk|segment|lap)
    request=$(node scripts/kh-walk-cli.cjs request "$cmd" "$@") || exit 64
    guard
    prev=$(node -e 'try{console.log(require("./Logs/kh-course-walk/latest.json").runId)}catch(e){console.log("")}')
    ok=$($API execute "$request" | result_of)
    [ "$ok" = "True" ] || { echo "walk NOT_EXECUTED ($1): ExecuteMenuItem=$ok"; exit 2; }
    # latest.json は開始時に RUNNING で書かれ、End で最終 status、Play を抜けた後の Finish で証拠 (endedUtc 等) が足される
    done_=0; t=0
    while [ $t -lt 2500 ]; do
      sleep 10; t=$((t+10))
      s=$(node -e 'try{const j=require("./Logs/kh-course-walk/latest.json");console.log(j.runId+" "+j.status+" "+(j.endedUtc?"ended":"open"))}catch(e){console.log("none")}')
      case "$s" in none|"$prev "*)
        [ $t -ge 60 ] && { echo "walk NO_NEW_RUN after 60 s (menu refused? latest: $s)"; exit 2; }
        continue;; esac
      case "$s" in *" RUNNING "*) continue;; *" EVIDENCE_ERROR "*) ;; *" open") continue;; esac  # EVIDENCE_ERROR は endedUtc が無いことがある
      [ "$(is_playing)" = "False" ] || continue
      done_=1; break
    done
    [ "$done_" = 1 ] || { echo "walk TIMEOUT: no finished run after 2500 s (latest: $s)"; exit 2; }
    node scripts/kh-walk-cli.cjs result "$cmd" "$@";;
  photo)
    # IMP-WLD-005 Photo/3 - Harness (自動写真の小さいケース群、約 1 分)。結果は Logs/kh-photo-harness/latest.json
    guard
    prev=$(node -e 'try{console.log(require("./Logs/kh-photo-harness/latest.json").runId)}catch(e){console.log("")}')
    ok=$(run_menu "Photo/3 - Harness")
    [ "$ok" = "True" ] || { echo "photo NOT_EXECUTED: ExecuteMenuItem=$ok"; exit 2; }
    done_=0; t=0
    while [ $t -lt 400 ]; do
      sleep 10; t=$((t+10))
      s=$(node -e 'try{const j=require("./Logs/kh-photo-harness/latest.json");console.log(j.runId+" "+j.status+" "+(j.endedUtc?"ended":"open"))}catch(e){console.log("none")}')
      case "$s" in none|"$prev "*)
        [ $t -ge 60 ] && { echo "photo NO_NEW_RUN after 60 s (PHOTO_HARNESS_BLOCKED in Editor.log? Photo/1 and Photo/4 are required; latest: $s)"; exit 2; }
        continue;; esac
      case "$s" in *" RUNNING "*|*" open") continue;; esac
      [ "$(is_playing)" = "False" ] || continue
      done_=1; break
    done
    [ "$done_" = 1 ] || { echo "photo TIMEOUT: no finished run after 400 s (latest: $s)"; exit 2; }
    node -e 'const j=require("./Logs/kh-photo-harness/latest.json");const bad=(j.cases||[]).filter(c=>c.status!=="PASS").map(c=>c.name+": "+(c.reason||c.status)).join(" | ");console.log("photo",j.runId,j.status,"cases",(j.cases||[]).length,"sceneUnchanged",j.sceneFileUnchanged,"playerDataRestored",j.playerDataRestored,"errors",(j.errors||[]).length,bad?"FAILS="+bad:"");process.exit(j.status==="PASS"&&j.sceneFileUnchanged&&j.playerDataRestored?0:1)';;
  *) echo 'usage: kh-harness-run.sh compile | menu <name> | proto <mode> [args] | walk <menu> | segment <case> | lap <forward|reverse> | photo   (Play entries need --reason "..." --consult "..."; whole groups and lap also --confirmed "...")'; exit 64;;
esac
