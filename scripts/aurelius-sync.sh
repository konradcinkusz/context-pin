#!/usr/bin/env bash
#
# aurelius-sync.sh — fetches the manifest currently pinned for (owner, repo,
# channel) from a running context-pin service and writes it to a lock file.
#
# The lock file is what a consuming repo commits and what its CI reads day to
# day: no network call needed on the hot path. Drift-checking the committed
# lock against what context-pin currently serves is a separate, always-online
# concern handled by this repository's GitHub Action (action.yml /
# check-drift.sh), not by this script — this script only ever writes what
# context-pin says right now; it never fails a build over staleness.
#
# Usage:
#   CONTEXT_PIN_URL=https://context-pin.example scripts/aurelius-sync.sh <owner> <repo> [channel] [lock-file]
#
# Defaults: channel "stable", lock-file ".aurelius/rules.lock.json",
# CONTEXT_PIN_URL "http://localhost:5080" (local dev).

set -euo pipefail

usage() {
  echo "Usage: CONTEXT_PIN_URL=<url> scripts/aurelius-sync.sh <owner> <repo> [channel] [lock-file]" >&2
}

if [ $# -lt 2 ]; then
  usage
  exit 1
fi

OWNER="$1"
REPO="$2"
CHANNEL="${3:-stable}"
LOCK_FILE="${4:-.aurelius/rules.lock.json}"
CONTEXT_PIN_URL="${CONTEXT_PIN_URL:-http://localhost:5080}"

red()   { printf '\033[0;31m%s\033[0m\n' "$*"; }
green() { printf '\033[0;32m%s\033[0m\n' "$*"; }
dim()   { printf '\033[0;90m%s\033[0m\n' "$*"; }

if ! command -v jq >/dev/null 2>&1; then
  red "aurelius-sync: jq is required but not on PATH."
  exit 1
fi

RESPONSE_FILE="$(mktemp)"
trap 'rm -f "${RESPONSE_FILE}"' EXIT

dim "Fetching ${OWNER}/${REPO}@${CHANNEL} from ${CONTEXT_PIN_URL} ..."

HTTP_STATUS="$(curl -sS -o "${RESPONSE_FILE}" -w '%{http_code}' \
  "${CONTEXT_PIN_URL}/api/repos/${OWNER}/${REPO}/manifest?channel=${CHANNEL}")"

if [ "${HTTP_STATUS}" != "200" ]; then
  red "aurelius-sync: manifest fetch failed (HTTP ${HTTP_STATUS})."
  cat "${RESPONSE_FILE}"
  exit 1
fi

mkdir -p "$(dirname "${LOCK_FILE}")"

# owner/repo/syncedAt are not part of the API response (the URL already
# scopes owner/repo, and the API has no reason to timestamp its own answer) —
# added here because the lock file is a standalone artifact read long after
# this request completes, and both are useful for a human looking at a diff.
jq --arg owner "${OWNER}" --arg repo "${REPO}" \
   --arg syncedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
   '{owner: $owner, repo: $repo, channel, version, contentHash, syncedAt: $syncedAt, rules}' \
  "${RESPONSE_FILE}" > "${LOCK_FILE}"

RULE_COUNT="$(jq '.rules | length' "${LOCK_FILE}")"
VERSION="$(jq -r '.version' "${LOCK_FILE}")"
green "Synced ${OWNER}/${REPO}@${CHANNEL} -> ${LOCK_FILE} (version ${VERSION}, ${RULE_COUNT} rules)."
