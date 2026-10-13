#!/usr/bin/env python3
"""Verify native Nyaa RSS searches in disposable Docker data; never acquire media."""

import argparse
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import sys
import threading
import time
import urllib.parse

REPOSITORY = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPOSITORY / "docker" / "tests"))
from smoke import SmokeFailure, require

spec = importlib.util.spec_from_file_location("functional_security", REPOSITORY / "docker" / "tests" / "functional-security.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def api_error_detail(value):
    if isinstance(value, list):
        return "; ".join(str(item.get("errorMessage", item.get("message", "validation failure")))
                         if isinstance(item, dict) else str(item) for item in value)
    if isinstance(value, dict):
        parts = [str(value[key]) for key in ("title", "detail", "errorMessage", "message") if value.get(key)]
        errors = value.get("errors")
        if isinstance(errors, dict):
            parts.extend(f"{name}: {api_error_detail(messages if isinstance(messages, list) else [messages])}"
                         for name, messages in errors.items())
        elif isinstance(errors, list):
            parts.append(api_error_detail(errors))
        return "; ".join(parts)
    return str(value) if value is not None else ""


class Verification(module.FunctionalSecurity):
    def __init__(self, args):
        super().__init__(args)
        self.searches = []
        self.request_urls = []
        self.fixture_requests = []
        self.fixture_server = None
        self.last_error_response = None

    def json_api(self, path, method="GET", data=None):
        status, _, body = self.raw(path, method, data)
        if not 200 <= status < 300:
            try:
                response = json.loads(body)
            except ValueError:
                response = body.decode("utf-8", errors="replace")
            serialized = json.dumps(response)
            for secret in (self.api_key, self.metadata_key, self.password):
                serialized = serialized.replace(secret, "[REDACTED]")
            response = json.loads(serialized)
            self.last_error_response = {"method": method, "path": path.split("?")[0],
                                        "status": status, "response": response}
            detail = api_error_detail(response)
            raise SmokeFailure(f"{method} {path.split('?')[0]} returned HTTP {status}: {detail[:2048]}")
        return json.loads(body) if body else None

    def start(self):
        super().start()
        status = self.json_api("/api/v5/system/status")
        self.runtime_metadata = {key: status.get(key) for key in ("productName", "productVersion", "version", "runtimeVersion", "migrationVersion")}
        self.image_labels = json.loads(self.docker("image", "inspect", self.args.image))[0]["Config"].get("Labels", {})
        general = self.json_api("/api/v5/settings/general")
        general["logLevel"] = "debug"
        general.update(username=self.username, password=self.password, passwordConfirmation=self.password)
        self.json_api("/api/v5/settings/general/1", "PUT", general)
        self.check("debug request logging enabled only in the disposable configuration")

    def insert(self, db, table, values):
        for _, name, kind, required, default, primary in db.execute('PRAGMA table_info("' + table + '")'):
            if required and default is None and not primary and name not in values:
                values[name] = 0 if kind in ("INTEGER", "BOOLEAN") else ""
        columns = ",".join('"' + name + '"' for name in values)
        db.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({",".join("?" for _ in values)})', list(values.values()))

    def seed(self):
        self.json_api("/api/v5/rootfolder", "POST", {"path": "/media"})
        quality = self.json_api("/api/v5/qualityprofile")[0]["id"]
        self.docker("stop", self.container)
        title = "Weeb & Alpha #1 + Plus" if self.args.fixture else self.args.title
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            self.insert(db, "Series", dict(Id=1, TvdbId=-1, Title=title, TitleSlug="isolated-nyaa-fixture", CleanTitle=re.sub(r"[^a-z0-9]", "", title.lower()),
                SortTitle=title.lower(), Path="/media/Fixture anime", Images="[]", Monitored=1, SeasonFolder=1, Runtime=24,
                SeriesType=2, QualityProfileId=quality, Tags="[]", Added="2026-10-13 00:00:00",
                Seasons='[{"seasonNumber":1,"monitored":true}]', Genres="[]", Actors="[]", Ratings='{"votes":0,"value":0}',
                OriginalLanguage=1, Language=1, MetadataSource=2, ForeignId="1", AniListIds="[1]", MalIds="[]", AlternateTitles=json.dumps(self.args.alias)))
            self.insert(db, "Episodes", dict(Id=1, SeriesId=1, SeasonNumber=1, EpisodeNumber=1, AbsoluteEpisodeNumber=1,
                Title="Synthetic episode", EpisodeFileId=0, Monitored=1, Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                AirDate="2026-03-01", AirDateUtc="2026-03-01 00:00:00"))
            for episode_number in range(2, self.args.episode_count + 1):
                self.insert(db, "Episodes", dict(Id=episode_number, SeriesId=1, SeasonNumber=1,
                    EpisodeNumber=episode_number, AbsoluteEpisodeNumber=episode_number, Title="Synthetic episode",
                    EpisodeFileId=0, Monitored=1, Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                    AirDate="2026-03-01", AirDateUtc="2026-03-01 00:00:00"))
            db.execute("UPDATE ScheduledTasks SET LastExecution='2050-01-01 00:00:00', LastStartTime='2050-01-01 00:00:00'")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        require(self.json_api("/api/v5/series/1")["title"] == title, "Synthetic anime fixture unreadable")
        require(self.json_api("/api/v5/episode/1")["absoluteEpisodeNumber"] == 1, "Synthetic absolute episode number lost")
        self.check("synthetic AniList-owned anime/episode seeded with scheduled tasks deferred")

    def start_fixture(self):
        verification = self
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_args):
                pass
            def do_GET(self):
                query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query, keep_blank_values=True)
                verification.fixture_requests.append({"path": self.path, "query": query})
                require("page" in query, "Fixture received a non-RSS request")
                body = b'''<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:nyaa="https://nyaa.si/xmlns/nyaa"><channel><title>Disposable native Nyaa fixture</title>
<description>No real downloadable content</description><link>http://127.0.0.1/</link><item>
<title>[Fixture] Weeb Alpha - 01 [1080p].mkv</title><guid isPermaLink="true">http://127.0.0.1/view/1</guid>
<link>http://127.0.0.1/download/1.torrent</link><pubDate>Mon, 12 Oct 2026 12:00:00 +0000</pubDate>
<nyaa:size>1 GiB</nyaa:size><nyaa:infoHash>0123456789abcdef0123456789abcdef01234567</nyaa:infoHash>
<nyaa:seeders>8</nyaa:seeders><nyaa:leechers>2</nyaa:leechers></item></channel></rss>'''
                self.send_response(200)
                self.send_header("Content-Type", "application/rss+xml")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
        self.fixture_server = ThreadingHTTPServer(("0.0.0.0", 0), Handler)
        threading.Thread(target=self.fixture_server.serve_forever, daemon=True).start()
        details = json.loads(self.docker("inspect", self.container))[0]
        gateway = next(iter(details["NetworkSettings"]["Networks"].values()))["Gateway"]
        return f"http://{gateway}:{self.fixture_server.server_port}"

    def indexer(self):
        endpoint = self.start_fixture() if self.args.fixture else "https://nyaa.si"
        provider = next(item for item in self.json_api("/api/v5/indexer/schema") if item["implementation"] == "Nyaa")
        provider.update(name="Disposable native Nyaa verification", enableRss=False, enableAutomaticSearch=False,
                        enableInteractiveSearch=True, priority=25)
        values = {"baseUrl": endpoint, "additionalParameters": "&cats=1_0&filter=1", "animeStandardFormatSearch": False}
        for field in provider["fields"]:
            if field["name"] in values:
                field["value"] = values[field["name"]]
        test_status, _, test_body = self.raw("/api/v5/indexer/test", "POST", provider)
        require(200 <= test_status < 300, f"Native Nyaa indexer connection test returned HTTP {test_status}")
        self.check("native Nyaa indexer connection test fetches and parses RSS successfully")
        saved = self.json_api("/api/v5/indexer?skipTesting=true", "POST", provider)
        require(saved["enableRss"] is False and saved["enableAutomaticSearch"] is False and saved["enableInteractiveSearch"] is True,
                "Indexer enabled acquisition or recurring RSS")
        require(self.json_api("/api/v5/downloadclient") == [], "Unexpected download client exists")
        self.check("native Nyaa configured for interactive search only with no download clients")

    def search(self, name, path):
        time.sleep(2.1)
        started = time.monotonic()
        status, _, body = self.raw(path)
        require(status == 200, f"{name} returned HTTP {status}")
        releases = json.loads(body)
        require(isinstance(releases, list), "Search response is not a release array")
        self.searches.append({"name": name, "api": path, "httpStatus": status, "elapsedSeconds": round(time.monotonic() - started, 3), "releaseCount": len(releases),
                              "firstReleaseKeys": sorted(releases[0]) if releases else [], "samples": []})
        samples = []
        for entry in releases:
            release = entry.get("release", entry)
            require(release.get("protocol") in ("torrent", "Torrent", 2), f"Release protocol is not torrent: {release.get('protocol')!r}")
            require(release.get("size", 0) > 0, "Parsed torrent size missing")
            require(re.fullmatch(r"[0-9a-fA-F]{40}", release.get("infoHash", "")), "Parsed infohash invalid")
            require(isinstance(release.get("seeders"), int), "Parsed seed count missing")
            require(release.get("indexer") == "Disposable native Nyaa verification", "Unexpected indexer returned releases")
            if len(samples) < 3:
                sample = {key: release.get(key) for key in ("title", "size", "infoHash", "seeders", "leechers", "protocol")}
                sample["rejections"] = entry.get("rejections", entry.get("decision", {}).get("rejections"))
                samples.append(sample)
        self.searches[-1]["samples"] = samples
        self.check(f"{name}: HTTP 200; {len(releases)} parsed torrent release metadata records")

    def logs(self):
        debug = self.docker("exec", self.container, "sh", "-c", "cat /config/logs/sonarr.debug*.txt")
        for line in debug.splitlines():
            if "HttpClient|Req:" in line or "Downloading Feed" in line:
                match = re.search(r"https?://[^\s]+", line)
                if match and ("nyaa.si" in match.group(0) or self.args.fixture):
                    url = match.group(0)
                    require(self.api_key not in url, "API credentials leaked into request logging")
                    self.request_urls.append(url)
        errors = [line for line in debug.splitlines() if "|Error|" in line and any(word in line for word in ("Nyaa", "indexer", "Indexer", "feed"))]
        require(not errors, "Runtime logged an indexer error during verification")
        if not self.args.fixture:
            require(self.request_urls, "No actual Nyaa query was logged")
        self.check("actual native Nyaa RSS request URLs captured without API credentials or indexer errors")

    def no_acquisition(self):
        require(self.json_api("/api/v5/queue")["totalRecords"] == 0, "Search populated download queue")
        require(self.json_api("/api/v5/history")["totalRecords"] == 0, "Search populated acquisition history")
        forbidden = {"SeriesSearch", "SeasonSearch", "EpisodeSearch", "MissingEpisodeSearch", "CutoffUnmetEpisodeSearch"}
        require(not any(item["name"] in forbidden for item in self.json_api("/api/v5/command")), "Acquisition command was scheduled")
        require(not list((self.root / "media").rglob("*.mkv")), "Media files were created")
        require(not list(self.root.rglob("*.torrent")), "Torrent file was downloaded")
        self.check("queue/history empty; no acquisition commands, media or torrent downloads")

    def run(self):
        self.start()
        self.seed()
        self.indexer()
        if self.args.fixture:
            self.search("reserved-character absolute episode search", "/api/v5/release?episodeId=1")
            searched = [item for item in self.fixture_requests if "term" in item["query"]]
            expected = ["Weeb & Alpha #1 + Plus 1", "Weeb & Alpha #1 + Plus 01"]
            actual = [item["query"].get("term", [None])[0] for item in searched]
            self.encoding = {"expected": expected, "actual": actual, "passed": actual == expected}
            self.no_acquisition()
            self.logs()
            require(actual == expected, "Reserved-character terms were changed, split into query parameters or truncated")
            self.check("reserved &, # and + survive actual HTTP query parsing as part of the term")
        else:
            self.search("V5 complete-series search", "/api/v5/release?seriesId=1&completeSeries=true")
            self.search("V5 absolute episode search", "/api/v5/release?episodeId=1")
            self.search("V3 complete-series search", "/api/v3/release?seriesId=1&completeSeries=true")
            require(all(item["releaseCount"] > 0 for item in self.searches), "A real Nyaa search returned a valid empty result rather than matched releases")
            self.no_acquisition()
            self.logs()

    def cleanup(self):
        if self.fixture_server:
            self.fixture_server.shutdown()
            self.fixture_server.server_close()
        super().cleanup()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--title", default="Cowboy Bebop", help="Public title to search; seeded only in disposable data")
    parser.add_argument("--alias", action="append", default=[], help="Known catalogue alternate title, repeatable")
    parser.add_argument("--episode-count", type=int, default=1, help="Known catalogue episode count for the disposable native season")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--fixture", action="store_true")
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    require(1 <= args.episode_count <= 1000, "Episode count must be from 1 to 1000")
    audit = Verification(args)
    error = None
    startup_logs = ""
    try:
        audit.run()
    except Exception as failure:
        error = str(failure)
        for secret in (audit.api_key, audit.password, audit.metadata_key):
            error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, flush=True)
    finally:
        if error and audit.container:
            try:
                startup_logs = audit.docker("logs", audit.container)
                for secret in (audit.api_key, audit.password, audit.metadata_key):
                    startup_logs = startup_logs.replace(secret, "[REDACTED]")
            except Exception:
                # Diagnostics must never prevent cleanup of the owned fixture.
                pass
        try:
            audit.cleanup()
        except Exception as failure:
            error = error or ("Cleanup: " + type(failure).__name__)
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({"utc": datetime.now(timezone.utc).isoformat(), "image": args.image,
            "imageId": audit.image_id, "sourceRevision": getattr(audit, "image_labels", {}).get("org.opencontainers.image.revision"),
            "fixture": args.fixture, "title": args.title if not args.fixture else "Reserved-character fixture",
            "nativeEpisodeCount": args.episode_count, "nativeSeasonCount": 1, "catalogueAliases": args.alias,
            "passed": error is None, "error": error, "httpError": audit.last_error_response, "checks": audit.results,
            "runtime": audit.runtime_metadata, "searches": audit.searches, "rssRequestUrls": audit.request_urls,
            "fixtureRequests": audit.fixture_requests, "encoding": getattr(audit, "encoding", None),
            "limits": ["No real library or authenticated Nyaa account was used.", "RSS search metadata only; no torrent or media acquisition.",
                       (f"Synthetic AniList-owned anime with reserved-character title and {args.episode_count} episode(s) in one native season were seeded directly in isolated SQLite."
                        if args.fixture else f"The requested title and {args.episode_count} synthetic episode(s) in one native season were seeded directly in isolated SQLite; supplied catalogue counts/aliases do not verify metadata ingestion or episode ordering.")]},
            indent=2) + "\n", encoding="utf-8")
        if startup_logs:
            args.report.with_suffix(".log").write_text(startup_logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    raise SystemExit(main())
