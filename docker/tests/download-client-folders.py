#!/usr/bin/env python3
"""Verify a genuine isolated qBittorrent client and generated collection imports."""

import argparse
import copy
import hashlib
import http.client
from http.cookies import SimpleCookie
import importlib.util
import json
from pathlib import Path
import re
import secrets
import select
import socket
import socketserver
import sys
import urllib.parse

from smoke import SmokeFailure, require


SPEC = importlib.util.spec_from_file_location("client_group_fixture", Path(__file__).with_name("anime-groups.py"))
GROUPS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GROUPS)
QB_IMAGE = "lscr.io/linuxserver/qbittorrent@sha256:97333cc1ccf903e0bbcd8eb67e3332e4970e9c1ecabe145e499e9c0847ebcaed"
MOCK_IMAGE = "python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a"


def serve_client_relay():
    """Relay only to this invocation's genuine internal qBittorrent WebUI."""
    class Relay(socketserver.BaseRequestHandler):
        def handle(self):
            with socket.create_connection(("fixture-qbittorrent", 8080), timeout=30) as destination:
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

    with Server(("0.0.0.0", 8080), Relay) as server:
        server.serve_forever()


class ClientFolders(GROUPS.AnimeGroups):
    def __init__(self, args):
        super().__init__(args)
        self.qb = None
        self.qb_relay = None
        self.qb_image_id = None
        self.qb_labels = {}
        self.qb_port = None
        self.qb_cookie = None
        self.qb_temporary_password = None
        self.qb_username = "fixture-" + secrets.token_hex(8)
        self.qb_password = secrets.token_urlsafe(32)
        self.client_id = None
        self.client_observations = []
        self.qb_observations = {}
        self.import_observations = []
        self.cleaned = False

    def prepare(self):
        super().prepare()
        naming = self.api_json("/api/v5/settings/naming")
        naming["standardEpisodeFormat"] = "{Series Title} - S{season:00}E{episode:00}"
        self.api_json("/api/v5/settings/naming/1", "PUT", naming)

    def redact(self, value):
        for secret in (self.api_key, self.qb_username, self.qb_password, self.qb_temporary_password, self.qb_cookie):
            if secret:
                value = value.replace(secret, "[REDACTED]")
        return value

    def qb_raw(self, path, data=None, authenticated=True):
        connection = http.client.HTTPConnection("127.0.0.1", self.qb_port, timeout=30)
        headers = {"Host": "fixture-qbittorrent:8080", "Origin": "http://fixture-qbittorrent:8080",
                   "Referer": "http://fixture-qbittorrent:8080/"}
        if authenticated and self.qb_cookie:
            headers["Cookie"] = self.qb_cookie
        payload = None
        if data is not None:
            headers["Content-Type"] = "application/x-www-form-urlencoded"
            payload = urllib.parse.urlencode(data).encode("utf-8")
        try:
            connection.request("GET" if data is None else "POST", "/api/v2/" + path, payload, headers)
            response = connection.getresponse()
            return response.status, response.read(), response.getheader("Set-Cookie")
        finally:
            connection.close()

    def qb_json(self, path, data=None):
        status, body, _cookie = self.qb_raw(path, data)
        require(status == 200, "Genuine qBittorrent API returned HTTP " + str(status))
        return json.loads(body) if body else None

    def qb_login(self, username, password):
        status, body, header = self.qb_raw("auth/login", {"username": username, "password": password}, False)
        self.qb_observations.setdefault("authenticationSteps", []).append({
            "httpStatus": status, "successBody": body == b"Ok.", "sessionHeaderPresent": bool(header),
            "temporaryLogin": username == "admin"})
        require(((status == 200 and body == b"Ok.") or (status == 204 and body == b"")) and header,
                "Generated qBittorrent login did not succeed (HTTP " + str(status) + ", successful body "
                + str(body == b"Ok.") + ", session header " + str(bool(header)) + ")")
        cookie = SimpleCookie()
        cookie.load(header)
        sessions = [name for name in cookie if name == "SID" or name.startswith("QBT_SID_")]
        require(len(sessions) == 1 and cookie[sessions[0]].value, "Genuine qBittorrent login did not return a session")
        self.qb_observations["authenticationSteps"][-1]["sessionCookieName"] = sessions[0]
        self.qb_cookie = sessions[0] + "=" + cookie[sessions[0]].value

    def start_client(self):
        require(re.fullmatch(r"[^\s]+@sha256:[0-9a-f]{64}", self.args.client_image),
                "The genuine client image must be pinned by immutable SHA256 digest")
        image = json.loads(self.docker("image", "inspect", self.args.client_image))[0]
        require(image["Os"] == "linux", "A locally available Linux qBittorrent image is required")
        self.qb_image_id = image["Id"]
        self.qb_labels = image["Config"].get("Labels", {}) or {}
        config = self.root / "qb-config"
        folder = config / "qBittorrent"
        folder.mkdir(parents=True)
        (folder / "qBittorrent.conf").write_text(
            "[BitTorrent]\nSession\\DHTEnabled=false\nSession\\LSDEnabled=false\nSession\\PeXEnabled=false\n"
            "Session\\DefaultSavePath=/media/incoming\nSession\\Port=6881\n"
            "[Network]\nPortForwardingEnabled=false\n"
            "[Preferences]\nWebUI\\Address=*\nWebUI\\Port=8080\nWebUI\\Username=admin\n"
            "WebUI\\HostHeaderValidation=true\nWebUI\\ServerDomains=fixture-qbittorrent\n"
            "WebUI\\CSRFProtection=true\nWebUI\\LocalHostAuth=true\n"
            "WebUI\\AuthSubnetWhitelistEnabled=false\nConnection\\UPnP=false\n", encoding="utf-8")
        suffix = secrets.token_hex(6)
        self.qb = self.docker(
            "run", "--detach", "--name", "tanukarr-genuine-qb-" + suffix,
            "--label", "tanukarr.download-client-test=true", "--network", self.network,
            "--network-alias", "fixture-qbittorrent", "--env", "PUID=1000", "--env", "PGID=1000",
            "--env", "TZ=Etc/UTC", "--env", "WEBUI_PORT=8080", "--env", "TORRENTING_PORT=6881",
            "--mount", f"type=bind,source={config},target=/config",
            "--mount", f"type=bind,source={self.root / 'media'},target=/media", self.args.client_image)
        self.qb_relay = self.docker(
            "create", "--name", "tanukarr-client-relay-" + suffix,
            "--label", "tanukarr.download-client-test=true", "--network", self.api_network,
            "--publish", "127.0.0.1::8080", "--mount",
            f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            self.args.mock_image, "python", "-u", "/runner/download-client-folders.py", "--serve-client-relay")
        self.docker("network", "connect", self.network, self.qb_relay)
        self.docker("start", self.qb_relay)
        binding = json.loads(self.docker("inspect", self.qb_relay))[0]["NetworkSettings"]["Ports"]["8080/tcp"]
        require(len(binding) == 1 and binding[0]["HostIp"] == "127.0.0.1", "Client relay must expose IPv4 loopback only")
        self.qb_port = int(binding[0]["HostPort"])

        def ready():
            logs = self.docker("logs", self.qb)
            match = re.search(r"A temporary password is provided for this session:\s*(\S+)", logs)
            if not match:
                return False
            self.qb_temporary_password = match[1]
            try:
                return self.qb_raw("app/version", authenticated=False)[0] == 403
            except (OSError, http.client.HTTPException):
                return False

        self.until(ready, "The genuine qBittorrent WebUI did not become ready with authentication required")
        self.qb_login("admin", self.qb_temporary_password)
        preferences = {"web_ui_username": self.qb_username, "web_ui_password": self.qb_password,
                       "save_path": "/media/incoming", "dht": False, "pex": False, "lsd": False,
                       "upnp": False, "web_ui_upnp": False, "queueing_enabled": True}
        require(200 <= self.qb_raw("app/setPreferences", {"json": json.dumps(preferences)})[0] < 300,
                "Could not set generated credentials and isolated save path")
        self.qb_cookie = None
        self.qb_login(self.qb_username, self.qb_password)
        version_status, version, _ = self.qb_raw("app/version")
        api_status, api_version, _ = self.qb_raw("app/webapiVersion")
        prefs = self.qb_json("app/preferences")
        require(version_status == api_status == 200 and re.fullmatch(rb"v?\d+\.\d+\.\d+.*", version)
                and re.fullmatch(rb"\d+\.\d+(\.\d+)?", api_version), "Invalid genuine client version responses")
        require(prefs["save_path"].rstrip("/") == "/media/incoming" and all(prefs[key] is False for key in ("dht", "pex", "lsd", "upnp")),
                "Genuine client did not retain isolated save-path/discovery settings")
        detail = json.loads(self.docker("inspect", self.qb))[0]
        require(len(detail["NetworkSettings"]["Networks"]) == 1
                and next(iter(detail["NetworkSettings"]["Networks"].values()))["NetworkID"] == self.network
                and not any(detail["NetworkSettings"]["Ports"].values()), "Genuine client has an external route or published port")
        require(self.qb_json("torrents/info") == [], "Generated client started with torrents")
        self.qb_observations.update(version=version.decode(), apiVersion=api_version.decode(),
                                    generatedCredentials=True, anonymousStatus=403, savePath=prefs["save_path"],
                                    peerDiscoveryDisabled=True, externallyRoutable=False, torrentsInitially=0)
        self.check("genuine pinned qBittorrent requires generated login, reports real versions and retains isolated save path")

    @staticmethod
    def fields(resource):
        return {item["name"]: item.get("value") for item in resource["fields"]}

    @staticmethod
    def set_fields(resource, **values):
        for key, value in values.items():
            field = next((item for item in resource["fields"] if item["name"] == key), None)
            require(field is not None, "qBittorrent schema field is missing: " + key)
            field["value"] = value

    def clients(self):
        definitions = {}
        for version in ("v3", "v5"):
            path = f"/api/{version}/downloadclient"
            require(self.raw(path + "/schema", api_key=False)[0] == 401, "Client schema accepted a missing API key")
            schema = self.api_json(path + "/schema")
            definition = next(item for item in schema if item["implementation"] == "QBittorrent")
            require(definition["configContract"] == "QBittorrentSettings", "Unexpected client contract")
            require(set(("host", "port", "username", "password", "tvCategory")) <= set(self.fields(definition)),
                    "Client schema omitted native configuration fields")
            definition.update(name="Generated genuine qBittorrent", enable=True, priority=1,
                              removeCompletedDownloads=False, removeFailedDownloads=False)
            self.set_fields(definition, host="fixture-qbittorrent", port=8080, username=self.qb_username,
                            password=self.qb_password, useSsl=False, tvCategory="fixture-tv",
                            tvImportedCategory="fixture-imported", recentTvPriority=0, olderTvPriority=0)
            definitions[version] = definition
            status, _body = self.raw(path + "/test", "POST", definition)
            require(200 <= status < 300 and self.api_json(path) == [], "Unsaved genuine client Test failed or saved a definition")
            self.client_observations.append({"api": version, "operation": "unsavedTest", "httpStatus": status,
                                             "savedDefinitionsAfterTest": 0})
        categories = self.qb_json("torrents/categories")
        require({"fixture-tv", "fixture-imported"} <= set(categories), "Real client Test did not create configured categories")
        self.check("v3/v5 native client schema and unsaved Test authenticate the genuine server without persisting definitions")
        self.check("genuine qBittorrent categories are created through Tanukarr Test")
        saved = self.api_json("/api/v5/downloadclient", "POST", definitions["v5"])
        self.client_id = saved["id"]
        for version in ("v3", "v5"):
            actual = self.api_json(f"/api/{version}/downloadclient/{self.client_id}")
            fields = self.fields(actual)
            require(actual["enable"] and actual["implementation"] == "QBittorrent"
                    and fields["host"] == "fixture-qbittorrent" and fields["tvCategory"] == "fixture-tv"
                    and fields["password"] == "********", "Saved client is missing, changed, or exposes its password")
        self.check("v5 Add persists the real client and v3/v5 Get mask its generated password")
        for version, category in (("v3", "fixture-tv-edited-v3"), ("v5", "fixture-tv-edited-v5")):
            path = f"/api/{version}/downloadclient/{self.client_id}"
            edited = self.api_json(path)
            edited["name"] = "Generated genuine qBittorrent " + version
            self.set_fields(edited, tvCategory=category)
            self.api_json(path, "PUT", edited)
            readback = self.api_json(path)
            require(self.fields(readback)["tvCategory"] == category and category in self.qb_json("torrents/categories"),
                    "Client edit did not persist or create the genuine category")
            status, _body = self.raw(f"/api/{version}/downloadclient/test", "POST", readback)
            require(200 <= status < 300, "Masked edit discarded saved credentials")
            self.client_observations.append({"api": version, "operation": "editAndMaskedRetest", "httpStatus": status,
                                             "category": category, "passwordMasked": True})
        self.check("v3/v5 Edit preserve masked credentials and create/test both real client categories")
        bad = copy.deepcopy(definitions["v5"])
        bad["name"] = "Rejected generated credentials"
        self.set_fields(bad, password="incorrect-generated-fixture-password")
        status, body = self.raw("/api/v5/downloadclient/test", "POST", bad)
        errors = json.loads(body)
        self.client_observations.append({"api": "v5", "operation": "badCredentials", "httpStatus": status,
                                         "errorProperties": [item.get("propertyName") for item in errors]
                                         if isinstance(errors, list) else [], "savedDefinitionsRetained": 1})
        direct_status, _direct_body, _cookie = self.qb_raw("auth/login", {
            "username": self.qb_username, "password": "incorrect-generated-fixture-password"}, False)
        require(status == 400 and isinstance(errors, list)
                and any(str(item.get("propertyName", "")).lower() in ("username", "host") for item in errors)
                and direct_status in (401, 403),
                "Incorrect genuine client credentials were not rejected as an authentication error")
        require(len(self.api_json("/api/v5/downloadclient")) == 1, "Failed unsaved Test modified saved definitions")
        self.client_observations[-1]["genuineInvalidLoginStatus"] = direct_status
        self.check("incorrect generated qBittorrent credentials fail Test while the saved definition remains intact")
        for name in ("fixture-tv-edited-v3", "fixture-tv-edited-v5"):
            require(200 <= self.qb_raw("torrents/editCategory", {"category": name, "savePath": "/media/incoming"})[0] < 300,
                    "Could not assign real category save paths")
        categories = self.qb_json("torrents/categories")
        require(all(categories[name]["savePath"].rstrip("/") == "/media/incoming"
                    for name in ("fixture-tv-edited-v3", "fixture-tv-edited-v5")), "Genuine category save paths were not retained")
        self.qb_observations["categories"] = categories
        self.check("real category and default save paths agree with the shared isolated media mount")

    def create_members(self):
        for media_id, expected in ((900401, "/media/library/Fixture Linear Anime/Season 01"),
                                   (900402, "/media/library/Fixture Linear Anime/Season 02"),
                                   (900501, "/media/library/Mixed Fixture Anime/Season 01"),
                                   (900502, "/media/library/Mixed Fixture Anime/Mixed Fixture Anime Movie"),
                                   (900601, "/media/library/Fixture Movie Saga/Film 01"),
                                   (900602, "/media/library/Fixture Movie Saga/Film 02")):
            member = self.add(media_id)
            require(member["path"] == expected and member["seasonFolder"] is False,
                    "Automatic collection selected an incorrect or nested folder")
        self.check("anime TV seasons, mixed TV/movie collection and movie-only Film 01/02 use server-generated leaves")
        for media_id, season in ((900301, 1), (900302, 2)):
            fields = {"seriesType": "standard", "animeGroupId": "manual:fixture-live-action-tv",
                      "animeGroupTitle": "Generated TV Collection", "animeGroupSeasonNumber": season}
            member = self.add(media_id, "manual", fields)
            require(member["seriesType"] == "standard"
                    and member["path"] == f"/media/library/Generated TV Collection/Season {season:02d}",
                    "Manual non-anime TV collection chose an incorrect folder or changed series type")
        self.check("manual standard TV collection uses Season 01/02 without changing native catalogue identities")

    def import_more(self):
        self.imports()
        require(self.imported_file is not None, "Generated anime import is missing")
        for media_id in (900601, 900302):
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
            self.until(lambda: self.api_json(f"/api/v5/episode/{episode['id']}")["hasFile"], "Additional generated media was not imported")
            before = self.api_json(f"/api/v5/episode/{episode['id']}")
            file = self.api_json(f"/api/v5/episodefile/{before['episodeFileId']}")
            require(file["path"] == member["path"] + "/" + source.name, "Generated TV/Film 01 import nested or renamed incorrectly")
            self.command({"name": "RescanSeries", "seriesId": member["id"]})
            after = self.api_json(f"/api/v5/episode/{episode['id']}")
            local = self.root / "media" / Path(file["path"]).relative_to("/media")
            require(after["hasFile"] and after["episodeFileId"] == before["episodeFileId"]
                    and self.episode_identity(member) == self.episode_snapshots[media_id]
                    and not (local.parent / "Season 01").exists(), "TV/Film 01 rescan changed native identity or nested directories")
        for media_id in (900402, 900502, 900601, 900602, 900302):
            member = self.series[media_id]
            episode = next(item for item in self.episodes(member) if item["hasFile"])
            file = self.api_json(f"/api/v5/episodefile/{episode['episodeFileId']}")
            local = self.root / "media" / Path(file["path"]).relative_to("/media")
            self.import_observations.append({"providerId": media_id, "seriesType": member["seriesType"],
                                             "path": file["path"], "nativeSeason": episode["seasonNumber"],
                                             "nativeEpisode": episode["episodeNumber"], "rescanRetainedIdentity": True,
                                             "sha256": hashlib.sha256(local.read_bytes()).hexdigest()})
        require(len({item["sha256"] for item in self.import_observations}) == 1, "Copy import changed generated media bytes")
        self.check("standard TV Season 02 and saga Film 01 import/rescan generated media without extra native-season folders")
        self.check("all five copied media retain identical generated bytes and independent native episode identities")

    def restart_and_remove(self):
        snapshot = self.api_json(f"/api/v5/downloadclient/{self.client_id}")
        self.docker("restart", "--time", "20", self.container)
        self.wait_ready()
        require(self.api_json(f"/api/v5/downloadclient/{self.client_id}") == snapshot, "App restart changed saved client settings")
        require(200 <= self.raw("/api/v5/downloadclient/test", "POST", snapshot)[0] < 300,
                "Real client authentication did not work after app restart")
        for record in self.import_observations:
            local = self.root / "media" / Path(record["path"]).relative_to("/media")
            require(hashlib.sha256(local.read_bytes()).hexdigest() == record["sha256"], "Restart changed imported bytes")
        self.check("app restart preserves saved genuine client credentials and generated collection imports")
        require(self.qb_json("torrents/info") == [] and self.api_json("/api/v5/indexer") == []
                and self.api_json("/api/v5/queue")["totalRecords"] == 0, "Fixture unexpectedly acquired or queued torrents")
        self.qb_observations["torrentsFinally"] = 0
        self.check("genuine client and app queues remain empty with no indexers or public acquisition")
        self.api_json(f"/api/v3/downloadclient/{self.client_id}", "DELETE")
        require(self.api_json("/api/v5/downloadclient") == [], "v3 Delete did not remove the saved client")
        self.check("v3 Delete removes the real saved client and v5 observes an empty client list")
        definition = next(item for item in self.api_json("/api/v3/downloadclient/schema")
                          if item["implementation"] == "QBittorrent")
        definition.update(name="Generated v3 compatibility client", enable=True, priority=1,
                          removeCompletedDownloads=False, removeFailedDownloads=False)
        self.set_fields(definition, host="fixture-qbittorrent", port=8080, username=self.qb_username,
                        password=self.qb_password, useSsl=False, tvCategory="fixture-tv-edited-v5",
                        recentTvPriority=0, olderTvPriority=0)
        self.api_json("/api/v3/downloadclient", "POST", definition)
        clients = self.api_json("/api/v5/downloadclient")
        require(len(clients) == 1 and self.fields(clients[0])["password"] == "********",
                "v3 Add did not expose one masked compatible client in v5")
        self.api_json(f"/api/v5/downloadclient/{clients[0]['id']}", "DELETE")
        require(self.api_json("/api/v3/downloadclient") == [], "v5 Delete did not remove the v3-created client")
        self.client_observations.append({"api": "v3/v5", "operation": "v3AddV5MaskedGetV5Delete",
                                         "savedDefinitionsFinally": 0, "passwordMasked": True})
        self.check("v3 Add, v5 masked Get and v5 Delete preserve client API compatibility")

    def cleanup(self):
        if self.args.keep:
            super().cleanup()
            return
        failures = []
        for attribute in ("qb_relay", "qb"):
            resource = getattr(self, attribute)
            if not resource:
                continue
            try:
                if attribute == "qb":
                    self.docker("exec", "--user", "0:0", resource, "chmod", "-R", "a+rwX", "/config")
                self.docker("rm", "--force", resource)
                setattr(self, attribute, None)
            except Exception as error:
                failures.append(self.redact(str(error)))
        try:
            super().cleanup()
        except Exception as error:
            failures.append(self.redact(str(error)))
        self.cleaned = not failures and all(value is None for value in (self.qb, self.qb_relay, self.container,
                                                                        self.mock, self.relay, self.network,
                                                                        self.api_network, self.root))
        require(self.cleaned, "Owned client fixture cleanup failed: " + "; ".join(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local", help="Already built Tanukarr Linux image")
    parser.add_argument("--mock-image", default=MOCK_IMAGE)
    parser.add_argument("--client-image", default=QB_IMAGE, help="Already available genuine qBittorrent digest")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--serve-client-relay", action="store_true", help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_client_relay:
        serve_client_relay()
        return 0
    require(args.timeout > 0, "--timeout must be positive")
    audit = ClientFolders(args)
    error = None
    logs = ""
    try:
        audit.start()
        audit.prepare()
        audit.start_client()
        audit.clients()
        audit.create_members()
        audit.import_more()
        audit.restart_and_remove()
    except (Exception, KeyboardInterrupt) as caught:
        error = audit.redact(f"{audit.last_request}: {caught or type(caught).__name__}")
        print("FAIL " + error, file=sys.stderr)
    finally:
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
            args.report.parent.mkdir(parents=True, exist_ok=True)
            report = {"createdUtc": GROUPS.utc_now(), "image": args.image, "imageId": audit.image_id,
                      "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                      "runtime": audit.runtime_metadata, "passed": error is None, "checks": audit.results,
                      "error": error, "mockImage": args.mock_image, "mockImageId": audit.mock_id,
                      "clientImage": args.client_image, "clientImageId": audit.qb_image_id,
                      "clientVersionLabel": audit.qb_labels.get("org.opencontainers.image.version"),
                      "clientSourceRevision": audit.qb_labels.get("org.opencontainers.image.revision"),
                      "genuineClient": audit.qb_observations, "clientApiChecks": audit.client_observations,
                      "generatedMediaImports": audit.import_observations, "cleanupComplete": audit.cleaned,
                      "scope": {"genuineQbittorrentServer": True, "trustedLocalCatalogue": True,
                                "clientAddEditTestGetDelete": any(item.get("operation") == "v3AddV5MaskedGetV5Delete"
                                                                   for item in audit.client_observations),
                                "generatedVideoManualImportRescan": len(audit.import_observations) == 5,
                                "publicDownloadsTested": False, "completedDownloadAutomaticImportTested": False,
                                "realLibraryTested": False, "mainConfigurationAccessed": False,
                                "privateAccountUsed": False, "externalProviderRoutingEnabled": False,
                                "tlsValidationDisabled": False, "apiBindAddress": "127.0.0.1:random"},
                      "keptContainer": audit.container if args.keep else None,
                      "keptClient": audit.qb if args.keep else None,
                      "keptData": str(audit.root) if args.keep and audit.root else None}
            args.report.write_text(audit.redact(json.dumps(report, indent=2)) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
