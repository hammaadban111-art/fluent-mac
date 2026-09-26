## End-to-end on Windows: 34/34 passed, 0 required failure(s)
- ✅ Fluent starts and answers
- ✅ Notepad opens
- ✅ Notepad text area found — Document
- ✅ Fluent sees the Notepad text box — process=notepad type=Document kind=Editable
- ✅ bubble appears next to the text box — bubble=[818,546,58,58] field=[138,181,752,437]
- ✅ bubble is round and bubble-sized — 58x58 px
- ✅ the orb is drawn where Fluent says the bubble is — pixel=220,134,220
- ✅ shortcut starts a dictation — phase=Recording
- ✅ capsule shows while recording — [280,10,464,82]
- ✅ bubble hides while the capsule owns the session
- ✅ shortcut dictation lands in Notepad — text="sounds good are you free for lunch tomorrow let's do twelve if that works" route=live insert=Pasted: sounds good are you free for lunch tomorrow let's do twelve if that works
- ✅ transcript came over Gemini Live (streamed) — latency=222.6244 ms
- ✅ latency after stop under 2 s — 222.6244 ms
- ✅ user's clipboard is put back — clipboard="ORIGINAL CLIPBOARD"
- ✅ Notepad kept the focus
- ✅ bubble click starts a dictation
- ✅ bubble click does not steal focus
- ✅ capsule Stop button reachable
- ✅ capsule Stop inserts, spaced after the earlier text — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ Notepad still focused after the capsule click
- ✅ holding Right Ctrl dictates and inserts on release — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ rejected live socket falls back to the batch request — route=batch batchHits=1
- ✅ Style applies (Other → Excited turns the full stop into !) — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works Batch fallback works!"
- ✅ Esc cancels without inserting — phase=Idle
- ✅ Edge opens the test page
- ✅ Edge Message: Fluent sees an editable box — type=Edit kind=Editable bubble=true
- ✅ Edge Message: text inserted — insert={   "kind": "Pasted",   "detail": "Hello from Fluent" } text="Hello from Fluent"
- ✅ Edge Subject: Fluent sees an editable box — type=Edit kind=Editable bubble=true
- ✅ Edge Subject: text inserted — insert={   "kind": "Pasted",   "detail": " lunch tomorrow" } text="Re: lunch tomorrow"
- ✅ Edge Editor: Fluent sees an editable box — type=Edit kind=Editable bubble=true
- ✅ Edge Editor: text inserted — insert={   "kind": "Pasted",   "detail": "Typed into a rich editor" } text="Typed into a rich editor"
- ✅ password field: no bubble — kind=Secure
- ✅ password field: Fluent refuses to type — {"kind":"Failed","detail":"refusing to type into a password field"}
- ✅ Fluent quits cleanly
