#!/usr/bin/env python3
"""Exercise an isolated Sonarr container; never use an existing deployment."""

import argparse
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import re
import secrets
import sqlite3
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse

from smoke import ContainerSmoke, SmokeFailure, require


class FunctionalSecurity(ContainerSmoke):
    def __init__(self, args):
        super().__init__(args)
        self.failures = []
        self.observations = []
        self.cookie = None
        self.username = "isolated-audit"
        self.password = secrets.token_urlsafe(32)
        self.series = None
        self.image_id = None
        self.indexer_server = None
        self.indexer_requests = []
        self.companion_tokens = []
        self.add_token = None
        self.browser_tested = False

    def raw(self, path, method="GET", data=None, api_key=True, cookie=False, headers=None):
        parsed = urllib.parse.urlsplit(self.base_url)
        connection = http.client.HTTPConnection(parsed.hostname, parsed.port, timeout=100)
        request_headers = {"Content-Type": "application/json"}
        if api_key is True:
            request_headers["X-Api-Key"] = self.api_key
        elif isinstance(api_key, str):
            request_headers["X-Api-Key"] = api_key
        if cookie and self.cookie:
            request_headers["Cookie"] = self.cookie
        if headers:
            request_headers.update(headers)
        payload = data
        if data is not None and not isinstance(data, (bytes, str)):
            payload = json.dumps(data).encode("utf-8")
        try:
            connection.request(method, path, body=payload, headers=request_headers)
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def json_api(self, path, method="GET", data=None):
        status, _, body = self.raw(path, method, data)
        if not 200 <= status < 300:
            try:
                errors = json.loads(body)
                detail = "; ".join(str(item.get("errorMessage", item.get("message", "validation failure")))
                                   for item in errors if isinstance(item, dict)) if isinstance(errors, list) else ""
            except ValueError:
                detail = ""
            raise SmokeFailure(f"{method} {path.split('?')[0]} returned HTTP {status}: {detail}")
        return json.loads(body)

    def run_check(self, name, callback):
        try:
            callback()
            self.check(name)
        except Exception as error:
            detail = str(error) or type(error).__name__
            # Runtime credentials must never end up in logs or reports.
            for secret in (self.api_key, self.metadata_key, self.password, *self.companion_tokens):
                detail = detail.replace(secret, "[REDACTED]")
            self.failures.append({"check": name, "error": detail})
            print(f"FAIL {name}: {detail}", flush=True)

    def start(self):
        require(self.docker("info", "--format", "{{.OSType}}") == "linux", "Linux Docker required")
        self.image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.image)
        self.root = Path(tempfile.mkdtemp(prefix="sonarr-container-smoke-security-")).resolve()
        config = self.root / "config"
        media = self.root / "media"
        config.mkdir()
        media.mkdir()
        media.chmod(0o777)
        (config / "config.xml").write_text(
            "<Config><BindAddress>*</BindAddress><Port>8989</Port>"
            f"<ApiKey>{self.api_key}</ApiKey><LaunchBrowser>False</LaunchBrowser>"
            "<AuthenticationMethod>Forms</AuthenticationMethod>"
            "<AuthenticationRequired>Enabled</AuthenticationRequired>"
            "<AllowedHosts>127.0.0.1,localhost</AllowedHosts>"
            "<AnalyticsEnabled>False</AnalyticsEnabled></Config>", encoding="utf-8")
        self.container = self.docker(
            "create", "--name", "sonarr-security-" + secrets.token_hex(6),
            "--label", "sonarr.security-test=true", "--publish", "127.0.0.1::8989",
            "--env", "PUID=1000", "--env", "PGID=1000", "--env", "UMASK=002",
            "--mount", f"type=bind,source={config},target=/config",
            "--mount", f"type=bind,source={media},target=/media", self.args.image)
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.check("isolated container startup with authentication always required")

    def authentication(self):
        for path in ("/api/v3/system/status", "/api/v5/system/status", "/api/v5/settings/general",
                     "/api/v5/series", "/api/v5/queue", "/api/v5/settings/metadata"):
            for key in (False, "invalid-audit-key"):
                status, _, body = self.raw(path, api_key=key)
                require(status in (401, 403), f"Unauthenticated {path} returned HTTP {status}")
                require(self.api_key.encode() not in body, "Denied response exposed API key")
            require(self.raw(path)[0] == 200, f"Valid API key failed for {path}")
        require(self.raw("/", api_key=False)[0] in (302, 401), "Anonymous UI access was not challenged")
        require(self.raw("/login", api_key=False)[0] == 200, "Public login page unavailable")
        settings = self.json_api("/api/v5/settings/general")
        require(settings["authenticationRequired"] == "enabled", "Authentication bypass enabled unexpectedly")
        settings.update(username=self.username, password=self.password, passwordConfirmation=self.password)
        saved = self.json_api("/api/v5/settings/general/1", "PUT", settings)
        require(saved["password"] == "********", "User password not masked on save")
        # No query-string API key is involved in this login.
        form = urllib.parse.urlencode({"username": self.username, "password": self.password})
        status, headers, _ = self.raw("/login?returnUrl=https%3A%2F%2Fevil.invalid%2F", "POST", form,
                                     api_key=False, headers={"Content-Type": "application/x-www-form-urlencoded"})
        require(status == 302, "Valid form login failed")
        require(headers.get("Location") == "/", "Login return URL allowed an external redirect")
        cookie = headers.get("Set-Cookie", "")
        require("httponly" in cookie.lower() and "samesite=lax" in cookie.lower(), "Cookie missing HttpOnly/SameSite")
        self.cookie = cookie.split(";", 1)[0]
        require(self.raw("/", api_key=False, cookie=True)[0] == 200, "Authenticated UI request failed")
        require(self.raw("/api/v5/series", api_key=False, cookie=True)[0] == 401,
                "UI cookie unexpectedly bypassed API-key authentication")
        settings = self.json_api("/api/v5/settings/general")
        require(self.password not in json.dumps(settings), "General settings exposed clear password")
        require(settings["password"] == "********", "General settings exposed password hash")

    def login_abuse(self):
        invalid_form = urllib.parse.urlencode({"username": self.username, "password": "invalid-audit-password"})
        for attempt in range(5):
            status, headers, _ = self.raw("/login", "POST", invalid_form, api_key=False,
                                        headers={"Content-Type": "application/x-www-form-urlencoded"})
            require(status == 302 and "loginFailed=true" in headers.get("Location", ""),
                    f"Invalid login attempt {attempt + 1} unexpectedly succeeded")
        valid_form = urllib.parse.urlencode({"username": self.username, "password": self.password})
        status, headers, _ = self.raw("/login", "POST", valid_form, api_key=False,
                                    headers={"Content-Type": "application/x-www-form-urlencoded"})
        require(status == 302 and "lockoutUntil=" in headers.get("Location", ""),
                "Five failed logins did not enforce temporary lockout")
        require(self.raw("/api/v5/system/status")[0] == 200, "Login lockout incorrectly disabled API-key access")
        # Wrong reset tokens must not overwrite the account.
        reset_form = urllib.parse.urlencode({"username": "attacker", "password": "audit-password",
                                            "passwordConfirmation": "audit-password", "token": "invalid-token"})
        _, headers, _ = self.raw("/reset-password", "POST", reset_form, api_key=False,
                                headers={"Content-Type": "application/x-www-form-urlencoded"})
        require("resetFailed=true" in headers.get("Location", ""), "Invalid reset token was accepted")
        require(self.json_api("/api/v5/settings/general")["username"] == self.username,
                "Invalid password reset changed the account")

    def hosts_and_cors(self):
        for host in ("evil.invalid", "127.0.0.1.evil.invalid", "localhost.evil.invalid"):
            status, _, _ = self.raw("/api/v5/system/status", headers={"Host": host})
            require(status == 400, f"Disallowed Host {host} returned HTTP {status}")
        status, headers, _ = self.raw("/api/v5/series", "OPTIONS", api_key=False,
                                    headers={"Origin": "https://evil.invalid", "Access-Control-Request-Method": "GET",
                                             "Access-Control-Request-Headers": "X-Api-Key"})
        require(status in (200, 204), "API preflight failed")
        require(headers.get("Access-Control-Allow-Credentials", "").lower() != "true",
                "CORS enabled credentialed cross-origin reads")
        require(self.raw("/api/v5/series", api_key=False, headers={"Origin": "https://evil.invalid"})[0] == 401,
                "Cross-origin request bypassed API authorization")
        self.observations.append("API intentionally allows wildcard CORS without Allow-Credentials; API keys remain required.")

    def traversal_and_validation(self):
        for path in ("/content/../../config.xml", "/content/%2e%2e/%2e%2e/config/config.xml",
                     "/content/..%2f..%2fconfig/config.xml", "/content/%2e%2e%5cconfig.xml",
                     "/content/%252e%252e%252fconfig.xml", "/content/%2fconfig/config.xml",
                     "/content/../../etc/passwd", "/content/config.xml"):
            for authenticated in (False, True):
                status, headers, body = self.raw(path, api_key=False, cookie=authenticated)
                redirect = urllib.parse.urlsplit(headers.get("Location", ""))
                safe_login_challenge = (status == 302 and redirect.path == "/login"
                                        and redirect.hostname in (None, "127.0.0.1", "localhost"))
                safe_ui_fallback = (status == 200 and "text/html" in headers.get("Content-Type", "")
                                    and b'id="root"' in body)
                require(status in (400, 404) or safe_login_challenge or safe_ui_fallback,
                        f"Traversal/static path returned HTTP {status}: {path}")
                require(self.api_key.encode() not in body and b"root:x:0:0:" not in body,
                        "Traversal request exposed a sensitive file")
        for path in ("/api/v5/release?completeSeries=true",
                     "/api/v5/release?completeSeries=true&seriesId=1&episodeId=1",
                     "/api/v5/release?completeSeries=true&seriesId=1&seasonNumber=1",
                     "/api/v5/release?seriesId=invalid",
                     "/api/v5/episode?seriesId=invalid"):
            require(self.raw(path)[0] == 400, f"Invalid API scope was not rejected: {path}")
        require(self.raw("/api/v5/series", "POST", b"{invalid-json")[0] == 400,
                "Malformed JSON was not rejected")
        require(self.raw("/api/v5/series", "POST", {})[0] == 400, "Empty series payload accepted")
        require(self.json_api("/api/v5/series") == [], "Invalid API requests added series")

    def container_security(self):
        details = json.loads(self.docker("inspect", self.container))[0]
        require(not details["HostConfig"]["Privileged"], "Container is privileged")
        require(not details["HostConfig"].get("CapAdd"), "Additional Linux capabilities granted")
        require(details["HostConfig"]["NetworkMode"] != "host", "Container shares host network")
        require(all(m["Destination"] in ("/config", "/media") for m in details["Mounts"]),
                "Unexpected host mount in audit container")
        require(not any("docker.sock" in m["Source"] for m in details["Mounts"]), "Docker socket exposed")
        status = self.docker("exec", self.container, "cat", "/proc/1/status")
        require(re.search(r"^Uid:\s+1000\s+1000\s+1000\s+1000$", status, re.M), "Application is not UID 1000")
        require(re.search(r"^CapEff:\s+0+$", status, re.M), "Application has effective Linux capabilities")
        require(re.search(r"^Seccomp:\s+2$", status, re.M), "Docker seccomp filter is not active")
        self.docker("exec", "--user", "1000:1000", self.container, "sh", "-c",
                    "test -f /entrypoint.sh && test ! -w /app && test ! -w /app/Sonarr && test ! -w /entrypoint.sh")
        self.observations.append("Default Docker seccomp applies; process has no effective capabilities. Entrypoint starts as root to set volume ownership.")
        self.check_permissions()
        self.check_runtime()

    def companion_permissions(self):
        created = []
        for permission in ("read-library", "add-series"):
            key = self.json_api("/api/v5/librarytools/keys", "POST",
                                {"name": "isolated-" + permission, "permission": permission, "days": 1})
            created.append(key)
            self.companion_tokens.append(key["token"])
        read_token = created[0]["token"]
        self.add_token = created[1]["token"]
        listed = self.json_api("/api/v5/librarytools/keys")
        require(all("hash" not in key and "token" not in key for key in listed), "Key listing exposed stored hashes/tokens")
        for token in (read_token, self.add_token):
            for path in ("/api/v5/series", "/api/v5/episode?seriesId=1", "/api/v5/rootfolder", "/api/v5/qualityprofile", "/api/v5/tag"):
                require(self.raw(path, api_key=token)[0] == 200, f"Companion library read rejected: {path}")
            for path in ("/api/v5/settings/general", "/api/v5/settings/metadata", "/api/v5/command", "/api/v5/log",
                         "/api/v5/librarytools/keys", "/api/v5/librarytools", "/api/v5/system/status",
                         "/api/v5/series/lookup?term=Cowboy", "/api/v5/series/0", "/api/v5/series/../settings/general",
                         "/api/v5/series/%2e%2e/settings/general", "/api/v5/series%2f..%2fsettings/general"):
                status, _, body = self.raw(path, api_key=token)
                require(status in (400, 401, 403, 404), f"Companion accessed restricted path: {path} (HTTP {status})")
                require(self.api_key.encode() not in body, "Companion restricted response exposed master API key")
            require(self.raw("/api/v5/command", "POST", {"name": "RefreshSeries"}, api_key=token)[0] == 401,
                    "Companion could invoke commands")
            require(self.raw("/api/v5/settings/metadata/1", "PUT", {}, api_key=token)[0] == 401,
                    "Companion could modify settings")
            require(self.raw("/api/v5/series/1", "DELETE", api_key=token)[0] == 401,
                    "Companion could delete a series")
        require(self.raw("/api/v5/series", "POST", {}, api_key=read_token)[0] == 401,
                "Read-only companion could add series")
        require(self.raw("/api/v5/series", "POST", {}, api_key=self.add_token)[0] == 400,
                "Add-series companion did not reach input validation")
        require(self.raw("/api/v5/librarytools/keys", "POST", {"name": "bad", "permission": "admin", "days": 1})[0] == 400,
                "Unsupported companion permission accepted")
        self.json_api_no_content("/api/v5/librarytools/keys/" + created[0]["key"]["id"], "DELETE")
        require(self.raw("/api/v5/series", api_key=read_token)[0] == 401, "Revoked companion key remained authorized")

    def json_api_no_content(self, path, method):
        require(self.raw(path, method)[0] == 204, f"{method} {path} failed")

    def ui_assets(self):
        status, _, body = self.raw("/", api_key=False, cookie=True)
        require(status == 200, "Authenticated UI unavailable")
        scripts = re.findall(r'<script\b[^>]*\bsrc=[\"\']([^\"\']+)[\"\']', body.decode("utf-8"), re.I)
        require(scripts, "No compiled UI asset")
        for script in scripts:
            asset_path = urllib.parse.urljoin("/", script)
            status, headers, asset = self.raw(asset_path, api_key=False, cookie=True)
            require(status == 200 and "javascript" in headers.get("Content-Type", ""), "Compiled JS not served")
            require(b"<html" not in asset[:100].lower(), "JS request received HTML fallback")

    def metadata_masking(self):
        metadata = self.check_metadata()
        for path in ("/api/v5/settings/metadata", "/api/v5/settings/metadatasource"):
            require(self.metadata_key not in json.dumps(self.json_api(path)), "TMDB secret exposed in settings")
        return metadata

    def anilist_functional(self):
        require(self.json_api("/api/v5/indexer") == [], "Audit instance has configured indexers")
        require(self.json_api("/api/v5/downloadclient") == [], "Audit instance has download clients")
        self.json_api("/api/v5/rootfolder", "POST", {"path": "/media"})
        profile_id = self.json_api("/api/v5/qualityprofile")[0]["id"]
        term = urllib.parse.urlencode({"term": "Cowboy Bebop", "metadataSource": "aniList"})
        found = self.json_api("/api/v5/series/lookup?" + term)
        item = next(item for item in found if item.get("metadataSource") == "aniList" and str(item.get("foreignId")) == "1")
        require(item["titleSlug"] == "anilist-1", "AniList lookup slug lost native identity")
        item.update(rootFolderPath="/media", qualityProfileId=profile_id, monitored=True, seasonFolder=True,
                    seriesType="anime", seasonType="official", language={"id": 1, "name": "English"},
                    addOptions={"monitor": "all", "searchForMissingEpisodes": False, "searchForCutoffUnmetEpisodes": False})
        if self.add_token:
            self.companion_add_boundaries(item)
        status, _, body = self.raw("/api/v5/series", "POST", item, api_key=self.add_token or True)
        require(status == 201, f"Native series add with scoped companion returned HTTP {status}")
        self.series = json.loads(body)
        require(self.series["metadataSource"] == "aniList" and str(self.series["foreignId"]) == "1",
                "AniList add did not preserve native ID")
        require(self.series["tvdbId"] < 0, "Native add did not allocate a synthetic negative TVDB ID")
        require(self.series["titleSlug"] == "anilist-1", "Native add changed slug")
        deadline = time.monotonic() + 90
        episodes = []
        while time.monotonic() < deadline:
            episodes = self.json_api(f"/api/v5/episode?seriesId={self.series['id']}")
            if len(episodes) >= 26:
                break
            time.sleep(2)
        require(len(episodes) >= 26, "Native series episodes did not refresh")
        require(len({e["id"] for e in episodes}) == len(episodes), "Duplicate episode database IDs")
        require(all(e["seriesId"] == self.series["id"] for e in episodes), "Episode series identity mismatch")
        require(self.raw("/api/v5/series", "POST", item)[0] == 400, "Duplicate native series was accepted")
        # A missing indexer is an explicit search error, not an empty successful search.
        require(self.raw(f"/api/v5/release?seriesId={self.series['id']}&completeSeries=true")[0] == 400,
                "No-indexer complete search did not report a configuration error")
        self.start_test_indexer()
        require(self.json_api(f"/api/v5/release?seriesId={self.series['id']}&completeSeries=true") == [],
                "No-indexer complete search returned releases")
        command = self.json_api("/api/v5/command", "POST", {"name": "CompleteSeriesSearch", "seriesId": self.series["id"], "dryRun": True})
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            command = self.json_api(f"/api/v5/command/{command['id']}")
            if command["status"] in ("completed", "failed", "aborted"):
                break
            time.sleep(1)
        require(command["status"] == "completed", "Complete-series dry run did not complete")
        require(self.json_api("/api/v5/queue")["totalRecords"] == 0, "Dry run created download queue entries")
        require(self.json_api("/api/v5/history")["totalRecords"] == 0, "Dry run fabricated acquisition history")
        require(any(request != "caps" for request in self.indexer_requests), "Search did not reach the isolated indexer")

    def companion_add_boundaries(self, valid_item):
        variants = []
        for change in ({"path": "/config/isolated-outside-library", "rootFolderPath": ""},
                       {"path": "/media/custom-explicit-series-path"},
                       {"rootFolderPath": "/config/isolated-outside-library"},
                       {"rootFolderPath": "/media/unregistered-nested-root"}):
            item = dict(valid_item)
            item.update(change)
            variants.append(item)
        for flag in ("searchForMissingEpisodes", "searchForCutoffUnmetEpisodes"):
            item = dict(valid_item)
            item["addOptions"] = dict(valid_item["addOptions"], **{flag: True})
            variants.append(item)
        series_before = self.json_api("/api/v5/series")
        acquisition = {"SeriesSearch", "SeasonSearch", "EpisodeSearch", "MissingEpisodeSearch", "CutoffUnmetEpisodeSearch"}
        commands_before = {c["id"] for c in self.json_api("/api/v5/command") if c["name"] in acquisition}
        for endpoint in ("/api/v3/series", "/api/v5/series"):
            for index, payload in enumerate(variants):
                status, _, _ = self.raw(endpoint, "POST", payload, api_key=self.add_token)
                require(status == 400, f"Companion add scope variant {index + 1} at {endpoint} returned HTTP {status}")
                require(self.json_api("/api/v5/series") == series_before,
                        "Rejected companion payload changed the series database")
        commands_after = {c["id"] for c in self.json_api("/api/v5/command") if c["name"] in acquisition}
        require(commands_after == commands_before, "Rejected companion payload scheduled acquisition commands")
        self.check("V3/V5 companion custom-path/unregistered-root/implicit-search violations rejected before database or command changes")

    def start_test_indexer(self):
        audit = self

        class EmptyIndexer(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_GET(self):
                query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
                request_type = query.get("t", [""])[0]
                audit.indexer_requests.append(request_type)
                if request_type == "caps":
                    body = b'''<?xml version="1.0" encoding="UTF-8"?><caps>
                    <server version="1.0" title="Isolated security test"/>
                    <limits max="100" default="100"/><registration available="no" open="no"/>
                    <searching><search available="yes" supportedParams="q"/>
                    <tv-search available="yes" supportedParams="q,season,ep,tvdbid"/></searching>
                    <categories><category id="5000" name="TV"><subcat id="5070" name="TV/Anime"/></category></categories>
                    </caps>'''
                else:
                    body = b'''<?xml version="1.0" encoding="UTF-8"?>
                    <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed"
                         xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
                    <channel><title>Isolated empty indexer</title><description>No downloadable releases</description>
                    <link>http://localhost/</link><newznab:response offset="0" total="0"/></channel></rss>'''
                self.send_response(200)
                self.send_header("Content-Type", "application/xml")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self.indexer_server = ThreadingHTTPServer(("0.0.0.0", 0), EmptyIndexer)
        threading.Thread(target=self.indexer_server.serve_forever, daemon=True).start()
        details = json.loads(self.docker("inspect", self.container))[0]
        gateway = next(iter(details["NetworkSettings"]["Networks"].values()))["Gateway"]
        provider = next(item for item in self.json_api("/api/v5/indexer/schema") if item["implementation"] == "Torznab")
        provider.update(name="Isolated empty audit indexer", enableRss=False, enableAutomaticSearch=True,
                        enableInteractiveSearch=True, priority=25)
        values = {"baseUrl": f"http://{gateway}:{self.indexer_server.server_port}",
                  "apiPath": "/api", "categories": [5000], "animeCategories": [5070]}
        for field in provider["fields"]:
            if field["name"] in values:
                field["value"] = values[field["name"]]
        # Sonarr's setup test requires at least one release. This deliberately
        # empty fixture uses the supported skipTesting flag; the subsequent
        # searches must actually connect and parse its caps/RSS responses.
        self.json_api("/api/v5/indexer?skipTesting=true", "POST", provider)

    def anilist_schema(self):
        schema = self.json_api("/api/v5/importlist/schema")
        provider = next(item for item in schema if item.get("implementation") == "AniListImport")
        fields = {field["name"]: field for field in provider["fields"]}
        list_status = ("importCurrent", "importPlanning", "importCompleted", "importDropped", "importPaused", "importRepeating")
        media_status = ("importFinished", "importReleasing", "importUnreleased", "importCancelled", "importHiatus")
        require(all(name in fields for name in list_status + media_status), "AniList statuses missing from API schema")
        require("customList" in fields and "username" in fields, "AniList username/custom-list fields not distinct")
        require(len({fields[name].get("section") for name in list_status}) == 1, "User-list statuses are not grouped")
        require(fields["importCurrent"].get("section") != fields["importFinished"].get("section"),
                "User-list status and media-release status conflated")
        require(self.json_api("/api/v5/importlist") == [], "Schema lookup saved an import list")
        self.observations.append("AniList list status, media release status, username and custom-list fields are distinct in runtime schema; private-account synchronization requires real OAuth credentials and was not run.")

    def persist(self):
        before = self.json_api("/api/v5/system/status")
        metadata = self.json_api("/api/v5/settings/metadata")
        series_before = self.json_api(f"/api/v5/series/{self.series['id']}") if self.series else None
        episodes_before = self.json_api(f"/api/v5/episode?seriesId={self.series['id']}") if self.series else []
        self.docker("restart", "--time", "20", self.container)
        self.refresh_binding()
        self.wait_ready()
        require(self.json_api("/api/v5/system/status")["startTime"] != before["startTime"], "Runtime did not restart")
        require(self.json_api("/api/v5/settings/metadata") == metadata, "Metadata settings did not persist")
        require(self.raw("/api/v5/system/status", api_key=False)[0] == 401, "Restart disabled authentication")
        if series_before:
            after = self.json_api(f"/api/v5/series/{self.series['id']}")
            for field in ("id", "metadataSource", "foreignId", "tvdbId", "titleSlug", "path"):
                require(after[field] == series_before[field], f"Restart changed series {field}")
            episodes_after = self.json_api(f"/api/v5/episode?seriesId={self.series['id']}")
            require({e["id"] for e in episodes_before} == {e["id"] for e in episodes_after}, "Restart changed episode IDs")
        # Database contents are inspected only for this script's fresh temporary instance.
        connection = sqlite3.connect(self.root / "config" / "sonarr.db")
        try:
            user = connection.execute("SELECT Username, Password, Salt, Iterations FROM Users").fetchone()
            require(user and user[0] == self.username, "Persisted user mismatch")
            require(user[1] != self.password and user[2] and user[3] >= 220000,
                    "Password storage is not salted PBKDF2 with configured iteration count")
        finally:
            connection.close()

    def browser(self):
        script = Path(__file__).with_name("ui-security.cjs").resolve()
        artifact = self.args.report.parent.resolve() if self.args.report else self.root / "browser-report"
        artifact.mkdir(parents=True, exist_ok=True)
        def native_path(value):
            if self.args.browser_node.lower().endswith(".exe") and sys.platform != "win32":
                return subprocess.check_output(["wslpath", "-w", str(value)], text=True).strip()
            return str(value)
        payload = {"baseUrl": self.base_url, "username": self.username, "password": self.password,
                   "apiKey": self.api_key, "seriesId": self.series["id"] if self.series else None,
                   "playwrightModule": self.args.playwright_module, "artifactDirectory": native_path(artifact)}
        result = subprocess.run([self.args.browser_node, native_path(script)], input=json.dumps(payload),
                                capture_output=True, text=True, encoding="utf-8", timeout=300)
        output = result.stdout + result.stderr
        for secret in (self.api_key, self.metadata_key, self.password, *self.companion_tokens):
            output = output.replace(secret, "[REDACTED]")
        print(output.strip(), flush=True)
        require(result.returncode == 0, "Playwright browser checks failed; see ui-security.json")
        self.browser_tested = True

    def run(self):
        self.start()
        for name, callback in (
            ("missing/wrong API keys rejected; Forms login, masked password, HttpOnly/SameSite cookie and redirect guard", self.authentication),
            ("compiled frontend assets served to authenticated user", self.ui_assets),
            ("login brute-force lockout and invalid reset-token rejection", self.login_abuse),
            ("allowed-host enforcement, DNS-rebinding Host variants and non-credentialed CORS", self.hosts_and_cors),
            ("static traversal variants and malformed/conflicting API scopes rejected", self.traversal_and_validation),
            ("non-root app, zero effective capabilities, no privileged/socket/host-network deployment", self.container_security),
            ("TMDB key masking, API aliases and legacy save compatibility", self.metadata_masking),
            ("companion read/add permission allowlists, path normalization, secret-safe listing and revocation", self.companion_permissions),
            ("AniList list-status/media-status/custom-list schema separation", self.anilist_schema),
        ):
            self.run_check(name, callback)
        if self.args.anilist:
            self.run_check("live AniList native add, refresh, duplicate rejection and complete-search dry run without acquisition", self.anilist_functional)
        self.run_check("restart preserves authentication, settings/native identity/episode IDs and salted password storage", self.persist)
        if self.args.browser_node:
            self.run_check("real Chromium browser UI E2E and provider/list distinctions", self.browser)

    def cleanup(self):
        if self.indexer_server:
            self.indexer_server.shutdown()
            self.indexer_server.server_close()
            self.indexer_server = None
        super().cleanup()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="sonarr-weeb:local")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--anilist", action="store_true")
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--browser-node", help="external Node executable for optional Playwright UI E2E")
    parser.add_argument("--playwright-module", help="absolute external Playwright module directory visible to Node")
    args = parser.parse_args()
    require(bool(args.browser_node) == bool(args.playwright_module), "Provide both --browser-node and --playwright-module")
    audit = FunctionalSecurity(args)
    try:
        audit.run()
    except Exception as error:
        audit.failures.append({"check": "startup or execution", "error": type(error).__name__})
        print(f"FAIL execution: {type(error).__name__}", flush=True)
    finally:
        try:
            audit.cleanup()
        except Exception as error:
            audit.failures.append({"check": "cleanup", "error": type(error).__name__})
            print(f"FAIL cleanup: {type(error).__name__}", flush=True)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({
                "image": args.image, "imageId": audit.image_id, "passed": not audit.failures,
                "checks": audit.results, "failures": audit.failures, "observations": audit.observations,
                "liveAniListRequested": args.anilist, "realLibraryTested": False, "downloadsTested": False,
                "browserUITested": audit.browser_tested, "privateAniListAccountTested": False,
                "keptContainer": audit.container if args.keep else None,
            }, indent=2) + "\n", encoding="utf-8")
    return 1 if audit.failures else 0


if __name__ == "__main__":
    sys.exit(main())
