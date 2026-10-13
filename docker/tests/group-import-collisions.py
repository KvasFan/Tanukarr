#!/usr/bin/env python3
"""Verify import-list group collisions against real SQLite using isolated Docker data."""

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import ssl
import sys
import threading
import time
import urllib.parse

from smoke import SmokeFailure, require


_spec = importlib.util.spec_from_file_location("anilist_reliability_fixture", Path(__file__).with_name("anilist-reliability.py"))
_reliability = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_reliability)
_groups = _reliability._groups

SEASON_ONE, SEASON_TWO, MOVIE_ONE, SLOW_ENTRY = 940201, 940202, 940205, 940220
OCCUPANT, DUPLICATE_SEASON, DUPLICATE_MOVIE = 940230, 940231, 940232
LIST_IDS = (SEASON_ONE, SEASON_TWO, MOVIE_ONE, SLOW_ENTRY)
GROUP_ID = "anilist:" + str(SEASON_ONE)
GROUP_TITLE = "Generated collision series"
CATALOGUE = {
    SEASON_ONE: (GROUP_TITLE, "TV", [("SEQUEL", SEASON_TWO)]),
    SEASON_TWO: (GROUP_TITLE + " Season 2", "TV", [("PREQUEL", SEASON_ONE), ("SEQUEL", MOVIE_ONE)]),
    MOVIE_ONE: (GROUP_TITLE + " Movie", "MOVIE", [("PREQUEL", SEASON_TWO)]),
    SLOW_ENTRY: ("Generated independent entry", "TV", []),
    OCCUPANT: ("Generated concurrent manual member", "TV", []),
    DUPLICATE_SEASON: ("Generated refused season", "TV", []),
    DUPLICATE_MOVIE: ("Generated refused movie", "MOVIE", []),
}


def media_resource(media_id):
    title, format_name, links = CATALOGUE[media_id]
    resource = _reliability.media_resource(media_id)
    resource.update(format=format_name, episodes=1 if format_name == "MOVIE" else 2,
                    title={"english": title, "romaji": title, "userPreferred": title}, coverImage={},
                    relations={"edges": [{"relationType": kind, "node": {
                        "id": target, "type": "ANIME", "format": CATALOGUE[target][1]}}
                        for kind, target in links]})
    resource["airingSchedule"]["nodes"] = resource["airingSchedule"]["nodes"][:resource["episodes"]]
    return resource


def serve_fixture(root):
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def answer(self, status, value):
            body = json.dumps(value).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("X-RateLimit-Limit", "30")
            self.send_header("X-RateLimit-Remaining", "29")
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
                if "mediaList(userName:" in query:
                    require(variables.get("id") == "generated-reliability-account" and variables.get("page") == 1,
                            "Unexpected fixture list query")
                    rows = [{"status": "CURRENT", "progress": 0, "media": media_resource(media_id),
                             "customLists": {"Include": True}, "customListEntries": [{"name": "Include", "enabled": True}]}
                            for media_id in LIST_IDS]
                    record.update(kind="list", page=1)
                    value = {"data": {"Page": {"pageInfo": {"currentPage": 1, "hasNextPage": False}, "mediaList": rows}}}
                elif "Media(" in query:
                    media_id = int(variables["id"])
                    require(media_id in CATALOGUE, "Unknown synthetic media ID")
                    kind = "metadata" if "airingSchedule" in query else "relations"
                    record.update(kind=kind, mediaId=media_id)
                    control = json.loads((root / "control.json").read_text(encoding="utf-8"))
                    if kind == "metadata" and media_id == control.get("holdMetadataId") and not control.get("releaseGate"):
                        self.record(dict(record, kind="metadata-gate-entered", status=None))
                        deadline = time.monotonic() + 60
                        while not json.loads((root / "control.json").read_text(encoding="utf-8")).get("releaseGate"):
                            require(time.monotonic() < deadline, "Metadata gate was not released")
                            time.sleep(0.05)
                        record["gateReleased"] = True
                    value = {"data": {"Media": media_resource(media_id)}}
                else:
                    raise SmokeFailure("Unsupported synthetic GraphQL request")
                record["status"] = 200
                self.answer(200, value)
            except (KeyError, TypeError, ValueError, SmokeFailure):
                self.answer(400, {"errors": [{"message": "Unsupported local collision fixture request"}]})
            finally:
                self.record(record)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local group collision TLS fixture ready", flush=True)
    server.serve_forever()


class GroupImportCollisions(_reliability.AniListReliability):
    fixture_script = "group-import-collisions.py"
    fixture_ready_message = "Local group collision TLS fixture ready"

    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"holdMetadataId": SLOW_ENTRY, "releaseGate": False}
        self.occupant = None
        self.occupant_before = None

    def add_resource(self, media_id, mode="disabled", member_kind="season", number=1, expected_status=None):
        title = CATALOGUE[media_id][0]
        resource = {"title": title, "metadataSource": "aniList", "foreignId": str(media_id), "tvdbId": 0,
                    "rootFolderPath": "/media/library", "qualityProfileId": self.profile_id,
                    "language": {"id": 1, "name": "English"}, "seasonType": "default",
                    "monitored": False, "seasonFolder": True, "seriesType": "anime", "animeGroupingMode": mode,
                    "seasons": [{"seasonNumber": 1, "monitored": False}],
                    "addOptions": {"monitor": "none", "searchForMissingEpisodes": False}}
        if mode == "manual":
            resource.update(animeGroupId=GROUP_ID, animeGroupTitle=GROUP_TITLE, animeGroupMemberKind=member_kind,
                            animeGroupKind="series", animeGroupSeasonNumber=number if member_kind == "season" else None,
                            animeGroupMovieNumber=number if member_kind == "movie" else None,
                            animeGroupMovieFolderStyle="title")
        status, raw = self.raw("/api/v5/series", "POST", resource)
        if expected_status is not None:
            require(status == expected_status, f"Expected addition HTTP {expected_status}, received {status}")
            return None
        require(200 <= status < 300, f"Synthetic series addition returned HTTP {status}: {raw[:500]!r}")
        return json.loads(raw)

    def prime_occupant_metadata(self):
        # The request coordinator serializes AniList HTTP calls. Warm the public
        # metadata cache so the concurrent add can commit while another call waits.
        resource = self.add_resource(OCCUPANT)
        self.idle()
        self.api_json(f"/api/v5/series/{resource['id']}?deleteFiles=false&addImportListExclusion=false", "DELETE")
        self.idle()
        require(self.library_ids() == [], "Metadata priming left a stored member")
        require(sum(r.get("kind") == "metadata" and r.get("mediaId") == OCCUPANT for r in self.records()) == 1,
                "Concurrent-add metadata was not fetched exactly once before the gate")
        self.check("public metadata primed without retaining any member or import exclusion")

    def create_list(self):
        resource = next(item for item in self.api_json("/api/v5/importlist/schema") if item["implementation"] == "AniListImport")
        resource.update(name="Generated collision list", rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                        language={"id": 1, "name": "English"}, metadataSource="aniList", enableAutomaticAdd=True,
                        shouldMonitor="none", searchForMissingEpisodes=False, seriesType="anime", seasonFolder=True)
        values = {"username": "generated-reliability-account", "accessToken": "generated-reliability-access",
                  "refreshToken": "generated-reliability-refresh", "expires": "2099-01-01T00:00:00Z",
                  "includedCustomLists": ["Include"], "excludedCustomLists": [], "customList": "",
                  "importCurrent": True, "importPlanning": False, "importCompleted": False, "importDropped": False,
                  "importPaused": False, "importRepeating": False, "importFinished": True, "importReleasing": False,
                  "importUnreleased": False, "importCancelled": False, "importHiatus": False,
                  "excludeCurrent": False, "excludePlanning": False, "excludeCompleted": False,
                  "excludeDropped": False, "excludePaused": False, "excludeRepeating": False}
        fields = {field["name"]: field for field in resource["fields"]}
        require(all(key in fields for key in values), "AniList schema omitted fixture settings")
        for key, value in values.items():
            fields[key]["value"] = value
        self.definition = self.api_json("/api/v5/importlist", "POST", resource)
        self.idle()
        self.check("synthetic import list saved with automatic adds and acquisition disabled")

    def group_snapshot(self, series):
        return {key: series.get(key) for key in ("id", "foreignId", "path", "tvdbId", "metadataSource", *_groups.GROUP_FIELDS)}

    def command_state(self, command):
        state = {}
        def terminal():
            nonlocal state
            state = self.api_json("/api/v5/command/" + str(command["id"]))
            return state["status"] in ("completed", "failed", "aborted")
        self.until(terminal, "ImportListSync did not settle after releasing metadata")
        return state

    def race(self):
        command = self.api_json("/api/v5/command", "POST", {"name": "ImportListSync"})
        self.until(lambda: any(r.get("kind") == "metadata-gate-entered" for r in self.records()),
                   "Batch did not reach the metadata gate after preparing group members")
        for media_id in (SEASON_ONE, SEASON_TWO, MOVIE_ONE):
            require(any(r.get("kind") == "metadata" and r.get("mediaId") == media_id and r["status"] == 200
                        for r in self.records()), "Gate opened before earlier metadata completed")
        require(self.library_ids() == [], "Prepared members were inserted before the race")
        self.occupant = self.add_resource(OCCUPANT, "manual")
        require(self.occupant["animeGroupId"] == GROUP_ID and self.occupant["animeGroupSeasonNumber"] == 1,
                "Concurrent manual member did not occupy the prepared season slot")
        self.occupant_before = self.group_snapshot(self.occupant)
        self.observations["concurrentOccupant"] = self.occupant_before
        require(sum(r.get("kind") == "metadata" and r.get("mediaId") == OCCUPANT for r in self.records()) == 1,
                "Concurrent add escaped the warmed metadata cache")
        self.check("manual member commits into season 1 after the list batch prepared that slot")
        self.control(releaseGate=True)
        state = self.command_state(command)
        self.idle()
        logs = self.docker("logs", self.container)
        self.observations["firstSync"] = {"commandStatus": state["status"], "libraryIds": self.library_ids(),
                                           "membershipIds": self.stored_ids()}
        require(self.group_snapshot(self.get(self.occupant)) == self.occupant_before,
                "Conflict moved, reassigned or replaced the stored manual member")
        require(self.stored_ids() == sorted(LIST_IDS), "Conflict modified the authoritative saved list")
        if self.args.expect_old_failure:
            require(state["status"] == "failed" and self.library_ids() == [OCCUPANT],
                    "Baseline did not fail atomically on the occupied prepared slot")
            require("UNIQUE constraint failed: Series.AnimeGroupId, Series.AnimeGroupMemberKind, Series.AnimeGroupSeasonNumber" in logs,
                    "Baseline failed without the reported SQLite group-slot constraint")
            self.check("old image reproduces the exact SQLite group-season UNIQUE failure and loses all prepared insertions")
            return
        require(state["status"] == "completed", "ImportListSync failed on a concurrently occupied group slot")
        require(self.library_ids() == sorted((SEASON_TWO, MOVIE_ONE, SLOW_ENTRY, OCCUPANT)),
                "Conflict did not defer only the occupied member while preserving valid insertions")
        require(re.search(r"Import List Sync Completed\. Items found: 4, Series added: 3[ \t]*\r?$", logs, re.MULTILINE),
                "Completion did not report the three actual persisted additions")
        require("UNIQUE constraint failed:" not in logs and "Error occurred while executing task ImportListSync" not in logs,
                "The repaired flow still emitted a SQLite import failure")
        self.check("occupied season is deferred while three valid prepared entries commit and actual added count is 3")
        self.check("successful complete list snapshot and stored manual group membership remain unchanged")

        before = sum(r.get("kind") == "list" for r in self.records())
        self.command({"name": "ImportListSync"})
        self.idle()
        require(self.library_ids() == sorted((SEASON_TWO, MOVIE_ONE, SLOW_ENTRY, OCCUPANT)) and self.stored_ids() == sorted(LIST_IDS),
                "Retry duplicated, discarded or overwrote list/group entries")
        require(sum(r.get("kind") == "list" for r in self.records()) == before,
                "Unchanged complete list retry refetched membership")
        logs = self.docker("logs", self.container)
        require(re.search(r"Import List Sync Completed\. Items found: 4, Series added: 0[ \t]*\r?$", logs, re.MULTILINE),
                "Retry completion did not report zero actual persisted additions")
        require("UNIQUE constraint failed:" not in logs, "Repeated sync emitted another SQLite group-slot failure")
        self.check("unchanged cached-list retry completes without duplicate inserts, a new fetch or a SQLite error")

    def identities_and_constraints(self):
        members = {int(s["foreignId"]): s for s in self.api_json("/api/v5/series")}
        second, movie = members[SEASON_TWO], members[MOVIE_ONE]
        require(second["animeGroupId"] == movie["animeGroupId"] == GROUP_ID and
                second["animeGroupSeasonNumber"] == 2 and movie["animeGroupMovieNumber"] == 1 and
                movie.get("animeGroupSeasonNumber") is None and self.occupant["animeGroupSeasonNumber"] == 1,
                "Season and movie ordinal namespaces were collapsed or renumbered")
        for media_id in (SEASON_TWO, MOVIE_ONE, SLOW_ENTRY, OCCUPANT):
            expected = 1 if media_id == MOVIE_ONE else 2
            self.until(lambda: len(self.episodes(members[media_id])) == expected, "Imported native episodes did not initialize")
            identity = self.episode_identity(members[media_id])
            require(all(row[2] == 1 and row[3] == row[4] for row in identity), "Group numbering replaced native S01/absolute episodes")
            self.episode_snapshots[media_id] = identity
        self.check("season 1 and movie 1 coexist while native series IDs and S01 episode numbers remain separate")
        self.add_resource(DUPLICATE_SEASON, "manual", "season", 2, expected_status=400)
        self.add_resource(DUPLICATE_MOVIE, "manual", "movie", 1, expected_status=400)
        require(DUPLICATE_SEASON not in self.library_ids() and DUPLICATE_MOVIE not in self.library_ids(),
                "An occupied manual ordinal was silently persisted")
        self.check("manual same-kind occupied season and movie slots return HTTP 400 without persistence")
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config", "Database inspection escaped the owned fixture")
        uri = "file:" + urllib.parse.quote(str(path), safe="/") + "?mode=ro"
        with sqlite3.connect(uri, uri=True, timeout=10) as connection:
            connection.execute("PRAGMA query_only = ON")
            statements = [row[0] for row in connection.execute("SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Series'") if row[0]]
            integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
        for ordinal in ("AnimeGroupSeasonNumber", "AnimeGroupMovieNumber"):
            require(any("UNIQUE INDEX" in sql.upper() and "AnimeGroupId" in sql and "AnimeGroupMemberKind" in sql and ordinal in sql
                        for sql in statements), "Required typed ordinal UNIQUE index was removed")
        require(integrity == "ok", "Owned SQLite integrity check failed")
        self.observations["typedUniqueIndexesRetained"] = True
        self.check("real SQLite retains unique season/movie membership indexes and passes integrity_check")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=_reliability.MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true", help="Keep only this disposable fixture for inspection")
    parser.add_argument("--expect-old-failure", action="store_true", help="Assert the unrepaired image reproduces the atomic SQLite failure")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    require(args.timeout > 0, "--timeout must be positive")
    audit = GroupImportCollisions(args)
    error, records, logs = None, [], ""
    cleanup_complete = False
    def redact(value):
        for secret in (audit.api_key, "generated-reliability-access", "generated-reliability-refresh"):
            value = value.replace(secret, "[REDACTED]")
        return re.sub(r"with the password: \S+", "with the password: [REDACTED]", value)
    try:
        audit.start()
        audit.prepare()
        audit.prime_occupant_metadata()
        audit.create_list()
        audit.race()
        if not args.expect_old_failure:
            audit.identities_and_constraints()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as caught:
        error = redact(f"{audit.last_request}: {caught or type(caught).__name__}")
        print("FAIL " + error, file=sys.stderr)
    finally:
        try:
            if audit.root:
                audit.control(releaseGate=True)
                records = audit.records()
            if audit.container:
                logs = redact(audit.docker("logs", audit.container))
        except Exception as caught:
            error = error or redact("Could not capture collision fixture evidence: " + str(caught))
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
                "expectedOldFailure": args.expect_old_failure, "observations": audit.observations,
                "nativeEpisodeIdentities": audit.episode_snapshots, "fixtureRequests": records,
                "scope": {"trustedLocalAniListMock": True, "liveAniListTested": False, "privateAccountUsed": False,
                          "realLibraryTested": False, "downloadsTested": False, "tlsValidationDisabled": False,
                          "externalProviderRoutingEnabled": False, "mainConfigurationAccessed": False,
                          "databaseInspection": "read-only, owned disposable configuration only"},
                "cleanupComplete": cleanup_complete,
                "keptData": str(audit.root) if args.keep and audit.root else None,
            }, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
