#!/usr/bin/env python3
"""Measure import-scoped account reuse on isolated AniList and MAL TLS fixtures."""

import argparse
from contextlib import closing
from datetime import datetime, timezone
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import sqlite3
import ssl
import sys
import threading

from smoke import SmokeFailure, require, sqlite_snapshot


def load_fixture(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


_ani = load_fixture("snapshot_anilist_fixture", "anilist-reliability.py")
_mal = load_fixture("snapshot_mal_fixture", "myanimelist-reliability.py")
MOCK_IMAGE = _ani.MOCK_IMAGE


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def collection(control, chunk):
    owner = {"id": 940001, "name": "generated-reliability-account"}
    names = ["Include", "Never", "After edit"]
    ids = _ani.IDS[(chunk - 1) * 2:chunk * 2]
    def entry(media_id):
        row = {"id": media_id + 1000000, "userId": owner["id"], "updatedAt": 1704067200,
                "status": "PLANNING" if media_id == _ani.IDS[1] else "CURRENT", "progress": 0,
                "media": _ani.media_resource(media_id)}
        if control.get("nullableAncillary"):
            if media_id == _ani.IDS[1]:
                row.pop("progress")
                row.pop("updatedAt")
            else:
                row.update(progress=None, updatedAt=None)
        return row
    # One Planning entry is hidden from status lists and exists only in a custom
    # list. Its public PREQUEL relation must still be available during grouping.
    groups = [{"name": None if control.get("unnamedDefaultGroup") else "Watching", "isCustomList": False,
               "entries": [entry(item) for item in ids if item != _ani.IDS[1]]}]
    for name in names:
        selected = {"Include": set(_ani.IDS[:3]), "Never": {_ani.IDS[3]}, "After edit": {_ani.IDS[2]}}[name]
        entries = [entry(item) for item in ids if item in selected]
        if entries or not control.get("omitEmptyCustomGroups"):
            groups.append({"name": name, "isCustomList": True, "entries": entries})
    return {"data": {"User": dict(owner, mediaListOptions={"animeList": {"customLists": None if control.get("nullableOptions") else names}}),
                     "MediaListCollection": {"user": owner, "hasNextChunk": chunk == 1, "lists": groups}}}


def serve_anilist(root):
    lock = threading.Lock()
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"
        def log_message(self, *_args):
            pass
        def record(self, value):
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                output.write(json.dumps(value) + "\n")
        def answer(self, status, value, content_type="application/json"):
            body = value if isinstance(value, bytes) else json.dumps(value).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("X-RateLimit-Limit", "30")
            self.send_header("X-RateLimit-Remaining", "29")
            self.end_headers()
            self.wfile.write(body)
        def do_POST(self):
            record = {"utc": utc_now(), "status": 400}
            try:
                length = int(self.headers.get("Content-Length", "0"))
                require(self.path == "/" and 0 < length < 65536, "Unexpected GraphQL request")
                request = json.loads(self.rfile.read(length))
                query, variables = request["query"], request["variables"]
                control = json.loads((root / "control.json").read_text())
                record["authorizationFingerprint"] = hashlib.sha256(self.headers.get("Authorization", "").encode()).hexdigest()
                record["epoch"] = control.get("epoch")
                if "MediaListCollection(" in query:
                    require(variables["id"] == "generated-reliability-account", "Unexpected selected account")
                    require("status_in" not in query and "perChunk: 500" in query, "Account fetch was filtered remotely")
                    chunk = int(variables["chunk"])
                    require(chunk in (1, 2), "Unexpected collection chunk")
                    record.update(kind="account-collection", chunk=chunk)
                    if chunk == 2 and control.get("failSecondChunk"):
                        record["status"] = 503
                        self.answer(503, {"data": None, "errors": [{"message": "Generated unavailable chunk"}]})
                        return
                    response = collection(control, chunk)
                elif "Media(" in query:
                    identity = int(variables["id"])
                    require(identity in _ani.IDS, "Unexpected native media identity")
                    record.update(kind="metadata" if "airingSchedule" in query else "relations", mediaId=identity)
                    response = {"data": {"Media": _ani.media_resource(identity)}}
                else:
                    raise SmokeFailure("Unexpected local query")
                record["status"] = 200
                self.answer(200, response)
            except (KeyError, TypeError, ValueError, SmokeFailure):
                self.answer(400, {"errors": [{"message": "Unsupported isolated snapshot query"}]})
            finally:
                self.record(record)
        def do_GET(self):
            if self.path.startswith("/cover/") and self.path.endswith(".png"):
                self.answer(200, _ani.poster_png(), "image/png")
                return
            self.answer(404, {})
        def do_HEAD(self):
            self.send_response(200 if self.path.startswith("/cover/") else 404)
            self.send_header("Content-Length", "0")
            self.end_headers()
    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local AniList snapshot TLS fixture ready", flush=True)
    server.serve_forever()


class SnapshotChecks:
    fixture_script = "import-snapshots.py"
    definitions = None
    exclusions = None
    def stored(self, definition):
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config" and self.root.name.startswith("sonarr-container-smoke-anilist-reliability-"),
                "Database read escaped the owned fixture")
        with sqlite_snapshot(path) as database:
            return sorted(row[0] for row in database.execute(
                f"SELECT {self.id_column} FROM ImportListItems WHERE ImportListId=?", (definition["id"],)))
    def force_due(self):
        self.idle()
        self.docker("stop", "--time", "20", self.container)
        self.prepare_config_for_host_sqlite()
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config" and self.root.name.startswith("sonarr-container-smoke-anilist-reliability-"),
                "Scheduling reset escaped the owned stopped fixture")
        with closing(sqlite3.connect(path, timeout=10)) as database:
            database.execute("DELETE FROM ImportListStatus")
            database.execute("UPDATE ScheduledTasks SET LastExecution='2099-01-01 00:00:00' WHERE TypeName LIKE '%ImportListSyncCommand'")
            database.commit()
        self.docker("start", self.container)
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        self.idle()
    def requests_since(self, start):
        return [row for row in self.records()[start:] if row.get("kind") == self.request_kind]
    def sync_pair(self):
        self.force_due()
        start = len(self.records())
        self.command({"name": "ImportListSync"})
        self.idle()
        rows = self.requests_since(start)
        require(len(rows) == 2 and sorted(row[self.page_field] for row in rows) == self.expected_pages,
                "Two definitions did not share exactly one complete two-page account read")
        require(self.stored(self.definitions[0]) == self.expected_a and self.stored(self.definitions[1]) == self.expected_b,
                "Shared snapshot mixed independent include/exclude projections")
        return rows
    def setup(self):
        self.exclusions = []
        for identity in self.ids:
            self.exclusions.append(self.api_json("/api/v5/importlistexclusion", "POST", {
                "tvdbId": -identity, "title": "Generated membership-only " + str(identity),
                "metadataSource": self.source, "foreignId": str(identity)}))
        self.definitions = []
        for label, values in self.selections:
            resource = next(item for item in self.api_json("/api/v5/importlist/schema") if item["implementation"] == self.implementation)
            resource.update(name="Generated snapshot " + label, rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                            language={"id": 1, "name": "English"}, enableAutomaticAdd=True, shouldMonitor="none",
                            searchForMissingEpisodes=False, seriesType="anime", seasonFolder=True, preferredMetadataSource="automatic")
            for name, value in dict(self.common_values, **values).items():
                self.field(resource, name)["value"] = value
            self.definitions.append(self.api_json("/api/v5/importlist", "POST", resource))
            self.idle()
        require(self.api_json("/api/v5/series") == [], "Global native exclusions failed to isolate membership reads")
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config", "Schema inspection escaped the owned configuration")
        with sqlite_snapshot(path) as database:
            columns = [list(row) for row in database.execute('PRAGMA table_info("ImportLists")')]
            require(columns[-1][1:] == ["PreferredMetadataSource", "INTEGER", 1, "0", 0], "Native preference migration types/defaults changed")
            self.observations["nativeMigration"] = {"version": database.execute('SELECT MAX(Version) FROM VersionInfo').fetchone()[0],
                "importListColumns": columns,
                "importListsSql": database.execute("SELECT sql FROM sqlite_master WHERE name='ImportLists' AND type='table'").fetchone()[0]}
        self.check("two real definitions configured with different local filters and API-created native exclusions")
    def verify_reuse(self):
        first = self.sync_pair()
        self.check("two definitions share one complete account fetch and keep independent include-minus-exclude results")
        second = self.sync_pair()
        self.observations["perSync"] = {"definitions": 2, "pagesWithoutSharing": 4, "firstReadRequests": len(first),
                                        "nextReadRequests": len(second), "previousScopeReused": False,
                                        "memberships": [self.expected_a, self.expected_b]}
        self.check("the next synchronization fetches both pages again instead of retaining a private account snapshot")
    def verify_test_and_preferences(self):
        definition = self.definitions[0]
        resource = self.api_json("/api/v5/importlist/" + str(definition["id"]))
        resource["preferredMetadataSource"] = self.source
        saved = self.api_json("/api/v5/importlist/" + str(definition["id"]), "PUT", resource)
        self.idle()
        require(saved["preferredMetadataSource"] == self.source, "Native provider preference was not saved")
        for version in ("v3", "v5"):
            old_client = self.api_json(f"/api/{version}/importlist/{definition['id']}")
            old_client.pop("preferredMetadataSource")
            saved = self.api_json(f"/api/{version}/importlist/{definition['id']}", "PUT", old_client)
            self.idle()
            require(saved["preferredMetadataSource"] == self.source, "An older client reset the saved catalogue preference")
            invalid = dict(saved, preferredMetadataSource="inventedProvider")
            require(self.raw(f"/api/{version}/importlist/{definition['id']}", "PUT", invalid)[0] == 400,
                    "Undefined catalogue preference was accepted")
            unsaved = self.api_json(f"/api/{version}/importlist/{definition['id']}")
            self.field(unsaved, self.include_field)["value"] = [self.test_include]
            start = len(self.records())
            status, _body = self.raw(f"/api/{version}/importlist/test", "POST", unsaved)
            require(status == (200 if version == "v3" else 204), "Unsaved local filter Test failed")
            require(len(self.requests_since(start)) == 2, "Standalone Test reused another operation's account snapshot")
            require(self.stored(definition) == self.expected_a, "Provider Test changed saved list membership")
        self.check("v3/v5 preserve preferences for older clients, reject invalid providers and fetch fresh unsaved Test inputs")
    def verify_failure(self):
        self.set_failure(True)
        self.force_due()
        before = [self.stored(item) for item in self.definitions]
        start = len(self.records())
        self.command({"name": "ImportListSync"})
        self.idle()
        require([self.stored(item) for item in self.definitions] == before, "Incomplete account read replaced complete saved memberships")
        rows = self.requests_since(start)
        require(any(row.get("status", 200) >= 400 for row in rows), "Missing-page failure was not exercised")
        self.set_failure(False)
        self.sync_pair()
        self.check("an incomplete page retains both previous memberships and a fresh later sync recovers")
    def verify_credentials(self):
        self.change_credentials()
        self.force_due()
        start = len(self.records())
        self.command({"name": "ImportListSync"})
        self.idle()
        rows = self.requests_since(start)
        require(len(rows) == 4, "Different authentication contexts shared a private account snapshot")
        require(self.stored(self.definitions[0]) == self.expected_a and self.stored(self.definitions[1]) == self.expected_b,
                "Credential isolation changed local filtering")
        self.observations["credentialIsolation"] = {"definitions": 2, "requests": len(rows), "privateAccountUsed": False}
        self.check("different generated authentication contexts fetch separate account snapshots")
    def no_acquisition(self):
        require(self.api_json("/api/v5/indexer") == [] and self.api_json("/api/v5/downloadclient") == [], "Fixture enabled acquisition")
        require(self.api_json("/api/v5/queue")["totalRecords"] == 0, "Fixture queued a download")
        require(not any("snapshot" in path.name.lower() for path in (self.root / "config").rglob("*")),
                "An account snapshot file was persisted")
        self.check("no snapshot files, indexers, download clients or downloads created in the owned disposable configuration")


class AniListSnapshots(SnapshotChecks, _ani.AniListReliability):
    fixture_ready_message = "Local AniList snapshot TLS fixture ready"
    source, implementation, id_column = "aniList", "AniListImport", "AniListId"
    ids = _ani.IDS
    request_kind, page_field, expected_pages = "account-collection", "chunk", [1, 2]
    expected_a, expected_b = [_ani.IDS[0], _ani.IDS[2]], [_ani.IDS[2]]
    include_field, test_include = "includedCustomLists", "After edit"
    selections = [("A", {"includedCustomLists": ["Include"]}), ("B", {"includedCustomLists": ["After edit"]})]
    common_values = {"username": "generated-reliability-account", "accessToken": "generated-reliability-access",
                     "refreshToken": "generated-reliability-refresh", "expires": "2099-01-01T00:00:00Z",
                     "excludedCustomLists": ["Never"], "customList": "", "importCurrent": True,
                     "importPlanning": False, "importCompleted": False, "importDropped": False,
                     "importPaused": False, "importRepeating": False, "importFinished": True,
                     "importReleasing": False, "importUnreleased": False, "importCancelled": False, "importHiatus": False}
    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"suite": "anilist", "epoch": "snapshot-proof"}
    def set_failure(self, fail):
        self.control(failSecondChunk=fail)
    def verify_nullable_collection(self):
        self.control(nullableOptions=True, nullableAncillary=True, unnamedDefaultGroup=True, omitEmptyCustomGroups=True)
        rows = self.sync_pair()
        self.observations["nullableCollection"] = {"requests": len(rows), "nullCustomListOptions": True,
            "nullAndOmittedProgressUpdatedAt": True, "unnamedStandardGroup": True,
            "customOnlyNameFirstSeenOnLaterChunk": "After edit", "memberships": [self.expected_a, self.expected_b]}
        self.control(nullableOptions=False, nullableAncillary=False, unnamedDefaultGroup=False, omitEmptyCustomGroups=False)
        self.check("nullable account options and ancillary fields preserve complete cross-chunk custom-only local filtering")
    def change_credentials(self):
        resource = self.api_json("/api/v5/importlist/" + str(self.definitions[1]["id"]))
        self.field(resource, "accessToken")["value"] = "generated-snapshot-second-access"
        self.api_json("/api/v5/importlist/" + str(resource["id"]), "PUT", resource)
        self.idle()
    def verify_relations(self):
        # Disable B and subtract the other selected entry from A. Enable only the
        # root's addition; the unselected custom-only Planning sequel was never
        # looked up and must come from the complete account's public projection.
        other = self.api_json("/api/v5/importlist/" + str(self.definitions[1]["id"]))
        other["enableAutomaticAdd"] = False
        self.api_json("/api/v5/importlist/" + str(other["id"]), "PUT", other)
        root_exclusion = next(item for item in self.exclusions if item["foreignId"] == str(self.ids[0]))
        self.api_json("/api/v5/importlistexclusion/" + str(root_exclusion["id"]), "DELETE")
        self.force_due()
        start = len(self.records())
        self.command({"name": "ImportListSync"})
        self.idle()
        series = self.api_json("/api/v5/series")
        require(len(series) == 1 and series[0]["foreignId"] == str(self.ids[0]), "Generated root was not added with its native identity")
        rows = self.records()[start:]
        require(not any(row.get("kind") == "relations" for row in rows), "Grouping re-fetched available complete account relations")
        require(any(row.get("kind") == "metadata" for row in rows), "Metadata resolution was not exercised")
        require(series[0].get("animeGroupId") == "anilist:" + str(self.ids[0]), "Scoped official relation group was not resolved")
        self.observations["publicRelations"] = {"rootImported": self.ids[0], "unselectedCustomOnlyNeighbor": self.ids[1],
                                                "groupRelationHttpRequests": 0, "fullMetadataStillFetched": True}
        self.check("grouping consumes public relations from an unselected custom-only entry without another relation request")


class MalSnapshots(SnapshotChecks, _mal.MyAnimeListReliability):
    source, implementation, id_column = "myAnimeList", "MyAnimeListImport", "MalId"
    ids = _mal.IDS
    request_kind, page_field, expected_pages = "account-list", "offset", [0, 2]
    expected_a, expected_b = list(_mal.IDS[:2]), [_mal.IDS[3]]
    include_field, test_include = "includedTags", "Future"
    selections = [("A", {"includedTags": ["Include", "Pinned"]}), ("B", {"includedTags": ["Future"]})]
    common_values = {"username": "generated-public-account", "clientId": _mal.CLIENT_ID, "clientSecret": "",
                     "accessToken": "", "refreshToken": "", "listStatuses": [1, 5], "excludedTags": ["Never", "Skip"],
                     "importFinished": True, "importAiring": True, "importUnreleased": True, "minimumScore": 5}
    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"suite": "mal", "failure": None, "page2Quota": None, "quotaEpoch": "snapshot-proof"}
    def set_failure(self, fail):
        self.control(failure="unavailable" if fail else None)
    def change_credentials(self):
        resource = self.api_json("/api/v5/importlist/" + str(self.definitions[1]["id"]))
        self.field(resource, "accessToken")["value"] = _mal.ACCESS
        self.field(resource, "refreshToken")["value"] = _mal.REFRESH
        self.field(resource, "expires")["value"] = "2099-01-01T00:00:00Z"
        self.api_json("/api/v5/importlist/" + str(resource["id"]), "PUT", resource)
        self.idle()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:verified")
    parser.add_argument("--mock-image", default=MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(keep=False, anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        control = json.loads((args.serve_fixture / "control.json").read_text())
        (_mal.serve_fixture if control["suite"] == "mal" else serve_anilist)(args.serve_fixture)
        return 0
    checks, suites, logs, error = [], {}, [], None
    image_id, runtime, labels, cleaned = None, {}, {}, True
    for suite_type in (AniListSnapshots, MalSnapshots):
        audit = suite_type(args)
        try:
            audit.start()
            image_id, runtime, labels = audit.image_id, audit.runtime_metadata, audit.image_labels
            audit.prepare()
            runtime = audit.runtime_metadata
            audit.setup()
            audit.verify_reuse()
            if isinstance(audit, AniListSnapshots):
                audit.verify_nullable_collection()
            audit.verify_test_and_preferences()
            audit.verify_failure()
            audit.verify_credentials()
            if isinstance(audit, AniListSnapshots):
                audit.verify_relations()
            audit.no_acquisition()
        except (Exception, KeyboardInterrupt) as caught:
            error = f"{audit.source}: {audit.last_request}: {caught or type(caught).__name__}"
            print("FAIL " + error, file=sys.stderr)
        finally:
            try:
                suites[audit.source] = {"checks": audit.results, "observations": audit.observations,
                                       "mockImageId": audit.mock_id,
                                       "fixtureConfigOwnership": audit.fixture_config_ownership,
                                       "fixtureRequests": audit.records() if audit.root else []}
                if audit.container:
                    log = audit.docker("logs", audit.container)
                    for secret in (audit.api_key, "generated-reliability-access", "generated-reliability-refresh",
                                   "generated-snapshot-second-access", _mal.ACCESS, _mal.REFRESH,
                                   _mal.ROTATED_ACCESS, _mal.ROTATED_REFRESH):
                        log = log.replace(secret, "[REDACTED]")
                    logs.append(log)
                checks.extend(dict(item, name=audit.source + ": " + item["name"]) if isinstance(item, dict)
                              else audit.source + ": " + str(item) for item in audit.results)
                audit.cleanup()
            except Exception as caught:
                cleaned = False
                error = error or "Snapshot fixture cleanup/evidence failed: " + str(caught)
        if error:
            break
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({"createdUtc": utc_now(), "image": args.image, "imageId": image_id,
            "mockImage": args.mock_image,
            "sourceRevisionLabel": labels.get("org.opencontainers.image.revision"), "runtime": runtime,
            "passed": error is None, "checks": checks, "error": error, "suites": suites, "cleanupComplete": cleaned,
            "scope": {"trustedLocalTlsFixtures": True, "liveProviderTested": False, "privateAccountUsed": False,
                      "ownerConfigurationAccessed": False, "externalProviderRoutingEnabled": False,
                      "databaseWrites": "scheduling rows reset only in owned stopped fixtures", "downloadsTested": False}},
            indent=2) + "\n", encoding="utf-8")
        args.report.with_suffix(".log").write_text("\n".join(logs), encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
