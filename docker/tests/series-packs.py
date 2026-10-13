#!/usr/bin/env python3
"""Verify native multi-season pack searches in disposable Docker data without acquisition."""

import argparse
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import re
import secrets
import sqlite3
import sys
import threading
import time
import urllib.parse
from xml.sax.saxutils import escape

from smoke import SmokeFailure, require


SCRIPTS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("pack_functional_security", SCRIPTS / "functional-security.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
TITLE = "Fixture Pack Show"
PACKS = [f"{TITLE}.S{season:02d}.1080p.WEB-DL.H264-Fixture" for season in (1, 2, 3)]
PACKS.append(f"{TITLE}.S01-S03.1080p.WEB-DL.H264-Fixture")
EPISODES = [f"{TITLE}.S{season:02d}E01.1080p.WEB-DL.H264-Fixture" for season in (1, 2, 3)]


def utc_now():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def serve_fixture(root):
    """A fixed synthetic RSS sidecar; never proxy a request or serve a torrent."""
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_arguments):
            pass

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            query = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
            record = {"utc": utc_now(), "path": parsed.path, "query": query, "status": 400}
            if len(self.path) > 2048 or parsed.path != "/" or query.get("page") != ["rss"]:
                self.send_response(400)
                self.send_header("Content-Length", "0")
                self.end_headers()
            else:
                term = query.get("term", [""])[0]
                match = re.fullmatch(re.escape(TITLE) + r"\s+s0([1-3])", term, re.IGNORECASE)
                season = int(match[1]) if match else None
                selected = [season - 1, 3, season + 3] if season else list(range(7))
                items = []
                for index in selected:
                    title = (PACKS + EPISODES)[index]
                    items.append(
                        f"<item><title>{escape(title)}</title>"
                        f'<guid isPermaLink="true">http://fixture-rss:8989/view/{index + 1}</guid>'
                        f"<link>http://fixture-rss:8989/download/{index + 1}.torrent</link>"
                        "<pubDate>Tue, 13 Oct 2026 09:00:00 +0000</pubDate>"
                        f"<nyaa:size>{4 if index < 4 else 1} GiB</nyaa:size>"
                        f"<nyaa:infoHash>{index + 1:040x}</nyaa:infoHash>"
                        "<nyaa:seeders>20</nyaa:seeders><nyaa:leechers>1</nyaa:leechers></item>")
                body = (
                    '<?xml version="1.0" encoding="UTF-8"?>'
                    '<rss version="2.0" xmlns:nyaa="https://nyaa.si/xmlns/nyaa"><channel>'
                    "<title>Disposable complete-series pack fixture</title><description>Synthetic metadata only</description>"
                    "<link>http://fixture-rss:8989/</link>" + "".join(items) + "</channel></rss>"
                ).encode("utf-8")
                record.update(status=200, suppliedTitles=[(PACKS + EPISODES)[index] for index in selected])
                self.send_response(200)
                self.send_header("Content-Type", "application/rss+xml; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                output.write(json.dumps(record) + "\n")

    print("Local native pack RSS fixture ready", flush=True)
    ThreadingHTTPServer(("0.0.0.0", 8989), Handler).serve_forever()


class SeriesPacks(module.FunctionalSecurity):
    def __init__(self, args):
        super().__init__(args)
        self.network = None
        self.mock = None
        self.mock_image_id = None
        self.fixture_root = None
        self.searches = []
        self.native_episodes = []
        self.image_labels = {}

    def start(self):
        super().start()
        status = self.json_api("/api/v5/system/status")
        self.runtime_metadata = {key: status.get(key) for key in
                                 ("productName", "productVersion", "version", "runtimeVersion", "migrationVersion")}
        self.image_labels = json.loads(self.docker("image", "inspect", self.args.image))[0]["Config"].get("Labels", {}) or {}
        require(self.json_api("/api/v5/series") == [] and self.json_api("/api/v5/downloadclient") == [],
                "Fixture unexpectedly starts with series or a download client")
        self.mock_image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.mock_image)
        suffix = secrets.token_hex(6)
        self.network = self.docker("network", "create", "tanukarr-native-packs-" + suffix)
        self.docker("network", "connect", self.network, self.container)
        self.fixture_root = self.root / "fixture"
        self.fixture_root.mkdir()
        self.mock = self.docker(
            "run", "--detach", "--name", "tanukarr-native-pack-rss-" + suffix,
            "--label", "tanukarr.native-pack-test=true", "--network", self.network,
            "--network-alias", "fixture-rss", "--env", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={SCRIPTS},target=/runner,readonly",
            "--mount", f"type=bind,source={self.fixture_root},target=/fixture",
            self.args.mock_image, "python", "-u", getattr(self, "fixture_script", "/runner/series-packs.py"), "--serve-fixture", "/fixture")
        deadline = time.monotonic() + self.args.timeout
        while "Local native pack RSS fixture ready" not in self.docker("logs", self.mock):
            require(time.monotonic() < deadline, "Local synthetic RSS did not become ready")
            time.sleep(0.2)
        details = json.loads(self.docker("inspect", self.mock))[0]
        require(not details["HostConfig"]["PortBindings"] and len(details["NetworkSettings"]["Networks"]) == 1,
                "RSS fixture unexpectedly publishes a port or joins another network")
        self.check("synthetic RSS sidecar stays on one owned network with no published ports or external proxy")

    @staticmethod
    def insert(db, table, values):
        for _, name, kind, required, default, primary in db.execute('PRAGMA table_info("' + table + '")'):
            if required and default is None and not primary and name not in values:
                values[name] = 0 if kind in ("INTEGER", "BOOLEAN") else ""
        columns = ",".join('"' + name + '"' for name in values)
        db.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({",".join("?" for _ in values)})', list(values.values()))

    def seed(self):
        self.json_api("/api/v5/rootfolder", "POST", {"path": "/media"})
        quality = self.json_api("/api/v5/qualityprofile")[0]["id"]
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            self.insert(db, "Series", dict(
                Id=1, TvdbId=900001, Title=TITLE, TitleSlug="isolated-native-pack-fixture",
                CleanTitle="fixturepackshow", SortTitle=TITLE.lower(), Path="/media/Fixture Pack Show",
                Images="[]", Monitored=1, SeasonFolder=1, Runtime=24, SeriesType=0, QualityProfileId=quality,
                Tags="[]", Added="2026-10-13 00:00:00", Genres="[]", Actors="[]",
                Seasons=json.dumps([{"seasonNumber": season, "monitored": True} for season in (1, 2, 3)]),
                Ratings='{"votes":0,"value":0}', OriginalLanguage=1, Language=1,
                MetadataSource=0, ForeignId="900001", AniListIds="[]", MalIds="[]", AlternateTitles="[]"))
            episode_id = 0
            for season, count in ((1, 2), (2, 3), (3, 1)):
                for number in range(1, count + 1):
                    episode_id += 1
                    self.insert(db, "Episodes", dict(
                        Id=episode_id, SeriesId=1, SeasonNumber=season, EpisodeNumber=number,
                        Title="Synthetic native TV episode", EpisodeFileId=0, Monitored=1,
                        Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                        AirDate="2024-01-01", AirDateUtc="2024-01-01 00:00:00"))
            db.execute("UPDATE ScheduledTasks SET LastExecution='2050-01-01 00:00:00', LastStartTime='2050-01-01 00:00:00'")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        series = self.json_api("/api/v5/series/1")
        require(series["seriesType"] == "standard" and series["metadataSource"] == "tvdb",
                "Fixture does not represent a native multi-season TV catalogue owner")
        self.native_episodes = self.episode_identity()
        require([item[1] for item in self.native_episodes] == [1, 1, 2, 2, 2, 3], "Native seasons/counts did not seed correctly")
        self.check("one native Standard TV identity contains seasons 1/2/3 and six unchanged episode records")

    def episode_identity(self):
        return sorted((item["id"], item["seasonNumber"], item["episodeNumber"])
                      for item in self.json_api("/api/v5/episode?seriesId=1"))

    def requests(self):
        file = self.fixture_root / "requests.jsonl"
        return [json.loads(line) for line in file.read_text(encoding="utf-8").splitlines() if line] if file.exists() else []

    def indexer(self):
        resource = next(item for item in self.json_api("/api/v5/indexer/schema") if item["implementation"] == "Nyaa")
        resource.update(name="Disposable native pack RSS", enableRss=False, enableAutomaticSearch=False,
                        enableInteractiveSearch=True, priority=25)
        values = {"baseUrl": "http://fixture-rss:8989", "additionalParameters": "&cats=1_0&filter=1",
                  "animeStandardFormatSearch": True}
        for field in resource["fields"]:
            if field["name"] in values:
                field["value"] = values[field["name"]]
        status, _, _ = self.raw("/api/v5/indexer/test", "POST", resource)
        require(status == 204, f"Native RSS indexer connection Test returned HTTP {status}")
        self.json_api("/api/v5/indexer?skipTesting=true", "POST", resource)
        self.check("native Nyaa parses only fixed local RSS and is configured for interactive search without acquisition")

    def search(self, version, season=None):
        path = f"/api/{version}/release?seriesId=1&" + (
            "completeSeries=true" if season is None else f"seasonNumber={season}&seasonPacksOnly=true")
        before = len(self.requests())
        started = time.monotonic()
        status, _, body = self.raw(path)
        elapsed = round(time.monotonic() - started, 3)
        require(status == 200, f"{path} returned HTTP {status}")
        entries = json.loads(body)
        require(isinstance(entries, list), "Pack search did not return an array")
        records = self.requests()[before:]
        terms = [record["query"].get("term", [""])[0] for record in records]
        require(0 < len(terms) <= 2 and all(not re.search(r"s\d+e\d+|\s\d+$", term, re.IGNORECASE) for term in terms),
                "Pack search performed an individual-episode query fanout or no actual fixture query")
        samples = []
        for entry in entries:
            release = entry.get("release", entry)
            parsed = entry.get("parsedInfo", entry)
            samples.append({"title": release.get("title"), "fullSeason": parsed.get("fullSeason"),
                            "seasonNumbers": parsed.get("seasonNumbers"), "mappedSeriesId": entry.get("mappedSeriesId"),
                            "mappedEpisodes": entry.get("mappedEpisodeInfo", []),
                            "rejections": entry.get("rejections", entry.get("decision", {}).get("rejections", []))})
        expected = set(PACKS) if season is None else {PACKS[season - 1], PACKS[3]}
        packs = {item["title"]: item for item in samples if item["fullSeason"]}
        require(expected.issubset(packs), "Pack search omitted a required single-season or multi-season pack")
        for title in expected:
            require(packs[title]["mappedSeriesId"] == 1 and packs[title]["mappedEpisodes"],
                    "A returned pack did not map onto the requested native series episodes")
        if season is not None:
            require(all(item["fullSeason"] for item in samples) and not any(item["title"] in EPISODES for item in samples),
                    "seasonPacksOnly returned an individual episode")
        else:
            for native_season in (1, 2, 3):
                require({item["seasonNumber"] for item in packs[PACKS[native_season - 1]]["mappedEpisodes"]} == {native_season},
                        "A single-season pack mapped onto another native season")
            require({item["seasonNumber"] for item in packs[PACKS[3]]["mappedEpisodes"]} == {1, 2, 3},
                    "S01-S03 pack did not retain all three native season boundaries")
        require(self.episode_identity() == self.native_episodes, "Interactive search changed native episode identities/counts")
        self.searches.append({"api": path, "status": status, "elapsedSeconds": elapsed,
                              "fixtureQueryCount": len(terms), "fixtureQueryTerms": terms,
                              "releaseCount": len(entries), "packCount": len(packs), "releases": samples})
        self.check(f"{version} {'complete-series' if season is None else 'Season ' + str(season) + ' pack-only'}: "
                   f"native packs returned without episode fanout ({len(terms)} RSS queries, {elapsed}s)")

    def no_acquisition(self):
        require(self.json_api("/api/v5/downloadclient") == [] and self.json_api("/api/v5/queue")["totalRecords"] == 0
                and self.json_api("/api/v5/history")["totalRecords"] == 0, "Pack search configured a client or populated queue/history")
        forbidden = {"SeriesSearch", "SeasonSearch", "EpisodeSearch", "CompleteSeriesSearch", "MissingEpisodeSearch", "CutoffUnmetEpisodeSearch"}
        require(not any(item["name"] in forbidden for item in self.json_api("/api/v5/command")),
                "Interactive pack search scheduled acquisition")
        require(all(record["status"] == 200 and record["path"] == "/" and record["query"].get("page") == ["rss"]
                    for record in self.requests()), "A request attempted to fetch a torrent or non-RSS fixture route")
        require(not list(self.root.rglob("*.torrent")) and not list((self.root / "media").rglob("*.mkv")),
                "Pack search created torrent/media files")
        self.check("no torrent/media requests or files, download clients, queue, history or acquisition commands")

    def cleanup(self):
        if self.args.keep:
            print(f"Kept fixture app {self.container}; RSS {self.mock}; network {self.network}; data {self.root}")
            return
        failures = []
        if self.mock:
            try:
                self.docker("rm", "--force", self.mock)
                self.mock = None
            except Exception as error:
                failures.append(str(error))
        try:
            super().cleanup()
        except Exception as error:
            failures.append(str(error))
        if self.network:
            try:
                self.docker("network", "rm", self.network)
                self.network = None
            except Exception as error:
                failures.append(str(error))
        require(not failures, "Owned fixture cleanup failed: " + "; ".join(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:groups-working")
    parser.add_argument("--mock-image", default="python:3.13-alpine")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    require(args.report is not None and args.timeout > 0, "--report and a positive timeout are required")
    audit = SeriesPacks(args)
    error = None
    records = []
    logs = ""

    def safe_error(failure):
        detail = str(failure) or type(failure).__name__
        for secret in (audit.api_key, audit.metadata_key, audit.password):
            detail = detail.replace(secret, "[REDACTED]")
        return detail

    try:
        audit.start()
        audit.seed()
        audit.indexer()
        for version in ("v5", "v3"):
            audit.search(version)
            for season in (1, 2, 3):
                audit.search(version, season)
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as failure:
        error = safe_error(failure)
        print("FAIL " + error, flush=True)
    finally:
        if audit.fixture_root:
            try:
                records = audit.requests()
            except (Exception, KeyboardInterrupt) as failure:
                error = error or "Could not read local RSS evidence: " + safe_error(failure)
                print("FAIL RSS evidence: " + safe_error(failure), flush=True)
        if audit.container:
            try:
                logs = audit.docker("logs", audit.container)
                for secret in (audit.api_key, audit.metadata_key, audit.password):
                    logs = logs.replace(secret, "[REDACTED]")
            except (Exception, KeyboardInterrupt) as failure:
                error = error or "Could not collect fixture container logs: " + safe_error(failure)
                print("FAIL container logs: " + safe_error(failure), flush=True)
        try:
            audit.cleanup()
        except (Exception, KeyboardInterrupt) as failure:
            error = error or "Cleanup: " + safe_error(failure)
            print("FAIL cleanup: " + safe_error(failure), flush=True)
        try:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
        except (Exception, KeyboardInterrupt) as failure:
            error = error or "Could not write fixture log report: " + safe_error(failure)
            print("FAIL log report: " + safe_error(failure), flush=True)
        report = {
            "createdUtc": utc_now(), "image": args.image, "imageId": audit.image_id,
            "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"),
            "mockImage": args.mock_image, "mockImageId": audit.mock_image_id, "runtime": audit.runtime_metadata,
            "passed": error is None, "error": error, "checks": audit.results, "searches": audit.searches,
            "nativeEpisodeIdentities": audit.native_episodes, "fixtureRequests": records,
            "fixtureConfigOwnership": audit.fixture_config_ownership,
            "scope": {"syntheticNativeTvSeries": True, "nativeSeasons": [1, 2, 3], "nativeEpisodeCount": 6,
                      "localRssOnly": True, "liveNyaaTested": False, "aniListUsed": False,
                      "acquisitionTested": False, "realLibraryTested": False, "mainConfigurationAccessed": False,
                      "rssPublishedPorts": False, "apiBindAddress": "127.0.0.1:random"},
            "keptApp": audit.container if args.keep else None, "keptMock": audit.mock if args.keep else None,
            "keptNetwork": audit.network if args.keep else None,
            "keptData": str(audit.root) if args.keep and audit.root else None,
        }
        try:
            args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        except (Exception, KeyboardInterrupt) as failure:
            error = error or "Could not write fixture JSON report: " + safe_error(failure)
            print("FAIL JSON report: " + safe_error(failure), flush=True)
    return 1 if error else 0


if __name__ == "__main__":
    raise SystemExit(main())
