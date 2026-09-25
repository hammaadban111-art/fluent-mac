"""A local stand-in for the Gemini Live WebSocket, for testing GeminiLiveClient without a key.

  python3 mock_gemini_live.py PORT REPORT.json [normal|reject|nosetup|stall]

Serves one session, writes the report and exits.

Behaves like the real server as the Android and Mac clients use it: waits for `setup`, answers
`setupComplete` after 0.4 s, sends a final transcript chunk for every ~1 s of audio while the user
is still talking, and after `audioStreamEnd` sends the last chunk and `turnComplete`. It records the
exact audio bytes and message order so the test can check nothing was dropped or reordered.
"""
import asyncio, base64, hashlib, json, sys
import websockets
from websockets.http11 import Response
from websockets.datastructures import Headers

PORT, REPORT, MODE = int(sys.argv[1]), sys.argv[2], (sys.argv[3] if len(sys.argv) > 3 else "normal")
WORDS = "sounds good are you free for lunch tomorrow let's do twelve if that works".split()


def reject(connection, request):
    if MODE == "reject":
        return Response(403, "Forbidden", Headers([("Content-Type", "application/json")]), b'{"error":"API key not valid"}')
    return None


async def handler(ws):
    rep = {"key_param": "key=" in ws.request.path, "order": [], "audio_bytes": 0}
    audio = bytearray()
    setup = json.loads(await ws.recv())
    rep["setup"] = setup
    if MODE != "nosetup":
        await asyncio.sleep(0.4)
        await ws.send(json.dumps({"setupComplete": {}}))
    sent_words, next_mark = 0, 32000          # one final chunk per second of 16 kHz PCM16
    try:
        async for raw in ws:
            msg = json.loads(raw)
            rt = msg.get("realtimeInput", {})
            if "activityStart" in rt: rep["order"].append("activityStart")
            if "audio" in rt:
                if not rep["order"] or rep["order"][-1] != "audio": rep["order"].append("audio")
                rep["mime"] = rt["audio"]["mimeType"]
                audio += base64.b64decode(rt["audio"]["data"])
                if len(audio) >= next_mark and sent_words < len(WORDS) - 2:
                    next_mark += 32000
                    await ws.send(json.dumps({"serverContent": {"inputTranscription": {"text": " ".join(WORDS[sent_words:sent_words + 2])}}}))
                    sent_words += 2
            if "activityEnd" in rt: rep["order"].append("activityEnd")
            if rt.get("audioStreamEnd"):
                rep["order"].append("audioStreamEnd")
                if MODE == "stall":
                    continue                     # never finalises; the client must use what it has
                await asyncio.sleep(0.3)
                await ws.send(json.dumps({"serverContent": {"inputTranscription": {"text": " ".join(WORDS[sent_words:])}}}))
                await ws.send(json.dumps({"serverContent": {"turnComplete": True}}))
    except websockets.ConnectionClosed:
        pass
    finally:
        rep["audio_bytes"] = len(audio)
        rep["audio_sha256"] = hashlib.sha256(bytes(audio)).hexdigest()
        json.dump(rep, open(REPORT, "w"), indent=1)
        DONE.set()


async def main():
    global DONE
    DONE = asyncio.Event()
    async with websockets.serve(handler, "127.0.0.1", PORT, process_request=reject, max_size=None):
        if MODE == "reject":
            await asyncio.sleep(8)   # a rejected handshake never reaches the handler
        else:
            await DONE.wait()

asyncio.run(main())
