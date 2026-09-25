#!/bin/bash
# CI end-to-end checks on a macOS runner, against the installed /Applications/Fluent.app.
# Everything lands in $OUT (screenshots, JSON reports, SUMMARY.md); nothing here prints secrets.
set -uo pipefail
cd "$(dirname "$0")/.."
OUT="${OUT:?}"
APP=/Applications/Fluent.app
HOST_BIN="${HOST_BIN:?}"      # PasteOnlyHost
CLI_BIN="${CLI_BIN:?}"        # fluent-cli
S="$OUT/SUMMARY.md"
mkdir -p "$OUT/shots/demo" "$OUT/reports"
FAILED=0

pass() { echo "- ✅ $*" | tee -a "$S"; }
fail() { echo "- ❌ $*" | tee -a "$S"; FAILED=1; }
note() { echo "- ℹ️ $*" | tee -a "$S"; }
shot() { screencapture -x "$OUT/shots/$1.png" 2>/dev/null && [ -s "$OUT/shots/$1.png" ] || note "screencapture could not save $1"; }
# json <file> <python subscript, e.g. "['outcome']">
json() { python3 - "$1" "$2" <<'PY' 2>/dev/null
import json, sys
d = json.load(open(sys.argv[1]))
v = eval("d" + sys.argv[2])
print(v if not isinstance(v, (dict, list)) else json.dumps(v))
PY
}

# Runs one `Fluent --self-test` in its own process (launched by LaunchServices, so macOS checks
# Fluent's own permissions) and waits for its JSON report.
selftest() {
    local name="$1"; shift
    local report="$OUT/reports/$name.json"
    rm -f "${report:?}"
    open -g -n -a "$APP" --args --self-test "$@" --out "$report"
    for _ in $(seq 1 60); do [ -s "$report" ] && break; sleep 0.5; done
    [ -s "$report" ] || echo '{"error":"no report (timed out)"}' > "$report"
    sleep 0.3
}

frontmost() { lsappinfo info -only bundleid "$(lsappinfo front)" 2>/dev/null | sed 's/.*="\(.*\)"/\1/'; }

echo "## End-to-end on $(sw_vers -productName) $(sw_vers -productVersion) ($(uname -m))" >> "$S"
selftest probe-start probe
if [ "$(json "$OUT/reports/probe-start.json" "['trusted']")" = "True" ]; then
    pass "Fluent is trusted for Accessibility on the runner (TCC rows written by ci-grant-tcc.sh)"
else
    fail "Fluent is NOT trusted for Accessibility on the runner, so the insertion tests below cannot pass"
fi

# ---------------------------------------------------------------- 1. TextEdit, Accessibility route
TE_FILE=/tmp/fluent-e2e.txt
: > "$TE_FILE"
open -a TextEdit "$TE_FILE"
sleep 4
selftest probe-textedit probe
echo "- TextEdit field as Fluent sees it: \`$(json "$OUT/reports/probe-textedit.json" "['field']")\`" >> "$S"
TE_TEXT="Fluent typed this into TextEdit on a Mac runner."
selftest insert-textedit insert --text "$TE_TEXT"
selftest read-textedit read
selftest save-textedit key --key s
sleep 1.5
ON_DISK="$(cat "$TE_FILE")"
OUTCOME="$(json "$OUT/reports/insert-textedit.json" "['outcome']")"
READ_BACK="$(json "$OUT/reports/read-textedit.json" "['value']")"
shot textedit-inserted
if [ "$ON_DISK" = "$TE_TEXT" ] && [ "$READ_BACK" = "$TE_TEXT" ]; then
    pass "TextEdit: inserted via **$OUTCOME**; read back through Accessibility by a fresh process, and saved to disk as exactly \"$ON_DISK\""
elif [ "$READ_BACK" = "$TE_TEXT" ]; then
    pass "TextEdit: inserted via **$OUTCOME** and read back exactly by a fresh process (⌘S save gave: \"$ON_DISK\")"
else
    fail "TextEdit: outcome=$OUTCOME, read back \"$READ_BACK\", on disk \"$ON_DISK\" (see reports/insert-textedit.json)"
fi

# A second dictation after existing text: the spacing rules on a real field.
selftest insert-textedit-2 insert --text "Second sentence."
selftest read-textedit-2 read
READ2="$(json "$OUT/reports/read-textedit-2.json" "['value']")"
if [ "$READ2" = "$TE_TEXT Second sentence." ]; then
    pass "TextEdit: a second dictation got exactly one separating space: \"$READ2\""
else
    fail "TextEdit spacing: got \"$READ2\""
fi

# ---------------------------------------------------------------- 2. A field that refuses Accessibility writes
PO_FILE=/tmp/paste-only.txt
: > "$PO_FILE"
"$HOST_BIN" "$PO_FILE" &
HOST_PID=$!
sleep 4
selftest probe-pasteonly probe
PO_TEXT="and this arrived by paste."
selftest insert-pasteonly insert --text "$PO_TEXT"
sleep 1
PO_CONTENT="$(cat "$PO_FILE" 2>/dev/null)"
PO_OUTCOME="$(json "$OUT/reports/insert-pasteonly.json" "['outcome']")"
PO_CLIP="$(json "$OUT/reports/insert-pasteonly.json" "['clipboardAfter']")"
shot pasteonly-inserted
if [ "$PO_CONTENT" = "Pasted: $PO_TEXT" ] && [ "$PO_OUTCOME" = "pasted" ]; then
    pass "Paste-only field: the Accessibility write was refused, Fluent fell back to ⌘V; the app's own text is exactly \"$PO_CONTENT\""
else
    fail "Paste-only field: outcome=$PO_OUTCOME, app text \"$PO_CONTENT\" (see reports/insert-pasteonly.json)"
fi
if [ "$PO_CLIP" = "clipboard-sentinel" ]; then
    pass "The paste fallback put the previous clipboard contents back afterwards"
else
    fail "Clipboard after paste was \"$PO_CLIP\", expected the previous contents back"
fi
kill "$HOST_PID" 2>/dev/null

# ---------------------------------------------------------------- 3. Chrome (its tree needs AXEnhancedUserInterface)
CHROME="/Applications/Google Chrome.app"
if [ -d "$CHROME" ]; then
    open -na "$CHROME" --args --no-first-run --no-default-browser-check --disable-search-engine-choice-screen \
        --user-data-dir=/tmp/chrome-e2e "data:text/html,<title>Fluent</title><textarea id=t autofocus style='width:90%;height:300px;font-size:20px'></textarea>"
    sleep 10
    selftest probe-chrome probe
    CH_TEXT="Hello from Fluent inside Chrome."
    selftest insert-chrome insert --text "$CH_TEXT"
    selftest read-chrome read
    CH_READ="$(json "$OUT/reports/read-chrome.json" "['value']")"
    CH_OUT="$(json "$OUT/reports/insert-chrome.json" "['outcome']")"
    shot chrome-inserted
    if [ "$CH_READ" = "$CH_TEXT" ]; then
        pass "Chrome textarea: inserted via **$CH_OUT** and read back exactly"
    else
        note "Chrome textarea (best effort): outcome=$CH_OUT, read back \"$CH_READ\" (reports/*chrome*.json, shots/chrome-inserted.png)"
    fi
    osascript -e 'quit app "Google Chrome"' >/dev/null 2>&1
    pkill -f chrome-e2e 2>/dev/null
else
    note "Google Chrome is not installed on this runner image, so the Chromium check was skipped"
fi

# ---------------------------------------------------------------- 4. The real app: bubble next to a field, click → capsule
defaults write com.hammaad.fluent.mac terms_accepted_version -string "2026-09-24"
defaults write com.hammaad.fluent.mac setup_complete -bool true
printf 'Meeting notes\n\n' > /tmp/fluent-bubble.txt
open -a TextEdit /tmp/fluent-bubble.txt
sleep 2
open -a "$APP"
sleep 5
shot app-main-window
open -a TextEdit
sleep 3
selftest windows-bubble windows
shot bubble-textedit
BUBBLE="$(python3 - "$OUT/reports/windows-bubble.json" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
small = [w for w in d.get("windows", []) if 30 <= w["bounds"].get("Width", 0) <= 90
         and abs(w["bounds"].get("Width", 0) - w["bounds"].get("Height", 0)) < 2]
if small:
    b = small[0]["bounds"]
    print(f'{b["X"] + b["Width"] / 2:.0f},{b["Y"] + b["Height"] / 2:.0f}')
PY
)"
if [ -n "$BUBBLE" ]; then
    pass "Bubble: shown next to the focused TextEdit document (centre at $BUBBLE); see shots/bubble-textedit.png"
    selftest click-bubble click --at "$BUBBLE"
    sleep 1.2
    shot capsule-after-click
    selftest windows-capsule windows
    if python3 -c "import json,sys; d=json.load(open('$OUT/reports/windows-capsule.json')); sys.exit(0 if any(w['bounds'].get('Width',0)>300 for w in d.get('windows',[])) else 1)"; then
        pass "Clicking the bubble opened the recording capsule; see shots/capsule-after-click.png"
    else
        fail "Clicking the bubble did not show the capsule (reports/windows-capsule.json)"
    fi
    FRONT="$(frontmost)"
    if [ "$FRONT" = "com.apple.TextEdit" ]; then
        pass "TextEdit stayed the active app when the bubble was clicked (the bubble does not steal focus)"
    else
        fail "Frontmost app after clicking the bubble: $FRONT"
    fi
    sleep 4
    shot capsule-later
else
    fail "Bubble: no bubble window found next to TextEdit (reports/windows-bubble.json)"
fi
pkill -x Fluent 2>/dev/null
sleep 1

# ---------------------------------------------------------------- 5. Every screen renders (UI check + screenshots)
printf 'Launch plan\n\n- Ship Fluent for Mac\n- Record the teaser\n' > /tmp/fluent-notes.txt
DEMO_DIR="$OUT/shots/demo"
demo() {  # screen theme expected-text
    local screen="$1" theme="$2" want="$3"
    rm -f "${DEMO_DIR:?}/${screen:?}.ready"
    open -n -a "$APP" --args --demo "$screen" --theme "$theme" --snap-dir "$DEMO_DIR"
    for _ in $(seq 1 40); do [ -f "$DEMO_DIR/$screen.ready" ] && break; sleep 0.5; done
    sleep 0.5
    local pid
    pid="$(pgrep -n -x Fluent)"
    shot "screen-$screen-$theme"
    selftest "dump-$screen-$theme" dump --pid "$pid"
    [ -f "$DEMO_DIR/app-$screen.png" ] && mv "$DEMO_DIR/app-$screen.png" "$DEMO_DIR/app-$screen-$theme.png"
    if grep -qF "$want" "$OUT/reports/dump-$screen-$theme.json"; then
        pass "Screen **$screen** ($theme) rendered; its Accessibility tree contains \"$want\""
    else
        fail "Screen $screen ($theme): \"$want\" not found in reports/dump-$screen-$theme.json"
    fi
    kill "$pid" 2>/dev/null
    sleep 0.8
}
demo terms aurora "Welcome to Fluent"
demo onboarding aurora "Set up Fluent"
demo dictate aurora "Ready when you are."
demo history aurora "History"
demo style aurora "Match my style"
demo settings aurora "Gemini API key"
for t in porcelain obsidian ember lagoon; do demo dictate "$t" "Ready when you are."; done
demo settings porcelain "Gemini API key"
open -a TextEdit /tmp/fluent-notes.txt
sleep 2
demo capsule-listening aurora "Listening"
demo capsule-writing aurora "Writing it up"
demo capsule-inserted aurora "Inserted"
demo capsule-error aurora "Add your Gemini API key in Settings first."
demo capsule-listening ember "Listening"
demo capsule-listening lagoon "Listening"

# ---------------------------------------------------------------- 6. Speech to text: mock server, then Gemini
say -o /tmp/fluent-say.aiff "Hey team, the Mac build is ready. Let's ship it on Friday."
afconvert -f WAVE -d LEI16@16000 -c 1 /tmp/fluent-say.aiff /tmp/fluent-say.wav
python3 scripts/mock_gemini.py 8765 "$OUT/reports/mock-requests.jsonl" &
MOCK=$!
sleep 1
MOCK_OUT="$(FLUENT_GEMINI_KEY=mock-key "$CLI_BIN" transcribe /tmp/fluent-say.wav --endpoint http://127.0.0.1:8765/v1beta/interactions --app com.apple.MobileSMS 2>&1)"
kill $MOCK 2>/dev/null
if [ "$MOCK_OUT" = "Hey the mock server heard you" ]; then
    pass "Mock Gemini server accepted the request from a \`say\` WAV ($(head -1 "$OUT/reports/mock-requests.jsonl")) and the transcript came back through the Messages (Personal, Casual) style: \"$MOCK_OUT\""
else
    fail "Mock Gemini round trip: \"$MOCK_OUT\" (reports/mock-requests.jsonl)"
fi

if [ -n "${GEMINI_API_KEY:-}" ]; then
    REAL="$(FLUENT_GEMINI_KEY="$GEMINI_API_KEY" "$CLI_BIN" transcribe /tmp/fluent-say.wav 2>&1)"
    echo "$REAL" > "$OUT/reports/gemini-real.txt"
    NORM="$(echo "$REAL" | tr 'A-Z' 'a-z' | tr -cd 'a-z ')"
    if echo "$NORM" | grep -q "mac build is ready" && echo "$NORM" | grep -q "ship it on friday"; then
        pass "Real Gemini transcription of a \`say\` recording: \"$REAL\""
    else
        fail "Real Gemini transcription did not match: \"$REAL\""
    fi
else
    note "No GEMINI_API_KEY repository secret, so the real Gemini transcription test was skipped"
fi

echo "" >> "$S"
if [ "$FAILED" = 0 ]; then echo "**All end-to-end checks passed.**" >> "$S"; else echo "**Some end-to-end checks failed (see above).**" >> "$S"; fi
exit 0
