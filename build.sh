#!/usr/bin/env bash
# Build Ditechs PC Check (Windows) from Linux, macOS or Windows.
#
#   ./build.sh              tests + the client exe + installer files + client zip
#   ./build.sh small        only the tiny .NET Framework 4.8 exe (best for clients)
#   ./build.sh big          only the self-contained .NET 8 exe
#   ./build.sh test         only the logic and wording tests
#   ./build.sh aptest       only the PowerShell installer analysis
#
# Code signing with a real certificate (see the repo README for which certificate):
#   SIGN_PFX=/path/to/cert.pfx SIGN_PASS=... [SIGN_ALIAS=...] ./build.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODE="${1:-all}"
DIST="$HERE/dist"
APP="DitechsPcCheck"

# ---------------------------------------------------------------- toolchain
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
[[ -z "$DOTNET" && -x /opt/data/dotnet/dotnet ]] && DOTNET=/opt/data/dotnet/dotnet
[[ -z "$DOTNET" ]] && { echo "ERROR: no dotnet SDK. Install the .NET 8 SDK or set DOTNET=/path/to/dotnet"; exit 1; }

# Slim images ship without libicu; dotnet and pwsh both need it.
ICU_DIR=""
if [[ -f /opt/data/tools/icu/libdir.txt ]]; then
  ICU_DIR="$(cat /opt/data/tools/icu/libdir.txt)"
  [[ "$ICU_DIR" != /* ]] && ICU_DIR="/opt/data/tools/icu/${ICU_DIR#./}"
  export LD_LIBRARY_PATH="$ICU_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$DOTNET")}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/opt/data/cache/scratch/dotnet-home}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [[ -n "$ICU_DIR" ]]; then
  unset DOTNET_SYSTEM_GLOBALIZATION_INVARIANT
else
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

PWSH="${PWSH:-$(command -v pwsh || true)}"
[[ -z "$PWSH" && -x /opt/data/tools/pwsh/pwsh ]] && PWSH=/opt/data/tools/pwsh/pwsh
JAVA="${JAVA:-$(command -v java || true)}"
[[ -z "$JAVA" && -x /opt/data/tools/jre21/bin/java ]] && JAVA=/opt/data/tools/jre21/bin/java
JSIGN="${JSIGN:-/opt/data/tools/jsign.jar}"

SIGN_TSA="${SIGN_TSA-http://timestamp.digicert.com}"

mkdir -p "$DIST"

# ------------------------------------------------------------------- helpers
do_tests() {
  echo "==> Logic and wording tests (PcCheckRules)"
  ( cd "$HERE" && "$DOTNET" run --project tests -c Release )
}

do_aptest() {
  echo "==> PowerShell installer analysis"
  if [[ -z "$PWSH" ]]; then echo "    SKIP: no pwsh available"; return 0; fi
  "$PWSH" -NoProfile -Command "
    \$issues = Invoke-ScriptAnalyzer -Path '$HERE/Installer/Install-DitechsPcCheck.ps1' -Severity Error,Warning
    if (\$issues) { \$issues | Format-Table -AutoSize; exit 1 } else { '    0 findings' }
  "
}

build_small() {
  echo "==> Building small .NET Framework 4.8 exe"
  ( cd "$HERE" && "$DOTNET" build -c Release -p:Fx=48 )
  cp "$HERE/bin/Release/net48/$APP.exe" "$DIST/$APP.exe"
}

build_big() {
  echo "==> Building self-contained .NET 8 exe"
  ( cd "$HERE" && "$DOTNET" publish -c Release )
  cp "$HERE/bin/Release/net8.0-windows/win-x64/publish/$APP.exe" "$DIST/$APP-standalone.exe"
}

do_sign() {
  local file="$1"
  [[ -z "${SIGN_PFX:-}" ]] && return 0
  [[ -f "$JSIGN" ]] || { echo "    SKIP signing: no jsign at $JSIGN"; return 0; }
  echo "==> Signing $(basename "$file")"
  "$JAVA" -jar "$JSIGN" \
    --keystore "$SIGN_PFX" --storepass "$SIGN_PASS" \
    ${SIGN_ALIAS:+--alias "$SIGN_ALIAS"} ${SIGN_CERT:+--certfile "$SIGN_CERT"} \
    --tsaurl "$SIGN_TSA" --tsmode RFC3161 "$file"
}

check_manifest() {
  # The whole point of this tool is that it never asks for permission. Fail the
  # build rather than ship a binary that prompts.
  echo "==> Checking the exe does not ask for administrator rights"
  python3 - "$DIST/$APP.exe" <<'PY'
import re, struct, sys
path = sys.argv[1]
b = open(path, "rb").read()
txt = b.decode("utf-8", errors="ignore")
levels = re.findall(r'level="([^"]+)"', txt)
print(f"    requestedExecutionLevel: {levels}")
if levels != ["asInvoker"]:
    print("    ERROR: this build would ask for permission. Refusing to package it.")
    sys.exit(1)
if re.search(r'level=.requireAdministrator', txt):
    print("    ERROR: requireAdministrator found.")
    sys.exit(1)
e = struct.unpack_from('<I', b, 0x3c)[0]
opt = e + 24
sub = struct.unpack_from('<H', b, opt + 68)[0]
print(f"    subsystem: {sub} ({'Windows GUI' if sub == 2 else 'not GUI'})")
sys.exit(0 if sub == 2 else 1)
PY
}

make_zip() {
  echo "==> Assembling the client zip"
  cp "$HERE/Installer/README.txt" "$DIST/README.txt"
  cp "$HERE/Installer/Install.cmd" "$DIST/Install.cmd"
  cp "$HERE/Installer/Install-DitechsPcCheck.ps1" "$DIST/Install-DitechsPcCheck.ps1"
  # Read the version out of the csproj rather than repeating it here. A hardcoded copy
  # drifts from the assembly version, and the website's download link is built from
  # this file name - so a stale one here becomes a broken link there.
  local version
  version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$HERE/DitechsPcCheck.csproj" | head -1)
  if [ -z "$version" ]; then
    echo "    !! could not read <Version> from DitechsPcCheck.csproj"
    return 1
  fi
  # Python rather than `zip`, so the build has no dependency on an external archiver.
  python3 - "$DIST" "$APP" "$version" <<'PY'
import os, sys, zipfile
dist, app, version = sys.argv[1], sys.argv[2], sys.argv[3]
files = [f"{app}.exe", f"{app}-standalone.exe",
         "Install.cmd", "Install-DitechsPcCheck.ps1", "README.txt"]
out = os.path.join(dist, f"{app}-{version}.zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in files:
        path = os.path.join(dist, name)
        if os.path.exists(path):
            z.write(path, name)
            print(f"    + {name:34} {os.path.getsize(path):>10,} bytes")
print(f"    = {os.path.basename(out)}  {os.path.getsize(out):,} bytes")
PY
}

# --------------------------------------------------------------------- modes
case "$MODE" in
  test)    do_tests ;;
  aptest)  do_aptest ;;
  small)   build_small; check_manifest; make_zip ;;
  big)     build_big ;;
  all)
    do_tests
    do_aptest
    build_small
    check_manifest
    do_sign "$DIST/$APP.exe"
    make_zip
    # The MSI is built by CI on a Windows runner (WiX is Windows-only).
    if [[ "${WITH_MSI:-0}" != "1" ]]; then
      echo "==> MSI: skipped on this host (WiX needs Windows). CI builds it on windows-latest."
    fi
    echo
    echo "Done. Output in $DIST:"
    ls -la "$DIST" | tail -n +2
    ;;
  *) echo "Unknown mode: $MODE"; exit 1 ;;
esac
