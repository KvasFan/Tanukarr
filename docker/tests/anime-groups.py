#!/usr/bin/env python3
"""Verify anime groups with disposable Docker data and a local trusted TLS catalogue."""

import argparse
from datetime import datetime, timezone
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import secrets
import select
import socket
import socketserver
import ssl
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse

from smoke import ContainerSmoke, SmokeFailure, require


CATALOGUE = {
    900101: ("Fixture Anime", "TV", 2, [("SEQUEL", 900102)]),
    900102: ("Fixture Anime Sequel", "TV", 3, [
        ("PREQUEL", 900101), ("SEQUEL", 900103), ("SEQUEL", 900111),
        ("SEQUEL", 900112), ("SPIN_OFF", 900113),
    ]),
    900103: ("Fixture Anime Finale", "ONA", 1, [("PREQUEL", 900102)]),
    900111: ("Fixture Anime Special", "SPECIAL", 1, [("PREQUEL", 900102)]),
    900112: ("Fixture Anime Movie", "MOVIE", 1, [("PREQUEL", 900102)]),
    900113: ("Fixture Anime Spinoff", "TV", 1, [("PARENT", 900102)]),
    900201: ("Branched Fixture Anime", "TV", 2, [("SEQUEL", 900202), ("SEQUEL", 900203)]),
    900202: ("Branched Fixture Route A", "TV", 1, [("PREQUEL", 900201)]),
    900203: ("Branched Fixture Route B", "ONA", 1, [("PREQUEL", 900201)]),
    900301: ("Manual Fixture Member", "TV", 2, []),
    900302: ("New Manual Fixture Member", "TV", 2, []),
    900303: ("Occupied Manual Fixture Member", "TV", 2, []),
    900304: ("Existing Explicit Fixture Member", "TV", 2, []),
    900401: ("Fixture Linear Anime", "TV", 2, [("SEQUEL", 900402)]),
    900402: ("Fixture Linear Anime Sequel", "TV", 3, [("PREQUEL", 900401), ("SEQUEL", 900403)]),
    900403: ("Fixture Linear Anime Finale", "ONA", 1, [("PREQUEL", 900402)]),
    900501: ("Mixed Fixture Anime", "TV", 2, [("SEQUEL", 900502)]),
    900502: ("Mixed Fixture Anime Movie", "MOVIE", None, [("PREQUEL", 900501), ("SEQUEL", 900503)]),
    900503: ("Mixed Fixture Anime Finale", "TV", 3, [("PREQUEL", 900502)]),
    900601: ("Fixture Movie Saga", "MOVIE", 1, [("SEQUEL", 900602)]),
    900602: ("Fixture Movie Saga Two", "MOVIE", None, [("PREQUEL", 900601)]),
    900701: ("CON", "MOVIE", 1, []),
    900702: ("Occupied Movie Fixture", "MOVIE", 1, []),
    900703: ("Safe/Film", "MOVIE", 1, []),
    900704: ("Safe+Film", "MOVIE", 1, []),
    900801: ("Configurable Fixture Anime", "TV", 2, [("SEQUEL", 900802)]),
    900802: ("Configurable Fixture Anime Sequel", "TV", 2, [("PREQUEL", 900801)]),
    900901: ("Configurable Movie Saga", "MOVIE", 1, [("SEQUEL", 900902)]),
    900902: ("Configurable Movie Saga Two", "MOVIE", 1, [("PREQUEL", 900901)]),
    901001: ("Tokyo Revengers", "TV", 2, [("SEQUEL", 901002)]),
    901002: ("Tokyo Revengers: War of the Three Titans Arc", "TV", 2, [("PREQUEL", 901001)]),
    901101: ("The Apothecary Diaries", "TV", 2, [("SEQUEL", 901102)]),
    901102: ("The Apothecary Diaries Season 2", "TV", 2, [("PREQUEL", 901101), ("SEQUEL", 901103)]),
    901103: ("The Apothecary Diaries Season 3", "TV", 2, [("PREQUEL", 901102)]),
}
GROUP_FIELDS = ("animeGroupingMode", "animeGroupId", "animeGroupTitle", "animeGroupSeasonNumber", "animeGroupFolder",
                "animeGroupMemberKind", "animeGroupKind", "animeGroupMovieNumber", "animeGroupMovieFolderStyle",
                "animeGroupMemberFolder")
NAMING_FIELDS = ("animeGroupFolderFormat", "animeGroupSeasonFolderFormat", "animeGroupArcFolderFormat",
                 "animeGroupMovieFolderFormat", "animeGroupMovieSagaFolderFormat")


def utc_now():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def media_resource(media_id):
    title, format_name, count, relations = CATALOGUE[media_id]
    return {
        "id": media_id, "idMal": None, "type": "ANIME", "format": format_name,
        "title": {"english": title, "romaji": title, "native": title, "userPreferred": title},
        "synonyms": [], "description": "Generated local anime-group runtime fixture.",
        "status": "FINISHED", "episodes": count, "duration": 24,
        "startDate": {"year": 2024, "month": 1, "day": 1},
        "endDate": {"year": 2024, "month": 1, "day": 21},
        "countryOfOrigin": "JP", "genres": ["Animation"], "averageScore": 80, "popularity": 100,
        "coverImage": {}, "bannerImage": None, "studios": {"nodes": []},
        "airingSchedule": {"nodes": [
            {"episode": number, "airingAt": 1704067200 + (number - 1) * 604800}
            for number in range(1, (count or 0) + 1)
        ]},
        "relations": {"edges": [
            {"relationType": kind, "node": {"id": target, "type": "ANIME", "format": CATALOGUE[target][1]}}
            for kind, target in relations
        ]},
    }


def serve_fixture(root):
    """Private Docker sidecar: no account tokens, HTTP forwarding or external requests."""
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
            self.end_headers()
            self.wfile.write(body)

        def do_POST(self):
            record = {"utc": utc_now(), "path": self.path, "status": 400}
            try:
                length = int(self.headers.get("Content-Length", "0"))
                require(0 < length < 65536 and self.path == "/", "Unexpected local fixture request")
                request = json.loads(self.rfile.read(length))
                query = request.get("query", "")
                variables = request.get("variables", {})
                require(isinstance(query, str) and isinstance(variables, dict), "Invalid GraphQL fixture payload")
                if "Media(" in query:
                    media_id = int(variables["id"])
                    require(media_id in CATALOGUE, "Unknown local catalogue ID")
                    record.update(kind="relations" if "relationType" in query else "metadata", mediaId=media_id)
                    response = {"data": {"Media": media_resource(media_id)}}
                elif "media(search:" in query:
                    title = variables.get("search")
                    matches = [media_resource(media_id) for media_id, node in CATALOGUE.items() if node[0] == title]
                    require(len(matches) == 1, "Unknown local catalogue search")
                    record.update(kind="search", mediaId=matches[0]["id"])
                    response = {"data": {"Page": {"media": matches}}}
                elif "User(name:" in query and "mediaListOptions" in query:
                    require(variables.get("username") == "fixture-array", "Unknown synthetic fixture username")
                    record.update(kind="custom-list-names")
                    response = {"data": {"User": {"mediaListOptions": {"animeList": {
                        "customLists": ["DaiBaNanaTop", "Fixture optional"]}}}}}
                elif "mediaList(userName:" in query:
                    username = variables.get("id")
                    require(username in ("fixture-array", "fixture-unknown") and variables.get("page") == 1,
                            "Unknown synthetic list fixture")
                    require("customListEntries:" in query, "Typed custom-list membership alias was not requested")
                    rows = []
                    for media_id, membership in ((900101, True), (900102, False), (900502, None)):
                        rows.append({"status": "CURRENT", "progress": 0, "media": media_resource(media_id),
                                     "customLists": None, "customListEntries": None if username == "fixture-unknown"
                                     else [] if membership is None else [{"name": "DaiBaNanaTop", "enabled": membership}]})
                    record.update(kind="custom-list-entries", entryCount=len(rows), unknownMembership=username == "fixture-unknown")
                    response = {"data": {"Page": {"pageInfo": {"currentPage": 1, "hasNextPage": False}, "mediaList": rows}}}
                else:
                    raise SmokeFailure("Only catalogue and synthetic list fixtures are supported")
                record["status"] = 200
                self.answer(200, response)
            except (KeyError, TypeError, ValueError, SmokeFailure):
                self.answer(400, {"errors": [{"message": "Unsupported local fixture request"}]})
            finally:
                with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                    output.write(json.dumps(record) + "\n")

        def do_GET(self):
            self.answer(400, {"errors": [{"message": "Only local GraphQL POST is supported"}]})

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local anime grouping TLS fixture ready", flush=True)
    server.serve_forever()


def serve_relay():
    """Expose the fixture app without giving the app an external network route."""
    class Relay(socketserver.BaseRequestHandler):
        def handle(self):
            with socket.create_connection(("fixture-app", 8989), timeout=30) as destination:
                peers = (self.request, destination)
                while True:
                    ready, _, _ = select.select(peers, [], [], 60)
                    if not ready:
                        return
                    for source in ready:
                        chunk = source.recv(65536)
                        if not chunk:
                            return
                        (destination if source is self.request else self.request).sendall(chunk)

    class Server(socketserver.ThreadingTCPServer):
        allow_reuse_address = True
        daemon_threads = True

    with Server(("0.0.0.0", 8989), Relay) as server:
        print("Local fixture API relay ready", flush=True)
        server.serve_forever()


class AnimeGroups(ContainerSmoke):
    def __init__(self, args):
        super().__init__(args)
        self.network = None
        self.api_network = None
        self.relay = None
        self.mock = None
        self.mock_id = None
        self.image_labels = {}
        self.series = {}
        self.initial_group = []
        self.episode_snapshots = {}
        self.imported_file = None
        self.imported_hash = None
        self.imported_movies = []
        self.list_test_observations = {}
        self.naming_snapshot = None
        self.last_request = "startup"

    def raw(self, path, method="GET", data=None, api_key=True):
        self.last_request = f"{method} {path.split('?')[0]}"
        parsed = urllib.parse.urlsplit(self.base_url)
        connection = http.client.HTTPConnection(parsed.hostname, parsed.port, timeout=self.args.timeout)
        headers = {"Content-Type": "application/json"}
        if api_key is True:
            headers["X-Api-Key"] = self.api_key
        elif isinstance(api_key, str):
            headers["X-Api-Key"] = api_key
        payload = None if data is None else json.dumps(data).encode("utf-8")
        try:
            connection.request(method, path, body=payload, headers=headers)
            response = connection.getresponse()
            return response.status, response.read()
        finally:
            connection.close()

    def api_json(self, path, method="GET", data=None):
        status, body = self.raw(path, method, data)
        require(200 <= status < 300, f"{self.last_request} returned HTTP {status}")
        return json.loads(body) if body else None

    def until(self, callback, message):
        deadline = time.monotonic() + self.args.timeout
        while time.monotonic() < deadline:
            if callback():
                return
            time.sleep(1)
        raise SmokeFailure(message)

    def command(self, payload):
        command = self.api_json("/api/v5/command", "POST", payload)
        state = {}

        def completed():
            nonlocal state
            state = self.api_json("/api/v5/command/" + str(command["id"]))
            return state["status"] in ("completed", "failed", "aborted")

        self.until(completed, payload["name"] + " command timed out")
        require(state["status"] == "completed", payload["name"] + " command did not complete")

    def create_tls(self):
        tls = self.root / "fixture"
        tls.mkdir()
        extensions = tls / "server.ext"
        extensions.write_text(
            "basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n"
            "extendedKeyUsage=serverAuth\nsubjectAltName=DNS:graphql.anilist.co\n", encoding="utf-8")
        commands = [
            [self.args.openssl, "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
             "-subj", "/CN=Tanukarr disposable anime grouping CA", "-addext", "basicConstraints=critical,CA:TRUE",
             "-addext", "keyUsage=critical,keyCertSign,cRLSign", "-keyout", str(tls / "ca.key"), "-out", str(tls / "ca.crt")],
            [self.args.openssl, "req", "-new", "-newkey", "rsa:2048", "-nodes", "-subj", "/CN=graphql.anilist.co",
             "-keyout", str(tls / "server.key"), "-out", str(tls / "server.csr")],
            [self.args.openssl, "x509", "-req", "-days", "1", "-in", str(tls / "server.csr"),
             "-CA", str(tls / "ca.crt"), "-CAkey", str(tls / "ca.key"), "-CAcreateserial",
             "-extfile", str(extensions), "-out", str(tls / "server.crt")],
        ]
        for command in commands:
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            require(result.returncode == 0, "Could not generate the disposable fixture certificate")
        (tls / "ca.crt").chmod(0o644)
        return tls

    def start(self):
        require(self.docker("info", "--format", "{{.OSType}}") == "linux", "Linux Docker required")
        image = json.loads(self.docker("image", "inspect", self.args.image))[0]
        self.image_id = image["Id"]
        self.image_labels = image["Config"].get("Labels", {}) or {}
        require(image["Os"] == "linux", "An already built Linux Tanukarr image is required")
        self.mock_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.mock_image)
        self.root = Path(tempfile.mkdtemp(prefix="sonarr-container-smoke-anime-groups-")).resolve()
        config = self.root / "config"
        media = self.root / "media"
        config.mkdir()
        media.mkdir()
        media.chmod(0o777)
        for name in ("library", "incoming", "explicit"):
            folder = media / name
            folder.mkdir()
            folder.chmod(0o777)
        (config / "config.xml").write_text(
            "<Config><BindAddress>*</BindAddress><Port>8989</Port>"
            f"<ApiKey>{self.api_key}</ApiKey><LaunchBrowser>False</LaunchBrowser>"
            "<AuthenticationMethod>Forms</AuthenticationMethod><AuthenticationRequired>Enabled</AuthenticationRequired>"
            "<AllowedHosts>127.0.0.1,localhost</AllowedHosts><AnalyticsEnabled>False</AnalyticsEnabled>"
            "<LogLevel>Debug</LogLevel></Config>", encoding="utf-8")
        tls = self.create_tls()
        suffix = secrets.token_hex(6)
        self.network = self.docker("network", "create", "--internal", "tanukarr-anime-groups-" + suffix)
        self.mock = self.docker(
            "run", "--detach", "--name", "tanukarr-anime-graph-" + suffix,
            "--label", "tanukarr.anime-group-test=true", "--network", self.network,
            "--network-alias", "graphql.anilist.co", "--env", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            "--mount", f"type=bind,source={tls},target=/fixture", self.args.mock_image,
            "python", "-u", "/runner/anime-groups.py", "--serve-fixture", "/fixture")
        self.until(lambda: "Local anime grouping TLS fixture ready" in self.docker("logs", self.mock),
                   "Local GraphQL fixture did not become ready")
        startup = (
            "set -eu; cp /fixture-ca.crt /usr/local/share/ca-certificates/tanukarr-anime-group-fixture.crt; "
            "update-ca-certificates >/dev/null 2>&1; exec /entrypoint.sh /app/Sonarr -nobrowser -data=/config"
        )
        self.container = self.docker(
            "create", "--name", "tanukarr-anime-groups-" + suffix,
            "--label", "tanukarr.anime-group-test=true", "--network", self.network,
            "--network-alias", "fixture-app", "--env", "PUID=1000", "--env", "PGID=1000", "--env", "UMASK=002",
            "--env", "HTTP_PROXY=", "--env", "HTTPS_PROXY=", "--env", "ALL_PROXY=",
            "--mount", f"type=bind,source={config},target=/config",
            "--mount", f"type=bind,source={media},target=/media",
            "--mount", f"type=bind,source={tls / 'ca.crt'},target=/fixture-ca.crt,readonly",
            "--entrypoint", "/bin/sh", self.args.image, "-c", startup)
        self.docker("start", self.container)
        self.api_network = self.docker("network", "create", "tanukarr-fixture-api-" + suffix)
        self.relay = self.docker(
            "create", "--name", "tanukarr-fixture-relay-" + suffix,
            "--label", "tanukarr.anime-group-test=true", "--network", self.api_network,
            "--publish", "127.0.0.1::8989", "--mount",
            f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            self.args.mock_image, "python", "-u", "/runner/anime-groups.py", "--serve-relay")
        self.docker("network", "connect", self.network, self.relay)
        self.docker("start", self.relay)
        self.refresh_binding()
        self.wait_ready()
        require(json.loads(self.docker("network", "inspect", self.network))[0]["Internal"],
                "The fixture network unexpectedly allows external routing")
        mock_address = json.loads(self.docker("inspect", self.mock))[0]["NetworkSettings"]["Networks"]
        mock_ip = next(iter(mock_address.values()))["IPAddress"]
        resolved = self.docker("exec", self.container, "getent", "hosts", "graphql.anilist.co")
        require(mock_ip in resolved, "AniList DNS did not resolve to the isolated mock")
        self.check("isolated startup, loopback-only API, internal network and trusted local AniList TLS")

    def refresh_binding(self):
        binding = json.loads(self.docker("inspect", self.relay))[0]["NetworkSettings"]["Ports"]["8989/tcp"]
        require(binding and len(binding) == 1 and binding[0]["HostIp"] == "127.0.0.1", "Fixture relay must bind only to loopback")
        self.base_url = "http://127.0.0.1:" + binding[0]["HostPort"]

    def prepare(self):
        status = self.api_json("/api/v5/system/status")
        self.runtime_metadata = {key: status[key] for key in (
            "appName", "productName", "productVersion", "version", "runtimeVersion", "migrationVersion"
        ) if key in status}
        require(self.api_json("/api/v5/series") == [], "Disposable instance unexpectedly contains existing series")
        require(self.api_json("/api/v5/indexer") == [] and self.api_json("/api/v5/downloadclient") == [],
                "Disposable instance has acquisition configuration")
        self.api_json("/api/v5/rootfolder", "POST", {"path": "/media/library"})
        self.api_json("/api/v5/rootfolder", "POST", {"path": "/media/explicit"})
        self.profile_id = self.api_json("/api/v5/qualityprofile")[0]["id"]
        naming = self.api_json("/api/v5/settings/naming")
        naming.update(renameEpisodes=True, animeEpisodeFormat="{Series Title} - S{season:00}E{episode:00}")
        self.api_json("/api/v5/settings/naming/1", "PUT", naming)
        self.check("empty library, no acquisition providers and explicit native seasonal filename format")

    def preview(self):
        for version in ("v3", "v5"):
            path = f"/api/{version}/series/grouping?metadataSource=aniList&foreignId=900402"
            for key in (False, "invalid-anime-group-key"):
                require(self.raw(path, api_key=key)[0] == 401, "Grouping preview accepted an invalid/missing API key")
            result = self.api_json(path)
            require(result["isResolved"] and result["animeGroupId"] == "anilist:900401"
                    and result["animeGroupSeasonNumber"] == 2, "Grouping preview did not resolve the official chain")
        self.check("v3/v5 authenticated grouping previews and missing/invalid-key 401 responses")

    def add(self, media_id, mode="automatic", fields=None, version="v5", expected_status=None):
        title = CATALOGUE[media_id][0]
        query = urllib.parse.urlencode({"term": title, "metadataSource": "aniList"})
        resources = self.api_json(f"/api/{version}/series/lookup?" + query)
        resource = next(item for item in resources if str(item.get("foreignId")) == str(media_id))
        resource.pop("path", None)
        resource.update(rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                        monitored=False, seasonFolder=True, seriesType="anime", animeGroupingMode=mode,
                        addOptions={"monitor": "none", "searchForMissingEpisodes": False})
        resource.update(fields or {})
        status, raw = self.raw(f"/api/{version}/series", "POST", resource)
        if expected_status is not None:
            require(status == expected_status, f"Expected addition HTTP {expected_status}, received {status}")
            return None
        require(200 <= status < 300, f"Catalogue addition returned HTTP {status}")
        added = json.loads(raw)
        self.series[media_id] = added
        expected_count = CATALOGUE[media_id][2] or (1 if CATALOGUE[media_id][1] == "MOVIE" else 0)
        self.until(lambda: len(self.episodes(added)) == expected_count, "Native fixture episode count did not initialize")
        self.episode_snapshots[media_id] = self.episode_identity(added)
        return added

    def get(self, series, version="v5"):
        return self.api_json(f"/api/{version}/series/{series['id']}")

    def episodes(self, series):
        return self.api_json(f"/api/v5/episode?seriesId={series['id']}")

    def episode_identity(self, series):
        return sorted((item["id"], str(item.get("foreignId")), item["seasonNumber"], item["episodeNumber"],
                       item.get("absoluteEpisodeNumber")) for item in self.episodes(series))

    def automatic(self):
        forged = {"animeGroupId": "manual:forged", "animeGroupTitle": "Forged", "animeGroupSeasonNumber": 99,
                  "animeGroupFolder": "../../escaped-client-folder"}
        for season, media_id in enumerate((900401, 900402, 900403), 1):
            series = self.add(media_id, fields=forged if season == 1 else None)
            expected_path = f"/media/library/Fixture Linear Anime/Season {season:02d}"
            require(series["animeGroupId"] == "anilist:900401" and series["animeGroupSeasonNumber"] == season,
                    "Automatic members did not share distinct canonical group seasons")
            require(series["animeGroupFolder"] == "Fixture Linear Anime" and series["path"] == expected_path,
                    "Client-supplied automatic membership/folder changed the server-generated path")
            require(series["seasonFolder"] is False and series["metadataSource"] == "aniList"
                    and series["foreignId"] == str(media_id), "Grouping replaced native owner/identity or nested-season setting")
            identity = self.episode_snapshots[media_id]
            require(all(item[2] == 1 and item[3] == item[4] for item in identity),
                    "A group-season position replaced native seasonal/absolute episode numbers")
            self.initial_group.append({key: series.get(key) for key in (
                "id", "title", "metadataSource", "foreignId", "path", "seasonFolder", *GROUP_FIELDS)})
        require(len({item["id"] for item in self.initial_group}) == 3, "Grouping merged native series records")
        self.check("three native catalogue entries share official group seasons 1/2/3 with isolated generated leaves")
        self.check("server-owned membership ignores forged client folders and preserves native IDs/counts/S01 numbering")
        for season, media_id in enumerate((900101, 900102, 900103), 1):
            series = self.add(media_id)
            require(series["animeGroupId"] == "anilist:900101" and series["animeGroupSeasonNumber"] == season
                    and series["animeGroupMemberKind"] == "season" and series["animeGroupKind"] == "series",
                    "A parallel movie candidate disrupted the valid preferred TV spine")
            require(series["path"] == f"/media/library/Fixture Anime/Season {season:02d}"
                    and series["seasonFolder"] is False and series["foreignId"] == str(media_id)
                    and all(item[2] == 1 for item in self.episode_snapshots[media_id]),
                    "Preferred TV-spine grouping changed native identity or generated the wrong leaf")
        self.check("preferred official TV spine retains seasons 1/2/3 while an ambiguous parallel movie requires manual grouping")
        for media_id in (900111, 900112, 900113, 900201):
            series = self.add(media_id)
            require(not series.get("animeGroupId") and not series.get("animeGroupSeasonNumber"),
                    "Special/spinoff, ambiguous parallel movie or true TV/TV branch was automatically grouped")
        self.check("specials, spin-offs, ambiguous parallel movie and true TV/TV branch remain independent")
        for media_id, kind, number, leaf in ((900501, "season", 1, "Season 01"),
                                           (900502, "movie", 1, "Mixed Fixture Anime Movie"),
                                           (900503, "season", 2, "Season 02")):
            series = self.add(media_id)
            require(series["animeGroupId"] == "anilist:900501" and series["animeGroupKind"] == "series"
                    and series["animeGroupMemberKind"] == kind, "Mixed chain did not retain distinct typed members")
            ordinal = series.get("animeGroupMovieNumber" if kind == "movie" else "animeGroupSeasonNumber")
            require(ordinal == number and series["animeGroupMemberFolder"] == leaf
                    and series["path"] == "/media/library/Mixed Fixture Anime/" + leaf
                    and series["seasonFolder"] is False, "Mixed chain used wrong ordinal, folder or nested-season setting")
            require(series["foreignId"] == str(media_id) and all(item[2] == 1 for item in self.episode_snapshots[media_id]),
                    "Mixed member numbering replaced its native identity")
        require(len(self.episode_snapshots[900502]) == 1, "Unknown-count movie did not receive one native episode")
        self.check("official TV/movie/TV chain keeps independent season/movie ordinals and native unknown-count movie S01E01")
        for number, media_id in enumerate((900601, 900602), 1):
            series = self.add(media_id)
            require(series["animeGroupId"] == "anilist:900601" and series["animeGroupKind"] == "movieSaga"
                    and series["animeGroupMemberKind"] == "movie" and series["animeGroupMovieNumber"] == number
                    and series["path"] == f"/media/library/Fixture Movie Saga/Film {number:02d}"
                    and series["seasonFolder"] is False, "Movie-only saga did not use numbered film leaves")
        self.check("official movie-only saga uses Film 01/02 leaves and preserves two native single-episode entries")

    def manual(self):
        joined = self.add(900301, "manual", {"animeGroupId": "anilist:900401", "animeGroupTitle": "Client title",
                                             "animeGroupSeasonNumber": 4, "animeGroupFolder": "../../client-folder"})
        require(joined["animeGroupTitle"] == "Fixture Linear Anime" and joined["animeGroupFolder"] == "Fixture Linear Anime"
                and joined["path"] == "/media/library/Fixture Linear Anime/Season 04", "Manual member did not join the existing group safely")
        new_group = self.add(900302, "manual", {"animeGroupId": "manual:fixture-new", "animeGroupTitle": "Manual Anime",
                                                "animeGroupSeasonNumber": 1, "animeGroupFolder": "/escaped-client-folder"})
        require(new_group["path"] == "/media/library/Manual Anime/Season 01" and new_group["animeGroupFolder"] == "Manual Anime",
                "New manual group trusted a client folder or used the wrong season path")
        self.add(900303, "manual", {"animeGroupId": "anilist:900401", "animeGroupTitle": "Fixture Linear Anime",
                                   "animeGroupSeasonNumber": 4}, expected_status=400)
        require(all(item["foreignId"] != "900303" for item in self.api_json("/api/v5/series")),
                "Rejected duplicate slot persisted a new series")
        self.check("manual existing/new groups, canonical server folders and occupied-slot HTTP 400")
        movie_fields = {"animeGroupId": "anilist:900501", "animeGroupTitle": "Client title",
                        "animeGroupMemberKind": "movie", "animeGroupKind": "series", "animeGroupMovieNumber": 2,
                        "animeGroupSeasonNumber": None, "animeGroupFolder": "../../escape",
                        "animeGroupMemberFolder": "../../member-escape", "animeGroupMovieFolderStyle": "title"}
        movie = self.add(900701, "manual", movie_fields)
        require(movie["animeGroupTitle"] == "Mixed Fixture Anime" and movie["animeGroupMemberFolder"] != "CON"
                and movie["path"].startswith("/media/library/Mixed Fixture Anime/")
                and "/../" not in movie["path"], "Manual movie trusted client paths or unsafe reserved title")
        self.add(900702, "manual", dict(movie_fields, animeGroupMovieNumber=1), expected_status=400)
        require(all(item["foreignId"] != "900702" for item in self.api_json("/api/v5/series")),
                "Rejected occupied movie slot persisted a series")
        self.check("manual movie joins existing mixed group, sanitizes reserved title and rejects occupied same-kind slot")
        explicit = self.add(900304, fields={"path": "/media/explicit/Existing Entry", "seasonFolder": True})
        require(explicit["path"] == "/media/explicit/Existing Entry" and explicit["seasonFolder"] is True,
                "An explicit path or its season-folder setting was replaced")
        updated = self.get(explicit)
        updated.update(animeGroupingMode="manual", animeGroupId="manual:fixture-new", animeGroupTitle="Manual Anime",
                       animeGroupSeasonNumber=2)
        self.api_json(f"/api/v5/series/{explicit['id']}", "PUT", updated)
        actual = self.get(explicit)
        require(actual["path"] == explicit["path"] and actual["seasonFolder"] is True,
                "Editing existing visual membership moved an explicit path or changed its season-folder setting")
        require(self.episode_identity(actual) == self.episode_snapshots[900304], "Manual edit replaced existing episode identity")
        self.check("manual grouping of an existing explicit path preserves path/settings and native episodes")

    def imports(self):
        series = self.series[900402]
        source = self.root / "media" / "incoming" / "Fixture Linear Anime Sequel - S01E01.mkv"
        generate = subprocess.run(
            [self.args.ffmpeg, "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=1",
             "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-y", str(source)],
            capture_output=True, text=True, timeout=60)
        require(generate.returncode == 0 and source.is_file(), "Could not generate the disposable import video")
        source.chmod(0o644)
        episode = next(item for item in self.episodes(series) if item["seasonNumber"] == 1 and item["episodeNumber"] == 1)
        self.command({"name": "ManualImport", "importMode": "copy", "files": [{
            "path": "/media/incoming/" + source.name, "seriesId": series["id"], "episodeIds": [episode["id"]],
            "quality": {"quality": {"id": 5}, "revision": {"version": 1}},
            "languages": [{"id": 1, "name": "English"}],
        }]})
        self.until(lambda: self.api_json(f"/api/v5/episode/{episode['id']}")["hasFile"], "Generated native episode was not imported")
        imported = self.api_json(f"/api/v5/episode/{episode['id']}")
        file = self.api_json(f"/api/v5/episodefile/{imported['episodeFileId']}")
        expected = series["path"] + "/Fixture Linear Anime Sequel - S01E01.mkv"
        require(file["path"] == expected, "Native S01 import was nested or renumbered to group season 2")
        self.imported_file = self.root / "media" / Path(file["path"]).relative_to("/media")
        self.imported_hash = hashlib.sha256(self.imported_file.read_bytes()).hexdigest()
        self.command({"name": "RescanSeries", "seriesId": series["id"]})
        rescanned = self.api_json(f"/api/v5/episode/{episode['id']}")
        require(rescanned["hasFile"] and rescanned["episodeFileId"] == imported["episodeFileId"],
                "Rescan lost or rematched the imported native episode")
        require(self.episode_identity(series) == self.episode_snapshots[900402], "Rescan changed native episode identity/counts")
        require(not (self.imported_file.parent / "Season 01").exists(), "Import/rescan created a nested native season folder")
        self.check("generated native S01 video imports and rescans in group Season 02 without nested Season 01 or remapping")
        for media_id in (900502, 900602):
            member = self.series[media_id]
            source = self.root / "media" / "incoming" / (member["title"] + " - S01E01.mkv")
            source.write_bytes(self.imported_file.read_bytes())
            source.chmod(0o644)
            episode = self.episodes(member)[0]
            self.command({"name": "ManualImport", "importMode": "copy", "files": [{
                "path": "/media/incoming/" + source.name, "seriesId": member["id"], "episodeIds": [episode["id"]],
                "quality": {"quality": {"id": 5}, "revision": {"version": 1}},
                "languages": [{"id": 1, "name": "English"}],
            }]})
            self.until(lambda: self.api_json(f"/api/v5/episode/{episode['id']}")["hasFile"],
                       "Generated movie episode was not imported")
            imported = self.api_json(f"/api/v5/episode/{episode['id']}")
            file = self.api_json(f"/api/v5/episodefile/{imported['episodeFileId']}")
            require(file["path"] == member["path"] + "/" + member["title"] + " - S01E01.mkv",
                    "Movie import nested a Season 01 folder or replaced the native filename")
            local = self.root / "media" / Path(file["path"]).relative_to("/media")
            self.command({"name": "RescanSeries", "seriesId": member["id"]})
            rescanned = self.api_json(f"/api/v5/episode/{episode['id']}")
            require(rescanned["hasFile"] and rescanned["episodeFileId"] == imported["episodeFileId"]
                    and self.episode_identity(member) == self.episode_snapshots[media_id]
                    and not (local.parent / "Season 01").exists(), "Movie rescan lost native identity or created a nested season")
            self.imported_movies.append({"mediaId": media_id, "path": file["path"],
                                         "sha256": hashlib.sha256(local.read_bytes()).hexdigest()})
        self.check("generated native S01E01 movies import/rescan inside mixed title and saga Film 02 leaves without nested seasons")

    def naming_and_arcs(self):
        before = {item["id"]: (item["path"], item.get("animeGroupFolder"), item.get("animeGroupMemberFolder"))
                  for item in self.api_json("/api/v5/series")}
        for media_id, expected in ((901002, "/media/library/Tokyo Revengers/War of the Three Titans Arc"),
                                   (901103, "/media/library/The Apothecary Diaries/Season 03")):
            series = self.add(media_id)
            require(series["path"] == expected and series["animeGroupMemberKind"] == "season",
                    "Automatic arc subtitle or native catalogue third season chose the wrong directory")
        self.check("strict canonical Tokyo arc subtitle gets its title leaf; Apothecary Season 3 gets Season 03")
        naming = self.api_json("/api/v5/settings/naming")
        naming.update(animeGroupFolderFormat="Collection {Group Title}",
                      animeGroupSeasonFolderFormat="Part {Group Season:00}",
                      animeGroupArcFolderFormat="Story {Group Arc Title}",
                      animeGroupMovieFolderFormat="Movie {Group Movie:00} - {Series Title}",
                      animeGroupMovieSagaFolderFormat="Feature {Group Movie:00}",
                      animeEpisodeFormat="{Group Title} - {Series Title}{ - Group Season:00}{ - Group Movie:00} - S{season:00}E{episode:00}")
        self.api_json("/api/v5/settings/naming/1", "PUT", naming)
        self.naming_snapshot = {key: naming[key] for key in NAMING_FIELDS}
        examples = self.api_json("/api/v5/settings/naming/examples")
        require(examples["animeGroupFolderExample"] == "Collection The Anime Group Title"
                and examples["animeGroupSeasonFolderExample"] == "Part 02"
                and examples["animeGroupArcFolderExample"] == "Story War of Three Titans Arc"
                and examples["animeGroupMovieSagaFolderExample"] == "Feature 02"
                and "S01E01" in examples["animeGroupMovieEpisodeExample"],
                "Naming examples do not use the configured folder/group-file expressions")
        for media_id, leaf in ((900801, "Part 01"), (900802, "Part 02"),
                              (900901, "Feature 01"), (900902, "Feature 02")):
            series = self.add(media_id)
            canonical = "Configurable Fixture Anime" if media_id < 900900 else "Configurable Movie Saga"
            require(series["path"] == "/media/library/Collection " + canonical + "/" + leaf
                    and series["animeGroupMemberFolder"] == leaf, "Configured expressions did not generate the new member path")
        legacy = self.api_json("/api/v3/config/naming")
        for field in NAMING_FIELDS:
            legacy.pop(field, None)
        self.api_json("/api/v3/config/naming/1", "PUT", legacy)
        require({key: self.api_json("/api/v5/settings/naming")[key] for key in NAMING_FIELDS} == self.naming_snapshot,
                "A legacy naming PUT reset new expressions")
        current = {item["id"]: (item["path"], item.get("animeGroupFolder"), item.get("animeGroupMemberFolder"))
                   for item in self.api_json("/api/v5/series")}
        require(all(current[key] == value for key, value in before.items()), "Changing expressions rewrote existing registered leaves")
        self.check("five configurable folder expressions, native filename group tokens, new paths and legacy naming PUT preservation")

    def anilist_unsaved_tests(self):
        before = self.api_json("/api/v5/importlist")
        resource = next(item for item in self.api_json("/api/v5/importlist/schema") if item["implementation"] == "AniListImport")
        resource.update(name="Generated unsaved AniList fixture", rootFolderPath="/media/library",
                        qualityProfileId=self.profile_id, language={"id": 1, "name": "English"},
                        metadataSource="aniList", enableAutomaticAdd=False)
        values = {"username": "fixture-array", "accessToken": "generated-fixture-access",
                  "refreshToken": "generated-fixture-refresh", "expires": "2099-01-01T00:00:00Z",
                  "includedCustomLists": [], "excludedCustomLists": ["DaiBaNanaTop"], "customList": "",
                  "importCurrent": True, "importPlanning": False, "importCompleted": False,
                  "importDropped": False, "importPaused": False, "importRepeating": False,
                  "importFinished": True, "importReleasing": False, "importUnreleased": False,
                  "importCancelled": False, "importHiatus": False,
                  "excludeCurrent": False, "excludePlanning": False, "excludeCompleted": False,
                  "excludeDropped": False, "excludePaused": False, "excludeRepeating": False}
        fields = {field["name"]: field for field in resource["fields"]}
        require(all(key in fields for key in values), "AniList runtime schema omitted required fixture fields")
        for key, value in values.items():
            fields[key]["value"] = value
        time.sleep(2)
        status, body = self.raw("/api/v5/importlist/test", "POST", resource)
        # Only the generated fixture account is used here; no private response
        # bodies may be included by a runner using a real account.
        require(status == 204, "Valid typed-array fallback did not pass the real unsaved provider Test: "
                + body.decode("utf-8", errors="replace"))
        time.sleep(2)
        options = self.api_json("/api/v5/importlist/action/getCustomLists", "POST", resource)["options"]
        require([item["value"] for item in options] == ["DaiBaNanaTop", "Fixture optional"],
                "Read-only provider action did not return configured custom-list names")
        fields["username"]["value"] = "fixture-unknown"
        time.sleep(2)
        status, body = self.raw("/api/v5/importlist/test", "POST", resource)
        require(status == 400 and "custom list membership" in body.decode("utf-8", errors="replace").lower(),
                "Unknown map and array membership failed to trigger conservative retention protection")
        require(self.api_json("/api/v5/importlist") == before, "Unsaved Test or custom-list name action saved provider settings")
        self.list_test_observations = {"knownArrayTestStatus": 204, "unknownMembershipTestStatus": 400,
                                       "customListNames": [item["value"] for item in options],
                                       "savedImportListsBefore": len(before), "savedImportListsAfter": len(before),
                                       "excludedFixtureMediaId": 900101, "knownFalseFixtureMediaId": 900102,
                                       "knownEmptyMovieFixtureMediaId": 900502}
        self.check("real unsaved AniList provider Test accepts explicit typed-array membership and read-only names without saving")
        self.check("unknown map plus unknown array returns HTTP 400 retention guard with saved import lists unchanged")

    def edits_and_restart(self):
        original = self.get(self.series[900402])
        original_identity = self.episode_identity(original)
        disabled = dict(original, animeGroupingMode="disabled")
        self.api_json(f"/api/v5/series/{original['id']}", "PUT", disabled)
        actual = self.get(original)
        require(not actual.get("animeGroupId") and actual["path"] == original["path"], "Disabled mode moved files or retained group membership")
        manual = dict(actual, animeGroupingMode="manual", animeGroupId="anilist:900401",
                      animeGroupTitle="Fixture Linear Anime", animeGroupSeasonNumber=5)
        self.api_json(f"/api/v5/series/{original['id']}", "PUT", manual)
        saved = self.get(original)
        legacy = self.get(saved, "v3")
        for key in GROUP_FIELDS:
            legacy.pop(key, None)
        legacy["monitored"] = True
        self.api_json(f"/api/v3/series/{original['id']}", "PUT", legacy)
        self.command({"name": "RefreshSeries", "seriesId": original["id"]})
        current = self.get(original)
        require(current["animeGroupingMode"] == "manual" and current["animeGroupSeasonNumber"] == 5
                and current["animeGroupId"] == saved["animeGroupId"] and current["path"] == original["path"],
                "Legacy v3 update or refresh lost manual membership or changed the registered path")
        require(current["metadataSource"] == original["metadataSource"] and current["foreignId"] == original["foreignId"]
                and self.episode_identity(current) == original_identity, "Grouping edits/refresh replaced native IDs or episode counts")
        require(self.imported_file.is_file() and hashlib.sha256(self.imported_file.read_bytes()).hexdigest() == self.imported_hash,
                "Grouping edits moved or modified the imported file")
        require(not (self.root / "media" / "library" / "Fixture Linear Anime" / "Season 05").exists(),
                "An existing member was automatically moved to its new visual group-season slot")
        self.check("Disabled/manual override, legacy v3 PUT and refresh preserve native identity/path/imported bytes")
        self.docker("restart", self.container)
        self.refresh_binding()
        self.wait_ready()
        restarted = self.get(original)
        require(restarted["animeGroupId"] == current["animeGroupId"] and restarted["animeGroupSeasonNumber"] == 5
                and restarted["path"] == current["path"] and self.episode_identity(restarted) == original_identity,
                "Restart lost membership, native identity or explicit path")
        require(hashlib.sha256(self.imported_file.read_bytes()).hexdigest() == self.imported_hash,
                "Restart modified imported file bytes")
        require({key: self.api_json("/api/v5/settings/naming")[key] for key in NAMING_FIELDS} == self.naming_snapshot,
                "Restart lost configured naming expressions")
        self.check("restart persists group membership, episode records and generated imported file")

    def no_acquisition(self):
        require(self.api_json("/api/v5/indexer") == [] and self.api_json("/api/v5/downloadclient") == [],
                "Tests created acquisition configuration")
        require(self.api_json("/api/v5/queue")["totalRecords"] == 0, "Tests created a download queue")
        acquisition = {"SeriesSearch", "SeasonSearch", "EpisodeSearch", "MissingEpisodeSearch", "CutoffUnmetEpisodeSearch", "CompleteSeriesSearch"}
        require(not any(item["name"] in acquisition for item in self.api_json("/api/v5/command")),
                "Grouping scheduled an acquisition command")
        self.check("no indexers, download clients, download queue or acquisition commands")

    def cleanup(self):
        if self.args.keep:
            print(f"Kept only this fixture: app {self.container}; mock {self.mock}; relay {self.relay}; "
                  f"internal network {self.network}; API network {self.api_network}; data {self.root}")
            print("Disposable API key and CA keys remain in those temporary files; credentials are not printed.")
            return
        failures = []
        try:
            if self.relay:
                self.docker("rm", "--force", self.relay)
                self.relay = None
        except Exception as error:
            failures.append(str(error))
        try:
            if self.mock:
                self.docker("rm", "--force", self.mock)
                self.mock = None
        except Exception as error:
            failures.append(str(error))
        try:
            super().cleanup()
        except Exception as error:
            failures.append(str(error))
        try:
            if self.network:
                self.docker("network", "rm", self.network)
                self.network = None
        except Exception as error:
            failures.append(str(error))
        try:
            if self.api_network:
                self.docker("network", "rm", self.api_network)
                self.api_network = None
        except Exception as error:
            failures.append(str(error))
        require(not failures, "Fixture cleanup failed: " + "; ".join(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local", help="Already built Linux image containing the grouping feature")
    parser.add_argument("--mock-image", default="python:3.13-alpine", help="Already available local Python image for the TLS mock")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl", help="Host OpenSSL executable")
    parser.add_argument("--ffmpeg", default="ffmpeg", help="Host FFmpeg with libx264 for the generated video")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true", help="Keep only the disposable fixture for failure inspection")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--serve-relay", action="store_true", help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    if args.serve_relay:
        serve_relay()
        return 0
    require(args.timeout > 0, "--timeout must be positive")
    audit = AnimeGroups(args)
    error = None
    records = []
    logs = ""
    try:
        audit.start()
        audit.prepare()
        audit.preview()
        audit.automatic()
        audit.manual()
        audit.imports()
        audit.naming_and_arcs()
        audit.anilist_unsaved_tests()
        audit.edits_and_restart()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as caught:
        error = f"{audit.last_request}: {caught or type(caught).__name__}".replace(audit.api_key, "[REDACTED]")
        print("FAIL " + error, file=sys.stderr)
    finally:
        if audit.root:
            request_file = audit.root / "fixture" / "requests.jsonl"
            try:
                if request_file.exists():
                    for line in request_file.read_text(encoding="utf-8").splitlines():
                        if line:
                            records.append(json.loads(line))
            except (OSError, ValueError) as caught:
                error = error or "Could not read local fixture request evidence: " + str(caught)
        if audit.container:
            try:
                logs = audit.docker("logs", audit.container).replace(audit.api_key, "[REDACTED]")
            except Exception:
                pass
        try:
            audit.cleanup()
        except Exception as caught:
            error = error or str(caught).replace(audit.api_key, "[REDACTED]")
            print("FAIL cleanup: " + str(caught).replace(audit.api_key, "[REDACTED]"), file=sys.stderr)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({
                "createdUtc": utc_now(), "image": args.image, "imageId": audit.image_id,
                "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                "mockImage": args.mock_image, "mockImageId": audit.mock_id, "runtime": audit.runtime_metadata,
                "passed": error is None, "checks": audit.results, "error": error, "initialGroup": audit.initial_group,
                "nativeEpisodeIdentities": audit.episode_snapshots, "fixtureRequests": records,
                "importedMovies": audit.imported_movies, "namingExpressions": audit.naming_snapshot,
                "unsavedAniListTests": audit.list_test_observations,
                "scope": {"trustedLocalAniListMock": True, "liveAniListTested": False, "jikanRuntimeTested": False,
                          "privateAccountUsed": False, "realLibraryTested": False, "downloadsTested": False,
                          "generatedVideoImportTested": audit.imported_file is not None,
                          "tlsValidationDisabled": False, "externalProviderRoutingEnabled": False,
                          "apiBindAddress": "127.0.0.1:random", "mainConfigurationAccessed": False},
                "keptContainer": audit.container if args.keep else None,
                "keptMock": audit.mock if args.keep else None,
                "keptRelay": audit.relay if args.keep else None,
                "keptInternalNetwork": audit.network if args.keep else None,
                "keptApiNetwork": audit.api_network if args.keep else None,
                "keptData": str(audit.root) if args.keep and audit.root else None,
            }, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
