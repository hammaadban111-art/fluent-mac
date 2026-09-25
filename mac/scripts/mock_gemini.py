#!/usr/bin/env python3
"""A stand-in for the Gemini Interactions endpoint, for CI. It checks that the request has the
shape the Android and iPhone apps send, records it, and answers with a canned transcript."""
import base64, json, sys
from http.server import BaseHTTPRequestHandler, HTTPServer

LOG = sys.argv[2] if len(sys.argv) > 2 else "mock-requests.json"

class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        problems = []
        try:
            req = json.loads(body)
        except Exception as e:
            req, problems = {}, [f"not JSON: {e}"]
        if self.path != "/v1beta/interactions": problems.append(f"path {self.path}")
        if not self.headers.get("x-goog-api-key"): problems.append("no x-goog-api-key header")
        if self.headers.get("Api-Revision") != "2026-05-20": problems.append("wrong Api-Revision")
        if req.get("model") != "gemini-3.5-transcribe": problems.append(f"model {req.get('model')}")
        audio = (req.get("input") or [{}])[0]
        wav = base64.b64decode(audio.get("data", "")) if audio.get("data") else b""
        if audio.get("mime_type") != "audio/wav" or wav[:4] != b"RIFF": problems.append("input is not a WAV")
        tc = (req.get("generation_config") or {}).get("transcription_config") or {}
        if tc.get("mode") not in ("smart", {"type": "verbatim"}): problems.append(f"mode {tc.get('mode')}")
        record = {"path": self.path, "wav_bytes": len(wav), "sample_rate": int.from_bytes(wav[24:28], "little") if len(wav) > 28 else 0,
                  "transcription_config": tc, "problems": problems}
        with open(LOG, "a") as f: f.write(json.dumps(record) + "\n")
        if problems:
            self.send_response(400); out = {"error": {"code": 400, "message": "; ".join(problems)}}
        else:
            self.send_response(200); out = {"steps": [{"content": [{"type": "text", "text": "Hey, the mock server heard you."}]}]}
        data = json.dumps(out).encode()
        self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(data)))
        self.end_headers(); self.wfile.write(data)

    def log_message(self, *a): pass

HTTPServer(("127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 8765), Handler).serve_forever()
