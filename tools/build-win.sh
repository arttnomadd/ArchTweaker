#!/bin/bash
# Build the Windows exe on Linux: Mono mcs against the .NET Framework 4.5 reference assemblies, C# 5, with the
# app manifest (requireAdministrator), icon and version block from tools/mkres.py, then an IL audit for calls that
# exist on Mono / .NET Core but not on .NET Framework 4.5.
#   bash tools/build-win.sh <a.b.c.d> <out.exe>      (from the ArchTweaker folder; needs mono-mcs,
#   mono-reference-assemblies-4.5 or mono-devel, mono-utils for ikdasm, python3)
set -e
VER="$1"; OUT="$2"
if [ -z "$VER" ] || [ -z "$OUT" ]; then echo "usage: bash tools/build-win.sh <a.b.c.d> <out.exe>"; exit 2; fi
case "$VER" in *.*.*.*) ;; *) echo "version must have 4 parts, like 3.35.1.0"; exit 2;; esac
case "$OUT" in /*) ;; *) OUT="$(pwd)/$OUT";; esac
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$(dirname "$HERE")"; cd "$ROOT"
API=/usr/lib/mono/4.5-api
[ -d "$API" ] || { echo "missing $API: apt-get install mono-reference-assemblies-4.5 (or mono-devel)"; exit 2; }
RES="$(mktemp --suffix=.res)"; IL="$(mktemp --suffix=.il)"
trap 'rm -f "$RES" "$IL"' EXIT
python3 "$HERE/mkres.py" app.manifest app.ico "$VER" "$RES"
R=""; for a in mscorlib System System.Core System.Windows.Forms System.Drawing System.Management System.ServiceProcess System.Xml Accessibility System.IO.Compression; do R="$R -r:$API/$a.dll"; done
mcs -nologo -langversion:5 -nostdlib -noconfig -target:winexe -platform:anycpu -optimize+ -win32res:"$RES" -out:"$OUT" $R -recurse:'src/*.cs'
ikdasm "$OUT" > "$IL" 2>/dev/null
BAD=$(grep -E "String::(TrimStart|TrimEnd)\(\)|String::Split\(char,|String::Split\(char\)|String::(Contains|StartsWith|EndsWith)\(char\)|Array::Empty<" "$IL" | head -5 || true)
if [ -n "$BAD" ]; then echo "AUDIT FAILED:"; echo "$BAD"; exit 1; fi
echo "audit OK: $OUT ($(stat -c %s "$OUT") bytes)"
