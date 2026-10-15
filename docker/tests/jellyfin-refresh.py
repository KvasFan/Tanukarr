#!/usr/bin/env python3
"""Verify selected-library Jellyfin refresh using real isolated servers and generated media."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import select
import socket
import socketserver
import sqlite3
import sys
import tempfile
import threading
import urllib.error
import urllib.parse

from smoke import ContainerSmoke, SmokeFailure, require


JF_IMAGE = "jellyfin/jellyfin:12.2@sha256:357724bf0ae27a672c7cbaa899db2d9abeb13dbd8657ccce750258a4c059d037"
RELAY_IMAGE = "python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a"


def serve_relay():
    """Expose only the two fixed owned destinations; there is no arbitrary HTTP proxy."""
    class Relay(socketserver.BaseRequestHandler):
        def handle(self):
            target = "fixture-app" if self.server.server_address[1] == 8989 else "jellyfin-test"
            with socket.create_connection((target, self.server.server_address[1]), timeout=30) as destination:
                peers = (self.request, destination)
                while True:
                    ready, _, _ = select.select(peers, [], [], 60)
                    if not ready:
                        return
                    for source in ready:
                        data = source.recv(65536)
                        if not data:
                            return
                        (destination if source is self.request else self.request).sendall(data)

    class Server(socketserver.ThreadingTCPServer):
        allow_reuse_address = True
        daemon_threads = True

    with Server(("0.0.0.0", 8989), Relay) as app, Server(("0.0.0.0", 8096), Relay) as jf:
        thread = threading.Thread(target=app.serve_forever, daemon=True)
        thread.start()
        print("Fixed-destination refresh relay ready", flush=True)
        jf.serve_forever()


if __name__ == "__main__" and "--serve-relay" in sys.argv:
    serve_relay()
    sys.exit(0)


from jellyfin import JellyfinIntegration


class JellyfinRefresh(JellyfinIntegration):
    def __init__(self, args):
        super().__init__(args)
        self.api_network = None
        self.relay = None
        self.jellyfin_image_id = None
        self.relay_image_id = None
        self.image_labels = {}
        self.cleanup_complete = False
        self.refresh_observations = []
        self.library_observations = []
        self.api_observations = []
        self.generated_imports = []
        self.native_identities = []
        self.latest_import_generation = 0

    def redact(self, value):
        for secret in (self.api_key, self.jellyfin_token, self.jellyfin_password, self.sonarr_password):
            if secret:
                value = value.replace(secret, "[REDACTED]")
        return value

    def docker(self, *arguments, timeout=60):
        # The inherited recovery case needs the actual reachable loopback binding.
        # Jellyfin itself stays unpublished on the internal network.
        if self.relay and arguments == ("port", self.jellyfin, "8096/tcp"):
            return super().docker("port", self.relay, "8096/tcp", timeout=timeout)
        return super().docker(*arguments, timeout=timeout)

    def refresh_binding(self):
        detail = json.loads(self.docker("inspect", self.relay))[0]
        for port, attribute in (("8989/tcp", "base_url"), ("8096/tcp", "jellyfin_url")):
            binding = detail["NetworkSettings"]["Ports"][port]
            require(len(binding) == 1 and binding[0]["HostIp"] == "127.0.0.1", "Refresh fixture relay must bind only to loopback")
            setattr(self, attribute, "http://127.0.0.1:" + binding[0]["HostPort"])

    def start_servers(self):
        require(self.docker("info", "--format", "{{.OSType}}") == "linux", "A Linux Docker daemon is required")
        image = json.loads(self.docker("image", "inspect", self.args.image))[0]
        require(image["Os"] == "linux", "An already built Linux Tanukarr image is required")
        self.image_id = image["Id"]
        self.image_labels = image["Config"].get("Labels", {}) or {}
        require(re.fullmatch(r"[^\s]+@sha256:[0-9a-f]{64}", self.args.jellyfin_image), "The real Jellyfin image must be digest-pinned")
        self.jellyfin_image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.jellyfin_image)
        self.relay_image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.relay_image)
        self.root = Path(tempfile.mkdtemp(prefix="sonarr-container-smoke-jellyfin-refresh-")).resolve()
        config = self.root / "config"
        config.mkdir()
        media = self.root / "media"
        media.mkdir()
        media.chmod(0o777)
        (config / "config.xml").write_text(
            "<Config><BindAddress>*</BindAddress><Port>8989</Port>"
            f"<ApiKey>{self.api_key}</ApiKey><LaunchBrowser>False</LaunchBrowser>"
            "<AuthenticationMethod>Forms</AuthenticationMethod><AuthenticationRequired>Enabled</AuthenticationRequired>"
            "<AllowedHosts>127.0.0.1,localhost</AllowedHosts><AnalyticsEnabled>False</AnalyticsEnabled>"
            "<LogLevel>Debug</LogLevel></Config>", encoding="utf-8")
        suffix = secrets.token_hex(6)
        self.network = self.docker("network", "create", "--internal", "tanukarr-jf-refresh-" + suffix)
        self.api_network = self.docker("network", "create", "tanukarr-jf-refresh-api-" + suffix)
        self.container = self.docker(
            "create", "--name", "tanukarr-jf-refresh-app-" + suffix,
            "--label", "sonarr.smoke-test=true", "--label", "tanukarr.jellyfin-refresh-test=true",
            "--network", self.network, "--network-alias", "fixture-app",
            "--env", "PUID=1000", "--env", f"PGID={self.fixture_gid}", "--env", "UMASK=002",
            "--env", "HTTP_PROXY=", "--env", "HTTPS_PROXY=", "--env", "ALL_PROXY=",
            "--mount", f"type=bind,source={config},target=/config",
            "--mount", f"type=bind,source={media},target=/media", self.args.image)
        self.docker("start", self.container)
        self.relay = self.docker(
            "create", "--name", "tanukarr-jf-refresh-relay-" + suffix,
            "--label", "tanukarr.jellyfin-refresh-test=true", "--network", self.api_network,
            "--publish", "127.0.0.1::8989", "--publish", "127.0.0.1::8096",
            "--mount", f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            self.args.relay_image, "python", "-u", "/runner/jellyfin-refresh.py", "--serve-relay")
        self.docker("network", "connect", self.network, self.relay)
        self.docker("start", self.relay)
        def relay_ready():
            detail = json.loads(self.docker("inspect", self.relay))[0]
            require(detail["State"]["Running"], "The owned refresh API relay exited during startup")
            return "Fixed-destination refresh relay ready" in self.docker("logs", self.relay)

        self.until(relay_ready, "The fixed refresh API relay did not start", 30)
        self.refresh_binding()
        self.wait_ready()
        jf_config = self.root / "jellyfin-config"
        jf_config.mkdir()
        self.jellyfin = self.docker(
            "run", "--detach", "--name", "tanukarr-jf-refresh-server-" + suffix,
            "--label", "tanukarr.jellyfin-refresh-test=true", "--network", self.network,
            "--network-alias", "jellyfin-test",
            "--mount", f"type=bind,source={jf_config},target=/config",
            "--mount", f"type=bind,source={media},target=/media", self.args.jellyfin_image)

        def ready():
            try:
                return self.json_request(self.jellyfin_url, "/Startup/User")[0] == 200
            except (OSError, urllib.error.URLError):
                return False

        self.until(ready, "Real isolated Jellyfin startup timed out", self.args.timeout)
        self.jf("/Startup/User", "POST", {"Name": "isolated-jellyfin-audit", "Password": self.jellyfin_password})
        self.jf("/Startup/Configuration", "POST", {"ServerName": "Isolated Jellyfin refresh audit", "UICulture": "en-US",
                                                 "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
        self.jf("/Startup/Complete", "POST")
        auth = self.jf("/Users/AuthenticateByName", "POST", {"Username": "isolated-jellyfin-audit", "Pw": self.jellyfin_password})
        self.jellyfin_token = auth["AccessToken"]
        self.jf("/Auth/Keys?app=Tanukarr-isolated-test", "POST")
        self.jellyfin_token = next(key["AccessToken"] for key in self.jf("/Auth/Keys")["Items"]
                                   if key["AppName"] == "Tanukarr-isolated-test")
        for name in ("Anime", "Untouched"):
            folder = media / name
            folder.mkdir()
            folder.chmod(0o777)
            options = {"LibraryOptions": {"EnableRealtimeMonitor": False, "SaveLocalMetadata": False,
                       "MetadataSavers": [], "TypeOptions": [{"Type": kind, "MetadataFetchers": [], "ImageFetchers": []}
                                                              for kind in ("Series", "Season", "Episode")]}}
            query = urllib.parse.urlencode({"name": name, "collectionType": "tvshows", "paths": "/media/" + name,
                                            "refreshLibrary": "false"})
            self.jf("/Library/VirtualFolders?" + query, "POST", options)
        self.libraries = {item["Name"]: item["ItemId"] for item in self.jf("/Library/VirtualFolders")}
        require(set(self.libraries) == {"Anime", "Untouched"}, "Unexpected isolated refresh libraries")
        self.generate("/media/Untouched/Baseline Show/Season 01/Baseline Show - S01E01.mkv")
        self.generate("/media/Anime/Bootstrap Show/Season 01/Bootstrap Show - S01E01.mkv")
        self.jf("/Library/Refresh", "POST")
        self.until(lambda: self.count("Untouched") == 1 and self.count("Anime") == 1, "Bootstrap library scan failed", 150)
        self.until(lambda: all(task["State"] != "Running" for task in self.jf("/ScheduledTasks")
                               if task["Key"] == "RefreshLibrary"), "Bootstrap global scan remained busy", 150)
        require(json.loads(self.docker("network", "inspect", self.network))[0]["Internal"], "Refresh network is externally routable")
        for container in (self.container, self.jellyfin):
            detail = json.loads(self.docker("inspect", container))[0]
            networks = list(detail["NetworkSettings"]["Networks"].values())
            require(len(networks) == 1 and networks[0]["NetworkID"] == self.network
                    and not any(detail["NetworkSettings"]["Ports"].values()), "Owned server has an external route or a direct host port")
        status = self.sonarr("/system/status", version="v5")
        self.runtime_metadata = {key: status.get(key) for key in ("appName", "productName", "productVersion", "version", "runtimeVersion", "migrationVersion")}
        self.check("real pinned Jellyfin and Tanukarr are isolated behind a fixed-destination loopback relay")
        self.check("two generated TV libraries are initialized once with realtime and external metadata providers disabled")

    @staticmethod
    def insert(db, table, values):
        for _, name, kind, required, default, primary in db.execute(f'PRAGMA table_info("{table}")'):
            if required and default is None and not primary and name not in values:
                values[name] = 0 if kind.upper() in ("INTEGER", "BOOLEAN") else ""
        columns = ",".join('"' + name + '"' for name in values)
        db.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({",".join("?" for _ in values)})', list(values.values()))

    def configure(self):
        old = self.args.browser_only
        self.args.browser_only = True
        try:
            super().configure()
        finally:
            self.args.browser_only = old
        for version in ("v3", "v5"):
            route = "/notification" if version == "v3" else "/connection"
            saved = self.sonarr(route + "/" + str(self.definition["id"]), version=version)
            token = next(item["value"] for item in saved["fields"] if item["name"] == "apiKey")
            require(token == "********", "A saved real Jellyfin API key was exposed")
            tested = self.sonarr(route + "/test", "POST", saved, version=version)
            self.api_observations.append({"api": version, "maskedSavedDefinitionTested": True, "testResultEmpty": tested in (None, "{}", {})})
        self.check("v3/v5 saved Connect definitions mask and preserve the real Jellyfin key during Test")
        self.sonarr("/rootfolder", "POST", {"path": "/media/Anime"}, version="v5")
        profile = self.sonarr("/qualityprofile", version="v5")[0]["id"]
        require(self.sonarr("/series", version="v5") == [], "The owned refresh fixture unexpectedly contains series")
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            self.insert(db, "Series", dict(
                Id=1, TvdbId=9999001, Title="Generated Refresh Series", TitleSlug="generated-refresh-series",
                CleanTitle="generatedrefreshseries", SortTitle="generated refresh series", Path="/media/Anime/Generated Refresh Series",
                Images="[]", Monitored=0, SeasonFolder=1, Runtime=24, SeriesType=1, QualityProfileId=profile,
                Tags="[]", Added="2026-10-14 00:00:00", Genres="[]", Actors="[]", AlternateTitles="[]",
                Seasons='[{"seasonNumber":1,"monitored":false}]', Ratings='{"votes":0,"value":0}',
                OriginalLanguage=1, Language=1, MetadataSource=0, ForeignId="9999001", AniListIds="[]", MalIds="[]"))
            for number in range(1, 6):
                self.insert(db, "Episodes", dict(
                    Id=number, SeriesId=1, SeasonNumber=1, EpisodeNumber=number, AbsoluteEpisodeNumber=number,
                    Title="Generated refresh episode " + str(number), EpisodeFileId=0, Monitored=0,
                    Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                    AirDate="2024-01-01", AirDateUtc="2024-01-01 00:00:00"))
            db.execute("UPDATE ScheduledTasks SET LastExecution='2050-01-01 00:00:00', LastStartTime='2050-01-01 00:00:00' "
                       "WHERE TypeName NOT LIKE '%ProcessPendingJellyfinRefreshCommand'")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        self.series = self.sonarr("/series/1", version="v5")
        self.native_identities = self.identities()
        require(self.native_identities == [(number, 1, 1, number) for number in range(1, 6)], "Native seeded episode identities changed")
        self.generate("/media/Untouched/Unscanned Show/Season 01/Unscanned Show - S01E01.mkv")
        self.check("owned native series and five missing episodes are seeded without any public catalogue request")

    def identities(self):
        return sorted((item["id"], item["seriesId"], item["seasonNumber"], item["episodeNumber"])
                      for item in self.sonarr("/episode?seriesId=1", version="v5"))

    def check(self, name):
        super().check(name)
        if self.definition and self.series:
            state = self.state()
            self.refresh_observations.append({"check": name, "importedGeneration": state["ImportedGeneration"],
                                              "immediateGeneration": state["ImmediateGeneration"], "tickGeneration": state["TickGeneration"],
                                              "lastRefreshMode": state["LastRefreshMode"], "lastTickUtc": state["LastTickUtc"]})
            self.library_observations.append({"check": name, "selectedEpisodes": self.count("Anime"),
                                              "unselectedEpisodes": self.count("Untouched")})

    def verify(self):
        super().verify()
        require(self.identities() == self.native_identities, "Refresh changed native episode identities")
        episodes = self.sonarr("/episode?seriesId=1", version="v5")
        require(len(episodes) == 5 and all(item["hasFile"] for item in episodes), "Not all five normal ManualImport operations persisted")
        for item in episodes:
            file = self.sonarr("/episodefile/" + str(item["episodeFileId"]), version="v5")
            local = self.root / "media" / Path(file["path"]).relative_to("/media")
            self.generated_imports.append({"episodeNumber": item["episodeNumber"], "path": file["path"],
                                           "size": local.stat().st_size, "sha256": hashlib.sha256(local.read_bytes()).hexdigest()})
        require(self.count("Anime") == 6 and self.count("Untouched") == 1, "Selected or unselected final counts changed")
        require(self.sonarr("/downloadclient") == [] and self.sonarr("/indexer") == []
                and self.sonarr("/importlist") == [] and self.sonarr("/queue")["totalRecords"] == 0,
                "Real Jellyfin refresh configured acquisition or private list data")
        self.check("all five normal generated imports preserve native identities and leave other libraries and acquisition empty")

    def cleanup(self):
        if self.args.keep:
            super().cleanup()
            return
        failures = []
        for attribute in ("relay", "jellyfin"):
            resource = getattr(self, attribute)
            if resource:
                try:
                    self.docker("rm", "--force", resource)
                    setattr(self, attribute, None)
                except Exception as error:
                    failures.append(self.redact(str(error)))
        if self.root and os.name == "posix":
            try:
                owned = self.root.resolve()
                require(owned == self.root and owned.parent == Path(tempfile.gettempdir()).resolve()
                        and owned.name.startswith("sonarr-container-smoke-") and not owned.is_symlink(),
                        "Refusing ownership recovery outside the owned refresh fixture")
                if self.container and self.running():
                    self.docker("stop", self.container)
                self.docker("run", "--rm", "--network", "none", "--user", "0:0",
                            "--label", "tanukarr.jellyfin-refresh-ownership=true", "--entrypoint", "/bin/chown",
                            "--mount", f"type=bind,source={owned},target=/owned", self.args.image,
                            "-R", "--no-dereference", f"{os.getuid()}:{os.getgid()}", "/owned")
            except Exception as error:
                failures.append(self.redact(str(error)))
        try:
            ContainerSmoke.cleanup(self)
        except Exception as error:
            failures.append(self.redact(str(error)))
        for attribute in ("network", "api_network"):
            resource = getattr(self, attribute)
            if resource:
                try:
                    self.docker("network", "rm", resource)
                    setattr(self, attribute, None)
                except Exception as error:
                    failures.append(self.redact(str(error)))
        self.cleanup_complete = not failures and all(value is None for value in (
            self.container, self.jellyfin, self.relay, self.root, self.network, self.api_network))
        require(self.cleanup_complete, "Owned Jellyfin refresh cleanup failed: " + "; ".join(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--jellyfin-image", default=JF_IMAGE)
    parser.add_argument("--relay-image", default=RELAY_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=240)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--serve-relay", action="store_true", help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False, browser_only=False)
    args = parser.parse_args()
    if args.serve_relay:
        serve_relay()
        return 0
    require(args.timeout > 0, "--timeout must be positive")
    if args.report:
        require(not args.report.exists(), "Use a fresh report destination; previous evidence is retained")
    audit = JellyfinRefresh(args)
    error = None
    logs = ""
    try:
        audit.start_servers()
        audit.configure()
        audit.verify()
    except (Exception, KeyboardInterrupt) as caught:
        error = audit.redact(audit.last_request + ": " + (str(caught) or type(caught).__name__))
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
            report = {"createdUtc": datetime.now(timezone.utc).isoformat(), "image": args.image,
                      "imageId": audit.image_id, "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                      "runtime": audit.runtime_metadata, "jellyfinImage": args.jellyfin_image,
                      "jellyfinImageId": audit.jellyfin_image_id, "relayImage": args.relay_image,
                      "relayImageId": audit.relay_image_id, "passed": error is None, "error": error,
                      "checks": audit.results, "cleanupComplete": audit.cleanup_complete,
                      "nativeEpisodeIdentities": audit.native_identities, "generatedImports": audit.generated_imports,
                      "refreshStateObservations": audit.refresh_observations, "libraryObservations": audit.library_observations,
                      "notificationApiChecks": audit.api_observations, "fixtureConfigOwnership": audit.fixture_config_ownership,
                      "scope": {"realJellyfinTested": bool(audit.library_observations), "generatedMediaOnly": True,
                                "seriesAndMissingEpisodeRowsSeededDirectly": True, "episodeFilesImportedThroughNormalApi": True,
                                "oneMinuteTickUsesRealScheduler": True, "initialGlobalBootstrapScanOnly": True,
                                "externalProviderRoutingEnabled": False, "ownerConfigurationAccessed": False,
                                "privateAccountsUsed": False, "downloadsStarted": False,
                                "apiBindAddress": "127.0.0.1:random", "watchedFeedbackTested": False},
                      "keptContainer": audit.container if args.keep else None,
                      "keptJellyfin": audit.jellyfin if args.keep else None,
                      "keptData": str(audit.root) if args.keep and audit.root else None}
            args.report.write_text(audit.redact(json.dumps(report, indent=2)) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
