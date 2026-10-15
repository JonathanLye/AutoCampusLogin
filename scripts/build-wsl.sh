#!/usr/bin/env bash
# 在 WSL 里调用 Windows 侧的 dotnet.exe 构建（同步到 Windows 临时目录，避开 \\wsl$ UNC 路径问题）。
# 用法：scripts/build-wsl.sh [build|test|publish] [额外参数...]
set -euo pipefail
cmd="${1:-build}"; shift || true
repo="$(cd "$(dirname "$0")/.." && pwd)"
win_temp="$(cd /mnt/c && wslpath "$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | iconv -f gbk -t utf-8 | tr -d '\r')")"
dst="$win_temp/AutoCampusLogin-build"
mkdir -p "$dst"
rsync -a --delete --exclude '.git' --exclude 'bin' --exclude 'obj' --exclude 'artifacts' "$repo/" "$dst/src-tree/"
dotnet="/mnt/c/Program Files/dotnet/dotnet.exe"
export DOTNET_CLI_UI_LANGUAGE=en-US DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export WSLENV="${WSLENV:+$WSLENV:}DOTNET_CLI_UI_LANGUAGE:DOTNET_CLI_TELEMETRY_OPTOUT:DOTNET_NOLOGO"
cd "$dst/src-tree"
case "$cmd" in
  build)   "$dotnet" build src/CampusLogin/CampusLogin.csproj -c Release -nologo "$@" ;;
  test)    "$dotnet" test tests/CampusLogin.Tests/CampusLogin.Tests.csproj -c Release -nologo "$@" ;;
  publish) "$dotnet" build src/CampusLogin/CampusLogin.csproj -c Release -nologo -o "$(wslpath -w "$dst/out")" "$@" ;;
  *) echo "unknown command: $cmd" >&2; exit 2 ;;
esac
echo "BUILD_DIR=$dst"
