#!/usr/bin/env python3
"""Verify AniList quota recovery using only disposable Docker data and trusted local TLS."""

import argparse
from contextlib import closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import secrets
import sqlite3
import ssl
import struct
import sys
import tempfile
import threading
import urllib.parse
import zlib

from smoke import SmokeFailure, require


_spec = importlib.util.spec_from_file_location("anime_groups_fixture", Path(__file__).with_name("anime-groups.py"))
_groups = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_groups)

IDS = (940101, 940102, 940103, 940104)
MOCK_IMAGE = "python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a"
REMOVED_STATUS_EXCLUSION_FIELDS = ("excludeCurrent", "excludePlanning", "excludeCompleted", "excludeDropped", "excludePaused", "excludeRepeating")
LEGACY_CUSTOM_LIST_NAME = "Legacy selection"


def poster_png():
    def chunk(name, data):
        return struct.pack("!I", len(data)) + name + data + struct.pack("!I", zlib.crc32(name + data) & 0xffffffff)
    pixels = b"".join(b"\x00" + b"\x80\x90\xa0" * 16 for _ in range(16))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack("!IIBBBBB", 16, 16, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))


def media_resource(media_id):
    title = "Generated reliability anime " + str(media_id)
    related = [("SEQUEL", IDS[1])] if media_id == IDS[0] else [("PREQUEL", IDS[0])] if media_id == IDS[1] else []
    return {
        "id": media_id, "type": "ANIME", "format": "TV", "idMal": None,
        "title": {"english": title, "romaji": title, "userPreferred": title},
        "synonyms": [], "description": "Synthetic local reliability fixture.",
        "status": "FINISHED", "episodes": 2, "duration": 24,
        "startDate": {"year": 2024, "month": 1, "day": 1},
        "endDate": {"year": 2024, "month": 1, "day": 8},
        "countryOfOrigin": "JP", "genres": ["Animation"], "averageScore": 80, "popularity": 100,
        "coverImage": {"large": "https://graphql.anilist.co/cover/" + str(media_id) + ".png"},
        "studios": {"nodes": []},
        "airingSchedule": {"pageInfo": {"currentPage": 1, "hasNextPage": False}, "nodes": [
            {"episode": 1, "airingAt": 1704067200}, {"episode": 2, "airingAt": 1704672000}]},
        "relations": {"edges": [{"relationType": kind, "node": {"id": target, "type": "ANIME", "format": "TV"}}
                                for kind, target in related]},
    }


def list_membership(media_id, control, include_override=None):
    legacy_only_id = control.get("legacyOnlyId")
    excluded_only = control.get("excludedOnlyIds", [IDS[3]])
    included = media_id != legacy_only_id and media_id not in excluded_only
    if media_id == IDS[3] and control.get("includeExcludedOverlap"):
        included = True
    if include_override is not None:
        included = include_override
    membership = {"Include": included, "Never": media_id == IDS[3], "After edit": media_id == IDS[2]}
    if legacy_only_id:
        membership[LEGACY_CUSTOM_LIST_NAME] = media_id == legacy_only_id
    return membership


def list_row(media_id, control, include_override=None):
    membership = list_membership(media_id, control, include_override)
    resource = media_resource(media_id)
    if media_id == IDS[3]:
        resource["status"] = control.get("overlapReleaseStatus", "FINISHED")
    row = {"status": control.get("overlapListStatus", "CURRENT") if media_id == IDS[3] else "CURRENT",
           "progress": 0, "media": resource}
    shape = "omitted" if control.get("unknownMembership") else control.get("membershipShape", "map")
    if shape != "omitted":
        row["customLists"] = membership if shape == "map" else None
        row["customListEntries"] = ([{"name": name, "enabled": enabled} for name, enabled in membership.items()]
                                    if shape in ("map", "array") else None)
    return row


def membership_collection(control, chunk):
    names = ["Include", "Never", "After edit"]
    if control.get("legacyOnlyId"):
        names.append(LEGACY_CUSTOM_LIST_NAME)
    owner = {"id": 940001, "name": "generated-reliability-account"}
    user = dict(owner, mediaListOptions={"animeList": {"customLists": names}})
    if control.get("unknownMembership") or control.get("collectionFailure") == "unavailable":
        return {"data": {"User": user, "MediaListCollection": None},
                "errors": [{"message": "Synthetic collection membership unavailable"}]}
    if control.get("collectionFailure") == "partial" and chunk == 2:
        return {"data": {"User": user, "MediaListCollection": None},
                "errors": [{"message": "Synthetic second collection chunk unavailable"}]}
    chunks = bool(control.get("collectionChunks"))
    members = list(IDS[(chunk - 1) * 2:chunk * 2]) if chunks else list(IDS)
    if chunks and chunk == 2:
        members.append(IDS[0])
    if control.get("collectionFailure") == "missingId":
        members = [media_id for media_id in members if media_id != IDS[2]]
    def entries(ids):
        return [{"media": {"id": media_id, "type": "ANIME"}} for media_id in ids]
    lists = [{"name": "Watching", "isCustomList": False, "entries": entries(members)}]
    for name in names:
        ids = [] if control.get("collectionEmptyCustomMembership") else [
            media_id for media_id in members if list_membership(media_id, control).get(name)]
        lists.append({"name": name, "isCustomList": True, "entries": entries(ids)})
    collection_owner = dict(owner)
    if control.get("collectionFailure") == "wrongOwner":
        collection_owner = {"id": 940002, "name": "generated-other-account"}
    return {"data": {"User": user, "MediaListCollection": {
        "user": collection_owner, "hasNextChunk": chunks and chunk == 1, "lists": lists}}}


def serve_fixture(root):
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def answer(self, status, value, kind="application/json"):
            body = value if isinstance(value, bytes) else json.dumps(value).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", kind)
            self.send_header("Content-Length", str(len(body)))
            if status == 429:
                self.send_header("Retry-After", "1")
                self.send_header("X-RateLimit-Remaining", "0")
            elif kind == "application/json":
                self.send_header("X-RateLimit-Remaining", "29")
            self.send_header("X-RateLimit-Limit", "30")
            self.end_headers()
            self.wfile.write(body)

        def record(self, value):
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                output.write(json.dumps(value) + "\n")

        def do_POST(self):
            record = {"utc": _groups.utc_now(), "status": 400}
            try:
                length = int(self.headers.get("Content-Length", "0"))
                require(self.path == "/" and 0 < length < 65536, "Unexpected fixture request")
                request = json.loads(self.rfile.read(length))
                query, variables = request["query"], request["variables"]
                control = json.loads((root / "control.json").read_text(encoding="utf-8"))
                if "MediaListCollection(" in query:
                    require(variables.get("id") == "generated-reliability-account", "Unexpected collection account")
                    chunk = int(variables["chunk"])
                    require(chunk in (1, 2) and "isCustomList" in query and "mediaListOptions" in query,
                            "Unexpected authoritative collection query")
                    record.update(kind="membership-collection", chunk=chunk, failure=control.get("collectionFailure"),
                                  unavailable=bool(control.get("unknownMembership")))
                    value = membership_collection(control, chunk)
                elif "mediaList(userName:" in query:
                    require(variables.get("id") == "generated-reliability-account", "Unexpected fixture account")
                    page = int(variables["page"])
                    require(page in (1, 2) and "customListEntries:" in query, "Unexpected fixture list query")
                    rows = []
                    for media_id in IDS[(page - 1) * 2:page * 2]:
                        override = None
                        if media_id == IDS[3] and control.get("overlapDuplicateOrder"):
                            override = control["overlapDuplicateOrder"] == "excludedThenIncluded"
                        rows.append(list_row(media_id, control, override))
                    if page == 1 and control.get("overlapDuplicateOrder"):
                        rows.append(list_row(IDS[3], control, control["overlapDuplicateOrder"] == "includedThenExcluded"))
                    record.update(kind="list", page=page, unknownMembership=bool(control.get("unknownMembership")))
                    value = {"data": {"Page": {"pageInfo": {"currentPage": page, "hasNextPage": page == 1}, "mediaList": rows}}}
                elif "Media(" in query:
                    media_id = int(variables["id"])
                    require(media_id in IDS, "Unknown synthetic media ID")
                    kind = "metadata" if "airingSchedule" in query else "relations"
                    record.update(kind=kind, mediaId=media_id)
                    if kind == "metadata" and media_id in control.get("blockedMetadataIds", []):
                        record["status"] = 429
                        self.answer(429, {"data": None, "errors": [{"message": "Too Many Requests", "status": 429}]})
                        return
                    value = {"data": {"Media": media_resource(media_id)}}
                else:
                    raise SmokeFailure("Unsupported synthetic GraphQL request")
                record["status"] = 200
                self.answer(200, value)
            except (KeyError, TypeError, ValueError, SmokeFailure):
                self.answer(400, {"errors": [{"message": "Unsupported local reliability fixture request"}]})
            finally:
                self.record(record)

        def do_GET(self):
            prefix = "/cover/"
            if self.path.startswith(prefix) and self.path.endswith(".png"):
                try:
                    media_id = int(self.path[len(prefix):-4])
                    require(media_id in IDS, "Unknown fixture cover")
                    self.answer(200, poster_png(), "image/png")
                    self.record({"utc": _groups.utc_now(), "kind": "cover", "mediaId": media_id, "status": 200})
                    return
                except (ValueError, SmokeFailure):
                    pass
            self.answer(404, {"errors": [{"message": "Unknown fixture endpoint"}]})

        def do_HEAD(self):
            prefix = "/cover/"
            try:
                media_id = int(self.path[len(prefix):-4])
                require(self.path.startswith(prefix) and self.path.endswith(".png") and media_id in IDS,
                        "Unknown fixture cover")
                self.send_response(200)
                self.send_header("Content-Type", "image/png")
                self.send_header("Content-Length", str(len(poster_png())))
                self.end_headers()
                self.record({"utc": _groups.utc_now(), "kind": "cover-head", "mediaId": media_id, "status": 200})
            except (ValueError, SmokeFailure):
                self.send_response(404)
                self.send_header("Content-Length", "0")
                self.end_headers()

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local AniList reliability TLS fixture ready", flush=True)
    server.serve_forever()


class AniListReliability(_groups.AnimeGroups):
    fixture_script = "anilist-reliability.py"
    fixture_ready_message = "Local AniList reliability TLS fixture ready"

    def __init__(self, args):
        super().__init__(args)
        self.observations = {}
        self.control_state = {"blockedMetadataIds": [IDS[1]], "unknownMembership": False}
        self.definition = None

    def control(self, **values):
        self.control_state.update(values)
        path = self.root / "fixture" / "control.json"
        temporary = path.with_suffix(".next")
        temporary.write_text(json.dumps(self.control_state), encoding="utf-8")
        temporary.replace(path)

    def start(self):
        require(self.docker("info", "--format", "{{.OSType}}") == "linux", "Linux Docker required")
        image = json.loads(self.docker("image", "inspect", self.args.image))[0]
        self.image_id, self.image_labels = image["Id"], image["Config"].get("Labels", {}) or {}
        require(image["Os"] == "linux", "An already built Linux Tanukarr image is required")
        self.mock_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.mock_image)
        self.root = Path(tempfile.mkdtemp(prefix="sonarr-container-smoke-anilist-reliability-")).resolve()
        config, media = self.root / "config", self.root / "media"
        config.mkdir()
        media.mkdir()
        media.chmod(0o777)
        for name in ("library", "explicit"):
            (media / name).mkdir()
            (media / name).chmod(0o777)
        (config / "config.xml").write_text(
            "<Config><BindAddress>*</BindAddress><Port>8989</Port>"
            f"<ApiKey>{self.api_key}</ApiKey><LaunchBrowser>False</LaunchBrowser>"
            "<AuthenticationMethod>Forms</AuthenticationMethod><AuthenticationRequired>Enabled</AuthenticationRequired>"
            "<AllowedHosts>127.0.0.1,localhost</AllowedHosts><AnalyticsEnabled>False</AnalyticsEnabled>"
            "<LogLevel>Debug</LogLevel></Config>", encoding="utf-8")
        tls = self.create_tls()
        self.control()
        suffix = secrets.token_hex(6)
        self.network = self.docker("network", "create", "--internal", "tanukarr-anilist-reliability-" + suffix)
        self.mock = self.docker(
            "run", "--detach", "--name", "tanukarr-anilist-reliability-mock-" + suffix,
            "--label", "tanukarr.anilist-reliability-test=true", "--network", self.network,
            "--network-alias", "graphql.anilist.co", "--env", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            "--mount", f"type=bind,source={tls},target=/fixture", self.args.mock_image,
            "python", "-u", "/runner/" + self.fixture_script, "--serve-fixture", "/fixture")
        self.until(lambda: self.fixture_ready_message in self.docker("logs", self.mock), "TLS fixture did not start")
        startup = ("set -eu; cp /fixture-ca.crt /usr/local/share/ca-certificates/tanukarr-reliability-fixture.crt; "
                   "update-ca-certificates >/dev/null 2>&1; exec /entrypoint.sh /app/Sonarr -nobrowser -data=/config")
        self.container = self.docker(
            "create", "--name", "tanukarr-anilist-reliability-" + suffix,
            "--label", "tanukarr.anilist-reliability-test=true", "--label", "sonarr.smoke-test=true",
            "--network", self.network, "--network-alias", "fixture-app",
            "--env", "PUID=1000", "--env", "PGID=1000", "--env", "UMASK=002",
            "--env", "HTTP_PROXY=", "--env", "HTTPS_PROXY=", "--env", "ALL_PROXY=",
            "--mount", f"type=bind,source={config},target=/config", "--mount", f"type=bind,source={media},target=/media",
            "--mount", f"type=bind,source={tls / 'ca.crt'},target=/fixture-ca.crt,readonly",
            "--entrypoint", "/bin/sh", self.args.image, "-c", startup)
        self.docker("start", self.container)
        self.api_network = self.docker("network", "create", "tanukarr-reliability-api-" + suffix)
        self.relay = self.docker(
            "create", "--name", "tanukarr-reliability-relay-" + suffix,
            "--label", "tanukarr.anilist-reliability-test=true", "--network", self.api_network,
            "--publish", "127.0.0.1::8989", "--mount", f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            self.args.mock_image, "python", "-u", "/runner/anime-groups.py", "--serve-relay")
        self.docker("network", "connect", self.network, self.relay)
        self.docker("start", self.relay)
        self.refresh_binding()
        self.wait_ready()
        require(json.loads(self.docker("network", "inspect", self.network))[0]["Internal"], "Fixture external routing enabled")
        mock_networks = json.loads(self.docker("inspect", self.mock))[0]["NetworkSettings"]["Networks"]
        mock_ip = next(iter(mock_networks.values()))["IPAddress"]
        require(mock_ip in self.docker("exec", self.container, "getent", "hosts", "graphql.anilist.co"), "AniList DNS escaped the mock")
        self.check("isolated trusted TLS AniList, loopback-only API and no external provider routing")

    def records(self):
        path = self.root / "fixture" / "requests.jsonl"
        return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line] if path.exists() else []

    def stored_ids(self):
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config" and self.root.name.startswith("sonarr-container-smoke-anilist-reliability-"),
                "Read-only database inspection escaped the owned fixture")
        uri = "file:" + urllib.parse.quote(str(path), safe="/") + "?mode=ro"
        with closing(sqlite3.connect(uri, uri=True, timeout=10)) as connection:
            connection.execute("PRAGMA query_only = ON")
            return sorted(row[0] for row in connection.execute("SELECT AniListId FROM ImportListItems WHERE ImportListId = ?", (self.definition["id"],)))

    def library_ids(self):
        return sorted(int(item["foreignId"]) for item in self.api_json("/api/v5/series"))

    def idle(self):
        self.until(lambda: all(item["status"] in ("completed", "failed", "aborted") for item in self.api_json("/api/v5/command")),
                   "Disposable command queue did not settle")

    def create_list(self):
        resource = next(item for item in self.api_json("/api/v5/importlist/schema") if item["implementation"] == "AniListImport")
        for version in ("v3", "v5"):
            schema = self.api_json(f"/api/{version}/importlist/schema")
            names = {field["name"] for provider in schema if provider["implementation"] == "AniListImport" for field in provider["fields"]}
            require(not names.intersection(REMOVED_STATUS_EXCLUSION_FIELDS), f"Removed status exclusions remain in {version} schema")
            require(all(name in names for name in ("importCurrent", "importPlanning", "importCompleted", "importDropped",
                                                   "importPaused", "importRepeating", "importFinished", "importReleasing",
                                                   "importUnreleased", "importCancelled", "importHiatus",
                                                   "includedCustomLists", "excludedCustomLists")), "Retained AniList filters are missing")
            scalar = next(field for provider in schema if provider["implementation"] == "AniListImport"
                          for field in provider["fields"] if field["name"] == "customList")
            require(scalar.get("hidden") == "hidden" and scalar.get("value") in (None, ""),
                    "The legacy scalar custom-list default is not hidden and blank")
        self.check("v3/v5 schemas omit all six removed status exclusions and retain import/release/custom-list filters")
        resource.update(name="Generated reliability list", rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                        language={"id": 1, "name": "English"}, metadataSource="aniList", enableAutomaticAdd=True,
                        shouldMonitor="none", searchForMissingEpisodes=False, seriesType="anime", seasonFolder=True)
        values = {"username": "generated-reliability-account", "accessToken": "generated-reliability-access",
                  "refreshToken": "generated-reliability-refresh", "expires": "2099-01-01T00:00:00Z",
                  "includedCustomLists": ["Include"], "excludedCustomLists": ["Never"], "customList": "",
                  "importCurrent": True, "importPlanning": False, "importCompleted": False, "importDropped": False,
                  "importPaused": False, "importRepeating": False, "importFinished": True, "importReleasing": False,
                  "importUnreleased": False, "importCancelled": False, "importHiatus": False}
        fields = {field["name"]: field for field in resource["fields"]}
        require(all(key in fields for key in values), "AniList schema omitted fixture settings")
        for key, value in values.items():
            fields[key]["value"] = value
        resource["fields"].extend({"name": name, "type": "checkbox", "value": True} for name in REMOVED_STATUS_EXCLUSION_FIELDS)
        self.definition = self.api_json("/api/v5/importlist", "POST", resource)
        self.idle()
        require(not {field["name"] for field in self.definition["fields"]}.intersection(REMOVED_STATUS_EXCLUSION_FIELDS),
                "Saving a legacy payload reintroduced removed status exclusions")
        self.check("synthetic custom-list definition saved with explicit inclusion and excluded-only membership")

    def legacy_status_payload(self):
        statuses = {}
        for version in ("v3", "v5"):
            resource = self.api_json(f"/api/{version}/importlist/{self.definition['id']}")
            require(not {field["name"] for field in resource["fields"]}.intersection(REMOVED_STATUS_EXCLUSION_FIELDS),
                    f"Saved legacy payload exposed removed status exclusions through {version}")
            resource["fields"].extend({"name": name, "type": "checkbox", "value": True} for name in REMOVED_STATUS_EXCLUSION_FIELDS)
            status, _body = self.raw(f"/api/{version}/importlist/test", "POST", resource)
            expected_status = 200 if version == "v3" else 204
            require(status == expected_status, f"Legacy true status-exclusion fields broke the {version} provider Test: HTTP {status}")
            statuses[version] = status
        saved = self.api_json("/api/v5/importlist/" + str(self.definition["id"]))
        require(saved["fields"] == self.definition["fields"], "Provider Test persisted legacy status-exclusion fields")
        self.observations["ignoredLegacyStatusExclusions"] = {"submittedTrueFields": list(REMOVED_STATUS_EXCLUSION_FIELDS),
                                                             "testStatuses": statuses, "savedFieldsUnchanged": True}
        self.check("legacy API payloads with all six former exclusions true pass v3/v5 Test without restoring removed settings")

    def quota_recovery(self):
        self.command({"name": "ImportListSync"})
        self.idle()
        require(self.library_ids() == [IDS[0]], "Metadata 429 did not retain exactly the earlier successful series")
        require(self.stored_ids() == list(IDS[:3]), "Complete membership fetch was lost after metadata 429")
        failed = [r for r in self.records() if r.get("kind") == "metadata" and r.get("mediaId") == IDS[1] and r["status"] == 429]
        require(len(failed) == 3, "Terminal metadata quota did not use exactly three bounded attempts")
        require(not any(r.get("kind") == "metadata" and r.get("mediaId") == IDS[2] for r in self.records()),
                "Batch continued AniList metadata calls after terminal quota exhaustion")
        self.observations["firstSync"] = {"libraryIds": self.library_ids(), "storedMembershipIds": self.stored_ids(), "terminal429Attempts": len(failed)}
        self.check("metadata 429 keeps the completed membership snapshot and earlier successful batch insertion")
        self.check("terminal quota retries are bounded and stop remaining AniList batch traffic")

        before = sum(r.get("kind") == "list" for r in self.records())
        self.control(blockedMetadataIds=[])
        self.command({"name": "ImportListSync"})
        self.idle()
        require(self.library_ids() == list(IDS[:3]), "Automatic cached membership retry did not add remaining series")
        after = sum(r.get("kind") == "list" for r in self.records())
        require(after == before, "Automatic quota recovery repeated the successful list GraphQL pagination")
        self.observations["cachedRecovery"] = {"listRequestsBefore": before, "listRequestsAfter": after, "libraryIds": self.library_ids()}
        self.check("automatic ImportListSync resumes unchanged successful membership without a fresh list request")
        require(IDS[3] not in self.library_ids() and IDS[3] not in self.stored_ids(),
                "An excluded-only custom-list entry was imported")
        self.check("legacy status exclusions do not block included Watching entries while excluded-only entries stay excluded")

        for series in self.api_json("/api/v5/series"):
            self.command({"name": "RefreshSeries", "seriesId": series["id"]})
        self.idle()
        records = self.records()
        for media_id in IDS[:3]:
            require(sum(r.get("kind") == "metadata" and r.get("mediaId") == media_id and r["status"] == 200 for r in records) == 1,
                    "Public metadata cache did not coalesce add and refresh")
        require(sum(r.get("kind") == "relations" for r in records) == 1, "Shared relation cache did not remove repeated chain lookups")
        self.check("one successful metadata fetch per series and reused root/neighbor relation data across add and refresh")
        self.until(lambda: all((self.root / "config" / "MediaCover" / str(s["id"]) / "poster.jpg").is_file()
                               for s in self.api_json("/api/v5/series")), "Generated cover images did not initialize")
        for series in self.api_json("/api/v5/series"):
            status, body = self.raw("/api/v5/mediacover/" + str(series["id"]) + "/poster.jpg")
            require(status == 200 and len(body) > 0, "Generated authenticated poster is not served: HTTP " + str(status))
        self.check("synthetic catalogue posters are downloaded and served through the real cover pipeline")

    def stale_guard(self):
        third = next(s for s in self.api_json("/api/v5/series") if int(s["foreignId"]) == IDS[2])
        self.api_json(f"/api/v5/series/{third['id']}?deleteFiles=false&addImportListExclusion=false", "DELETE")
        self.idle()
        resource = self.api_json("/api/v5/importlist/" + str(self.definition["id"]))
        next(field for field in resource["fields"] if field["name"] == "excludedCustomLists")["value"] = ["Never", "After edit"]
        self.control(unknownMembership=True, excludedOnlyIds=[IDS[2], IDS[3]])
        before = sum(r.get("kind") == "list" for r in self.records())
        self.api_json("/api/v5/importlist/" + str(self.definition["id"]) + "?skipTesting=true", "PUT", resource)
        self.idle()
        require(sum(r.get("kind") == "list" for r in self.records()) > before, "Changed settings did not perform a new full fetch attempt")
        require(self.stored_ids() == list(IDS[:3]), "Unknown authoritative membership replaced the previous saved list")
        self.command({"name": "ImportListSync"})
        self.idle()
        require(self.library_ids() == list(IDS[:2]), "Failed changed-settings fetch resumed stale list rows")
        self.check("changed custom-list exclusions plus unknown membership retain saved rows without stale automatic additions")

        self.control(unknownMembership=False)
        resource = self.api_json("/api/v5/importlist/" + str(self.definition["id"]))
        status, _body = self.raw("/api/v5/importlist/test", "POST", resource)
        require(status == 204, "Synthetic recovered connection test failed")
        before = sum(r.get("kind") == "list" for r in self.records())
        self.command({"name": "ImportListSync"})
        self.idle()
        require(self.library_ids() == list(IDS[:2]) and sum(r.get("kind") == "list" for r in self.records()) == before,
                "Connection Test recreated an invalid successful-fetch witness or resumed stale rows")
        self.check("a later successful Test cannot restore the invalidated cached membership witness")
        self.command({"name": "ImportListSync", "definitionId": self.definition["id"]})
        self.idle()
        require(self.library_ids() == list(IDS[:2]) and self.stored_ids() == list(IDS[:2]),
                "Authoritative fresh sync did not apply the changed exclusion list")
        self.observations["changedExclusions"] = {"libraryIds": self.library_ids(), "storedMembershipIds": self.stored_ids()}
        self.check("explicit fresh sync stores the new authoritative exclusions and does not re-add excluded Watching media")

    def saved_definition(self, version="v5"):
        return self.api_json(f"/api/{version}/importlist/{self.definition['id']}")

    @staticmethod
    def field(resource, name):
        return next(field for field in resource["fields"] if field["name"] == name)

    def stored_settings_json(self):
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config" and self.root.name.startswith("sonarr-container-smoke-anilist-reliability-"),
                "Legacy-settings inspection escaped the owned fixture")
        uri = "file:" + urllib.parse.quote(str(path), safe="/") + "?mode=ro"
        with closing(sqlite3.connect(uri, uri=True, timeout=10)) as connection:
            connection.execute("PRAGMA query_only = ON")
            row = connection.execute("SELECT Settings FROM ImportLists WHERE Id = ?", (self.definition["id"],)).fetchone()
        require(row is not None, "Owned fixture import-list row is missing")
        return row[0]

    def normalized_custom_lists(self, expected):
        for version in ("v3", "v5"):
            resource = self.saved_definition(version)
            included = self.field(resource, "includedCustomLists")["value"]
            scalar = self.field(resource, "customList")
            require(included == expected, f"{version} did not retain the exact normalized custom-list selection")
            require(scalar.get("hidden") == "hidden" and scalar.get("value") in (None, ""),
                    f"{version} exposed a visible or active scalar custom-list filter")
        return self.saved_definition()

    def legacy_custom_list_migration(self):
        self.control(legacyOnlyId=IDS[2], excludedOnlyIds=[IDS[3]])
        resource = self.saved_definition()
        self.field(resource, "includedCustomLists")["value"] = ["Include"]
        self.field(resource, "excludedCustomLists")["value"] = ["Never"]
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.command({"name": "ImportListSync", "definitionId": self.definition["id"]})
        require(self.library_ids() == list(IDS[:2]) and self.stored_ids() == list(IDS[:2]),
                "Legacy migration setup did not isolate the custom-list-only member")

        # Exercise the real settings converter on startup. This is one generated
        # row in our stopped temporary app, never the user's configuration.
        self.docker("stop", "--time", "20", self.container)
        self.prepare_config_for_host_sqlite()
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config", "Legacy-settings seed escaped the owned fixture")
        settings = json.loads(self.stored_settings_json())
        key = next((name for name in settings if name.lower() == "customlist"), "customList")
        settings[key] = "  " + LEGACY_CUSTOM_LIST_NAME + "  "
        raw_seed = json.dumps(settings)
        with closing(sqlite3.connect(path, timeout=10)) as connection:
            changed = connection.execute("UPDATE ImportLists SET Settings = ? WHERE Id = ?", (raw_seed, self.definition["id"])).rowcount
            require(changed == 1, "Legacy-settings seed affected an unexpected number of rows")
            connection.commit()
        self.docker("start", self.container)
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        self.idle()
        self.normalized_custom_lists(["Include", LEGACY_CUSTOM_LIST_NAME])
        require(self.stored_settings_json() == raw_seed, "Loading legacy custom-list settings rewrote their stored JSON before Save")
        self.command({"name": "ImportListSync", "definitionId": self.definition["id"]})
        self.idle()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Stored legacy custom-list selection was lost rather than merged into included tags")
        self.observations["legacyCustomListMigration"] = {"normalizedIncludedLists": ["Include", LEGACY_CUSTOM_LIST_NAME],
                                                        "storedSettingsUnchangedBeforeSave": True,
                                                        "libraryIdsAfterMigration": self.library_ids()}
        self.check("restart preserves a stored legacy custom-list selection as modern included tags without rewriting JSON before Save")

        third = next(s for s in self.api_json("/api/v5/series") if int(s["foreignId"]) == IDS[2])
        self.api_json(f"/api/v5/series/{third['id']}?deleteFiles=false&addImportListExclusion=false", "DELETE")
        self.idle()
        resource = self.saved_definition()
        self.field(resource, "includedCustomLists")["value"] = ["Include"]
        resource["fields"] = [field for field in resource["fields"] if field["name"] != "customList"]
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.normalized_custom_lists(["Include"])
        self.command({"name": "ImportListSync", "definitionId": self.definition["id"]})
        self.idle()
        require(self.library_ids() == list(IDS[:2]) and self.stored_ids() == list(IDS[:2]),
                "Removing the migrated tag restored a hidden legacy filter or re-added its exclusive member")
        saved_settings = json.loads(self.stored_settings_json())
        require(all(not value for key, value in saved_settings.items() if key.lower() == "customlist"),
                "Saving modern tags retained an active legacy scalar setting")
        self.check("editing modern tags clears the old scalar filter and stops re-adding its exclusive Watching member")

        statuses = {}
        for version in ("v3", "v5"):
            resource = self.saved_definition(version)
            self.field(resource, "customList")["value"] = "  " + LEGACY_CUSTOM_LIST_NAME + "  "
            status, _body = self.raw(f"/api/{version}/importlist/test", "POST", resource)
            expected_status = 200 if version == "v3" else 204
            require(status == expected_status, f"Legacy scalar custom-list input failed the {version} provider Test: HTTP {status}")
            statuses[version] = status
        self.normalized_custom_lists(["Include"])
        self.api_json(f"/api/v3/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        resource = self.normalized_custom_lists(["Include", LEGACY_CUSTOM_LIST_NAME])
        self.field(resource, "customList")["value"] = "  " + LEGACY_CUSTOM_LIST_NAME + "  "
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.normalized_custom_lists(["Include", LEGACY_CUSTOM_LIST_NAME])
        self.command({"name": "ImportListSync", "definitionId": self.definition["id"]})
        self.idle()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]) and IDS[3] not in self.library_ids(),
                "Legacy scalar API normalization lost its selection or weakened the custom-list exclusion")
        self.observations["legacyScalarApi"] = {"testStatuses": statuses, "normalizedIncludedLists": ["Include", LEGACY_CUSTOM_LIST_NAME],
                                                "normalizationDeduplicated": True, "excludedOnlyWatchingEntryRemainsExcluded": True}
        self.check("v3/v5 legacy scalar Test/save inputs normalize once into tags while excluded-only entries remain excluded")

    def delete_owned_member(self, media_id):
        matches = [s for s in self.api_json("/api/v5/series") if int(s["foreignId"]) == media_id]
        require(len(matches) == 1, "Expected exactly one owned synthetic member to delete")
        self.api_json(f"/api/v5/series/{matches[0]['id']}?deleteFiles=false&addImportListExclusion=false", "DELETE")
        self.idle()

    def fresh_sync(self):
        self.command({"name": "ImportListSync", "definitionId": self.definition["id"]})
        self.idle()

    def explicit_inclusion_precedence(self):
        self.control(includeExcludedOverlap=True)
        self.fresh_sync()
        require(self.library_ids() == list(IDS) and self.stored_ids() == list(IDS),
                "Explicit custom-list inclusion did not override membership in an excluded custom list")
        self.check("an explicitly included Watching member imports even when it also belongs to an excluded custom list")

        duplicate_orders = {}
        for order in ("excludedThenIncluded", "includedThenExcluded"):
            self.delete_owned_member(IDS[3])
            self.control(overlapDuplicateOrder=order)
            self.fresh_sync()
            require(self.library_ids() == list(IDS) and self.stored_ids() == list(IDS),
                    "An excluded duplicate on another page cancelled an eligible explicit inclusion")
            duplicate_orders[order] = self.library_ids()
        self.observations["crossPageExplicitInclusion"] = duplicate_orders
        self.check("explicit inclusion survives earlier/later excluded copies across pages with one native series per media ID")

        self.delete_owned_member(IDS[3])
        self.control(overlapDuplicateOrder=None, overlapListStatus="PLANNING")
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Custom inclusion bypassed an unselected user-list status")
        self.check("explicit custom-list inclusion still requires a selected user-list status")

        self.control(overlapListStatus="CURRENT", overlapReleaseStatus="NOT_YET_RELEASED")
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Custom inclusion bypassed an unselected media-release status")
        self.check("explicit custom-list inclusion still requires a selected media-release status")

        self.control(includeExcludedOverlap=False, overlapReleaseStatus="FINISHED")
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "An excluded-only member was added without explicit custom inclusion")
        self.check("an excluded-only Watching member remains excluded when explicit custom inclusion is absent")

        resource = self.saved_definition()
        self.field(resource, "includedCustomLists")["value"] = []
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.control(includeExcludedOverlap=True)
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Watching or an unconfigured custom-list name incorrectly overrode an exclusion")
        self.check("Watching and an empty explicit custom include list do not override custom-list exclusions")
        resource = self.saved_definition()
        self.field(resource, "includedCustomLists")["value"] = ["Include", LEGACY_CUSTOM_LIST_NAME]
        self.control(includeExcludedOverlap=False)
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()

    def sparse_membership_recovery(self):
        before = sum(r.get("kind") == "membership-collection" for r in self.records())
        self.control(membershipShape="array")
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Explicit array flags changed known membership")
        require(sum(r.get("kind") == "membership-collection" for r in self.records()) == before,
                "Known typed-array membership unnecessarily fetched a collection")
        self.check("complete typed-array flags retain membership without an extra collection request")

        self.control(membershipShape="null", collectionChunks=True)
        before = sum(r.get("kind") == "membership-collection" for r in self.records())
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Sparse null map/array fields did not recover the authoritative custom memberships")
        after = sum(r.get("kind") == "membership-collection" for r in self.records())
        require(after - before == 2, "Sparse membership repeated collection pagination for each list page")
        self.observations["sparseNullRecovery"] = {"collectionRequestsPerSync": after - before,
                                                 "collectionChunks": 2, "storedMembershipIds": self.stored_ids()}
        self.check("sparse null map/array membership recovers from two complete trusted collection chunks once per sync")

        resource = self.saved_definition()
        self.field(resource, "includedCustomLists")["value"] = []
        self.control(collectionChunks=False, collectionEmptyCustomMembership=True)
        self.api_json(f"/api/v5/importlist/{self.definition['id']}?skipTesting=true", "PUT", resource)
        self.idle()
        self.fresh_sync()
        require(self.library_ids() == list(IDS) and self.stored_ids() == list(IDS),
                "A complete collection proving empty custom membership was mistaken for unknown membership")
        self.check("complete collection evidence permits an eligible null-membership entry belonging to no custom lists")

        self.delete_owned_member(IDS[3])
        self.control(collectionEmptyCustomMembership=False)
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Recovered excluded-only collection membership failed to veto the entry")
        self.check("recovered collection memberships still exclude an unselected excluded-only custom member")

        failures = {}
        for failure in ("partial", "missingId", "wrongOwner", "unavailable"):
            self.control(membershipShape="omitted", collectionChunks=failure == "partial", collectionFailure=failure)
            resource = self.saved_definition()
            status, _body = self.raw("/api/v5/importlist/test", "POST", resource)
            require(status == 400, f"Unconfirmed {failure} collection incorrectly passed provider Test")
            self.fresh_sync()
            require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                    f"Unconfirmed {failure} collection replaced the prior complete list or added stale rows")
            failures[failure] = status
        self.observations["unconfirmedCollectionRetention"] = failures
        self.check("partial, missing-ID, wrong-owner and unavailable collections reject unknown membership and retain the complete saved list")

        self.control(membershipShape="null", collectionChunks=False, collectionFailure=None)
        before = sum(r.get("kind") == "membership-collection" for r in self.records())
        self.fresh_sync()
        require(self.library_ids() == list(IDS[:3]) and self.stored_ids() == list(IDS[:3]),
                "Recovered complete collection did not resume a valid fresh sync")
        require(sum(r.get("kind") == "membership-collection" for r in self.records()) - before == 1,
                "Recovery did not execute a fresh complete collection request")
        self.check("a later complete collection restores fresh synchronization after conservative membership retention")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true", help="Keep only this disposable fixture for inspection")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    require(args.timeout > 0, "--timeout must be positive")
    audit = AniListReliability(args)
    error, records, logs = None, [], ""
    cleanup_complete = False
    secrets_to_hide = (audit.api_key, "generated-reliability-access", "generated-reliability-refresh")
    def redact(value):
        for secret in secrets_to_hide:
            value = value.replace(secret, "[REDACTED]")
        return value
    try:
        audit.start()
        audit.prepare()
        audit.create_list()
        audit.legacy_status_payload()
        audit.quota_recovery()
        audit.stale_guard()
        audit.legacy_custom_list_migration()
        audit.explicit_inclusion_precedence()
        audit.sparse_membership_recovery()
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
            error = error or redact("Could not capture fixture evidence: " + str(caught))
        try:
            audit.cleanup()
            cleanup_complete = not args.keep
        except Exception as caught:
            error = error or redact("Fixture cleanup failed: " + str(caught))
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({
                "createdUtc": _groups.utc_now(), "image": args.image, "imageId": audit.image_id,
                "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                "mockImage": args.mock_image, "mockImageId": audit.mock_id, "runtime": audit.runtime_metadata,
                "passed": error is None, "checks": audit.results, "error": error,
                "observations": audit.observations, "fixtureRequests": records,
                "fixtureConfigOwnership": audit.fixture_config_ownership,
                "scope": {"trustedLocalAniListMock": True, "liveAniListTested": False, "privateAccountUsed": False,
                          "realLibraryTested": False, "downloadsTested": False, "tlsValidationDisabled": False,
                          "sparseNullMembershipSimulated": True, "liveSparseNullFailureReproduced": False,
                          "externalProviderRoutingEnabled": False, "mainConfigurationAccessed": False,
                          "databaseInspection": "read-only plus one targeted legacy-settings seed in the owned stopped fixture"},
                "cleanupComplete": cleanup_complete,
                "keptData": str(audit.root) if args.keep and audit.root else None,
            }, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
