#!/usr/bin/env bash
#
# seed-principles.sh — publishes seed/principles.json as a rule set via the
# running service's own write API (POST /api/rulesets), rather than inserting
# rows directly. The API is what computes the content hash and enforces the
# same validation any other publisher goes through — a seed script is a
# publisher, not a privileged shortcut.
#
# seed/principles.json carries a "_attribution" field for provenance (this
# rule set is condensed from konradcinkusz/architecture-standards); that
# field is informational only and is stripped before the request body is
# built, since CreateRuleSetRequest has no field for it.
#
# Usage:
#   ADMIN_API_KEY=... scripts/seed-principles.sh
#   ADMIN_API_KEY=... CONTEXT_PIN_URL=https://context-pin.example scripts/seed-principles.sh
#
# A version that already exists is treated as success, not failure: this
# script is meant to be safe to re-run, e.g. from a setup guide or CI step
# that doesn't track whether the seed already ran.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
SEED_FILE="${REPO_ROOT}/seed/principles.json"
CONTEXT_PIN_URL="${CONTEXT_PIN_URL:-http://localhost:5080}"

red()   { printf '\033[0;31m%s\033[0m\n' "$*"; }
green() { printf '\033[0;32m%s\033[0m\n' "$*"; }
dim()   { printf '\033[0;90m%s\033[0m\n' "$*"; }

if [ -z "${ADMIN_API_KEY:-}" ]; then
  red "seed-principles: ADMIN_API_KEY is not set."
  echo "This is the value the target service's AdminApiKey configuration holds —"
  echo "see appsettings.Development.json for the local-dev value."
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  red "seed-principles: jq is required but not on PATH."
  exit 1
fi

if [ ! -f "${SEED_FILE}" ]; then
  red "seed-principles: ${SEED_FILE} not found."
  exit 1
fi

VERSION="$(jq -r '.version' "${SEED_FILE}")"
ATTRIBUTION="$(jq -r '._attribution' "${SEED_FILE}")"
RULE_COUNT="$(jq '.rules | length' "${SEED_FILE}")"
REQUEST_BODY="$(jq '{version, status, rules}' "${SEED_FILE}")"

RESPONSE_FILE="$(mktemp)"
trap 'rm -f "${RESPONSE_FILE}"' EXIT

dim "Source: ${ATTRIBUTION}"
dim "Publishing rule set ${VERSION} (${RULE_COUNT} rules) to ${CONTEXT_PIN_URL} ..."

HTTP_STATUS="$(curl -sS -o "${RESPONSE_FILE}" -w '%{http_code}' \
  -X POST "${CONTEXT_PIN_URL}/api/rulesets" \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: ${ADMIN_API_KEY}" \
  -d "${REQUEST_BODY}")"

case "${HTTP_STATUS}" in
  201)
    green "Published rule set ${VERSION}."
    jq '.' "${RESPONSE_FILE}"
    ;;
  409)
    green "Rule set ${VERSION} already exists — nothing to do."
    ;;
  401)
    red "seed-principles: rejected — ADMIN_API_KEY does not match the service's AdminApiKey."
    exit 1
    ;;
  *)
    red "seed-principles: unexpected response (HTTP ${HTTP_STATUS})."
    cat "${RESPONSE_FILE}"
    exit 1
    ;;
esac
