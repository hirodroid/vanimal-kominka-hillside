#!/usr/bin/env bash
# Synaptic AI Pro for Blender の HTTP API ラッパー (unity-api.sh の Blender 版)。
#
# 接続先と認証ヘッダをスクリプト内に閉じ込め、settings.local.json 側の許可を
# `Bash(bash scripts/blender-api.sh:*)` の 1 行で済ませる。localhost:8085 以外へは
# 到達できない。
#
# Bearer トークンは **リポジトリに置かない**。次の順で探す:
#   1. 環境変数 SYNAPTIC_BLENDER_TOKEN
#   2. ~/.config/synaptic-ai/blender-token (1 行)
#      Blender: Edit > Preferences > Add-ons > Synaptic AI > HTTP Server の
#      "HTTP API Token" をそのまま貼る (インストールごとに違う値)
#
# Usage:
#   blender-api.sh health
#   blender-api.sh categories
#   blender-api.sh tools/category/Mesh            # カテゴリ名は大文字始まり (大小区別あり)
#   blender-api.sh 'tools/search?q=shape+key'
#   blender-api.sh execute '{"tool":"get_scene_info","params":{}}'
#   blender-api.sh batch '[{"tool":"create_primitive","params":{"type":"cube","name":"Box","size":2}}]'
set -euo pipefail

BASE="http://localhost:8085"
TOKEN_FILE="${HOME}/.config/synaptic-ai/blender-token"

endpoint="${1:-}"
if [ -z "$endpoint" ]; then
  echo "usage: blender-api.sh <endpoint> [json-payload]" >&2
  exit 2
fi
payload="${2:-}"

token="${SYNAPTIC_BLENDER_TOKEN:-}"
if [ -z "$token" ] && [ -f "$TOKEN_FILE" ]; then
  token="$(head -n 1 "$TOKEN_FILE" | tr -d '\r\n')"
fi
if [ -z "$token" ]; then
  echo "blender-api.sh: token not found" >&2
  echo "  Blender の Preferences > Add-ons > Synaptic AI > HTTP Server にある HTTP API Token を" >&2
  echo "  $TOKEN_FILE に 1 行で保存するか、環境変数 SYNAPTIC_BLENDER_TOKEN に入れてください" >&2
  exit 3
fi

if [ -n "$payload" ]; then
  curl -s -m 60 "$BASE/$endpoint" -X POST \
    -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "$payload"
else
  curl -s -m 30 "$BASE/$endpoint" -H "Authorization: Bearer $token"
fi
