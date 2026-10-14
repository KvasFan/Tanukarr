#!/usr/bin/env python3
"""Verify packaged collection UI with generated catalogue data and a local browser."""

import argparse
from datetime import datetime, timezone
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import secrets
import sqlite3
import ssl
import struct
import subprocess
import sys
import threading
import urllib.parse
from xml.sax.saxutils import escape
import zlib

from smoke import SmokeFailure, require

SCRIPTS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("collection_browser_groups", SCRIPTS / "anime-groups.py")
groups = importlib.util.module_from_spec(spec)
spec.loader.exec_module(groups)
PINNED_MOCK = "python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a"
NATIVE_TITLE = "Fixture Native Collection Show"
MEDIA_IDS = (900401, 900402, 900403, 900501, 900502, 900503, 900601, 900602)
ADDED_IDS = tuple(item for item in MEDIA_IDS if item != 900501)


def png(width, height, color):
    """Create valid, easily decoded test artwork using only the standard library."""
    def chunk(kind, payload):
        return struct.pack("!I", len(payload)) + kind + payload + struct.pack("!I", zlib.crc32(kind + payload) & 0xffffffff)
    pixels = b"".join(b"\x00" + bytes(color) * width for _ in range(height))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack("!IIBBBBB", width, height, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b"")


def media(media_id):
    resource = groups.media_resource(media_id)
    resource["description"] = f"Canonical generated synopsis for {resource['title']['english']}."
    resource["coverImage"] = {"large": f"https://s4.anilist.co/file/anilistcdn/media/{media_id}/poster.png"}
    resource["bannerImage"] = f"https://s4.anilist.co/file/anilistcdn/media/{media_id}/banner.png"
    return resource


def serve_fixture(root):
    lock = threading.Lock()
    poster = png(340, 500, (66, 104, 150))
    backdrop = png(960, 360, (38, 82, 105))

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def answer(self, status, payload, content_type="application/json"):
            body = json.dumps(payload).encode() if content_type == "application/json" else payload
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def record(self, value):
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                output.write(json.dumps(value) + "\n")

        def do_POST(self):
            record = {"kind": "catalogue", "path": self.path, "status": 400}
            try:
                length = int(self.headers.get("Content-Length", 0))
                require(0 < length < 65536 and self.path == "/", "Unexpected GraphQL request")
                request = json.loads(self.rfile.read(length))
                query, variables = request.get("query", ""), request.get("variables", {})
                if "Media(" in query:
                    media_id = int(variables["id"])
                    require(media_id in MEDIA_IDS, "Unknown generated catalogue ID")
                    response = {"data": {"Media": media(media_id)}}
                    record["mediaId"] = media_id
                elif "media(search:" in query:
                    matching = [media(item) for item in MEDIA_IDS if groups.CATALOGUE[item][0] == variables.get("search")]
                    require(len(matching) == 1, "Unknown generated catalogue title")
                    response = {"data": {"Page": {"media": matching}}}
                else:
                    raise SmokeFailure("Only generated catalogue requests are supported")
                record["status"] = 200
                self.answer(200, response)
            except (SmokeFailure, KeyError, ValueError, TypeError):
                self.answer(400, {"errors": [{"message": "Unexpected generated catalogue request"}]})
            finally:
                self.record(record)

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            if parsed.path.startswith("/file/anilistcdn/media/") and parsed.path.endswith(("/poster.png", "/banner.png")):
                self.answer(200, backdrop if parsed.path.endswith("banner.png") else poster, "image/png")
                self.record({"kind": "image", "path": parsed.path, "status": 200})
                return
            query = urllib.parse.parse_qs(parsed.query)
            if parsed.path == "/" and query.get("page") == ["rss"]:
                items = []
                titles = [groups.CATALOGUE[item][0] for item in MEDIA_IDS] + [NATIVE_TITLE]
                for index, title in enumerate(titles):
                    for suffix in ("S01", "S01E01"):
                        release = f"{title}.{suffix}.1080p.WEB-DL.H264-Fixture"
                        identity = hashlib.sha256(release.encode()).hexdigest()[:40]
                        items.append(f"<item><title>{escape(release)}</title><guid>http://fixture-rss:8989/view/{index}-{suffix}</guid>"
                                     f"<link>http://fixture-rss:8989/download/{index}-{suffix}.torrent</link>"
                                     "<pubDate>Tue, 13 Oct 2026 09:00:00 +0000</pubDate>"
                                     f"<nyaa:size>4 GiB</nyaa:size><nyaa:infoHash>{identity}</nyaa:infoHash>"
                                     "<nyaa:seeders>20</nyaa:seeders><nyaa:leechers>1</nyaa:leechers></item>")
                rss = ('<?xml version="1.0"?><rss version="2.0" xmlns:nyaa="https://nyaa.si/xmlns/nyaa"><channel>'
                       '<title>Generated collection fixture</title><description>Local RSS only</description>'
                       '<link>http://fixture-rss:8989/</link>' + "".join(items) + '</channel></rss>').encode()
                self.answer(200, rss, "application/rss+xml")
                self.record({"kind": "rss", "path": parsed.path, "query": query, "status": 200})
                return
            self.answer(400, {"message": "Only generated images and RSS are available"})
            self.record({"kind": "forbidden", "path": parsed.path, "status": 400})

        def do_HEAD(self):
            path = urllib.parse.urlsplit(self.path).path
            if path.startswith("/file/anilistcdn/media/") and path.endswith(("/poster.png", "/banner.png")):
                body = backdrop if path.endswith("banner.png") else poster
                self.send_response(200)
                self.send_header("Content-Type", "image/png")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.record({"kind": "image-head", "path": path, "status": 200})
            else:
                self.send_response(405)
                self.send_header("Content-Length", "0")
                self.end_headers()
                self.record({"kind": "forbidden", "path": path, "status": 405})

    rss = ThreadingHTTPServer(("0.0.0.0", 8989), Handler)
    rss.daemon_threads = True
    threading.Thread(target=rss.serve_forever, daemon=True).start()
    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local anime grouping TLS fixture ready", flush=True)
    server.serve_forever()


class CollectionsBrowser(groups.AnimeGroups):
    fixture_hosts = ("graphql.anilist.co", "s4.anilist.co")

    def __init__(self, args):
        super().__init__(args)
        self.username = "generated-collection-browser"
        self.password = secrets.token_urlsafe(32)
        self.native_id = 100

    def docker(self, *arguments):
        values = list(arguments)
        if values[0] == "create" and "--entrypoint" in values and "/bin/sh" in values:
            at = values.index(self.args.image)
            values[at:at] = ["--label", "sonarr.smoke-test=true"]
        if "/runner/anime-groups.py" in values and "--serve-fixture" in values:
            values[values.index("/runner/anime-groups.py")] = "/runner/collections-browser.py"
            values.extend(("--fixture-only",))
            at = values.index(self.args.mock_image)
            values[at:at] = ["--network-alias", "s4.anilist.co", "--network-alias", "fixture-rss"]
        return super().docker(*values)

    @staticmethod
    def insert(db, table, values):
        for _, name, kind, required, default, primary in db.execute('PRAGMA table_info("' + table + '")'):
            if required and default is None and not primary and name not in values:
                values[name] = 0 if kind in ("INTEGER", "BOOLEAN") else ""
        columns = ",".join('"' + name + '"' for name in values)
        db.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({",".join("?" for _ in values)})', list(values.values()))

    def setup(self):
        self.prepare()
        settings = self.api_json("/api/v5/settings/general")
        settings.update(username=self.username, password=self.password, passwordConfirmation=self.password)
        self.api_json("/api/v5/settings/general/1", "PUT", settings)
        for item in ADDED_IDS:
            self.add(item)
        self.until(lambda: len(list((self.root / "config" / "MediaCover").rglob("poster.jpg"))) >= len(ADDED_IDS),
                   "Generated real covers did not arrive")
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        image_url = "https://s4.anilist.co/file/anilistcdn/media/900401/poster.png"
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            self.insert(db, "Series", dict(Id=self.native_id, TvdbId=900880, Title=NATIVE_TITLE,
                TitleSlug="fixture-native-collection-show", CleanTitle="fixturenativecollectionshow", SortTitle=NATIVE_TITLE.lower(),
                Path="/media/library/Fixture Native Collection Show", Images=json.dumps([{"coverType": "poster", "remoteUrl": image_url}]),
                Monitored=0, SeasonFolder=1, Runtime=24, SeriesType=0, QualityProfileId=self.profile_id,
                Tags="[]", Added="2026-10-13 00:00:00", Genres='["Drama"]', Actors="[]", Overview="Generated native TV synopsis.",
                Seasons=json.dumps([{"seasonNumber": season, "monitored": False} for season in (0, 1, 2, 3)]),
                Ratings='{"votes":0,"value":0}', OriginalLanguage=1, Language=1, MetadataSource=0, ForeignId="900880",
                AniListIds="[]", MalIds="[]", AlternateTitles="[]"))
            for season in (0, 1, 2, 3):
                self.insert(db, "Episodes", dict(SeriesId=self.native_id, SeasonNumber=season, EpisodeNumber=1,
                    Title=f"Generated season {season} episode", EpisodeFileId=0, Monitored=0, Runtime=24,
                    Images="[]", Ratings='{"votes":0,"value":0}', AirDate="2024-01-01", AirDateUtc="2024-01-01 00:00:00"))
            db.execute("UPDATE ScheduledTasks SET LastExecution='2050-01-01 00:00:00', LastStartTime='2050-01-01 00:00:00'")
        destination = self.root / "config" / "MediaCover" / str(self.native_id)
        destination.mkdir()
        for name, width, height in (("poster.jpg", 340, 500), ("poster-250.jpg", 170, 250), ("poster-500.jpg", 340, 500)):
            (destination / name).write_bytes(png(width, height, (60, 99, 130)))
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        resource = next(item for item in self.api_json("/api/v5/indexer/schema") if item["implementation"] == "Nyaa")
        resource.update(name="Generated collection RSS", enableRss=False, enableAutomaticSearch=False, enableInteractiveSearch=True, priority=25)
        for field in resource["fields"]:
            if field["name"] == "baseUrl":
                field["value"] = "http://fixture-rss:8989"
            elif field["name"] == "additionalParameters":
                field["value"] = "&cats=1_0&filter=1"
            elif field["name"] == "animeStandardFormatSearch":
                field["value"] = True
        require(self.raw("/api/v5/indexer/test", "POST", resource)[0] == 204, "Generated Nyaa RSS Test failed")
        self.api_json("/api/v5/indexer?skipTesting=true", "POST", resource)
        self.check("generated native TV, anime chain, mixed movie chain, pure movie saga, Specials, real image files and interactive-only Nyaa")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image")
    parser.add_argument("--mock-image", default=PINNED_MOCK)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--browser-node", default="node")
    parser.add_argument("--playwright-module", default="playwright")
    parser.add_argument("--playwright-version", required=False)
    parser.add_argument("--windows-browser", action="store_true")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--fixture-only", action="store_true", help=argparse.SUPPRESS)
    parser.set_defaults(keep=False, anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    require(args.image and args.report and args.playwright_version, "Image, report and pinned Playwright version are required")
    require(args.mock_image == PINNED_MOCK, "Use the digest-pinned Python fixture image")
    args.report.parent.mkdir(parents=True, exist_ok=True)
    directory = args.report.parent / (args.report.stem + "-browser")
    require(not args.report.exists() and not directory.exists(), "Preserve existing browser evidence; choose a new report path")
    directory.mkdir()
    audit = CollectionsBrowser(args)
    error, browser, cleanup, requests = None, None, False, []
    try:
        audit.start()
        audit.setup()
        require(audit.runtime_metadata.get("productVersion") == (SCRIPTS.parents[1] / "VERSION").read_text().strip(),
                "Runtime product version differs from the release source")
        native = audit.api_json(f"/api/v5/series/{audit.native_id}")
        series = audit.api_json("/api/v5/series")
        presentation = audit.api_json("/api/v5/collection/presentation?groupId=anilist%3A900401")
        require(any(image.get("coverType") == "poster" and image.get("url") for image in presentation.get("images", [])),
                "Canonical presentation does not expose its real cover through the expected API contract")
        def browser_path(value):
            return subprocess.check_output(["wslpath", "-w", str(value)], text=True).strip() if args.windows_browser else str(value)
        payload = {"ownedFixture": True, "baseUrl": audit.base_url, "apiKey": audit.api_key,
                   "username": audit.username, "password": audit.password, "imageId": audit.image_id,
                   "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"),
                   "series": series, "nativeId": native["id"], "playwrightModule": args.playwright_module,
                   "playwrightVersion": args.playwright_version, "artifactDirectory": browser_path(directory),
                   "canonicalPresentation": presentation}
        payload["episodeIdentities"] = sorted((episode["id"], item["id"], episode["seasonNumber"], episode["episodeNumber"])
                                             for item in series for episode in audit.episodes(item))
        result = subprocess.run([args.browser_node, browser_path(SCRIPTS / "collections-browser.cjs")], input=json.dumps(payload),
                                capture_output=True, text=True, encoding="utf-8", timeout=args.timeout + 300)
        output = result.stdout + result.stderr
        for secret in (audit.api_key, audit.username, audit.password):
            output = output.replace(secret, "[REDACTED]")
        print(output.strip(), flush=True)
        browser = json.loads((directory / "collections-browser.json").read_text())
        require(result.returncode == 0 and browser["passed"], "Packaged collection browser checks failed")
        require(audit.api_json("/api/v5/downloadclient") == [] and audit.api_json("/api/v5/queue")["totalRecords"] == 0,
                "Browser fixture created acquisition state")
        require(audit.api_json("/api/v5/history")["totalRecords"] == 0, "Browser fixture acquired media")
        require(not any(item["name"] in ("SeriesSearch", "SeasonSearch", "EpisodeSearch", "CompleteSeriesSearch")
                        for item in audit.api_json("/api/v5/command")), "Browser scheduled acquisition")
        audit.check("actual packaged browser never configures a download client, acquires media or schedules acquisition")
    except (Exception, KeyboardInterrupt) as caught:
        error = str(caught) or type(caught).__name__
        for secret in (audit.api_key, audit.username, audit.password):
            error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, flush=True)
    finally:
        if audit.root:
            log = audit.root / "fixture" / "requests.jsonl"
            if log.exists():
                requests = [json.loads(line) for line in log.read_text().splitlines()]
        try:
            audit.cleanup()
            cleanup = True
        except (Exception, KeyboardInterrupt) as caught:
            error = error or "Owned fixture cleanup failed: " + type(caught).__name__
        report = {"createdUtc": datetime.now(timezone.utc).isoformat(), "image": args.image, "imageId": audit.image_id,
                  "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"), "runtime": getattr(audit, "runtime_metadata", {}),
                  "mockImage": args.mock_image, "mockImageId": audit.mock_id, "passed": error is None,
                  "error": error, "checks": audit.results + ((browser or {}).get("checks", [])), "browser": browser,
                  "fixtureRequests": requests, "cleanupConfirmed": cleanup, "fixtureConfigOwnership": audit.fixture_config_ownership,
                  "scope": {"ownedFixture": True, "ownerConfigurationAccessed": False, "privateAccountUsed": False,
                            "publicMetadataRequests": False, "acquisitionTested": False, "internalNetwork": True,
                            "externalRoutingAllowed": False, "trustedCatalogueTls": True, "realLibraryTested": False,
                            "apiBindAddress": "127.0.0.1:random", "keptData": False},
                  "keptApp": None, "keptMock": None, "keptNetwork": None, "keptData": None}
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
