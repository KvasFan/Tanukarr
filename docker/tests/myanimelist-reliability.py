#!/usr/bin/env python3
"""Verify official MAL account imports and distinct Jikan metadata with owned TLS/SQLite Docker fixtures."""

import argparse
from contextlib import closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import secrets
import sqlite3
import ssl
import sys
import threading
import urllib.parse

from smoke import require


_spec = importlib.util.spec_from_file_location("mal_fixture_parent", Path(__file__).with_name("anilist-reliability.py"))
_parent = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_parent)
IDS = (950101, 950102, 950103, 950104)
CLIENT_ID = "generated-mal-client-id"
ACCESS = "generated-mal-access"
REFRESH = "generated-mal-refresh"
ROTATED_ACCESS = "generated-mal-access-rotated"
ROTATED_REFRESH = "generated-mal-refresh-rotated"


def list_row(media_id):
    index = IDS.index(media_id)
    return {"node": {"id": media_id, "title": "Generated MAL " + str(index + 1),
                     "status": "not_yet_aired" if index == 3 else "finished_airing"},
            "list_status": {"status": "plan_to_watch" if index == 1 else "watching",
                            "score": 9 if index == 1 else 8, "is_rewatching": False,
                            "tags": (("Include",), ("Pinned",), ("Include", "Never"), ("Future",))[index]}}


def catalogue(media_id):
    movie = media_id == IDS[1]
    return {"mal_id": media_id, "title": "Generated MAL " + str(IDS.index(media_id) + 1),
            "title_english": "Generated MAL " + str(IDS.index(media_id) + 1),
            "type": "Movie" if movie else "TV", "episodes": None if movie else 3,
            "status": "Finished Airing", "duration": "1 hr 30 min" if movie else "24 min per ep",
            "scored_by": 12, "score": 8, "genres": [], "studios": [], "relations": [],
            "images": {}, "aired": {"from": "2020-01-01T00:00:00Z", "to": "2020-02-01T00:00:00Z"}}


def serve_fixture(root):
    lock, quota_counts = threading.Lock(), {}
    refreshed = False

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def control(self):
            return json.loads((root / "control.json").read_text(encoding="utf-8"))

        def answer(self, status, value, record, retry_after=None):
            body = json.dumps(value).encode("utf-8")
            with lock:
                with (root / "requests.jsonl").open("a", encoding="utf-8") as stream:
                    stream.write(json.dumps(dict(record, status=status)) + "\n")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            if retry_after is not None:
                self.send_header("Retry-After", str(retry_after))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            parsed = urllib.parse.urlparse(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            host = self.headers.get("Host", "").split(":")[0]
            record = {"method": "GET", "host": host, "path": parsed.path}
            control = self.control()
            try:
                if host == "api.myanimelist.net" and parsed.path in ("/v2/users/generated-public-account/animelist", "/v2/users/@me/animelist"):
                    auth = self.headers.get("Authorization")
                    public = self.headers.get("X-MAL-CLIENT-ID") == CLIENT_ID
                    require(public or auth in ("Bearer " + ACCESS, "Bearer " + ROTATED_ACCESS), "Synthetic MAL request lacked its expected credential mode")
                    require("list_status" in query.get("fields", [""])[0] and "tags" in query.get("fields", [""])[0], "MAL did not request supported list tags")
                    offset = int(query.get("offset", ["0"])[0])
                    require(offset in (0, 2), "Unexpected synthetic MAL offset")
                    record.update(kind="account-list", offset=offset, authMode="publicClientId" if public else "bearer", epoch=control.get("quotaEpoch"))
                    if offset == 2 and control.get("page2Quota"):
                        epoch = control.get("quotaEpoch", "default")
                        with lock:
                            quota_counts[epoch] = quota_counts.get(epoch, 0) + 1
                            count = quota_counts[epoch]
                        if control["page2Quota"] == "terminal" or count <= 2:
                            self.answer(429, {"error": "rate_limited"}, record, 1)
                            return
                    failure = control.get("failure")
                    if offset == 2 and failure == "unavailable":
                        self.answer(503, {"error": "synthetic unavailable"}, record)
                        return
                    rows = [list_row(media_id) for media_id in IDS[offset:offset + 2]]
                    next_url = "https://api.myanimelist.net" + parsed.path + "?offset=2" if offset == 0 else None
                    if failure == "unsafeNext" and offset == 0:
                        next_url = "https://foreign.invalid/stolen?offset=2"
                    if failure == "repeatedNext" and offset == 2:
                        next_url = "https://api.myanimelist.net" + parsed.path + "?offset=2"
                    if failure == "missingTags" and offset == 2:
                        rows[0]["list_status"].pop("tags")
                    if failure == "nullData" and offset == 2:
                        rows = None
                    self.answer(200, {"data": rows, "paging": {"next": next_url}}, record)
                    return
                if host == "api.jikan.moe" and parsed.path == "/v4/anime":
                    require(query.get("q") == ["Generated"], "Unexpected Jikan search term")
                    record.update(kind="catalogue-search")
                    self.answer(200, {"data": [catalogue(IDS[0]), catalogue(IDS[1]), dict(catalogue(IDS[0]), mal_id=0)]}, record)
                    return
                if host == "api.jikan.moe" and parsed.path.startswith("/v4/anime/"):
                    pieces = parsed.path.split("/")
                    media_id = int(pieces[3])
                    require(media_id in IDS, "Unknown synthetic Jikan catalogue ID")
                    require(not self.headers.get("Authorization") and not self.headers.get("X-MAL-CLIENT-ID"), "Account credentials leaked to public Jikan metadata")
                    if pieces[4:] == ["full"]:
                        record.update(kind="catalogue-full", mediaId=media_id)
                        self.answer(200, {"data": catalogue(media_id)}, record)
                        return
                    if pieces[4:] == ["episodes"]:
                        page = int(query.get("page", ["1"])[0])
                        require(page in (1, 2), "Unexpected Jikan episode page")
                        record.update(kind="catalogue-episodes", mediaId=media_id, page=page)
                        if media_id == IDS[1]:
                            rows, following = [], False
                        elif page == 1:
                            rows = [{"mal_id": 1, "title": "First native episode", "aired": "2020-01-02T00:00:00Z"},
                                    {"mal_id": 3, "title": "Third native episode"}]
                            following = True
                        else:
                            rows, following = [{"mal_id": 3, "title": "Repeated third"}], False
                        self.answer(200, {"data": rows, "pagination": {"has_next_page": following}}, record)
                        return
                raise ValueError("Unsupported provider fixture request")
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

        def do_POST(self):
            nonlocal refreshed
            record = {"method": "POST", "host": self.headers.get("Host", "").split(":")[0], "path": self.path}
            try:
                require(record["host"] == "myanimelist.net" and self.path == "/v1/oauth2/token", "OAuth escaped the fixed MAL token endpoint")
                require(self.headers.get("Content-Type", "").startswith("application/x-www-form-urlencoded"), "MAL token exchange did not send an encoded form")
                form = urllib.parse.parse_qs(self.rfile.read(int(self.headers.get("Content-Length", "0"))).decode("utf-8"))
                require(form.get("client_id") == [CLIENT_ID], "Wrong generated Client ID")
                grant = form.get("grant_type", [""])[0]
                record.update(kind="oauth-token", grantType=grant)
                if grant == "authorization_code":
                    require(form.get("code") == ["generated+code=with/slashes"], "Encoded OAuth code was truncated or changed")
                    require(43 <= len(form.get("code_verifier", [""])[0]) <= 128, "PKCE verifier missing or invalid")
                    require(form.get("redirect_uri") == ["http://localhost:6970/oauth.html"], "OAuth callback changed")
                    value = {"token_type": "Bearer", "access_token": ACCESS, "refresh_token": REFRESH, "expires_in": 3600}
                else:
                    require(grant == "refresh_token" and form.get("refresh_token") == [REFRESH], "Unexpected synthetic token refresh")
                    with lock:
                        require(not refreshed, "Previously rotated refresh token was reused")
                        refreshed = True
                    value = {"token_type": "Bearer", "access_token": ROTATED_ACCESS, "refresh_token": ROTATED_REFRESH, "expires_in": 3600}
                self.answer(200, value, record)
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local MAL and Jikan TLS fixture ready", flush=True)
    server.serve_forever()


class MyAnimeListReliability(_parent.AniListReliability):
    fixture_script = "myanimelist-reliability.py"
    fixture_ready_message = "Local MAL and Jikan TLS fixture ready"
    fixture_hosts = ("api.myanimelist.net", "myanimelist.net", "api.jikan.moe")
    fixture_startup_check = "isolated trusted TLS MAL/Jikan, loopback-only API and no external provider routing"

    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"failure": None, "page2Quota": None}
        self.expected_library = set()

    def stored_ids(self):
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config" and self.root.name.startswith("sonarr-container-smoke-anilist-reliability-"), "Database inspection escaped the owned fixture")
        uri = "file:" + urllib.parse.quote(str(path), safe="/") + "?mode=ro"
        with closing(sqlite3.connect(uri, uri=True, timeout=10)) as connection:
            connection.execute("PRAGMA query_only=ON")
            return sorted(row[0] for row in connection.execute("SELECT MalId FROM ImportListItems WHERE ImportListId=?", (self.definition["id"],)))

    def create_list(self):
        for version in ("v3", "v5"):
            schema = next(item for item in self.api_json(f"/api/{version}/importlist/schema") if item["implementation"] == "MyAnimeListImport")
            fields = {field["name"]: field for field in schema["fields"]}
            require(fields["listStatuses"]["type"] == "tagSelect", "MAL status schema is not a real multiselect")
            require(fields["listStatus"].get("hidden") == "hidden", "Legacy scalar status remains visible")
            require(all(name in fields for name in ("username", "clientId", "clientSecret", "includedTags", "excludedTags", "importFinished", "importAiring", "importUnreleased")), "Supported MAL filters/auth fields missing")
            require("includedCustomLists" not in fields and "excludedCustomLists" not in fields, "MAL exposed unsupported AniList custom lists")
        self.check("v3/v5 expose real MAL multi-status, tag and release filters while keeping Jikan account flows separate")
        schema.update(name="Generated MAL fixture", enableAutomaticAdd=True, rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                      language={"id": 1, "name": "English"}, metadataSource="myAnimeList",
                      shouldMonitor="none", searchForMissingEpisodes=False, seriesType="anime", seasonFolder=True)
        values = {"username": "generated-public-account", "clientId": CLIENT_ID, "clientSecret": "", "accessToken": "", "refreshToken": "",
                  "listStatuses": [1, 5], "includedTags": ["Include", "Pinned"], "excludedTags": ["Never", "Skip"],
                  "importFinished": True, "importAiring": True, "importUnreleased": True, "minimumScore": 5}
        for name, value in values.items():
            self.field(schema, name)["value"] = value
        self.definition = self.api_json("/api/v5/importlist", "POST", schema)
        self.idle()
        self.check("public account list saves using a generated Client ID without private OAuth credentials")

    def baseline(self):
        self.control(page2Quota="transient", quotaEpoch="transient-proof")
        self.fresh_sync()
        require(self.stored_ids() == list(IDS[:2]) and self.library_ids() == list(IDS[:2]), "MAL include/exclude/status selection lost native memberships")
        self.expected_library.update(IDS[:2])
        attempts = [row for row in self.records() if row.get("epoch") == "transient-proof" and row.get("offset") == 2]
        require([row["status"] for row in attempts] == [429, 429, 200], "MAL transient429 did not recover within exactlythree attempts")
        self.control(page2Quota=None)
        self.check("complete two-page MAL membership recovers from429 and subtracts tags even when the entry is explicitly included")
        native = self.api_json("/api/v5/series")
        require(all(series["metadataSource"] == "myAnimeList" and series["foreignId"] == str(series["malIds"][0]) for series in native), "Native MAL identity changed to another catalogue")
        snapshots = {}
        for series in native:
            episodes = self.episodes(series)
            expected = [1] if int(series["foreignId"]) == IDS[1] else [1, 2, 3]
            require([episode["episodeNumber"] for episode in episodes] == expected and all(episode["seasonNumber"] == 1 for episode in episodes), "Jikan changed native TV/movie episode numbering")
            if len(expected) == 3:
                require(episodes[0]["title"] == "First native episode" and episodes[2]["title"] == "Third native episode", "Episode gap fill overwrote a real title")
            snapshots[series["id"]] = self.episode_identity(series)
        self.observations["nativeEpisodeSnapshots"] = snapshots
        self.check("real Jikan JSON creates separate native MAL TV/movie entries and fills gaps without duplicating or renumbering episode titles")
        found = self.api_json("/api/v5/series/lookup?metadataSource=myAnimeList&term=Generated")
        require(sorted(int(item["foreignId"]) for item in found) == list(IDS[:2]), "Jikan catalogue search exposed invalid/duplicate IDs")
        self.check("v5 MyAnimeList catalogue searches use public Jikan and exclude invalid IDs without leaking account credentials")

    def edit_filters(self, expected, **values):
        resource = self.saved_definition()
        resource["enableAutomaticAdd"] = True
        for name, value in values.items():
            self.field(resource, name)["value"] = value
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.fresh_sync()
        self.expected_library.update(expected)
        require(self.stored_ids() == expected and self.library_ids() == sorted(self.expected_library), "A MAL filter returned the wrong membership or added an unselected/removed existing native member")

    def set_difference(self):
        self.edit_filters([IDS[0], IDS[1], IDS[3]], includedTags=[])
        self.check("empty MAL tag inclusion means all eligible statuses minus every excluded tag")
        self.edit_filters([IDS[0], IDS[3]], listStatuses=[1])
        self.edit_filters([IDS[0]], importUnreleased=False)
        self.edit_filters([], minimumScore=9)
        self.check("account-status, catalogue-release and minimum-score filters remain independent AND conditions")
        self.edit_filters([], includedTags=["future"], minimumScore=5, importUnreleased=True)
        self.check("MAL tags remain exact literal names rather than fuzzy or case-insensitive custom-list substitutes")
        self.edit_filters(list(IDS[:2]), includedTags=["Include", "Pinned"], listStatuses=[1, 5])

    def retain_complete(self):
        failures = {}
        for failure in ("unavailable", "missingTags", "nullData", "unsafeNext", "repeatedNext"):
            self.control(failure=failure)
            status, _body = self.raw("/api/v5/importlist/test", "POST", self.saved_definition())
            require(status == 400, "Incomplete MAL " + failure + " passed Test")
            self.fresh_sync()
            require(self.stored_ids() == list(IDS[:2]) and self.library_ids() == sorted(self.expected_library), "Incomplete MAL response replaced complete membership or added stale data")
            failures[failure] = status
        self.control(failure=None, page2Quota="terminal", quotaEpoch="terminal-proof")
        status, _body = self.raw("/api/v5/importlist/test", "POST", self.saved_definition())
        require(status == 400, "Terminal MAL quota incorrectly passed Test")
        self.fresh_sync()
        quota = [row for row in self.records() if row.get("epoch") == "terminal-proof" and row.get("offset") == 2]
        require(len(quota) == 6 and all(row["status"] == 429 for row in quota), "Terminal MAL Test/sync quota exceeded three attempts each")
        require(self.stored_ids() == list(IDS[:2]), "Terminal quota replaced the complete saved list")
        self.control(page2Quota=None)
        self.fresh_sync()
        self.observations["unconfirmedListRetention"] = failures
        self.check("HTTP errors, missing tags/data, unsafe/repeated pagination and terminal429 retain complete MAL membership and later recover")

    def action(self, name, resource, **query):
        return self.raw("/api/v5/importlist/action/" + name + "?" + urllib.parse.urlencode(query), "POST", resource)

    def oauth(self):
        resource = self.saved_definition()
        before = sum(row.get("kind") == "oauth-token" for row in self.records())
        status, body = self.action("startOAuth", resource, callbackUrl="http://localhost:6970/oauth.html")
        require(status == 200, "Registered-client direct OAuth did not start")
        uri = urllib.parse.urlparse(json.loads(body)["oauthUrl"])
        query = urllib.parse.parse_qs(uri.query)
        require(uri.hostname == "myanimelist.net" and uri.path == "/v1/oauth2/authorize" and query["code_challenge_method"] == ["plain"] and 43 <= len(query["code_challenge"][0]) <= 128, "OAuth omitted official endpoint/PKCE")
        status, _ = self.action("getOAuthToken", resource, state="unrecognized-state", code="generated+code=with/slashes")
        require(status == 400 and sum(row.get("kind") == "oauth-token" for row in self.records()) == before, "Unknown OAuth state reached the token endpoint")
        status, body = self.action("getOAuthToken", resource, state=query["state"][0], code="generated+code=with/slashes")
        require(status == 200, "Single-use OAuth callback failed")
        token = json.loads(body)
        require(token["accessToken"] == ACCESS and token["refreshToken"] == REFRESH, "Token response deserialization lost OAuth fields")
        status, _ = self.action("getOAuthToken", resource, state=query["state"][0], code="generated+code=with/slashes")
        require(status == 400 and sum(row.get("kind") == "oauth-token" for row in self.records()) == before + 1, "Replayed OAuth state executed another token exchange")
        self.check("official direct OAuth uses PKCE, validates state, preserves encoded codes and refuses callback replay without Sonarr cloud")
        stored_before = self.stored_settings_json()
        for name, value in {"username": "@me", "accessToken": ACCESS, "refreshToken": REFRESH,
                            "expires": "2000-01-01T00:00:00Z", "includedTags": ["Unsaved tag"]}.items():
            self.field(resource, name)["value"] = value
        status, _ = self.raw("/api/v5/importlist/test", "POST", resource)
        require(status == 204 and self.stored_settings_json() == stored_before, "Unsaved Test token refresh persisted its credentials or filters")
        self.check("Test can renew an unsaved token without saving credentials, account choice or filters")
        self.field(resource, "includedTags")["value"] = ["Include", "Pinned"]
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.fresh_sync()
        require(self.stored_ids() == list(IDS[:2]), "Bearer account refresh changed complete membership")
        records = self.records()
        require(sum(row.get("grantType") == "refresh_token" for row in records) == 1 and any(row.get("authMode") == "bearer" for row in records), "Fetch replayed a rotated refresh token or lost the renewed Bearer grant")
        settings = {key.lower(): value for key, value in json.loads(self.stored_settings_json()).items()}
        require(settings["accesstoken"] == ROTATED_ACCESS and settings["refreshtoken"] == ROTATED_REFRESH, "The successful Fetch did not persist the safely handed-off grant")
        require(not any("sonarr.tv" in row.get("host", "") for row in records), "MAL authentication contacted Sonarr cloud")
        self.check("Test-to-Fetch safely hands off a rotated MAL grant without replaying the old refresh token or leaking credentials to Jikan")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=_parent.MOCK_IMAGE)
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
    audit = MyAnimeListReliability(args)
    error, records, logs, cleanup = None, [], "", False
    def redact(value):
        for secret in (audit.api_key, ACCESS, REFRESH, ROTATED_ACCESS, ROTATED_REFRESH):
            value = value.replace(secret, "[REDACTED]")
        return value
    try:
        audit.start()
        audit.prepare()
        audit.create_list()
        audit.baseline()
        audit.set_difference()
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
            args.report.write_text(json.dumps({"createdUtc": _parent._groups.utc_now(), "image": args.image, "imageId": audit.image_id,
                "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"), "runtime": audit.runtime_metadata,
                "mockImage": args.mock_image, "mockImageId": audit.mock_id, "passed": error is None, "error": error,
                "checks": audit.results, "observations": audit.observations, "fixtureRequests": records,
                "cleanupComplete": cleanup, "keptData": str(audit.root) if args.keep and audit.root else None,
                "scope": {"trustedLocalMALAndJikanMock": True, "liveMALAccountTested": False, "liveJikanTested": False,
                          "privateAccountUsed": False, "realLibraryTested": False, "downloadsTested": False,
                          "mainConfigurationAccessed": False, "externalProviderRoutingEnabled": False,
                          "tlsValidationDisabled": False, "databaseInspection": "read-only in the owned temporary fixture"}}, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
