#!/usr/bin/env bash
#
# setup.sh — one-command onboarding.
#
# Checks prerequisites, installs the pre-commit secret-scan hook, and verifies
# the tree it expects to find. Pass --check for the strict form used in CI (exits
# 1 rather than warning when a scanner is missing).

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
STRICT=0
[ "${1:-}" = "--check" ] && STRICT=1

red()   { printf '\033[0;31m%s\033[0m\n' "$*"; }
green() { printf '\033[0;32m%s\033[0m\n' "$*"; }
dim()   { printf '\033[0;90m%s\033[0m\n' "$*"; }

fail_or_warn() {
  if [ "${STRICT}" -eq 1 ]; then
    red "$1"
    exit 1
  else
    dim "$1"
  fi
}

echo "context-pin setup"
echo

if command -v dotnet >/dev/null 2>&1; then
  green ".NET SDK found: $(dotnet --version)"
else
  fail_or_warn ".NET SDK not found — install .NET 9 to build/run/test the service."
fi

if command -v gitleaks >/dev/null 2>&1; then
  green "gitleaks found: $(gitleaks version 2>&1 | head -1)"
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  green "gitleaks not on PATH, but Docker is available — the scanner will run via container."
else
  fail_or_warn "Neither gitleaks nor a running Docker daemon found — secret scanning will not run locally."
fi

HOOK_SRC="${REPO_ROOT}/scripts/hooks/pre-commit"
HOOK_DST="${REPO_ROOT}/.git/hooks/pre-commit"

if [ ! -d "${REPO_ROOT}/.git" ]; then
  fail_or_warn "Not a git checkout — skipping hook installation."
else
  if [ -f "${HOOK_DST}" ] && [ ! -L "${HOOK_DST}" ]; then
    mv "${HOOK_DST}" "${HOOK_DST}.bak.$(date +%s)"
    dim "Existing pre-commit hook backed up."
  fi
  cp "${HOOK_SRC}" "${HOOK_DST}"
  chmod +x "${HOOK_DST}"
  green "pre-commit hook installed."
fi

echo
green "Setup complete."
echo "Run 'scripts/scan-secrets.sh' any time to reproduce the CI secret scan locally."
