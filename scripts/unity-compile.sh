#!/usr/bin/env bash
# Unity のコンパイル完了と成否を**実測で**確かめる (vrc-synaptic-unity「待ち方 (1 つに統一)」のスクリプト化)。
#
#   bash scripts/unity-compile.sh           # force refresh → C# のコンパイル完了を待ち、error CS を数える
#   bash scripts/unity-compile.sh --udon    # さらに UdonSharp の CompileAllCsPrograms を走らせ、完了ログと Error を確認
#   bash scripts/unity-compile.sh --no-refresh   # refresh せず現在の状態だけ判定 (Unity が勝手に refresh した後など)
#
# 出力: 最後の 1 行が JSON。exit 0 = エラー 0 件、1 = エラーあり、2 = 打ち切り (まだコンパイル中かも)、3 = 接続先が違う。
#   {"csharp":"ok|error|nochange","csErrors":N,"csErrorLines":[...],"udon":"ok|error|skipped|timeout","udonErrors":N,"seconds":S}
#
# ## なぜ要るのか (実測 2026-08-24、KominkaDogRun IMP-TRT-001)
#
# `unity_force_refresh_assets` は**コンパイル完了を待たずに success を返す。** その直後にメニューを叩くと
# 1 つ前のアセンブリのまま実行され、しかも成功応答が返る (実装をわざと壊して Verify したら PASS が返った)。
# `unity_analyze_console_logs` は CS エラーを 1 件も返さないことがある (実測 2026-08-25)。
# `EditorApplication.isCompiling == false` は「落ちて止まった」でも false。どれも完了・成功の根拠にならない。
#
# ## 判定 (vrc-synaptic-unity §「コンパイルは Editor.log で確かめる」)
#
# 1. refresh の前に Editor.log の末尾位置と Assembly-CSharp*.dll の更新時刻を記録
# 2. force refresh
# 3. 記録位置より後に完了の証拠が出るまで待つ: dll の更新時刻が進み、ドメインリロード後の軽い eval が通る。
#    接続断・isCompiling==true は「コンパイル中」。dll が進まないまま `error CS` が出たら失敗
#    (ソースが dll より新しくなければ「コンパイル不要」で抜ける)
# 4. 記録位置より後の `error CS` を数える (重複行は 1 件)
# 5. --udon: 位置を記録 → CompileAllCsPrograms(true) → 記録位置より後に `Compile of N scripts finished` か Error/Exception が出るまで待つ
set -u
cd "$(dirname "$0")/.."

API="bash scripts/unity-api.sh"
MAX_SECONDS=${MAX_SECONDS:-300}
QUIET_POLLS=${QUIET_POLLS:-5}          # 「コンパイル不要」と見なす連続の静かなポーリング数 (2 秒間隔)
EDITOR_LOG="${EDITOR_LOG:-${LOCALAPPDATA:-$HOME/AppData/Local}/Unity/Editor/Editor.log}"
DLL_DIR="Library/ScriptAssemblies"
DO_UDON=0; DO_REFRESH=1
for a in "$@"; do
  case "$a" in
    --udon) DO_UDON=1 ;;
    --no-refresh) DO_REFRESH=0 ;;
    *) echo "unknown option: $a" >&2; exit 2 ;;
  esac
done

start_ts=$(date +%s)
log_size() { [ -f "$EDITOR_LOG" ] && wc -c < "$EDITOR_LOG" | tr -d ' ' || echo 0; }
dll_stamp() { ls -l --time-style=+%s "$DLL_DIR"/Assembly-CSharp.dll "$DLL_DIR"/Assembly-CSharp-Editor.dll 2>/dev/null | awk '{print $6}' | sort -n | tail -1; }
newest_cs() { find Assets -name '*.cs' -newer "$DLL_DIR/Assembly-CSharp-Editor.dll" 2>/dev/null | head -1; }
tail_from() { tail -c +"$(( $1 + 1 ))" "$EDITOR_LOG" 2>/dev/null; }
eval_json() { $API execute "$1" 2>/dev/null; }
alive() { eval_json '{"tool":"unity_run_csharp","params":{"code":"return \"alive\";"}}' | grep -q 'alive'; }
compiling() { eval_json '{"tool":"unity_run_csharp","params":{"code":"return UnityEditor.EditorApplication.isCompiling;"}}' | grep -q '"result":true'; }
emit() {
  node -e 'const a=process.argv;const lines=a[3]?a[3].split("\n").filter(Boolean):[];const ulines=a[7]?a[7].split("\n").filter(Boolean):[];
    console.log(JSON.stringify({csharp:a[1],csErrors:+a[2],csErrorLines:lines.slice(0,20),udon:a[4],udonErrors:+a[5],udonLines:ulines.slice(0,20),seconds:+a[6]}))' \
    "$1" "$2" "$3" "$4" "$5" "$6" "$7"
}

# 0. 接続先の照合 (別プロジェクトなら exit 3)
$API check >/dev/null || { echo "[unity-compile] 接続先の照合に失敗" >&2; exit 3; }

# 1. 記録
pos0=$(log_size)
dll0=$(dll_stamp)
[ -z "$dll0" ] && dll0=0
expect_compile=0
[ -n "$(newest_cs)" ] && expect_compile=1

wait_csharp() {
# 2. refresh
if [ "$DO_REFRESH" = "1" ]; then
  UNITY_API_NO_GUARD=1 $API execute '{"tool":"unity_force_refresh_assets","params":{"importMode":"force"}}' >/dev/null 2>&1 || true
fi

# 3. 待つ
cs_state="timeout"; quiet=0; saw_busy=0
while [ $(( $(date +%s) - start_ts )) -lt "$MAX_SECONDS" ]; do
  if ! UNITY_API_NO_GUARD=1 alive || UNITY_API_NO_GUARD=1 compiling; then
    saw_busy=1; quiet=0
  else
    dll1=$(dll_stamp); [ -z "$dll1" ] && dll1=0
    if [ "$dll1" -gt "$dll0" ]; then
      # dll が進んだ。リロード完了の目安として静かな 1 回を追加で見る
      quiet=$((quiet + 1))
      [ "$quiet" -ge 2 ] && { cs_state="ok"; break; }
    elif tail_from "$pos0" | grep -q 'error CS'; then
      cs_state="error"; break
    else
      quiet=$((quiet + 1))
      if [ "$expect_compile" = "0" ] && [ "$saw_busy" = "0" ] && [ "$quiet" -ge "$QUIET_POLLS" ]; then
        cs_state="nochange"; break
      fi
      if [ "$saw_busy" = "1" ] && [ "$quiet" -ge "$QUIET_POLLS" ]; then
        # busy を見たのに dll が進まず error CS も無い → U# 側や import だけだった可能性。変更なし扱い
        cs_state="nochange"; break
      fi
    fi
  fi
  sleep 2
done

# 4. error CS を数える
cs_lines="$(tail_from "$pos0" | grep -o 'error CS[0-9]*:.*' | sed 's/\r$//' | sort -u)"
cs_errors=0; [ -n "$cs_lines" ] && cs_errors=$(printf '%s\n' "$cs_lines" | wc -l | tr -d ' ')
[ "$cs_errors" -gt 0 ] && cs_state="error"
}
wait_csharp
# 削除した .cs がまだファイル一覧に残っている CS2001 だけなら、次のビルドで消える (vrc-synaptic-unity)。1 回だけ再 refresh して待ち直す
if [ "$cs_state" = "error" ] && [ -n "$cs_lines" ] && ! printf '%s\n' "$cs_lines" | grep -qv 'error CS2001:'; then
  echo "[unity-compile] CS2001 (削除済みソース) のみ。再 refresh して待ち直す" >&2
  pos0=$(log_size); dll0=$(dll_stamp); [ -z "$dll0" ] && dll0=0; expect_compile=1; DO_REFRESH=1
  wait_csharp
fi

# 5. UdonSharp
udon_state="skipped"; udon_lines=""; udon_errors=0
if [ "$DO_UDON" = "1" ] && [ "$cs_state" != "error" ] && [ "$cs_state" != "timeout" ]; then
  posU=$(log_size)
  UNITY_API_NO_GUARD=1 eval_json '{"tool":"unity_run_csharp","params":{"code":"UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms(true); return \"started\";"}}' >/dev/null 2>&1
  udon_state="timeout"
  while [ $(( $(date +%s) - start_ts )) -lt "$MAX_SECONDS" ]; do
    seg="$(tail_from "$posU")"
    # 完了ログの実物: "[<color=#0c824c>UdonSharp</color>] Compile of 82 scripts finished in 00:08.375" (SDK 3.10.5、実測 2026-09-25)
    if printf '%s' "$seg" | grep -qE 'UdonSharp.*Compile of [0-9]+ scripts finished'; then udon_state="ok"; break; fi
    # エラー行の実物も色付き: "[<color=#FF00FF>UdonSharp</color>] <path>(行,列): Method is not exposed to Udon: ..." (2026-10-15 実測。素の [UdonSharp] だけだと拾えず timeout に化けた)
    if printf '%s' "$seg" | grep -qiE 'UdonSharp(</color>)?\].*(error|exception|not exposed)|error CS'; then udon_state="error"; break; fi
    sleep 2
  done
  udon_lines="$(tail_from "$posU" | grep -iE 'UdonSharp(</color>)?\].*(error|exception|not exposed)|error CS' | sed 's/\r$//' | sort -u)"
  [ -n "$udon_lines" ] && { udon_errors=$(printf '%s\n' "$udon_lines" | wc -l | tr -d ' '); udon_state="error"; }
fi

emit "$cs_state" "$cs_errors" "$cs_lines" "$udon_state" "$udon_errors" "$(( $(date +%s) - start_ts ))" "$udon_lines"
case "$cs_state:$udon_state" in
  error:*|*:error) exit 1 ;;
  timeout:*|*:timeout) exit 2 ;;
  *) exit 0 ;;
esac
