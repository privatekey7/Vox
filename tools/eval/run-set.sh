#!/bin/sh
# Raw -> Final для обеих колонок набора через Vox.Replay; результат печатает score-set.py.
# Использование: tools/eval/run-set.sh [--misses] [выходная папка]
set -e
PY=python3; python3 -c "" 2>/dev/null || PY=python  # на Windows python3 бывает заглушкой Microsoft Store
cd "$(dirname "$0")/../.."
MISSES=""; [ "$1" = "--misses" ] && { MISSES="--misses"; shift; }
OUT="${1:-${TMPDIR:-/tmp}/vox-eval}"; mkdir -p "$OUT"
tail -n +2 tools/eval/anglicisms-30.tsv | cut -f2 | awk '{print $0"\t"$0}' > "$OUT/gigaam.in"
tail -n +2 tools/eval/anglicisms-30.tsv | cut -f3 | awk '{print $0"\t"$0}' > "$OUT/parakeet.in"
dotnet build tools/Vox.Replay -c Release -v q --nologo | grep -E "error|Warn.*[1-9] " || true
dotnet tools/Vox.Replay/bin/Release/net10.0*/Vox.Replay.dll --model gigaam "$OUT/gigaam.in" > "$OUT/gigaam.out"
dotnet tools/Vox.Replay/bin/Release/net10.0*/Vox.Replay.dll --model parakeet "$OUT/parakeet.in" > "$OUT/parakeet.out"
"$PY" tools/eval/score-set.py $MISSES "$OUT/gigaam.out" "$OUT/parakeet.out"
