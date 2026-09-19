#!/usr/bin/env bash
# Synaptic AI Pro (Unity) HTTP API wrapper.
#
# 接続先を固定し curl のフラグをスクリプト内に閉じ込めるためのラッパー。
# settings.local.json 側の許可ルールを 1 行で済ませ、localhost:8086 以外へは
# 到達できないようにする狙い。
#
# Usage:
#   unity-api.sh health
#   unity-api.sh categories
#   unity-api.sh tools/category/scene
#   unity-api.sh execute '{"tool":"unity_analyze_console_logs","params":{"logType":"error","limit":50}}'
#   unity-api.sh batch '[{"tool":"unity_create_gameobject","params":{"name":"Cube1","type":"cube"}}]'
set -euo pipefail

BASE="http://localhost:8086"

endpoint="${1:-}"
if [ -z "$endpoint" ]; then
  echo "usage: unity-api.sh <endpoint> [json-payload]" >&2
  exit 2
fi
payload="${2:-}"

if [ -n "$payload" ]; then
  curl -s -m 60 "$BASE/$endpoint" -X POST -H "Content-Type: application/json" -d "$payload"
else
  curl -s -m 30 "$BASE/$endpoint"
fi
