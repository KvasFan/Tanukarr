#!/usr/bin/env python3
"""Download generated season packs through real qBittorrent and verify automatic imports."""

import argparse
from contextlib import closing
from datetime import datetime, timezone
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import re
import secrets
import sqlite3
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
from xml.sax.saxutils import escape

from smoke import require


SCRIPTS = Path(__file__).resolve().parent


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


CLIENT = load_module("pack_client_fixture", "download-client-folders.py")
PACKS = load_module("native_pack_fixture", "series-packs.py")
TITLE = "Generated Download Show"


def bencode(value):
    if isinstance(value, int):
        return b"i" + str(value).encode() + b"e"
    if isinstance(value, str):
        value = value.encode("utf-8")
    if isinstance(value, bytes):
        return str(len(value)).encode() + b":" + value
    if isinstance(value, list):
        return b"l" + b"".join(bencode(item) for item in value) + b"e"
    if isinstance(value, dict):
        return b"d" + b"".join(bencode(key) + bencode(value[key]) for key in sorted(value)) + b"e"
    raise TypeError(type(value).__name__)


def serve_fixture(root):
    """Fixed local RSS, generated .torrent files and range-capable BEP19 web seeds."""
    catalogue = json.loads((root / "catalogue.json").read_text(encoding="utf-8"))
    releases = {str(item["id"]): item for item in catalogue}
    payloads = {"/seed/" + item["title"] + "/" + file["name"]: root / file["source"]
                for item in catalogue for file in item["files"]}
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def reply(self, head=False):
            parsed = urllib.parse.urlsplit(self.path)
            path = urllib.parse.unquote(parsed.path)
            query = urllib.parse.parse_qs(parsed.query)
            status = 200
            kind = "rss"
            content_type = "application/octet-stream"
            range_header = None
            if path == "/" and query.get("page") == ["rss"]:
                term = query.get("term", [""])[0]
                season_match = re.search(r"\bs(\d+)\b", term, re.IGNORECASE)
                season = int(season_match[1]) if season_match else None
                selected = [item for item in catalogue if season is None or season in item["seasons"]]
                items = []
                for item in selected:
                    items.append(
                        f"<item><title>{escape(item['title'])}</title>"
                        f'<guid isPermaLink="true">http://fixture-seed:8989/view/{item["id"]}</guid>'
                        f'<link>http://fixture-seed:8989/download/{item["id"]}.torrent</link>'
                        '<pubDate>Thu, 15 Oct 2026 08:00:00 +0000</pubDate>'
                        f'<nyaa:size>{item["size"]} B</nyaa:size>'
                        f'<nyaa:infoHash>{item["infoHash"]}</nyaa:infoHash>'
                        '<nyaa:seeders>20</nyaa:seeders><nyaa:leechers>0</nyaa:leechers></item>')
                body = ('<?xml version="1.0" encoding="UTF-8"?>'
                        '<rss version="2.0" xmlns:nyaa="https://nyaa.si/xmlns/nyaa"><channel>'
                        '<title>Generated local packs</title><description>Owned media</description>'
                        '<link>http://fixture-seed:8989/</link>' + ''.join(items) + '</channel></rss>').encode()
                content_type = "application/rss+xml; charset=utf-8"
            elif re.fullmatch(r"/download/[1-9]\d*\.torrent", path):
                kind = "torrent"
                release = releases.get(path.split("/")[-1].removesuffix(".torrent"))
                if release is None:
                    status, body = 404, b""
                else:
                    body = (root / (str(release["id"]) + ".torrent")).read_bytes()
                    content_type = "application/x-bittorrent"
            elif path in payloads:
                kind = "payload"
                body = payloads[path].read_bytes()
                size = len(body)
                requested = self.headers.get("Range")
                if requested:
                    match = re.fullmatch(r"bytes=(\d+)-(\d*)", requested)
                    if match:
                        start = int(match[1])
                        end = min(int(match[2]) if match[2] else size - 1, size - 1)
                        if 0 <= start <= end:
                            status = 206
                            body = body[start:end + 1]
                            range_header = f"bytes {start}-{end}/{size}"
                        else:
                            status, body = 416, b""
                    else:
                        status, body = 416, b""
            else:
                kind, status, body = "unexpected", 404, b""
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            if kind == "payload":
                self.send_header("Accept-Ranges", "bytes")
            if range_header:
                self.send_header("Content-Range", range_header)
            self.end_headers()
            if not head:
                self.wfile.write(body)
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps({"kind": kind, "path": path, "status": status,
                                         "bytes": 0 if head else len(body), "range": range_header,
                                         "query": query}) + "\n")

        def do_GET(self):
            self.reply()

        def do_HEAD(self):
            self.reply(True)

    print("Generated pack RSS and web seed ready", flush=True)
    ThreadingHTTPServer(("0.0.0.0", 8989), Handler).serve_forever()


def serve_jellyfin_proxy(root):
    """Record refresh targets while forwarding only to the owned real server."""
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def forward(self):
            body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
            headers = {key: value for key, value in self.headers.items()
                       if key.lower() not in ("host", "connection", "transfer-encoding")}
            with closing(http.client.HTTPConnection("jellyfin-test", 8096, timeout=30)) as target:
                target.request(self.command, self.path, body, headers)
                response = target.getresponse()
                payload = response.read()
                self.send_response(response.status)
                for key, value in response.getheaders():
                    if key.lower() not in ("connection", "transfer-encoding", "content-length"):
                        self.send_header(key, value)
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
            # Credentials are forwarded in memory, never written to the trace.
            with lock, (root / "jellyfin-requests.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps({"method": self.command, "path": urllib.parse.urlsplit(self.path).path,
                                         "status": response.status}) + "\n")

        do_GET = forward
        do_POST = forward

    print("Owned real Jellyfin request recorder ready", flush=True)
    ThreadingHTTPServer(("0.0.0.0", 8096), Handler).serve_forever()


if __name__ == "__main__" and len(sys.argv) == 3 and sys.argv[1] == "--serve-fixture":
    serve_fixture(Path(sys.argv[2]))
    sys.exit(0)

if __name__ == "__main__" and len(sys.argv) == 3 and sys.argv[1] == "--serve-jellyfin-proxy":
    serve_jellyfin_proxy(Path(sys.argv[2]))
    sys.exit(0)


JELLYFIN = load_module("download_jellyfin_fixture", "jellyfin.py")
JF_REFRESH = load_module("download_jellyfin_refresh", "jellyfin-refresh.py")


class DownloadPacks(CLIENT.ClientFolders):
    fixture_hosts = ("graphql.anilist.co", "api.themoviedb.org", "www.omdbapi.com", "api.tvmaze.com")

    def __init__(self, args):
        super().__init__(args)
        self.seed_server = None
        self.seed_root = None
        self.catalogue = []
        self.transfers = []
        self.native_identity = []
        self.payload_requests = []
        self.cleanup_complete = False
        self.jellyfin = None
        self.jellyfin_relay = None
        self.jellyfin_proxy = None
        self.jellyfin_url = None
        self.jellyfin_token = None
        self.jellyfin_password = secrets.token_urlsafe(32)
        self.jellyfin_image_id = None
        self.libraries = {}
        self.definition = None
        self.refresh_observations = []
        self.refresh_requests = []
        self.movie_catalogue_server = None
        self.movie_members = {}
        self.movie_transfers = []
        self.movie_definition = None
        self.tv_tag = None
        self.movie_tag = None

    json_request = JELLYFIN.JellyfinIntegration.json_request
    jf = JELLYFIN.JellyfinIntegration.jf
    sonarr = JELLYFIN.JellyfinIntegration.sonarr
    state = JELLYFIN.JellyfinIntegration.state

    def count(self, library):
        query = urllib.parse.urlencode({"parentId": self.libraries[library], "recursive": "true",
                                        "includeItemTypes": "Movie" if library == "Films" else "Episode"})
        return self.jf("/Items?" + query)["TotalRecordCount"]

    def redact(self, value):
        value = super().redact(value)
        for secret in (self.metadata_key, self.jellyfin_password, self.jellyfin_token):
            if secret:
                value = value.replace(secret, "[REDACTED]")
        return value

    def docker(self, *arguments, timeout=60):
        if arguments and arguments[0] == "create" and "fixture-app" in arguments:
            arguments = (arguments[0], "--label", "sonarr.smoke-test=true", *arguments[1:])
        return super().docker(*arguments, timeout=timeout)

    def prepare(self):
        super().prepare()
        naming = self.api_json("/api/v5/settings/naming")
        naming["seasonFolderFormat"] = "Season {season:00}"
        if self.args.movies:
            naming["movieFormat"] = "{Series Title} ({Series Year}) {Quality Full}"
            films = self.root / "media" / "films"
            films.mkdir()
            films.chmod(0o777)
            self.api_json("/api/v5/rootfolder", "POST", {"path": "/media/films"})
            self.tv_tag = self.api_json("/api/v5/tag", "POST", {"label": "generated-tv"})["id"]
            self.movie_tag = self.api_json("/api/v5/tag", "POST", {"label": "generated-movies"})["id"]
        self.api_json("/api/v5/settings/naming/1", "PUT", naming)

    def seed_native_series(self):
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            PACKS.SeriesPacks.insert(db, "Series", dict(
                Id=1, TvdbId=9900101, Title=TITLE, TitleSlug="generated-download-show",
                CleanTitle="generateddownloadshow", SortTitle=TITLE.lower(), Path="/media/library/" + TITLE,
                Images="[]", Monitored=1, SeasonFolder=1, Runtime=24, SeriesType=0, QualityProfileId=self.profile_id,
                Tags=json.dumps([self.tv_tag]) if self.tv_tag else "[]", Added="2026-10-15 00:00:00", Genres="[]", Actors="[]", AlternateTitles="[]",
                Seasons=json.dumps([{"seasonNumber": value, "monitored": True} for value in range(1, 5)]),
                Ratings='{"votes":0,"value":0}', OriginalLanguage=1, Language=1,
                MetadataSource=0, ForeignId="9900101", AniListIds="[]", MalIds="[]"))
            for season in range(1, 5):
                for number in (1, 2):
                    PACKS.SeriesPacks.insert(db, "Episodes", dict(
                        Id=(season - 1) * 2 + number, SeriesId=1, SeasonNumber=season, EpisodeNumber=number,
                        Title=f"Generated episode {season}-{number}", EpisodeFileId=0, Monitored=1,
                        Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                        AirDate="2024-01-01", AirDateUtc="2024-01-01 00:00:00"))
            db.execute("UPDATE ScheduledTasks SET LastExecution='2050-01-01 00:00:00', LastStartTime='2050-01-01 00:00:00' "
                       "WHERE TypeName NOT LIKE '%ProcessPendingJellyfinRefreshCommand'")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        self.native_identity = self.identities()
        require(len(self.native_identity) == 8, "Native download fixture episodes are missing")
        self.check("four native seasons and eight aired episodes retain independent persisted identities")

    def identities(self):
        return sorted((item["id"], item["seriesId"], item["seasonNumber"], item["episodeNumber"])
                      for item in self.api_json("/api/v5/episode?seriesId=1"))

    def generate_payloads(self):
        self.seed_root = self.root / "generated-seed"
        self.seed_root.mkdir()
        template = self.seed_root / "template.mkv"
        generated = subprocess.run(
            [self.args.ffmpeg, "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=1",
             "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo",
             "-t", "610", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "512k", "-minrate", "512k",
             "-maxrate", "512k", "-bufsize", "1024k", "-x264-params", "nal-hrd=cbr:force-cfr=1",
             "-c:a", "aac", "-b:a", "64k", "-metadata:s:a:0", "language=eng", "-pix_fmt", "yuv420p", "-y", str(template)],
            capture_output=True, text=True, timeout=120)
        require(generated.returncode == 0 and template.is_file(), "Could not generate full-runtime owned media")
        require(template.stat().st_size >= 10 * 1024 * 1024, "Generated video must pass the unchanged fake-release size guard")
        piece_length = 16 * 1024
        for release_id, seasons in enumerate(((1,), (2,), (3, 4)), 1):
            season_tag = f"S{seasons[0]:02d}" if len(seasons) == 1 else f"S{seasons[0]:02d}-S{seasons[-1]:02d}"
            title = f"{TITLE}.{season_tag}.720p.WEB-DL.H264-Fixture"
            files = []
            for season in seasons:
                for number in (1, 2):
                    filename = f"{TITLE}.S{season:02d}E{number:02d}.720p.WEB-DL.H264-Fixture.mkv"
                    source = f"payload-{season}-{number}.mkv"
                    remux = subprocess.run(
                        [self.args.ffmpeg, "-nostdin", "-v", "error", "-i", str(template), "-c", "copy",
                         "-metadata", "title=" + filename, "-y", str(self.seed_root / source)],
                        capture_output=True, text=True, timeout=30)
                    require(remux.returncode == 0, "Could not identify generated episode bytes")
                    payload = (self.seed_root / source).read_bytes()
                    files.append({"name": filename, "source": source, "size": len(payload),
                                  "sha256": hashlib.sha256(payload).hexdigest(), "season": season, "episode": number})
            payload = b"".join((self.seed_root / file["source"]).read_bytes() for file in files)
            pieces = b"".join(hashlib.sha1(payload[index:index + piece_length]).digest()
                              for index in range(0, len(payload), piece_length))
            info = {b"name": title, b"piece length": piece_length, b"pieces": pieces, b"private": 1,
                    b"files": [{b"length": file["size"], b"path": [file["name"]]} for file in files]}
            info_hash = hashlib.sha1(bencode(info)).hexdigest()
            torrent = {b"info": info, b"url-list": [b"http://fixture-seed:8989/seed/"]}
            (self.seed_root / (str(release_id) + ".torrent")).write_bytes(bencode(torrent))
            self.catalogue.append({"id": release_id, "title": title, "seasons": list(seasons), "files": files,
                                   "size": len(payload), "infoHash": info_hash})
        if self.args.movies:
            self.generate_movie_payloads(template, piece_length)
        (self.seed_root / "catalogue.json").write_text(json.dumps(self.catalogue), encoding="utf-8")
        suffix = secrets.token_hex(6)
        self.seed_server = self.docker(
            "run", "--detach", "--name", "tanukarr-generated-pack-seed-" + suffix,
            "--label", "tanukarr.download-pack-test=true", "--network", self.network,
            "--network-alias", "fixture-seed", "--env", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={SCRIPTS},target=/runner,readonly",
            "--mount", f"type=bind,source={self.seed_root},target=/fixture",
            self.args.mock_image, "python", "-u", "/runner/download-packs.py", "--serve-fixture", "/fixture")
        def ready():
            status = json.loads(self.docker("inspect", self.seed_server))[0]["State"]["Status"]
            require(status not in ("exited", "dead"), "Owned web seed exited during startup")
            return "Generated pack RSS and web seed ready" in self.docker("logs", self.seed_server)

        self.until(ready, "Owned web seed did not start")
        self.check("generated uniquely identifiable full-runtime videos and private torrents use one internal web seed")

    def generate_movie_payloads(self, template, piece_length):
        packages = (
            (4, "Generated Feature One.2019.720p.WEB-DL.H264-Fixture", [("970001", "Generated Feature One", 2019)]),
            (5, "Generated Feature Collection.2019-2021.720p.WEB-DL.H264-Fixture",
             [("970002", "Generated Feature Two", 2021), ("tt9700101", "Generated IMDb Feature", 2020)]),
        )
        for release_id, title, members in packages:
            files = []
            for identity, movie_title, year in members:
                filename = f"{movie_title}.{year}.720p.WEB-DL.H264-Fixture.mkv"
                source = "movie-" + identity + ".mkv"
                generated = subprocess.run([self.args.ffmpeg, "-nostdin", "-v", "error", "-i", str(template), "-c", "copy",
                                            "-metadata", "title=" + filename, "-y", str(self.seed_root / source)],
                                           capture_output=True, text=True, timeout=30)
                require(generated.returncode == 0, "Could not generate uniquely identified movie payload")
                payload = (self.seed_root / source).read_bytes()
                files.append({"name": filename, "source": source, "movieId": identity, "year": year,
                              "size": len(payload), "sha256": hashlib.sha256(payload).hexdigest()})
            payload = b"".join((self.seed_root / file["source"]).read_bytes() for file in files)
            pieces = b"".join(hashlib.sha1(payload[index:index + piece_length]).digest()
                              for index in range(0, len(payload), piece_length))
            info = {b"name": title, b"piece length": piece_length, b"pieces": pieces, b"private": 1,
                    b"files": [{b"length": file["size"], b"path": [file["name"]]} for file in files]}
            (self.seed_root / (str(release_id) + ".torrent")).write_bytes(
                bencode({b"info": info, b"url-list": [b"http://fixture-seed:8989/seed/"]}))
            self.catalogue.append({"id": release_id, "title": title, "seasons": [], "movies": [item[0] for item in members],
                                   "files": files, "size": len(payload), "infoHash": hashlib.sha1(bencode(info)).hexdigest()})

    def start_movie_catalogues(self):
        tls = self.root / "fixture"
        (tls / "control.json").write_text(json.dumps({"malformed": False, "revision": 1}), encoding="utf-8")
        self.movie_catalogue_server = self.docker(
            "run", "--detach", "--name", "tanukarr-pack-movie-catalogue-" + secrets.token_hex(6),
            "--label", "tanukarr.download-pack-test=true", "--network", self.network,
            "--network-alias", "api.themoviedb.org", "--network-alias", "www.omdbapi.com", "--network-alias", "api.tvmaze.com",
            "--env", "PYTHONDONTWRITEBYTECODE=1", "--mount", f"type=bind,source={SCRIPTS},target=/runner,readonly",
            "--mount", f"type=bind,source={tls},target=/fixture", self.args.mock_image,
            "python", "-u", "/runner/movie-catalogues.py", "--serve-fixture", "/fixture")
        self.until(lambda: "Local movie catalogues TLS fixture ready" in self.docker("logs", self.movie_catalogue_server),
                   "Owned movie catalogue server did not start")
        settings = self.api_json("/api/v5/settings/metadata")
        settings.update(tmdbApiKey=self.metadata_key, omdbApiKey=self.metadata_key,
                        disabledMetadataSources=["tvdb", "tmdb", "myAnimeList"])
        self.api_json("/api/v5/settings/metadata/1", "PUT", settings)
        for source, identity, imdb in (("tmdbMovie", "970001", "tt9700001"), ("tmdbMovie", "970002", "tt9700002"),
                                       ("omdb", "tt9700101", "tt9700101")):
            query = urllib.parse.urlencode({"term": "imdb:" + imdb, "metadataSource": source})
            rows = self.api_json("/api/v5/series/lookup?" + query)
            resource = next(item for item in rows if item["foreignId"] == identity and item["metadataSource"] == source)
            resource.pop("path", None)
            resource.update(rootFolderPath="/media/films", qualityProfileId=self.profile_id, monitored=True,
                            seasonFolder=False, seriesType="standard", animeGroupingMode="automatic", tags=[self.movie_tag],
                            addOptions={"monitor": "all", "searchForMissingEpisodes": False})
            if source == "omdb":
                anchor = self.movie_members["970001"]
                resource.update(animeGroupingMode="manual", animeGroupId=anchor["animeGroupId"],
                                animeGroupTitle=anchor["animeGroupTitle"], animeGroupMemberKind="movie",
                                animeGroupMovieNumber=3, animeGroupKind=anchor["animeGroupKind"])
            movie = self.api_json("/api/v5/series", "POST", resource)
            self.until(lambda: len(self.episodes(movie)) == 1, "Provider-owned movie feature did not initialize")
            movie = self.get(movie)
            require(movie["mediaType"] == "movie" and movie["foreignId"] == identity and movie["imdbId"] == imdb,
                    "Actual IMDb lookup/movie addition changed catalogue identity")
            self.movie_members[identity] = movie
        require(len({movie["animeGroupId"] for movie in self.movie_members.values()}) == 1,
                "Movie download fixture did not create one verified collection")
        self.check("actual IMDb lookups add three monitored native TMDB/OMDb movies to one stored film collection")

    def configure_acquisition(self):
        # Small synthetic files use explicitly configured zero size minima. Real
        # media probing, sample checks, quality parsing and automatic import run.
        for version in ("v3",):
            for definition in self.api_json(f"/api/{version}/qualitydefinition"):
                definition.update(minSize=0, preferredSize=None, maxSize=None)
                self.api_json(f"/api/{version}/qualitydefinition/{definition['id']}", "PUT", definition)
        resource = next(item for item in self.api_json("/api/v5/indexer/schema") if item["implementation"] == "Nyaa")
        resource.update(name="Owned generated pack feed", enableRss=False, enableAutomaticSearch=True,
                        enableInteractiveSearch=True, priority=25)
        values = {"baseUrl": "http://fixture-seed:8989", "additionalParameters": "&cats=1_0&filter=1",
                  "animeStandardFormatSearch": True}
        for field in resource["fields"]:
            if field["name"] in values:
                field["value"] = values[field["name"]]
        self.indexer_id = self.api_json("/api/v5/indexer?skipTesting=true", "POST", resource)["id"]
        management = self.api_json("/api/v3/config/mediamanagement")
        management.update(recycleBin="", minimumFreeSpaceWhenImporting=100, skipFreeSpaceCheckWhenImporting=False)
        self.api_json("/api/v3/config/mediamanagement", "PUT", management)
        download = self.api_json("/api/v3/config/downloadclient")
        download.update(enableCompletedDownloadHandling=True)
        self.api_json("/api/v3/config/downloadclient", "PUT", download)
        self.check("normal native indexer/client acquisition and completed-download handling enabled only for owned feed")

    def request_records(self):
        path = self.seed_root / "requests.jsonl"
        return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line] if path.exists() else []

    def start_jellyfin(self):
        require(re.fullmatch(r"[^\s]+@sha256:[0-9a-f]{64}", self.args.jellyfin_image), "Jellyfin must be digest-pinned")
        self.jellyfin_image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.jellyfin_image)
        config = self.root / "jellyfin-config"
        cache = self.root / "jellyfin-cache"
        for folder in (config, cache, self.root / "media" / "Untouched"):
            folder.mkdir()
            folder.chmod(0o777)
        suffix = secrets.token_hex(6)
        self.jellyfin = self.docker(
            "run", "--detach", "--name", "tanukarr-pack-jellyfin-" + suffix,
            "--label", "tanukarr.download-pack-test=true", "--network", self.network,
            "--network-alias", "jellyfin-test", "--user", f"1000:{self.fixture_gid}",
            "--mount", f"type=bind,source={config},target=/config",
            "--mount", f"type=bind,source={cache},target=/cache",
            "--mount", f"type=bind,source={self.root / 'media'},target=/media,readonly", self.args.jellyfin_image)
        self.jellyfin_relay = self.docker(
            "create", "--name", "tanukarr-pack-jellyfin-relay-" + suffix,
            "--label", "tanukarr.download-pack-test=true", "--network", self.api_network,
            "--publish", "127.0.0.1::8096", "--mount", f"type=bind,source={SCRIPTS},target=/runner,readonly",
            self.args.mock_image, "python", "-u", "/runner/jellyfin-refresh.py", "--serve-relay")
        self.docker("network", "connect", self.network, self.jellyfin_relay)
        self.docker("start", self.jellyfin_relay)
        self.jellyfin_proxy = self.docker(
            "run", "--detach", "--name", "tanukarr-pack-jellyfin-recorder-" + suffix,
            "--label", "tanukarr.download-pack-test=true", "--network", self.network,
            "--network-alias", "fixture-jellyfin-recorder", "--env", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={SCRIPTS},target=/runner,readonly",
            "--mount", f"type=bind,source={self.root / 'fixture'},target=/fixture",
            self.args.mock_image, "python", "-u", "/runner/download-packs.py", "--serve-jellyfin-proxy", "/fixture")
        self.until(lambda: "Owned real Jellyfin request recorder ready" in self.docker("logs", self.jellyfin_proxy),
                   "Owned Jellyfin recorder did not start")
        binding = json.loads(self.docker("inspect", self.jellyfin_relay))[0]["NetworkSettings"]["Ports"]["8096/tcp"]
        require(len(binding) == 1 and binding[0]["HostIp"] == "127.0.0.1", "Jellyfin relay is not restricted to loopback")
        self.jellyfin_url = "http://127.0.0.1:" + binding[0]["HostPort"]

        def ready():
            try:
                return self.json_request(self.jellyfin_url, "/Startup/User")[0] == 200
            except (OSError, urllib.error.URLError):
                return False

        self.until(ready, "Real Jellyfin server did not start")
        self.jf("/Startup/User", "POST", {"Name": "generated-pack-audit", "Password": self.jellyfin_password})
        self.jf("/Startup/Configuration", "POST", {"ServerName": "Generated pack audit", "UICulture": "en-US",
                                                   "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
        self.jf("/Startup/Complete", "POST")
        auth = self.jf("/Users/AuthenticateByName", "POST", {"Username": "generated-pack-audit", "Pw": self.jellyfin_password})
        self.jellyfin_token = auth["AccessToken"]
        self.jf("/Auth/Keys?app=Tanukarr-pack-audit", "POST")
        self.jellyfin_token = next(item["AccessToken"] for item in self.jf("/Auth/Keys")["Items"]
                                   if item["AppName"] == "Tanukarr-pack-audit")
        library_paths = [("TV", "/media/library"), ("Untouched", "/media/Untouched")]
        if self.args.movies:
            library_paths.append(("Films", "/media/films"))
        for name, path in library_paths:
            options = {"LibraryOptions": {"EnableRealtimeMonitor": False, "SaveLocalMetadata": False,
                       "MetadataSavers": [], "TypeOptions": [
                           {"Type": kind, "MetadataFetchers": [], "ImageFetchers": []}
                           for kind in ("Series", "Season", "Episode", "Movie")]}}
            query = urllib.parse.urlencode({"name": name, "collectionType": "movies" if name == "Films" else "tvshows",
                                            "paths": path, "refreshLibrary": "false"})
            self.jf("/Library/VirtualFolders?" + query, "POST", options)
        self.libraries = {item["Name"]: item["ItemId"] for item in self.jf("/Library/VirtualFolders")}
        for leaf in ("library/Bootstrap Show/Season 01/Bootstrap Show - S01E01.mkv",
                     "Untouched/Baseline Show/Season 01/Baseline Show - S01E01.mkv"):
            path = self.root / "media" / leaf
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((self.seed_root / "template.mkv").read_bytes())
        if self.args.movies:
            path = self.root / "media/films/Bootstrap Feature (2010)/Bootstrap Feature (2010).mkv"
            path.parent.mkdir(parents=True)
            path.write_bytes((self.seed_root / "template.mkv").read_bytes())
        # Bootstrap this fresh, owned server before observing selected-library
        # refreshes. Application connections never call the global endpoint.
        self.jf("/Library/Refresh", "POST")
        self.until(lambda: self.count("TV") == 1 and self.count("Untouched") == 1
                   and (not self.args.movies or self.count("Films") == 1), "Jellyfin initial isolated libraries did not scan")
        self.until(lambda: not any(task["State"] == "Running" for task in self.jf("/ScheduledTasks")
                                   if task["Key"] == "RefreshLibrary"), "Jellyfin bootstrap scan did not finish")
        pending = self.root / "media" / "Untouched/Unscanned Show/Season 01/Unscanned Show - S01E01.mkv"
        pending.parent.mkdir(parents=True)
        pending.write_bytes((self.seed_root / "template.mkv").read_bytes())
        definition = next(item for item in self.api_json("/api/v5/connection/schema") if item["implementation"] == "Jellyfin")
        definition.update(name="Generated download refresh", onDownload=True, onUpgrade=True,
                          tags=[self.tv_tag] if self.tv_tag else [])
        values = {"host": "fixture-jellyfin-recorder", "port": 8096, "useSsl": False, "urlBase": "", "apiKey": self.jellyfin_token,
                  "libraryId": self.libraries["TV"], "refreshMode": 0, "refreshIntervalMinutes": 1}
        self.set_fields(definition, **values)
        self.api_json("/api/v5/connection/test", "POST", definition)
        self.definition = self.api_json("/api/v5/connection", "POST", definition)
        if self.args.movies:
            definition = next(item for item in self.api_json("/api/v5/connection/schema") if item["implementation"] == "Jellyfin")
            definition.update(name="Generated film refresh", onDownload=True, onUpgrade=True, tags=[self.movie_tag])
            self.set_fields(definition, **dict(values, libraryId=self.libraries["Films"]))
            self.api_json("/api/v5/connection/test", "POST", definition)
            self.movie_definition = self.api_json("/api/v5/connection", "POST", definition)
        self.check("real Jellyfin initializes selected and unselected libraries with automatic metadata and realtime scanning disabled")

    def save_refresh_mode(self, mode):
        self.set_fields(self.definition, apiKey=self.jellyfin_token, refreshMode=mode)
        self.definition = self.api_json("/api/v5/connection/" + str(self.definition["id"]), "PUT", self.definition)

    def record_refresh(self, phase):
        state = self.state()
        self.refresh_observations.append({"phase": phase, "selectedEpisodes": self.count("TV"),
                                          "unselectedEpisodes": self.count("Untouched"),
                                          "importedGeneration": state["ImportedGeneration"],
                                          "immediateGeneration": state["ImmediateGeneration"],
                                          "tickGeneration": state["TickGeneration"]})
        return state

    def jellyfin_requests(self):
        trace = self.root / "fixture" / "jellyfin-requests.jsonl"
        return [json.loads(line) for line in trace.read_text().splitlines() if line] if trace.exists() else []

    def require_import_not_blocked(self, info_hash):
        records = self.api_json("/api/v5/queue?includeUnknownSeriesItems=true")["records"]
        for record in records:
            if record.get("downloadId", "").lower() != info_hash or record.get("trackedDownloadState") != "importBlocked":
                continue
            reasons = [message for item in record.get("statusMessages", [])
                       for message in [item.get("title", ""), *item.get("messages", [])] if message]
            require(False, "Automatic import blocked: " + "; ".join(reasons))

    def transfer(self, release_id, version, requested_season=None):
        expected = next(item for item in self.catalogue if item["id"] == release_id)
        season = requested_season if requested_season is not None else expected["seasons"][0]
        require(season in expected["seasons"], "Requested season is outside the owned pack")
        path = f"/api/{version}/release?seriesId=1&seasonNumber={season}&seasonPacksOnly=true"
        results = self.api_json(path)
        entry = next((item for item in results if item.get("release", item).get("title") == expected["title"]), None)
        require(entry is not None, "Native pack search omitted generated release " + expected["title"])
        release = entry.get("release", entry)
        parsed = entry.get("parsedInfo", entry)
        require(parsed.get("fullSeason") is True and not parsed.get("episodeNumbers"),
                "Season/multi-season pack regex did not classify the whole pack")
        require(set(parsed.get("seasonNumbers") or [parsed.get("seasonNumber")]) == set(expected["seasons"]),
                "Pack regex changed native season coverage")
        if version == "v5":
            grab = {"guid": release["guid"], "indexerId": release["indexerId"],
                    "searchInfo": {"seriesId": 1, "seasonNumber": season}}
        else:
            grab = {"guid": release["guid"], "indexerId": release["indexerId"], "seriesId": 1, "seasonNumber": season}
        status, body = self.raw(f"/api/{version}/release", "POST", grab)
        require(200 <= status < 300, f"Normal {version} release grab failed with HTTP {status}: " + body.decode()[:300])
        info_hash = expected["infoHash"]
        completed = None

        def received():
            nonlocal completed
            entries = self.qb_json("torrents/info?" + urllib.parse.urlencode({"hashes": info_hash}))
            if entries and entries[0]["progress"] == 1:
                completed = entries[0]
                return True
            return False

        self.until(received, "Real qBittorrent did not complete the generated pack transfer")
        require(completed["completed"] == expected["size"] and completed["amount_left"] == 0,
                "Genuine client byte counts did not match generated pack")
        files = self.qb_json("torrents/files?" + urllib.parse.urlencode({"hash": info_hash}))
        require(len(files) == len(expected["files"]) and all(file["progress"] == 1 for file in files),
                "Genuine client did not download every native episode file")
        for file in expected["files"]:
            source = self.root / "media" / "incoming" / expected["title"] / file["name"]
            require(source.is_file() and hashlib.sha256(source.read_bytes()).hexdigest() == file["sha256"],
                    "Downloaded generated episode bytes differ from local seed")
        self.command({"name": "RefreshMonitoredDownloads"})
        expected_ids = {(season - 1) * 2 + number for number in (1, 2)}

        def imported():
            episodes = self.api_json("/api/v5/episode?seriesId=1")
            selected = [item for item in episodes if item["id"] in expected_ids]
            if len(selected) == len(expected_ids) and all(item["hasFile"] for item in selected):
                return True
            self.require_import_not_blocked(info_hash)
            return False

        self.until(imported, "Normal completed-download handling did not automatically import every pack file")
        imported_files = []
        for file in expected["files"]:
            if file["season"] != season:
                continue
            episode_id = (file["season"] - 1) * 2 + file["episode"]
            episode = self.api_json(f"/api/v5/episode/{episode_id}")
            imported_file = self.api_json(f"/api/v5/episodefile/{episode['episodeFileId']}")
            local = self.root / "media" / Path(imported_file["path"]).relative_to("/media")
            require(local.is_file() and hashlib.sha256(local.read_bytes()).hexdigest() == file["sha256"],
                    "Automatic import changed media bytes or assigned the wrong episode")
            require(f"/Season {file['season']:02d}/" in imported_file["path"],
                    "Native season naming chose the wrong directory: " + imported_file["path"])
            imported_files.append({"episodeId": episode_id, "path": imported_file["path"], "sha256": file["sha256"]})
        require(self.identities() == self.native_identity, "Download/import changed native episode numbering or identity")
        def history_recorded():
            history = self.api_json("/api/v5/history?page=1&pageSize=100")
            related = [item for item in history["records"] if item.get("downloadId", "").lower() == info_hash]
            return (any(item["eventType"] == "grabbed" for item in related)
                    and expected_ids <= {item.get("episodeId") for item in related if item["eventType"] == "downloadFolderImported"})

        # History handlers run asynchronously after episode-file persistence.
        self.until(history_recorded, "Normal grab and completed-download import history was not recorded")
        self.transfers.append({"api": version, "release": expected["title"], "nativeSeasons": expected["seasons"],
                               "requestedSeasons": [season],
                               "infoHash": info_hash, "receivedBytes": completed["completed"], "fileCount": len(files),
                               "automaticImport": True, "files": imported_files})
        self.check(f"{version} native pack {expected['seasons']} searched, grabbed and downloaded; requested season {season} automatically imported")

    def transfer_movie(self, release_id, version):
        expected = next(item for item in self.catalogue if item["id"] == release_id)
        collection_pack = len(expected["movies"]) > 1
        anchor = self.movie_members[expected["movies"][0]]
        identities = {identity: self.episode_identity(movie) for identity, movie in self.movie_members.items()}
        before_requests = len(self.request_records())
        before_refresh = len(self.jellyfin_requests()) if self.args.jellyfin else 0
        tv_generation = self.state()["ImportedGeneration"] if self.args.jellyfin else None
        path = f"/api/{version}/release?seriesId={anchor['id']}&" + ("completeSeries=true" if collection_pack else "seasonNumber=1")
        entries = self.api_json(path)
        entry = next((item for item in entries if item.get("release", item).get("title") == expected["title"]), None)
        require(entry is not None, "Native movie search omitted generated " + ("collection pack" if collection_pack else "feature"))
        terms = [record["query"].get("term", [""])[0] for record in self.request_records()[before_requests:]
                 if record["kind"] == "rss"]
        require(terms and all(not re.search(r"\bs\d+e\d+\b", term, re.IGNORECASE) for term in terms),
                "Movie release search used TV episode coordinates or made no real indexer query")
        release = entry.get("release", entry)
        context = {"seriesId": anchor["id"], **({"completeSeries": True} if collection_pack else {"seasonNumber": 1})}
        grab = {"guid": release["guid"], "indexerId": release["indexerId"]}
        if version == "v5":
            grab["searchInfo"] = context
        else:
            grab.update(context)
        status, body = self.raw(f"/api/{version}/release", "POST", grab)
        require(200 <= status < 300, f"Normal {version} movie grab returned HTTP {status}: " + body.decode()[:300])
        completed = None

        def received():
            nonlocal completed
            records = self.qb_json("torrents/info?" + urllib.parse.urlencode({"hashes": expected["infoHash"]}))
            if records and records[0]["progress"] == 1:
                completed = records[0]
                return True
            return False

        self.until(received, "Genuine client did not download the generated movie package")
        require(completed["completed"] == expected["size"] and completed["amount_left"] == 0,
                "Genuine client movie byte count differs from owned seed")
        for file in expected["files"]:
            source = self.root / "media/incoming" / expected["title"] / file["name"]
            require(source.is_file() and hashlib.sha256(source.read_bytes()).hexdigest() == file["sha256"],
                    "Downloaded movie bytes differ from owned seed")
        self.command({"name": "RefreshMonitoredDownloads"})
        def imported():
            if all(self.episodes(self.movie_members[identity])[0]["hasFile"] for identity in expected["movies"]):
                return True
            self.require_import_not_blocked(expected["infoHash"])
            return False

        self.until(imported, "Normal automatic import did not map each physical movie to its stored collection member")
        imported_files = []
        for file in expected["files"]:
            movie = self.movie_members[file["movieId"]]
            episode = self.episodes(movie)[0]
            imported = self.api_json(f"/api/v5/episodefile/{episode['episodeFileId']}")
            local = self.root / "media" / Path(imported["path"]).relative_to("/media")
            require(local.is_file() and hashlib.sha256(local.read_bytes()).hexdigest() == file["sha256"],
                    "Automatic movie import changed bytes or matched another film")
            require(local.name.startswith(f"{movie['title']} ({file['year']}) ") and "S01E01" not in local.name,
                    "Movie naming used episodic coordinates or discarded its title/year")
            require(local.parent.as_posix() == (self.root / "media" / Path(movie["path"]).relative_to("/media")).as_posix(),
                    "Movie import introduced a native season directory")
            self.command({"name": "RescanSeries", "seriesId": movie["id"]})
            rescanned = self.episodes(movie)[0]
            require(rescanned["hasFile"] and rescanned["episodeFileId"] == episode["episodeFileId"],
                    "Native movie rescan lost the feature file identity")
            imported_files.append({"metadataSource": movie["metadataSource"], "foreignId": movie["foreignId"],
                                   "imdbId": movie["imdbId"], "episodeId": episode["id"],
                                   "path": imported["path"], "sha256": file["sha256"]})
        require(all(self.episode_identity(movie) == identities[identity] for identity, movie in self.movie_members.items()),
                "Movie download/import changed a native feature identity")
        require(self.identities() == self.native_identity, "Movie download/import changed native TV episode identities")
        self.movie_transfers.append({"api": version, "release": expected["title"], "collectionPack": collection_pack,
                                     "queryTerms": terms, "infoHash": expected["infoHash"], "receivedBytes": completed["completed"],
                                     "automaticImport": True, "files": imported_files})
        if self.args.jellyfin:
            expected_count = 4 if collection_pack else 2
            self.until(lambda: self.count("Films") == expected_count, "Real Jellyfin movie-library refresh missed automatically imported films")
            observed = {"phase": "movie collection pack" if collection_pack else "single movie",
                        "selectedMovies": self.count("Films"), "selectedTvEpisodes": self.count("TV"),
                        "unselectedEpisodes": self.count("Untouched")}
            self.refresh_observations.append(observed)
            require(observed["selectedTvEpisodes"] == 9 and observed["unselectedEpisodes"] == 1,
                    "Movie refresh changed library counts: " + json.dumps(observed))
            refreshes = [item for item in self.jellyfin_requests()[before_refresh:]
                         if item["method"] == "POST" and item["path"].endswith("/Refresh")]
            selected_path = "/Items/" + self.libraries["Films"].replace("-", "") + "/Refresh"
            require(len(refreshes) == len(expected["movies"])
                    and all(item["path"] == selected_path and item["status"] == 204 for item in refreshes),
                    "Movie imports did not refresh only their selected real Jellyfin library: " + json.dumps(refreshes))
            require(self.state()["ImportedGeneration"] == tv_generation,
                    "Tagged movie import scheduled the TV connection")
            observed["actualRefreshRequests"] = refreshes
        self.check(f"{version} native movie {'collection pack' if collection_pack else 'feature'} downloaded, mapped, named and automatically imported")

    def verify(self):
        self.transfer(1, "v5")
        if self.args.jellyfin:
            self.until(lambda: self.count("TV") == 3, "Immediate Jellyfin refresh missed automatically imported season 1")
            require(self.count("Untouched") == 1, "Immediate pack refresh reached unselected library")
            self.record_refresh("immediate season pack")
            self.save_refresh_mode(1)
        self.transfer(2, "v3")
        if self.args.jellyfin:
            pending = self.record_refresh("pending periodic season pack")
            require(self.count("TV") == 3 and pending["ImportedGeneration"] > pending["TickGeneration"],
                    "Periodic mode did not retain pending automatic-import refresh")
            self.docker("restart", self.container)
            self.refresh_binding()
            self.wait_ready()
            require(self.state()["ImportedGeneration"] == pending["ImportedGeneration"], "Restart discarded pending pack refresh")
            self.until(lambda: self.count("TV") == 5, "Real scheduler did not refresh completed pack after application restart")
            require(self.count("Untouched") == 1, "Periodic pack refresh reached unselected library")
            self.record_refresh("periodic season pack after restart")
            self.check("real scheduler refreshes downloaded season pack after restart without scanning other libraries")
            self.save_refresh_mode(0)
            self.docker("stop", self.jellyfin)
        self.transfer(3, "v5")
        require(all(not item["hasFile"] for item in self.api_json("/api/v5/episode?seriesId=1") if item["seasonNumber"] == 4),
                "Season 3 pack grab imported unrequested native season 4")
        self.check("multi-season source remains reusable while an unrequested season stays outside the library")
        self.transfer(3, "v3", requested_season=4)
        if self.args.jellyfin:
            state = self.state()
            require(state["ImportedGeneration"] > state["ImmediateGeneration"], "Offline Jellyfin discarded pending multi-season import refresh")
            self.docker("start", self.jellyfin)

            def recovered():
                try:
                    return self.json_request(self.jellyfin_url, "/Library/VirtualFolders", token=self.jellyfin_token)[0] == 200
                except (OSError, urllib.error.URLError):
                    return False

            self.until(recovered, "Jellyfin did not recover after failure injection")
            self.command({"name": "ProcessPendingJellyfinRefresh"})
            self.until(lambda: self.count("TV") == 9, "Jellyfin retry missed automatically imported multi-season pack")
            require(self.count("Untouched") == 1, "Recovery refresh reached unselected library")
            self.record_refresh("recovered multi-season pack")
            self.check("multi-season import succeeds while Jellyfin is unavailable and selected refresh recovers pending work")
        if self.args.movies:
            tv_generation = self.state()["ImportedGeneration"] if self.args.jellyfin else None
            if self.args.jellyfin:
                # Counts become visible while Jellyfin is still scanning. Let
                # the preceding TV refresh finish before planting the sentinel
                # used to detect a movie-triggered cross-library refresh.
                self.until(lambda: not any(task["State"] == "Running" for task in self.jf("/ScheduledTasks")
                                           if task["Key"] == "RefreshLibrary"), "Prior TV refresh did not finish")
            self.transfer_movie(4, "v3")
            self.transfer_movie(5, "v5")
            if self.args.jellyfin:
                require(self.state()["ImportedGeneration"] == tv_generation, "Tagged movie imports scheduled the TV connection")
                self.check("movie and TV tag filters isolate selected Jellyfin libraries after genuine automatic downloads")
        self.command({"name": "RescanSeries", "seriesId": 1})
        require(self.identities() == self.native_identity, "Rescan changed native pack identities")
        require(all(item["hasFile"] for item in self.api_json("/api/v5/episode?seriesId=1")), "Rescan lost imported pack files")
        self.payload_requests = self.request_records()
        self.refresh_requests = self.jellyfin_requests() if self.args.jellyfin else []
        require(sum(item["bytes"] for item in self.payload_requests if item["kind"] == "payload" and item["status"] in (200, 206))
                >= sum(item["size"] for item in self.catalogue), "No actual local payload download was observed")
        require(all(item["kind"] in ("rss", "torrent", "payload") and item["status"] in (200, 206)
                    for item in self.payload_requests), "Fixture received an unexpected network request")
        self.check("post-download native rescan retains all eight files and payload server records genuine transfers")

    def cleanup(self):
        failures = []
        for attribute in ("jellyfin_relay", "jellyfin_proxy", "jellyfin", "movie_catalogue_server"):
            resource = getattr(self, attribute)
            if resource and not self.args.keep:
                try:
                    if attribute == "jellyfin":
                        self.docker("exec", "--user", "0:0", resource, "chmod", "-R", "a+rwX", "/config", "/cache")
                    self.docker("rm", "--force", resource)
                    setattr(self, attribute, None)
                except Exception as error:
                    failures.append(self.redact(str(error)))
        if self.seed_server and not self.args.keep:
            try:
                self.docker("rm", "--force", self.seed_server)
                self.seed_server = None
            except Exception as error:
                failures.append(self.redact(str(error)))
        try:
            super().cleanup()
        except Exception as error:
            failures.append(self.redact(str(error)))
        self.cleanup_complete = self.cleaned and all(item is None for item in (
            self.seed_server, self.jellyfin, self.jellyfin_relay, self.jellyfin_proxy, self.movie_catalogue_server))
        require(not failures and (self.args.keep or self.cleanup_complete), "Pack fixture cleanup failed: " + "; ".join(failures))


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "--serve-fixture":
        serve_fixture(Path(sys.argv[2]))
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=CLIENT.MOCK_IMAGE)
    parser.add_argument("--client-image", default=CLIENT.QB_IMAGE)
    parser.add_argument("--jellyfin", action="store_true", help="Verify actual selected-library refresh following automatic downloads")
    parser.add_argument("--movies", action="store_true", help="Download native single movies and a same-collection movie pack")
    parser.add_argument("--jellyfin-image", default=JF_REFRESH.JF_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    require(args.timeout > 0, "--timeout must be positive")
    require(args.report is None or not args.report.exists(), "Use a fresh report path to preserve earlier evidence")
    audit = DownloadPacks(args)
    error = None
    logs = ""
    try:
        audit.start()
        audit.prepare()
        audit.start_client()
        audit.clients()
        audit.seed_native_series()
        if args.movies:
            audit.start_movie_catalogues()
        audit.generate_payloads()
        audit.configure_acquisition()
        if args.jellyfin:
            audit.start_jellyfin()
        audit.verify()
    except (Exception, KeyboardInterrupt) as caught:
        error = audit.redact(audit.last_request + ": " + (str(caught) or type(caught).__name__))
        print("FAIL " + error, file=sys.stderr)
    finally:
        if audit.root and args.jellyfin:
            audit.refresh_requests = audit.jellyfin_requests()
        if audit.container:
            try:
                logs = audit.redact(audit.docker("logs", audit.container))
            except Exception:
                pass
        try:
            audit.cleanup()
        except Exception as caught:
            error = error or audit.redact(str(caught))
        if args.report:
            report = {"createdUtc": datetime.now(timezone.utc).isoformat(), "image": args.image,
                      "imageId": audit.image_id, "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                      "runtime": audit.runtime_metadata, "passed": error is None, "error": error, "checks": audit.results,
                      "clientImage": args.client_image, "clientImageId": audit.qb_image_id,
                      "mockImage": args.mock_image, "mockImageId": audit.mock_id,
                      "transfers": audit.transfers, "nativeEpisodeIdentities": audit.native_identity,
                      "movieTransfers": audit.movie_transfers,
                      "payloadRequests": audit.payload_requests, "cleanupComplete": audit.cleanup_complete,
                      "jellyfinImage": args.jellyfin_image if args.jellyfin else None,
                      "jellyfinImageId": audit.jellyfin_image_id, "refreshObservations": audit.refresh_observations,
                      "actualJellyfinRequests": audit.refresh_requests,
                      "scope": {"genuineQbittorrent": True, "generatedMediaOnly": True,
                                "localWebSeedTransfers": True, "normalReleaseGrab": True,
                                "automaticCompletedDownloadImport": True, "publicDownloads": False,
                                "realJellyfinAfterAutomaticDownload": args.jellyfin,
                                "nativeMovieAndCollectionPackDownloads": args.movies,
                                "ownerConfigurationAccessed": False, "externalProviderRouting": False},
                      "keptData": str(audit.root) if args.keep and audit.root else None}
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(audit.redact(json.dumps(report, indent=2)) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
