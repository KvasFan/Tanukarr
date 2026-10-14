#!/usr/bin/env python3
"""Owned TLS watched-progress provider doubles; never contact a public account."""

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import ssl
import threading
import urllib.parse


ACCESS = "generated-watched-access"
OWNER = "generated-watched-owner"
OWNER_ID = 900001
MEDIA_ID = 424242
ENTRY_ID = 900002


def serve_fixture(root):
    lock = threading.Lock()
    state_path = root / "progress-state.json"
    state = {"anilist": 0, "mal": 0, "trakt": [], "simkl": [], "mutations": []}

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def answer(self, code, value):
            body = json.dumps(value).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("X-RateLimit-Limit", "30")
            self.send_header("X-RateLimit-Remaining", "29")
            if code == 429:
                self.send_header("Retry-After", "60")
            self.end_headers()
            self.wfile.write(body)

        def route(self):
            with lock:
                control_path = root / "control.json"
                control = json.loads(control_path.read_text()) if control_path.exists() else {}
                host = self.headers.get("Host", "").split(":")[0]
                path = urllib.parse.urlsplit(self.path).path
                if path == "/__fixture/state":
                    return self.answer(200, state)
                if self.headers.get("Authorization") != "Bearer " + ACCESS:
                    return self.answer(401, {"error": "generated fixture token required"})
                if control.get("rateLimited"):
                    return self.answer(429, {"error": "owned fixture cooldown"})
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 <= length <= 262144:
                    return self.answer(400, {"error": "owned fixture body bound"})
                body = self.rfile.read(length).decode("utf-8") if length else ""
                record = {"provider": host, "method": self.command, "path": path}
                if host == "graphql.anilist.co":
                    payload = json.loads(body)
                    query, variables = payload.get("query", ""), payload.get("variables", {})
                    record["operation"] = "mutation" if "SaveMediaListEntry" in query else "query"
                    if "SaveMediaListEntry" in query:
                        if set(variables) != {"entryId", "progress"} or variables["entryId"] != ENTRY_ID:
                            return self.answer(400, {"errors": [{"message": "unexpected fixture mutation"}]})
                        value = int(variables["progress"])
                        if value < state["anilist"]:
                            return self.answer(400, {"errors": [{"message": "fixture forbids progress reduction"}]})
                        state["anilist"] = value
                        state["mutations"].append({"provider": "anilist", "progress": value})
                        result = {"SaveMediaListEntry": {"id": ENTRY_ID, "userId": OWNER_ID,
                                                        "mediaId": MEDIA_ID, "progress": value}}
                    elif "Viewer" in query:
                        result = {"Viewer": {"id": OWNER_ID, "name": OWNER},
                                  "User": {"id": OWNER_ID + 1 if control.get("foreignAccount") else OWNER_ID,
                                           "name": OWNER}}
                    elif "MediaList(" in query:
                        if variables != {"mediaId": MEDIA_ID, "userId": OWNER_ID}:
                            return self.answer(400, {"errors": [{"message": "unexpected fixture identity"}]})
                        result = {"MediaList": None if control.get("missingEntry") else {
                            "id": ENTRY_ID, "userId": OWNER_ID, "mediaId": MEDIA_ID,
                            "progress": state["anilist"], "media": {"episodes": 3, "type": "ANIME"}}}
                    else:
                        return self.answer(400, {"errors": [{"message": "unexpected fixture operation"}]})
                    response = {"data": result}
                elif host == "api.myanimelist.net":
                    if path == "/v2/users/@me":
                        response = {"id": OWNER_ID, "name": OWNER}
                    elif path == f"/v2/anime/{MEDIA_ID}" and self.command == "GET":
                        response = {"id": MEDIA_ID, "num_episodes": 3,
                                    "my_list_status": None if control.get("missingEntry") else {
                                        "num_episodes_watched": state["mal"]}}
                    elif path == f"/v2/anime/{MEDIA_ID}/my_list_status" and self.command == "PATCH":
                        form = urllib.parse.parse_qs(body)
                        if set(form) != {"num_watched_episodes"}:
                            return self.answer(400, {"error": "unexpected fixture form"})
                        state["mal"] = int(form["num_watched_episodes"][0])
                        state["mutations"].append({"provider": "mal", "progress": state["mal"]})
                        response = {"num_episodes_watched": state["mal"]}
                    else:
                        return self.answer(400, {"error": "unexpected fixture route"})
                else:
                    return self.answer(400, {"error": "unsupported fixture host"})
                with (root / "progress-requests.jsonl").open("a", encoding="utf-8") as output:
                    output.write(json.dumps(record) + "\n")
                state_path.write_text(json.dumps(state), encoding="utf-8")
                self.answer(200, response)

        do_POST = route
        do_GET = route
        do_PATCH = route

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local watched-progress provider TLS fixture ready", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serve-fixture", type=Path, required=True)
    options = parser.parse_args()
    serve_fixture(options.serve_fixture.resolve(strict=True))
