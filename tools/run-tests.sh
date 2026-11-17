#!/bin/sh
# Headless unit tests: build Tests/ + src/ into one exe and run it from the ArchTweaker folder (so the tests that
# read src/, CLAUDE.md, lang/ and e2e/ find them). UI tests need a display: xvfb-run is used when it is installed.
# Must end with "N passed, 0 failed".
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; cd "$(dirname "$HERE")"
OUT="${TMPDIR:-/tmp}/archtweaker-tests.exe"
mcs -nologo -langversion:5 -target:exe -main:Runner -out:"$OUT" \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Management.dll -r:System.ServiceProcess.dll \
  -r:System.Core.dll -r:System.Xml.dll -r:Accessibility.dll -r:System.IO.Compression.dll \
  -recurse:'src/*.cs' -recurse:'Tests/*.cs'
if command -v xvfb-run >/dev/null 2>&1; then xvfb-run -a mono "$OUT"; else mono "$OUT"; fi
