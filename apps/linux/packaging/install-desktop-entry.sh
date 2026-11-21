#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../../.." && pwd)"
BIN_PATH="${REPO_ROOT}/apps/linux/src/Xueqing.Linux/bin/Release/net10.0/Xueqing.Linux"

if [[ ! -x "${BIN_PATH}" ]]; then
  echo "Xueqing.Linux binary not found at ${BIN_PATH}. Build it first with:" >&2
  echo "  dotnet build apps/linux/src/Xueqing.Linux/Xueqing.Linux.csproj -c Release" >&2
  exit 1
fi

EXTRA_ARGS=""
if [[ "${1:-}" == "--prototype" ]]; then
  EXTRA_ARGS="--prototype"
fi

mkdir -p "${HOME}/.local/bin" "${HOME}/.local/share/applications"

cat <<EOF > "${HOME}/.local/bin/xueqing-linux"
#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT="\${DOTNET_ROOT:-\${HOME}/.dotnet}"
export PATH="\${DOTNET_ROOT}:\${PATH}"
exec "${BIN_PATH}" ${EXTRA_ARGS} "\$@"
EOF
chmod 755 "${HOME}/.local/bin/xueqing-linux"

install -m 644 "${SCRIPT_DIR}/com.xueqing.app.desktop" "${HOME}/.local/share/applications/com.xueqing.app.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "${HOME}/.local/share/applications" || true
fi
if command -v xdg-mime >/dev/null 2>&1; then
  xdg-mime default com.xueqing.app.desktop x-scheme-handler/xueqing || true
fi

echo "Installed ~/.local/bin/xueqing-linux and registered x-scheme-handler/xueqing -> com.xueqing.app.desktop"
