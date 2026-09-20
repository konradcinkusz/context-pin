#!/usr/bin/env bash
#
# check-drift.sh — the logic behind this repository's drift-check GitHub
# Action (action.yml). Not meant to be run directly by a consumer; a
# consuming repo's workflow invokes it via `uses: konradcinkusz/context-pin@<ref>`
# with inputs, which the action maps to the environment variables read below.
#
# Compares the content hash recorded in a lock file (written by
# scripts/aurelius-sync.sh and committed by the consuming repo) against what
# context-pin currently serves for the same (owner, repo, channel), using
# If-None-Match so an unchanged manifest costs a 304 with no response body.

set -euo pipefail

: "${CONTEXT_PIN_URL:?CONTEXT_PIN_URL is required}"
CHANNEL="${CHANNEL:-stable}"
LOCK_FILE="${LOCK_FILE:-.aurelius/rules.lock.json}"

# owner/repo default to the repository this Action is running in, so the
# common case (a repo checking drift on itself) needs no explicit input.
OWNER="${OWNER:-}"
REPO="${REPO:-}"
if [ -z "${OWNER}" ] || [ -z "${REPO}" ]; then
  IFS='/' read -r DEFAULT_OWNER DEFAULT_REPO <<< "${GITHUB_REPOSITORY:-}"
  OWNER="${OWNER:-${DEFAULT_OWNER:-}}"
  REPO="${REPO:-${DEFAULT_REPO:-}}"
fi

if [ -z "${OWNER}" ] || [ -z "${REPO}" ]; then
  echo "::error::owner/repo were not provided and could not be inferred from GITHUB_REPOSITORY."
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "::error::jq is required on the runner but is not on PATH."
  exit 1
fi

if [ ! -f "${LOCK_FILE}" ]; then
  echo "::error::Lock file not found at ${LOCK_FILE}. Run scripts/aurelius-sync.sh and commit its output first."
  exit 1
fi

LOCKED_HASH="$(jq -r '.contentHash' "${LOCK_FILE}")"
LOCKED_VERSION="$(jq -r '.version' "${LOCK_FILE}")"

RESPONSE_FILE="$(mktemp)"
trap 'rm -f "${RESPONSE_FILE}"' EXIT

HTTP_STATUS="$(curl -sS -o "${RESPONSE_FILE}" -w '%{http_code}' \
  -H "If-None-Match: \"${LOCKED_HASH}\"" \
  "${CONTEXT_PIN_URL}/api/repos/${OWNER}/${REPO}/manifest?channel=${CHANNEL}")"

case "${HTTP_STATUS}" in
  304)
    echo "No drift: ${OWNER}/${REPO}@${CHANNEL} is still at version ${LOCKED_VERSION} (${LOCKED_HASH})."
    ;;
  200)
    LIVE_HASH="$(jq -r '.contentHash' "${RESPONSE_FILE}")"
    LIVE_VERSION="$(jq -r '.version' "${RESPONSE_FILE}")"
    if [ "${LIVE_HASH}" = "${LOCKED_HASH}" ]; then
      # A 200 with a matching hash is a valid (if less efficient) way for a
      # server to answer — not every context-pin-compatible server needs to
      # implement conditional requests for this Action to work correctly.
      echo "No drift: ${OWNER}/${REPO}@${CHANNEL} is still at version ${LOCKED_VERSION} (${LOCKED_HASH})."
    else
      echo "::error::Drift detected for ${OWNER}/${REPO}@${CHANNEL}: locked version ${LOCKED_VERSION} (${LOCKED_HASH}) but context-pin now serves version ${LIVE_VERSION} (${LIVE_HASH}). Run scripts/aurelius-sync.sh and commit the result."
      exit 1
    fi
    ;;
  404)
    echo "::error::${OWNER}/${REPO}@${CHANNEL} has no pin on ${CONTEXT_PIN_URL} — was it ever pinned, or was the pin removed?"
    exit 1
    ;;
  *)
    echo "::error::Unexpected response from context-pin (HTTP ${HTTP_STATUS})."
    cat "${RESPONSE_FILE}"
    exit 1
    ;;
esac
