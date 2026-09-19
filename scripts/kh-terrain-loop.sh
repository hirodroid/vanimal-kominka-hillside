#!/usr/bin/env bash
# Terrain 試作の 1 周: 保存 → Build Stage A [→ Stage B] → Verify → View Shots。引数 "B" で Stage B も回す。
set -euo pipefail
cd "$(dirname "$0")/.."
run() { bash scripts/unity-api.sh execute "{\"tool\":\"unity_run_csharp\",\"params\":{\"code\":\"return UnityEditor.EditorApplication.ExecuteMenuItem(\\\"Tools/KominkaHillside/Terrain Prototype/$1\\\").ToString();\"}}" >/dev/null; }
bash scripts/unity-api.sh execute '{"tool":"unity_run_csharp","params":{"code":"bool d = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty; if (d) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); return d.ToString();"}}' >/dev/null
run "Build Stage A (Ground)"; sleep 4
grep -a "ABORT\|ERROR\|path profile\|heights" Temp/KH_TerrainPrototype/build-A.log || true
if [ "${1:-}" = "B" ]; then run "Build Stage B (Dressing)"; sleep 4; grep -a "ABORT\|ERROR\|trees\|viewset\|details" Temp/KH_TerrainPrototype/build-B.log || true; fi
run "Verify"; sleep 3
grep -a "RESULT\|^NG\|DIAG\|edge min\|length" Temp/KH_TerrainPrototype/verify.txt
run "Render View Shots"; sleep 8
echo "shots: $(ls Temp/KH_ViewShots/*.png | wc -l)"
