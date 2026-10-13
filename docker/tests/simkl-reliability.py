#!/usr/bin/env python3
"""Verify optional SIMKL V2 custom-list sets, persisted deltas and OAuth with isolated TLS fixtures."""

import argparse
import base64
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import ssl
import sys
import threading
import urllib.parse

from smoke import require

_spec = importlib.util.spec_from_file_location("simkl_fixture_parent", Path(__file__).with_name("myanimelist-reliability.py"))
_parent = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_parent)
CLIENT = "generated-simkl-v2-client"
ACCESS = "generated-simkl-access"
REFRESH = "generated-simkl-refresh"
RENEWED = "generated-simkl-renewed-access"
ACCOUNT = "900001"
NATIVE = (960101, 960102, 960103, 960104, 960105, 960106)
MAL = (970101, 970102, 970103, 970104, 970105, 970106)
BASE_STAMP = "2026-10-13T10:00:00Z"


def stamp(epoch):
    return "2026-10-13T10:" + str(epoch).zfill(2) + ":00Z"


def account_rows(control):
    rows = [{"status": "completed" if index == 3 else "watching", "anime_type": "movie" if index == 4 else "tv",
             "show": {"title": "Generated SIMKL " + str(index + 1), "ids": {"simkl": native, "mal": str(MAL[index])}}}
            for index, native in enumerate(NATIVE[:5])]
    if control.get("moved"):
        rows[1]["status"] = "completed"
        rows.append({"status": "watching", "anime_type": "ona", "show": {"title": "Generated SIMKL 6", "ids": {"simkl": NATIVE[5], "mal": str(MAL[5])}}})
    if control.get("removed"):
        rows = [row for row in rows if row["show"]["ids"]["simkl"] != NATIVE[0]]
    return [] if control.get("empty") else rows


def catalogue(media_id):
    return {"mal_id": media_id, "title": "Generated SIMKL " + str(MAL.index(media_id) + 1), "type": "TV", "episodes": 1,
            "status": "Finished Airing", "duration": "24 min per ep", "scored_by": 12, "score": 8,
            "genres": [], "studios": [], "relations": [], "images": {},
            "aired": {"from": "2020-01-01T00:00:00Z", "to": "2020-01-02T00:00:00Z"}}


def serve_fixture(root):
    lock, attempts = threading.Lock(), {}

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def control(self):
            return json.loads((root / "control.json").read_text(encoding="utf-8"))

        def answer(self, status, value, record, retry=None):
            content = json.dumps(value).encode("utf-8")
            with lock:
                with (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                    output.write(json.dumps(dict(record, status=status)) + "\n")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(content)))
            if retry:
                self.send_header("Retry-After", str(retry))
            self.end_headers()
            self.wfile.write(content)

        def do_GET(self):
            parsed = urllib.parse.urlparse(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            host = self.headers.get("Host", "").split(":")[0]
            control = self.control()
            record = {"method": "GET", "host": host, "path": parsed.path, "epoch": control.get("epoch", 0)}
            try:
                if host == "api.jikan.moe":
                    require(not self.headers.get("Authorization"), "SIMKL credentials leaked to public Jikan")
                    pieces = parsed.path.split("/")
                    require(pieces[:3] == ["", "v4", "anime"] and int(pieces[3]) in MAL, "Unsupported synthetic Jikan identity")
                    media_id = int(pieces[3])
                    if pieces[4:] == ["full"]:
                        record.update(kind="catalogue-full", mediaId=media_id)
                        self.answer(200, {"data": catalogue(media_id)}, record)
                        return
                    if pieces[4:] == ["episodes"]:
                        record.update(kind="catalogue-episodes", mediaId=media_id)
                        self.answer(200, {"data": [{"mal_id": 1, "title": "Native episode"}], "pagination": {"has_next_page": False}}, record)
                        return
                    raise ValueError("Unsupported catalogue path")
                require(host == "api.simkl.com", "SIMKL request escaped its fixed host")
                require(self.headers.get("Authorization") in ("Bearer " + ACCESS, "Bearer " + RENEWED), "Wrong generated SIMKL grant")
                require(query.get("client_id") == [CLIENT], "Missing registered V2 Client ID")
                if parsed.path == "/users/settings":
                    record.update(kind="profile")
                    self.answer(200, {"account": {"id": int(ACCOUNT)}, "user": {"name": "Generated fixture"}}, record)
                    return
                if parsed.path == "/sync/activities":
                    record.update(kind="activities")
                    block = {name: stamp(control.get("watchEpoch", 0)) for name in ("watching", "plantowatch", "hold", "completed", "dropped")}
                    block["removed_from_list"] = stamp(control.get("removeEpoch", 0))
                    self.answer(200, {"all": stamp(control.get("epoch", 0)), "anime": block, "tv_shows": block}, record)
                    return
                if parsed.path == "/sync/all-items/anime":
                    since = query.get("date_from", [None])[0]
                    identities = query.get("extended") == ["simkl_ids_only"]
                    record.update(kind="watchlist-identities" if identities else "watchlist-delta" if since else "watchlist-full", dateFrom=since)
                    rows = account_rows(control)
                    if identities:
                        rows = [{"show": {"ids": {"simkl": row["show"]["ids"]["simkl"]}}} for row in rows]
                    elif since:
                        require(since == control.get("expectedSince"), "Incremental sync changed the exact previous server cursor")
                        rows = [row for row in rows if row["show"]["ids"]["simkl"] in (NATIVE[1], NATIVE[5])]
                    self.answer(200, {"anime": rows} if rows else {}, record)
                    return
                if parsed.path.startswith("/lists/"):
                    page = int(query.get("page", ["1"])[0])
                    require(query.get("limit") == ["500"], "Custom list paging ignored the documented maximum")
                    if parsed.path == "/lists/user/" + ACCOUNT:
                        record.update(kind="choices", page=page)
                        self.answer(200, {"pagination": {"page": 1, "limit": 500, "total_items": 3, "total_pages": 1},
                                          "lists": [{"id": item, "name": name, "media_type": "anime", "user": {"id": int(ACCOUNT)}}
                                                    for item, name in ((7001, "Include, literal"), (7002, "Pinned"), (7003, "Never"))]}, record)
                        return
                    list_id = int(parsed.path.split("/")[2])
                    members = {7001: [NATIVE[0], NATIVE[2]] + list(range(980000, 980499)), 7002: [NATIVE[1], NATIVE[5]],
                               7003: [NATIVE[2]], 7004: []}[list_id]
                    record.update(kind="custom-list", listId=list_id, page=page)
                    failure = control.get("failure")
                    if list_id == 7001 and page == 2 and control.get("quota"):
                        key = control.get("quotaEpoch", "default")
                        with lock:
                            attempts[key] = attempts.get(key, 0) + 1
                            count = attempts[key]
                        record["quotaEpoch"] = key
                        if control["quota"] == "terminal" or count <= 2:
                            self.answer(429, {"error": "rate_limited"}, record, 1)
                            return
                    if list_id == 7001 and page == 2 and failure == "unavailable":
                        self.answer(503, {"error": "generated unavailable"}, record)
                        return
                    if failure == "premium":
                        self.answer(200, {"error": "premium_only", "item": {"id": 1, "name": "Upgrade"}}, record)
                        return
                    selected = members[(page - 1) * 500:page * 500]
                    value = {"id": list_id, "name": "Generated " + str(list_id), "media_type": "anime", "type": "private",
                             "user": {"id": int(ACCOUNT)}, "counts": {"items": len(members)},
                             "pagination": {"page": page, "limit": 500, "total_items": len(members), "total_pages": (len(members) + 499) // 500},
                             "items": [{"type": "anime", "ids": {"simkl_id": item}} for item in selected]}
                    if list_id == 7001 and page == 2:
                        if failure == "nullItems":
                            value["items"] = None
                        elif failure == "changedTotal":
                            value["pagination"]["total_items"] += 1
                        elif failure == "wrongNamespace":
                            value["items"][0]["type"] = "movie"
                    self.answer(200, value, record)
                    return
                raise ValueError("Unsupported SIMKL fixture path")
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

        def do_POST(self):
            record = {"method": "POST", "host": self.headers.get("Host", "").split(":")[0], "path": self.path}
            try:
                require(record["host"] == "api.simkl.com" and self.path == "/oauth2/token", "OAuth escaped its fixed SIMKL token endpoint")
                require(self.headers.get("Content-Type", "").startswith("application/x-www-form-urlencoded"), "SIMKL token request was not an encoded form")
                form = urllib.parse.parse_qs(self.rfile.read(int(self.headers.get("Content-Length", "0"))).decode())
                require(form.get("client_id") == [CLIENT], "Token request changed the registered client")
                grant = form.get("grant_type", [""])[0]
                record.update(kind="oauth-token", grantType=grant)
                if grant == "authorization_code":
                    verifier = form.get("code_verifier", [""])[0]
                    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode("ascii")).digest()).decode().rstrip("=")
                    require(challenge == self.control().get("challenge"), "SIMKL token exchange did not match the S256 challenge")
                    require(form.get("code") == ["generated-code-with-symbols"] and form.get("redirect_uri") == ["http://localhost:6970/oauth.html"], "OAuth code/callback changed")
                    access = ACCESS
                else:
                    require(grant == "refresh_token" and form.get("refresh_token") == [REFRESH], "Unexpected SIMKL refresh grant")
                    access = RENEWED
                self.answer(200, {"token_type": "Bearer", "access_token": access, "refresh_token": REFRESH, "expires_in": 604800, "scope": "media:read"}, record)
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local SIMKL V2 TLS fixture ready", flush=True)
    server.serve_forever()


class SimklReliability(_parent.MyAnimeListReliability):
    fixture_script = "simkl-reliability.py"
    fixture_ready_message = "Local SIMKL V2 TLS fixture ready"
    fixture_hosts = ("api.simkl.com", "simkl.com", "api.jikan.moe")
    fixture_startup_check = "isolated trusted TLS SIMKL V2/Jikan, loopback-only API and no external provider routing"

    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"epoch": 0, "watchEpoch": 0, "removeEpoch": 0, "failure": None, "quota": None}

    def snapshot_digest(self):
        return self.docker("exec", self.container, "/bin/sh", "-c", "if test -d /config/cache/simkl-v2; then sha256sum /config/cache/simkl-v2/*.json; fi")

    def edit(self, expected, **values):
        resource = self.saved_definition()
        resource["enableAutomaticAdd"] = True
        for name, value in values.items():
            self.field(resource, name)["value"] = value
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.fresh_sync()
        self.expected_library.update(expected)
        require(self.stored_ids() == expected and self.library_ids() == sorted(self.expected_library), "SIMKL returned the wrong selected set or changed existing/unselected native library members")

    def create_list(self):
        for version in ("v3", "v5"):
            schema = next(item for item in self.api_json(f"/api/{version}/importlist/schema") if item["implementation"] == "SimklUserImport")
            fields = {field["name"]: field for field in schema["fields"]}
            require(all(key in fields for key in ("clientIdV2", "clientSecretV2", "includedCustomListIds", "excludedCustomListIds", "showType", "listType")), "Optional SIMKL V2 controls missing")
            require(fields["includedCustomListIds"]["type"] == "tag" and fields["excludedCustomListIds"]["type"] == "tag", "Custom ID controls changed to unsupported named memberships")
            require(not any(name.startswith("importFinished") or name == "listStatuses" for name in fields), "SIMKL invented unavailable release or account-status fields")
        self.check("v3/v5 expose optional V2 app/custom IDs and keep existing Shows/Anime and account-bucket choices")
        schema.update(name="Generated SIMKL fixture", enableAutomaticAdd=True, rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                      language={"id": 1, "name": "English"}, metadataSource="myAnimeList", shouldMonitor="none",
                      searchForMissingEpisodes=False, seriesType="anime", seasonFolder=True)
        for name, value in {"clientIdV2": CLIENT, "clientSecretV2": "", "accessToken": ACCESS, "refreshToken": REFRESH,
                            "expires": "2099-01-01T00:00:00Z", "authUser": ACCOUNT, "showType": 1, "listType": 0,
                            "includedCustomListIds": ["7001", "7002"], "excludedCustomListIds": ["7003"]}.items():
            self.field(schema, name)["value"] = value
        self.definition = self.api_json("/api/v5/importlist", "POST", schema)
        self.idle()
        self.check("optional AUTH V2 account settings save with synthetic credentials and explicit native list IDs")

    def baseline(self):
        full_before = sum(row.get("kind") == "watchlist-full" for row in self.records())
        had_snapshot = bool(self.snapshot_digest())
        self.control(quota="transient", quotaEpoch="transient-proof")
        self.fresh_sync()
        require(self.stored_ids() == list(MAL[:2]) and self.library_ids() == list(MAL[:2]), "SIMKL native include union/exclude veto lost TV memberships or imported an outside/moving/movie item")
        self.expected_library.update(MAL[:2])
        quotas = [row["status"] for row in self.records() if row.get("quotaEpoch") == "transient-proof"]
        require(quotas == [429, 429, 200], "SIMKL did not recover within three bounded custom-page attempts")
        self.control(quota=None)
        self.check("all custom pages recover from429, intersect native account members and subtract overlapping included/excluded IDs")
        events = [row["kind"] for row in self.records() if row.get("kind") in ("activities", "watchlist-full", "custom-list")]
        require(events.index("activities") < events.index("watchlist-full") < events.index("custom-list"), "V2 did not check activities before its initial inventory")
        full_after = sum(row.get("kind") == "watchlist-full" for row in self.records())
        require(full_after - full_before == (0 if had_snapshot else 1), "Fetch redownloaded an existing committed inventory or omitted its first complete snapshot")
        self.check("activities are checked first, and the successful initial all-status snapshot is persisted only after complete membership")
        require(self.snapshot_digest(), "No persisted snapshot exists after complete sync")
        permissions = self.docker("exec", self.container, "/bin/sh", "-c", "stat -c '%a' /config/cache/simkl-v2 /config/cache/simkl-v2/*.json").splitlines()
        require(permissions[0] == "700" and all(mode == "600" for mode in permissions[1:]), "Private watchlist cache permissions are not bounded to its owner")
        saved_inventory = self.docker("exec", self.container, "/bin/sh", "-c", "cat /config/cache/simkl-v2/*.json")
        require(not any(secret in saved_inventory for secret in (ACCESS, REFRESH, RENEWED, CLIENT)), "The private snapshot persisted account credentials or raw Client ID")
        self.check("persisted native inventory/cursors have private directory700 and files600 without credential payloads")
        for series in self.api_json("/api/v5/series"):
            require(series["metadataSource"] == "myAnimeList" and series["foreignId"] in [str(item) for item in MAL[:2]], "SIMKL replaced the native resolvable catalogue ID")
            require([episode["episodeNumber"] for episode in self.episodes(series)] == [1], "SIMKL/Jikan changed native numbering")
        self.check("SIMKL native membership resolves public Jikan metadata independently and keeps native episode numbers")

    def choices_and_sets(self):
        before_settings, before_snapshot = self.stored_settings_json(), self.snapshot_digest()
        status, body = self.action("getCustomLists", self.saved_definition())
        require(status == 200 and [(item["value"], item["name"]) for item in json.loads(body)["options"]] == [("7001", "Include, literal"), ("7002", "Pinned"), ("7003", "Never")], "Custom choices lost literal names/native IDs")
        require(self.stored_settings_json() == before_settings and self.snapshot_digest() == before_snapshot, "Loading list choices saved settings or inventory")
        self.check("custom-list choices preserve literal names and positive IDs without saving settings or inventory")
        self.edit(list(MAL[:2]), includedCustomListIds=[])
        self.check("empty inclusion selects the whole eligible bucket minus all excluded native custom members")
        self.edit([], includedCustomListIds=["7003"], excludedCustomListIds=["7003"])
        self.check("the same explicit custom list in inclusion and exclusion remains an exclusion veto")
        self.edit([], includedCustomListIds=["7004"], excludedCustomListIds=[])
        self.check("a complete known-empty custom list produces a valid empty import result")
        self.edit(list(MAL[:2]), includedCustomListIds=["7001", "7002"], excludedCustomListIds=["7003"])
        self.edit([], listType=3)
        self.check("the existing selected account status remains an independent AND condition")
        self.edit(list(MAL[:2]), listType=0)

    def incremental(self):
        before = len(self.records())
        self.control(epoch=1, expectedSince=BASE_STAMP)
        self.fresh_sync()
        delta = self.records()[before:]
        require(any(row.get("kind") == "activities" for row in delta) and not any(row.get("kind", "").startswith("watchlist-") for row in delta), "A custom-only change redownloaded account inventory")
        self.check("custom-list-only activity refreshes memberships without another account inventory download")
        self.control(epoch=2, watchEpoch=2, moved=True, expectedSince=stamp(1))
        self.fresh_sync()
        require(self.stored_ids() == [MAL[0], MAL[5]], "An item leaving Watching survived the all-status delta or the newly eligible item was lost")
        self.expected_library.add(MAL[5])
        require(self.library_ids() == sorted(self.expected_library), "A valid status move changed unrelated existing native members")
        require(any(row.get("kind") == "watchlist-delta" and row.get("dateFrom") == stamp(1) for row in self.records()), "Delta did not use the exact prior server all cursor")
        self.check("continuous sync uses prior server date_from and reconciles status moves across all account buckets")
        self.control(epoch=3, removeEpoch=3, removed=True, expectedSince=stamp(2))
        self.fresh_sync()
        require(self.stored_ids() == [MAL[5]] and any(row.get("kind") == "watchlist-identities" for row in self.records()), "A removed account entry survived lightweight native-ID reconciliation")
        self.check("removed_from_list triggers lightweight native-ID reconciliation and removes stale membership")
        before = len(self.records())
        self.docker("restart", self.container)
        self.wait_ready()
        self.idle()
        self.fresh_sync()
        require(self.stored_ids() == [MAL[5]] and not any(row.get("kind") == "watchlist-full" for row in self.records()[before:]), "Restart discarded the persisted cursor and repeated a full account download")
        self.check("a container restart reloads the complete private snapshot without replaying a full inventory request")

    def retain_complete(self):
        saved = [MAL[5]]
        results = {}
        for failure in ("premium", "unavailable", "nullItems", "changedTotal", "wrongNamespace"):
            before = self.snapshot_digest()
            self.control(failure=failure)
            status, _ = self.raw("/api/v5/importlist/test", "POST", self.saved_definition())
            require(status == 400 and self.snapshot_digest() == before, "An unconfirmed Test wrote inventory or passed")
            self.fresh_sync()
            require(self.stored_ids() == saved and self.snapshot_digest() == before and self.library_ids() == sorted(self.expected_library), "An incomplete/premium response replaced previous complete membership/cursor or added stale native members")
            results[failure] = status
        self.control(failure=None, quota="terminal", quotaEpoch="terminal-proof")
        status, _ = self.raw("/api/v5/importlist/test", "POST", self.saved_definition())
        require(status == 400, "Terminal SIMKL quota passed Test")
        self.fresh_sync()
        quotas = [row for row in self.records() if row.get("quotaEpoch") == "terminal-proof"]
        require(len(quotas) == 6 and all(row["status"] == 429 for row in quotas), "Terminal SIMKL Test/Fetch exceeded three requests each")
        require(self.stored_ids() == saved, "Terminal quota removed complete membership")
        self.control(quota=None)
        self.fresh_sync()
        self.observations["unconfirmedListRetention"] = results
        self.check("premium-only200, unavailable/missing/changed/wrong-namespace pages and terminal429 retain complete memberships/cursors and recover")

    def oauth(self):
        resource = self.saved_definition()
        before = sum(row.get("kind") == "oauth-token" for row in self.records())
        status, body = self.action("startOAuth", resource, callbackUrl="http://localhost:6970/oauth.html")
        require(status == 200, "Registered V2 OAuth did not start")
        uri = urllib.parse.urlparse(json.loads(body)["oauthUrl"])
        query = urllib.parse.parse_qs(uri.query)
        require(uri.hostname == "simkl.com" and uri.path == "/oauth2/authorize" and query["code_challenge_method"] == ["S256"] and query["scope"] == ["media:read"], "SIMKL OAuth omitted the official endpoint, S256 or read scope")
        self.control(challenge=query["code_challenge"][0])
        status, _ = self.action("getOAuthToken", resource, state=query["state"][0], code="generated-code-with-symbols", iss="https://foreign.invalid")
        require(status == 400 and sum(row.get("kind") == "oauth-token" for row in self.records()) == before, "A foreign issuer reached the token endpoint")
        status, body = self.action("getOAuthToken", resource, state=query["state"][0], code="generated-code-with-symbols", iss="https://simkl.com")
        require(status == 200 and json.loads(body)["authUser"] == ACCOUNT, "Direct OAuth/profile exchange lost the native account identity")
        status, _ = self.action("getOAuthToken", resource, state=query["state"][0], code="generated-code-with-symbols", iss="https://simkl.com")
        require(status == 400 and sum(row.get("kind") == "oauth-token" for row in self.records()) == before + 1, "Replayed V2 state submitted a second authorization-code exchange")
        self.check("direct V2 OAuth uses S256/read scope, validates issuer and single-use state and resolves the authenticated account")
        before_settings, before_snapshot = self.stored_settings_json(), self.snapshot_digest()
        self.field(resource, "expires")["value"] = "2000-01-01T00:00:00Z"
        self.field(resource, "includedCustomListIds")["value"] = ["7004"]
        status, _ = self.raw("/api/v5/importlist/test", "POST", resource)
        require(status == 204 and self.stored_settings_json() == before_settings and self.snapshot_digest() == before_snapshot, "Unsaved Test persisted refreshed credentials, filters or cursor")
        self.check("Test renews tokens and stages unsaved filters without saving credentials/settings or committing inventory")
        self.field(resource, "includedCustomListIds")["value"] = ["7001", "7002"]
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.fresh_sync()
        require(sum(row.get("grantType") == "refresh_token" for row in self.records()) == 1, "Fetch repeated the Test refresh instead of using its bounded in-memory grant")
        settings = {key.lower(): value for key, value in json.loads(self.stored_settings_json()).items()}
        require(settings["accesstoken"] == RENEWED and self.stored_ids() == [MAL[5]], "Fetch did not persist the renewed grant or preserved set")
        self.check("subsequent Fetch safely hands off the Test-renewed V2 grant once and persists the saved definition only")
        self.control(epoch=4, watchEpoch=4, removeEpoch=4, empty=True, expectedSince=stamp(3))
        self.fresh_sync()
        require(self.stored_ids() == [], "A documented empty{} Watchlist did not clear the authoritative account inventory")
        self.check("documented successful empty{} inventories and native-ID reconciliation yield a valid empty selected set")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=_parent._parent.MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    audit = SimklReliability(args)
    error, records, logs, cleanup = None, [], "", False

    def redact(value):
        for secret in (audit.api_key, ACCESS, REFRESH, RENEWED):
            value = value.replace(secret, "[REDACTED]")
        return value

    try:
        audit.start()
        audit.prepare()
        audit.create_list()
        audit.baseline()
        audit.choices_and_sets()
        audit.incremental()
        audit.retain_complete()
        audit.oauth()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as caught:
        error = redact(f"{audit.last_request}: {caught or type(caught).__name__}")
        print("FAIL " + error, file=sys.stderr)
    finally:
        try:
            if audit.root:
                records = audit.records()
            if audit.container:
                logs = redact(audit.docker("logs", audit.container))
        except Exception as caught:
            error = error or redact("Evidence capture failed: " + str(caught))
        try:
            audit.cleanup()
            cleanup = not args.keep
        except Exception as caught:
            error = error or redact("Fixture cleanup failed: " + str(caught))
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({"createdUtc": _parent._parent._groups.utc_now(), "image": args.image, "imageId": audit.image_id,
                "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"), "runtime": audit.runtime_metadata,
                "mockImage": args.mock_image, "mockImageId": audit.mock_id, "passed": error is None, "error": error,
                "checks": audit.results, "observations": audit.observations, "fixtureRequests": records,
                "cleanupComplete": cleanup, "keptData": str(audit.root) if args.keep and audit.root else None,
                "scope": {"trustedLocalSimklAndJikanMock": True, "livePremiumAccountTested": False, "privateAccountUsed": False,
                          "realLibraryTested": False, "downloadsTested": False, "mainConfigurationAccessed": False,
                          "externalProviderRoutingEnabled": False, "tlsValidationDisabled": False,
                          "databaseInspection": "read-only in the owned temporary fixture"}}, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
