#!/usr/bin/env bash
# Synaptic AI Pro (Unity) HTTP API wrapper.
#
# 接続先を固定し curl のフラグをスクリプト内に閉じ込めるためのラッパー。
# settings.local.json 側の許可ルールを 1 行で済ませ、localhost:8086 以外へは
# 到達できないようにする狙い。
#
# Usage:
#   unity-api.sh health
#   unity-api.sh check                      # 接続先が「このプロジェクト」か照合する (Application.dataPath)
#   unity-api.sh categories
#   unity-api.sh tools/category/scene
#   unity-api.sh execute '{"tool":"unity_analyze_console_logs","params":{"logType":"error","limit":50}}'
#   unity-api.sh batch '[{"tool":"unity_create_gameobject","params":{"name":"Cube1","type":"cube"}}]'
#
# ## 接続先の照合 (2026-09-25)
#
# Unity を 2 プロジェクト同時に開くと 8086 は先に起動した方が占有し、health が ok でも
# 別プロジェクトに繋がっていることがある (実測 2026-09-05)。execute / batch の前に
# `Application.dataPath` がこのプロジェクトの Assets/ か確かめ、違えば exit 3 で止める。
# 照合結果は一時ファイルに UNITY_API_GUARD_TTL 秒 (既定 600) キャッシュする。
# ドメインリロード中で dataPath が取れないときは照合をスキップして通す (WARN を出す)。
# UNITY_API_NO_GUARD=1 で照合を無効化できる (照合自身の呼び出しにも使う)。
set -euo pipefail

BASE="http://localhost:8086"
ROOT="$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))"

endpoint="${1:-}"
if [ -z "$endpoint" ]; then
  echo "usage: unity-api.sh <endpoint> [json-payload]" >&2
  exit 2
fi
payload="${2:-}"

norm() { printf '%s' "$1" | tr 'A-Z' 'a-z' | sed 's#\\#/#g; s#/*$##'; }

# 接続先の dataPath を返す (取れなければ空)
data_path() {
  local resp
  resp="$(curl -s -m 15 "$BASE/execute" -X POST -H "Content-Type: application/json" \
    -d '{"tool":"unity_run_csharp","params":{"code":"return UnityEngine.Application.dataPath;"}}' 2>/dev/null || true)"
  node -e 'try{const r=JSON.parse(process.argv[1]);let v=r.result;try{v=JSON.parse(v)}catch(_){}if(v&&typeof v==="object")v=v.result||"";process.stdout.write(String(v||""))}catch(e){}' "$resp"
}

guard() {
  [ "${UNITY_API_NO_GUARD:-}" = "1" ] && return 0
  local ttl="${UNITY_API_GUARD_TTL:-600}"
  local key cache now
  key="$(printf '%s' "$ROOT" | node -e 'const c=require("crypto");let s="";process.stdin.on("data",d=>s+=d).on("end",()=>process.stdout.write(c.createHash("sha1").update(s).digest("hex").slice(0,16)))')"
  cache="${TMPDIR:-${TEMP:-/tmp}}/unity-api-guard-$key"
  now="$(date +%s)"
  if [ -f "$cache" ]; then
    local ts; ts="$(cat "$cache" 2>/dev/null || echo 0)"
    [ $((now - ts)) -lt "$ttl" ] && return 0
  fi
  local got want
  got="$(data_path)"
  want="$(norm "$ROOT/Assets")"
  if [ -z "$got" ]; then
    echo "[unity-api] WARN: dataPath が取れない (リロード中?)。接続先の照合をスキップ" >&2
    return 0
  fi
  if [ "$(norm "$got")" != "$want" ]; then
    echo "[unity-api] 接続先が別プロジェクト: $got (期待: $ROOT/Assets)。もう一方の Unity の HTTP Server を止める (Tools > Synaptic Pro > Synaptic Setup > HTTP Server)" >&2
    exit 3
  fi
  printf '%s' "$now" > "$cache"
}

case "$endpoint" in
  check)
    got="$(data_path)"
    if [ -z "$got" ]; then echo "[unity-api] dataPath が取れない (未接続かリロード中)" >&2; exit 1; fi
    if [ "$(norm "$got")" = "$(norm "$ROOT/Assets")" ]; then
      echo "[unity-api] OK: $got"; exit 0
    else
      echo "[unity-api] 接続先が別プロジェクト: $got (期待: $ROOT/Assets)" >&2; exit 3
    fi
    ;;
  execute|batch) guard ;;
esac

if [ -n "$payload" ]; then
  curl -s -m 60 "$BASE/$endpoint" -X POST -H "Content-Type: application/json" -d "$payload"
else
  curl -s -m 30 "$BASE/$endpoint"
fi
