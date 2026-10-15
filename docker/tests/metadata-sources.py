#!/usr/bin/env python3
"""Verify packaged metadata-source policy and native TVmaze updates on isolated TLS catalogues."""

import argparse
from contextlib import closing
import copy
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import sqlite3
import ssl
import sys
import threading
import urllib.parse

from smoke import ContainerSmoke, require


_spec = importlib.util.spec_from_file_location("metadata_sources_parent", Path(__file__).with_name("anilist-reliability.py"))
_parent = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_parent)
IDS = (980701, 980702)
SOURCES = ["tvdb", "tmdb", "aniList", "myAnimeList", "tvMaze", "tmdbMovie", "omdb"]
TITLE = "Generated native metadata"


def show_resource(identity, revision):
    return {"id": identity, "name": TITLE + " " + str(identity), "type": "Scripted", "language": "English",
            "status": "Ended", "summary": "<p>Generated metadata revision " + str(revision) + "</p>",
            "premiered": "2024-01-01", "ended": "2024-01-08", "averageRuntime": 24,
            "genres": ["Drama"], "externals": {"thetvdb": None, "tvrage": None, "imdb": None},
            "image": None, "rating": {"average": 8.1}}


def episode_resources(identity):
    return [{"id": identity * 10 + number, "name": "Generated native episode " + str(number),
             "season": 1 if number != 2 else 2, "number": 1 if number != 3 else None,
             "type": "regular" if number != 3 else "significant_special",
             "airdate": "2024-01-0" + str(number), "airstamp": "2024-01-0" + str(number) + "T20:00:00+00:00",
             "runtime": 24, "summary": "<p>Generated episode overview</p>"} for number in (1, 2, 3)]


def ani_resource():
    return {"id": IDS[0], "idMal": None, "type": "ANIME", "format": "TV",
            "title": {"english": TITLE, "romaji": TITLE, "userPreferred": TITLE},
            "episodes": 2, "duration": 24, "status": "FINISHED", "genres": [], "synonyms": [],
            "countryOfOrigin": "JP", "description": "Generated native AniList entry",
            "startDate": {"year": 2024, "month": 1, "day": 1}, "endDate": {"year": 2024, "month": 1, "day": 8},
            "coverImage": {}, "bannerImage": None, "studios": {"nodes": []}, "relations": {"edges": []}}


def serve_fixture(root):
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def answer(self, status, value, record):
            body = json.dumps(value).encode("utf-8")
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(dict(record, status=status)) + "\n")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            record = {"host": self.headers.get("Host", "").split(":")[0], "path": parsed.path, "method": "GET"}
            try:
                require(record["host"] == "api.tvmaze.com" and not self.headers.get("Authorization"),
                        "Unexpected catalogue host or private authorization")
                control = json.loads((root / "control.json").read_text(encoding="utf-8"))
                if parsed.path == "/search/shows":
                    require(urllib.parse.parse_qs(parsed.query).get("q", [""])[0].startswith(TITLE), "Unexpected title query")
                    record["kind"] = "tvmaze-search"
                    self.answer(200, [{"score": 1.0, "show": show_resource(IDS[0], control["revision"])}], record)
                    return
                parts = parsed.path.strip("/").split("/")
                require(len(parts) in (2, 3) and parts[0] == "shows" and int(parts[1]) in IDS, "Unexpected TVmaze native ID")
                identity = int(parts[1])
                record.update(nativeId=identity, revision=control["revision"])
                if len(parts) == 2:
                    record["kind"] = "tvmaze-show"
                    self.answer(200, show_resource(identity, control["revision"]), record)
                    return
                require(parts[2] == "episodes" and urllib.parse.parse_qs(parsed.query).get("specials") == ["1"],
                        "The complete episode list must include specials")
                record.update(kind="tvmaze-episodes", malformed=control["malformed"])
                episodes = episode_resources(identity)
                if control.get("reverseNumbering"):
                    for episode in episodes[:2]:
                        episode["season"] = 3 - episode["season"]
                    record["reverseNumbering"] = True
                if control["malformed"]:
                    episodes.append(copy.deepcopy(episodes[0]))
                self.answer(200, episodes, record)
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

        def do_POST(self):
            record = {"host": self.headers.get("Host", "").split(":")[0], "path": self.path, "method": "POST"}
            try:
                require(record["host"] == "graphql.anilist.co" and self.path == "/" and not self.headers.get("Authorization"),
                        "Unexpected AniList route or private authorization")
                length = int(self.headers.get("Content-Length", "0"))
                require(0 < length < 65536, "Unexpected GraphQL body size")
                request = json.loads(self.rfile.read(length))
                require(request["variables"].get("search", "").startswith(TITLE) and "media(search:" in request["query"],
                        "Only the selected public native title query is supported")
                record["kind"] = "anilist-search"
                self.answer(200, {"data": {"Page": {"media": [ani_resource()]}}}, record)
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local metadata-source TLS fixture ready", flush=True)
    server.serve_forever()


class MetadataSources(_parent.AniListReliability):
    fixture_script = "metadata-sources.py"
    fixture_ready_message = "Local metadata-source TLS fixture ready"
    fixture_hosts = ("api.tvmaze.com", "graphql.anilist.co")
    fixture_startup_check = "isolated trusted TVmaze and AniList TLS, internal routing and loopback-only application API"

    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"malformed": False, "revision": 1, "reverseNumbering": False}

    @staticmethod
    def settings_path(version):
        return "/api/v3/config/metadatasource" if version == "v3" else "/api/v5/settings/metadata"

    def read_database(self, query, parameters=()):
        database = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(database.parent == self.root / "config" and self.root.name.startswith("sonarr-container-smoke-anilist-reliability-"),
                "Read-only configuration inspection escaped the disposable fixture")
        uri = "file:" + urllib.parse.quote(str(database), safe="/") + "?mode=ro"
        with closing(sqlite3.connect(uri, uri=True, timeout=10)) as connection:
            connection.execute("PRAGMA query_only = ON")
            return connection.execute(query, parameters).fetchall()

    def stored_key(self):
        rows = self.read_database("SELECT Value FROM Config WHERE lower(Key) = ?", ("tmdbapikey",))
        return rows[0][0] if rows else None

    def episode_identity(self, series):
        # The public episode resource omits provider ForeignId; inspect only this
        # fixture's owned SQLite rows to verify the persisted native identity.
        return sorted(self.read_database("SELECT Id, ForeignId, SeasonNumber, EpisodeNumber, TvdbId FROM Episodes WHERE SeriesId = ?", (series["id"],)))

    def save_policy(self, order, disabled, version="v5"):
        path = self.settings_path(version)
        settings = self.api_json(path)
        settings.update(metadataSourceOrder=order, disabledMetadataSources=disabled)
        self.api_json(path + "/1", "PUT", settings)

    def policy(self):
        for version in ("v3", "v5"):
            path = self.settings_path(version)
            settings = self.api_json(path)
            require(set(settings["metadataSourceOrder"]) == set(SOURCES), "Fresh policy omitted a native source")
            settings.update(tmdbApiKey=self.metadata_key, metadataSourceOrder=["tvMaze", "aniList", "tvdb", "tmdb", "myAnimeList"],
                            disabledMetadataSources=["tvdb", "tmdb", "myAnimeList", "tmdbMovie", "omdb"])
            self.api_json(path + "/1", "PUT", settings)
            saved = self.api_json(path)
            require(saved["tmdbApiKey"] == "********" and self.metadata_key not in json.dumps(saved), "Metadata credentials leaked through settings")
            self.api_json(path + "/1", "PUT", saved)
            require(self.stored_key() == self.metadata_key, "A masked settings save replaced the real credential")
            self.check(version + " saves source order and enabled sources while masking and preserving the generated TMDB credential")

            before = self.api_json(path)
            self.api_json(path + "/1", "PUT", {"id": 1})
            require(self.api_json(path) == before and self.stored_key() == self.metadata_key,
                    "An older client's omitted credential or policy reset saved settings")
            self.check(version + " preserves omitted policy and credential fields from older clients")

            for field, value in (("disabledMetadataSources", SOURCES), ("metadataSourceOrder", ["tvMaze", "tvMaze"]),
                                 ("metadataSourceOrder", ["unknownSource"])):
                invalid = copy.deepcopy(before)
                invalid.update({field: value, "tmdbApiKey": "rejected-generated-replacement"})
                require(self.raw(path + "/1", "PUT", invalid)[0] == 400, "Invalid metadata policy was accepted")
                require(self.api_json(path) == before and self.stored_key() == self.metadata_key,
                        "Rejected metadata policy partially changed saved configuration")
            self.check(version + " rejects disabling every source, duplicate order and unknown sources without partial settings mutation")

            before_requests = len(self.records())
            query = urllib.parse.urlencode({"term": TITLE, "metadataSource": "tmdb"})
            require(self.raw(f"/api/{version}/series/lookup?" + query)[0] == 400, "Disabled explicit source was searchable")
            require(len(self.records()) == before_requests, "A disabled-source lookup contacted a provider")
            self.check(version + " rejects an explicit disabled source before any catalogue request")

    def ordered_search(self):
        for version, order in (("v3", ["tvMaze", "aniList"]), ("v5", ["aniList", "tvMaze"])):
            self.save_policy(order, ["tvdb", "tmdb", "myAnimeList", "tmdbMovie", "omdb"], version)
            before = len(self.records())
            query = urllib.parse.urlencode({"term": TITLE, "allSources": "true"})
            rows = self.api_json(f"/api/{version}/series/lookup?" + query)
            require([(row.get("metadataSource"), row.get("foreignId")) for row in rows] == [(source, str(IDS[0])) for source in order],
                    "Multi-source lookup changed saved order or collapsed equal numeric IDs from different catalogues")
            search_calls = [row["kind"].split("-")[0] for row in self.records()[before:] if row.get("kind", "").endswith("-search")]
            require(search_calls == [source.lower() for source in order], "Actual catalogue request order differs from saved source order")
            self.check(version + " all-source lookup follows saved request and result order while retaining separate native catalogue identities")
        self.observations["sameNumericNativeIds"] = {"aniList": IDS[0], "tvMaze": IDS[0], "collapsed": False}

    def refresh(self, series, expect_valid=True):
        before = len(self.records())
        command = self.api_json("/api/v5/command", "POST", {"name": "RefreshSeries", "seriesId": series["id"]})
        state = {}

        def terminal():
            nonlocal state
            state = self.api_json("/api/v5/command/" + str(command["id"]))
            return state["status"] in ("completed", "failed", "aborted")

        self.until(terminal, "Native TVmaze refresh did not finish")
        if expect_valid:
            require(state["status"] == "completed", "Valid native TVmaze refresh failed")
        require(any(row.get("kind") == "tvmaze-episodes" and row.get("nativeId") == int(series["foreignId"])
                    for row in self.records()[before:]), "Refresh did not read the actual native TVmaze episode endpoint")

    def native_series(self):
        self.save_policy(["tvMaze", "aniList"], ["tvdb", "tmdb", "myAnimeList", "tmdbMovie", "omdb"])
        for version, identity in zip(("v3", "v5"), IDS):
            query = urllib.parse.urlencode({"term": "tvmaze:" + str(identity), "metadataSource": "tvMaze"})
            row = self.api_json(f"/api/{version}/series/lookup?" + query)[0]
            require(row["metadataSource"] == "tvMaze" and row["foreignId"] == str(identity) and row["tvdbId"] == 0,
                    "TVmaze lookup used its native ID as a TVDB ID")
            row.update(rootFolderPath="/media/library", qualityProfileId=self.profile_id, monitored=False, seasonFolder=True,
                       seriesType="standard", animeGroupingMode="disabled", addOptions={"monitor": "none", "searchForMissingEpisodes": False})
            row.pop("path", None)
            added = self.api_json(f"/api/{version}/series", "POST", row)
            self.until(lambda: len(self.episodes(added)) == 3, "TVmaze regular and special episode rows did not initialize")
            saved = self.get(added)
            require(saved["metadataSource"] == "tvMaze" and saved["foreignId"] == str(identity) and saved["tvMazeId"] == identity
                    and saved["tvdbId"] < 0, "TVmaze addition lost native identity or invented a positive TVDB ID")
            episodes = self.episodes(saved)
            self.observations.setdefault("nativeAddSnapshots", []).append({"version": version, "foreignId": str(identity),
                "storedEpisodeIdentities": self.episode_identity(saved),
                "apiNumbering": sorted((item["seasonNumber"], item["episodeNumber"]) for item in episodes)})
            require({item[1] for item in self.episode_identity(saved)} == {str(identity * 10 + number) for number in (1, 2, 3)}
                    and sorted((item["seasonNumber"], item["episodeNumber"]) for item in episodes) == [(0, 1), (1, 1), (2, 1)],
                    "TVmaze episodes or specials lost their native IDs or seasonal numbering")
            require(all(item["title"].startswith("Generated native episode ") and item["airDate"].startswith("2024-01-") for item in episodes),
                    "Native episode titles or air dates were discarded")
            self.series[identity] = saved
            self.check(version + " adds TVmaze titles, native episode IDs, air dates, native seasons and deterministic specials without TVDB ID substitution")

        self.control(revision=2)
        self.save_policy(["aniList"], ["tvMaze", "tvdb", "tmdb", "myAnimeList", "tmdbMovie", "omdb"])
        snapshots = {}
        for identity, series in self.series.items():
            before = self.episode_identity(series)
            self.refresh(series)
            saved = self.get(series)
            require(saved["overview"] == "Generated metadata revision 2" and self.episode_identity(saved) == before
                    and all(saved[key] == series[key] for key in ("metadataSource", "foreignId", "tvdbId", "path", "tvMazeId")),
                    "Disabling a search source changed native refresh ownership, IDs, paths or episode rows")
            snapshots[identity] = {"series": saved, "episodes": self.episode_identity(saved)}
        self.check("disabling TVmaze search preserves existing native ownership and still refreshes both v3/v5 additions from their owning catalogue")

        self.control(malformed=True, revision=3)
        for identity, snapshot in snapshots.items():
            self.refresh(snapshot["series"], expect_valid=False)
            saved = self.get(snapshot["series"])
            require(self.episode_identity(saved) == snapshot["episodes"] and saved["overview"] == "Generated metadata revision 2"
                    and all(saved[key] == snapshot["series"][key] for key in ("metadataSource", "foreignId", "tvdbId", "path", "tvMazeId")),
                    "Malformed TVmaze episode response replaced previous successful metadata or episode rows")
        self.check("malformed complete TVmaze episode lists retain previous metadata, ownership, paths and every saved episode row")
        self.control(malformed=False, revision=2, reverseNumbering=True)
        for identity, snapshot in snapshots.items():
            previous_rows = {row[1]: row for row in snapshot["episodes"]}
            self.refresh(snapshot["series"])
            current_rows = self.episode_identity(snapshot["series"])
            require({row[1]: row[0] for row in current_rows} == {native: row[0] for native, row in previous_rows.items()},
                    "A native provider correction recreated or swapped persisted episode identities")
            require(all(row[2] == (3 - previous_rows[row[1]][2] if previous_rows[row[1]][2] > 0 else 0) for row in current_rows),
                    "The corrected native season numbering did not apply to the matching episode IDs")
            snapshot["episodes"] = current_rows
        self.check("native provider season-number corrections preserve the original persisted database row for each native episode ID")
        before = self.api_json(self.settings_path("v5"))
        self.docker("restart", self.container)
        self.wait_ready()
        require(self.api_json(self.settings_path("v5")) == before and self.stored_key() == self.metadata_key,
                "Restart lost the saved source policy or preserved credential")
        for identity, snapshot in snapshots.items():
            saved = self.get(snapshot["series"])
            require(self.episode_identity(saved) == snapshot["episodes"] and saved["foreignId"] == str(identity), "Restart changed native library identity")
        self.check("a fresh application process retains saved policy, masked credential and native episode identities")
        self.observations["nativeLibrary"] = [{"metadataSource": "tvMaze", "foreignId": str(identity),
                                                "tvdbPlaceholder": snapshot["series"]["tvdbId"], "episodeIdentities": snapshot["episodes"]}
                                               for identity, snapshot in snapshots.items()]
        self.observations["preservedCredentialSha256"] = hashlib.sha256(self.metadata_key.encode()).hexdigest()
        self.observations["nativeEpisodeIdsReadFromOwnedDatabase"] = True
        require(all(row["status"] == 200 for row in self.records()), "Unexpected TLS catalogue route or fixture request occurred")


def live_tvmaze(args):
    """Separate opt-in evidence; never contacts public providers in mandatory CI."""
    live_args = copy.copy(args)
    live_args.keep = False
    audit = ContainerSmoke(live_args)
    error, cleanup, observations = None, False, {}
    try:
        audit.start()
        query = urllib.parse.urlencode({"term": "The Expanse", "metadataSource": "tvMaze"})
        rows = audit.api("/api/v5/series/lookup?" + query)
        match = next((row for row in rows if row["title"] == "The Expanse"), None)
        require(match and match.get("metadataSource") == "tvMaze" and int(match["foreignId"]) == match["tvMazeId"] > 0,
                "The live TVmaze lookup did not retain a native public catalogue identity")
        require(audit.api("/api/v5/series") == [] and audit.api("/api/v5/queue")["totalRecords"] == 0
                and audit.api("/api/v5/indexer") == [] and audit.api("/api/v5/downloadclient") == [],
                "The read-only public lookup changed a disposable library or acquisition configuration")
        observations = {"publicCatalogue": "https://www.tvmaze.com", "title": match["title"],
                        "metadataSource": match["metadataSource"], "foreignId": match["foreignId"], "tvMazeId": match["tvMazeId"]}
        audit.check("actual packaged TVmaze provider performs one read-only public title lookup with native identity and an empty disposable library")
    except (Exception, KeyboardInterrupt) as caught:
        error = str(caught) or type(caught).__name__
        for secret in (audit.api_key, audit.metadata_key):
            error = error.replace(secret, "[REDACTED]")
        print("FAIL optional public TVmaze lookup: " + error, file=sys.stderr)
    finally:
        try:
            audit.cleanup()
            cleanup = True
        except Exception as caught:
            error = error or "Public lookup cleanup failed: " + str(caught)
        if args.report:
            public_report = args.report.with_name(args.report.stem + "-public-tvmaze.json")
            public_report.write_text(json.dumps({"createdUtc": _parent._groups.utc_now(), "image": args.image,
                "imageId": audit.image_id, "passed": error is None, "error": error, "checks": audit.results,
                "observations": observations, "cleanupComplete": cleanup,
                "scope": {"liveTVmazeReadOnlyLookup": True, "trustedLocalMockUsed": False, "realLibraryTested": False,
                          "privateAccountUsed": False, "seriesAdded": False, "downloadsTested": False,
                          "mainConfigurationAccessed": False}}, indent=2) + "\n", encoding="utf-8")
    return 1 if error else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=_parent.MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--live-tvmaze", action="store_true", help="Also run a separate disposable read-only lookup against public TVmaze; omitted in CI")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    require(args.timeout > 0, "--timeout must be positive")
    audit = MetadataSources(args)
    error, records, logs, cleanup = None, [], "", False
    try:
        audit.start()
        audit.prepare()
        audit.policy()
        audit.ordered_search()
        audit.native_series()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as caught:
        error = f"{audit.last_request}: {caught or type(caught).__name__}"
        for secret in (audit.api_key, audit.metadata_key):
            error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, file=sys.stderr)
    finally:
        try:
            if audit.root:
                records = audit.records()
            if audit.container:
                logs = audit.docker("logs", audit.container)
                for secret in (audit.api_key, audit.metadata_key):
                    logs = logs.replace(secret, "[REDACTED]")
        except Exception as caught:
            error = error or "Evidence capture failed: " + str(caught)
        try:
            audit.cleanup()
            cleanup = not args.keep
        except Exception as caught:
            error = error or "Fixture cleanup failed: " + str(caught)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({"createdUtc": _parent._groups.utc_now(), "image": args.image,
                "imageId": audit.image_id, "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                "runtime": audit.runtime_metadata, "mockImage": args.mock_image, "mockImageId": audit.mock_id,
                "passed": error is None, "error": error, "checks": audit.results, "observations": audit.observations,
                "fixtureRequests": records, "cleanupComplete": cleanup,
                "keptData": str(audit.root) if args.keep and audit.root else None,
                "scope": {"trustedLocalMetadataCatalogue": True, "livePublicCataloguesTested": False, "privateAccountUsed": False,
                          "realLibraryTested": False, "downloadsTested": False, "mainConfigurationAccessed": False,
                          "externalProviderRoutingEnabled": False, "tlsValidationDisabled": False}}, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    if error:
        return 1
    return live_tvmaze(args) if args.live_tvmaze else 0


if __name__ == "__main__":
    sys.exit(main())
