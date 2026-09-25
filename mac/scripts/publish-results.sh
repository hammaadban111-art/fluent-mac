#!/bin/bash
# Publishes CI output to the mac-ci-results branch (Actions artifact storage on this account is
# full). Usage: publish-results.sh <folder on the branch> <local folder>
# Other folders already on the branch are kept; the branch is a single squashed commit so it stays small.
set -euo pipefail
NAME="${1:?}"
SRC="$(cd "${2:?}" && pwd)"
URL="https://x-access-token:${GITHUB_TOKEN:?}@github.com/${GITHUB_REPOSITORY:?}.git"
WORK="$(mktemp -d)"
for attempt in 1 2 3; do
    rm -rf "${WORK:?}/r"
    if git clone -q --depth 1 --branch mac-ci-results "$URL" "$WORK/r" 2>/dev/null; then :; else
        mkdir -p "$WORK/r" && git -C "$WORK/r" init -q -b mac-ci-results
    fi
    cd "$WORK/r"
    rm -rf "./${NAME:?}"
    mkdir -p "$NAME"
    cp -R "$SRC/." "$NAME/"
    printf 'Run %s for %s at %s\n' "${GITHUB_RUN_ID:-local}" "${GITHUB_SHA:-?}" "$(date -u +%FT%TZ)" > "$NAME/RUN.txt"
    git config user.name "fluent-mac-ci"
    git config user.email "ci@users.noreply.github.com"
    git checkout -q --orphan fresh
    git add -A
    git commit -qm "Mac CI results: $NAME (run ${GITHUB_RUN_ID:-local})"
    git branch -D mac-ci-results >/dev/null 2>&1 || true
    git branch -m mac-ci-results
    if git push -qf "$URL" mac-ci-results; then echo "Published $NAME to mac-ci-results"; exit 0; fi
    cd /
    sleep $((attempt * 5))
done
echo "Could not publish to mac-ci-results"
exit 1
