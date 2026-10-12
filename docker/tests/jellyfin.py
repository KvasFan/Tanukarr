#!/usr/bin/env python3
"""Test Jellyfin refresh using disposable servers and generated videos only."""

import argparse
import json
from pathlib import Path
import secrets
import sqlite3
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from smoke import ContainerSmoke, SmokeFailure, require


class JellyfinIntegration(ContainerSmoke):
    def __init__(self, args):
        super().__init__(args)
        self.jellyfin = None
        self.network = None
        self.jellyfin_url = None
        self.jellyfin_token = None
        self.jellyfin_password = secrets.token_urlsafe(32)
        self.definition = None
        self.series = None
        self.libraries = {}
        self.last_request = "startup"

    def json_request(self, base, path, method="GET", data=None, token=None):
        self.last_request = f"{'Jellyfin' if base == self.jellyfin_url else 'Sonarr'} {method} {path.split('?')[0]}"
        headers = {"Content-Type": "application/json"}
        if base == self.jellyfin_url:
            headers["Authorization"] = 'MediaBrowser Client="SonarrWeeb integration test", Device="Generated fixture", DeviceId="sonarrweeb-isolated-audit", Version="0.0.1"'
        if token:
            if base == self.jellyfin_url:
                headers["Authorization"] += f', Token="{token}"'
            else:
                headers["X-Api-Key"] = token
        payload = None if data is None else json.dumps(data).encode()
        request = urllib.request.Request(base + path, data=payload, headers=headers, method=method)
        try:
            with self.http.open(request, timeout=60) as response:
                body = response.read()
                return response.status, json.loads(body) if body else None
        except urllib.error.HTTPError as error:
            # Neither HTTP request headers nor response bodies are included in failures.
            return error.code, None

    def sonarr(self, path, method="GET", data=None):
        status, resource = self.json_request(self.base_url, "/api/v3" + path, method, data, self.api_key)
        require(200 <= status < 300, f"Sonarr {method} {path.split('?')[0]} returned {status}")
        return resource

    def jf(self, path, method="GET", data=None):
        status, resource = self.json_request(self.jellyfin_url, path, method, data, self.jellyfin_token)
        require(200 <= status < 300, f"Jellyfin {method} {path.split('?')[0]} returned {status}")
        return resource

    def until(self, callback, message, timeout=90):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if callback():
                return
            time.sleep(1)
        raise SmokeFailure(message)

    def start_servers(self):
        self.start()
        suffix = secrets.token_hex(6)
        self.network = self.docker("network", "create", "sonarr-jellyfin-" + suffix)
        self.docker("network", "connect", self.network, self.container)
        jf_config = self.root / "jellyfin-config"
        jf_config.mkdir()
        self.jellyfin = self.docker(
            "run", "--detach", "--name", "sonarr-jellyfin-test-" + suffix,
            "--label", "sonarr.jellyfin-test=true", "--network", self.network,
            "--network-alias", "jellyfin-test", "--publish", "127.0.0.1::8096",
            "--mount", f"type=bind,source={jf_config},target=/config",
            "--mount", f"type=bind,source={self.root / 'media'},target=/media",
            self.args.jellyfin_image)
        port = self.docker("port", self.jellyfin, "8096/tcp").rsplit(":", 1)[1]
        self.jellyfin_url = "http://127.0.0.1:" + port

        def ready():
            try:
                return self.json_request(self.jellyfin_url, "/Startup/User")[0] == 200
            except (OSError, urllib.error.URLError):
                return False

        self.until(ready, "Jellyfin startup timed out", self.args.timeout)
        self.jf("/Startup/User")
        self.jf("/Startup/User", "POST", {"Name": "isolated-jellyfin-audit", "Password": self.jellyfin_password})
        self.jf("/Startup/Configuration", "POST", {"ServerName": "Isolated Jellyfin audit", "UICulture": "en-US",
                                                    "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
        self.jf("/Startup/Complete", "POST")
        auth = self.jf("/Users/AuthenticateByName", "POST", {"Username": "isolated-jellyfin-audit", "Pw": self.jellyfin_password})
        self.jellyfin_token = auth["AccessToken"]
        self.jf("/Auth/Keys?app=SonarrWeeb-isolated-test", "POST")
        self.jellyfin_token = next(key["AccessToken"] for key in self.jf("/Auth/Keys")["Items"]
                                   if key["AppName"] == "SonarrWeeb-isolated-test")
        for name in ("Anime", "Untouched"):
            (self.root / "media" / name).mkdir()
            options = {"LibraryOptions": {"EnableRealtimeMonitor": False, "SaveLocalMetadata": False,
                       "MetadataSavers": [], "TypeOptions": [
                           {"Type": kind, "MetadataFetchers": [], "ImageFetchers": []}
                           for kind in ("Series", "Season", "Episode")]}}
            query = urllib.parse.urlencode({"name": name, "collectionType": "tvshows", "paths": "/media/" + name,
                                            "refreshLibrary": "false"})
            self.jf("/Library/VirtualFolders?" + query, "POST", options)
        self.libraries = {item["Name"]: item["ItemId"] for item in self.jf("/Library/VirtualFolders")}
        require(set(self.libraries) == {"Anime", "Untouched"}, "Unexpected isolated libraries")
        self.check("disposable Jellyfin server and two separate TV libraries")

    def configure(self):
        schemas = self.sonarr("/notification/schema")
        self.definition = next((item for item in schemas if item["implementation"] == "Jellyfin"), None)
        require(self.definition is not None, "Jellyfin missing from Connect provider schema")
        self.definition.update(name="Isolated Jellyfin", onDownload=True, onUpgrade=True, tags=[])
        fields = {item["name"]: item for item in self.definition["fields"]}
        require(fields["apiKey"]["privacy"] == "apiKey", "Jellyfin token is not marked private")
        require(fields["libraryId"].get("selectOptionsProviderAction") == "libraries", "Library discovery action missing")
        for name, value in {"host": "jellyfin-test", "port": 8096, "useSsl": False, "urlBase": "",
                            "apiKey": self.jellyfin_token, "libraryId": self.libraries["Anime"],
                            "refreshMode": 0, "refreshIntervalMinutes": 1}.items():
            fields[name]["value"] = value
        options = self.sonarr("/notification/action/libraries", "POST", self.definition)
        require({item["value"] for item in options["options"]} == set(self.libraries.values()), "Library dropdown IDs differ")
        self.sonarr("/notification/test", "POST", self.definition)
        self.definition = self.sonarr("/notification", "POST", self.definition)
        saved = self.sonarr("/notification/" + str(self.definition["id"]))
        saved_token = next(item["value"] for item in saved["fields"] if item["name"] == "apiKey")
        require(saved_token != self.jellyfin_token and set(saved_token) == {"*"}, "Saved Jellyfin token not masked")
        # Keep the real key in memory only when updating this disposable connection.
        self.set_field("apiKey", self.jellyfin_token)
        self.check("Connect schema, library dropdown, connection test and masked credentials")
        self.sonarr("/rootfolder", "POST", {"path": "/media/Anime"})
        profile = self.sonarr("/qualityprofile")[0]["id"]
        found = self.sonarr("/series/lookup?" + urllib.parse.urlencode({"term": "Cowboy Bebop", "metadataSource": "aniList"}))
        item = next(item for item in found if item.get("metadataSource") == "aniList" and str(item.get("foreignId")) == "1")
        item.update(rootFolderPath="/media/Anime", qualityProfileId=profile, monitored=False, seasonFolder=True,
                    seriesType="anime", addOptions={"monitor": "none", "searchForMissingEpisodes": False})
        self.series = self.sonarr("/series", "POST", item)
        self.until(lambda: len(self.sonarr(f"/episode?seriesId={self.series['id']}")) >= 26, "Fixture episodes unavailable")
        self.generate("/media/Untouched/Unscanned Show/Season 01/Unscanned Show - S01E01.mkv")

    def set_field(self, name, value):
        next(item for item in self.definition["fields"] if item["name"] == name)["value"] = value

    def save_mode(self, mode):
        self.set_field("refreshMode", mode)
        self.set_field("apiKey", self.jellyfin_token)
        self.definition = self.sonarr("/notification/" + str(self.definition["id"]), "PUT", self.definition)
        self.set_field("apiKey", self.jellyfin_token)

    def generate(self, path):
        self.docker("exec", self.jellyfin, "mkdir", "-p", str(Path(path).parent))
        self.docker("exec", self.jellyfin, "/usr/lib/jellyfin-ffmpeg/ffmpeg", "-nostdin", "-v", "error",
                    "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=1", "-t", "2", "-c:v", "libx264",
                    "-pix_fmt", "yuv420p", "-y", path)
        self.docker("exec", self.jellyfin, "chmod", "0644", path)

    def command(self, payload):
        command = self.sonarr("/command", "POST", payload)
        result = {}

        def done():
            nonlocal result
            result = self.sonarr("/command/" + str(command["id"]))
            return result["status"] in ("completed", "failed", "aborted")

        self.until(done, "Sonarr command timed out", 120)
        require(result["status"] == "completed", f"{payload['name']} command failed")

    def import_episodes(self, numbers):
        episodes = {episode["episodeNumber"]: episode for episode in self.sonarr(f"/episode?seriesId={self.series['id']}")
                    if episode["seasonNumber"] == 1}
        files = []
        for number in numbers:
            path = f"/media/incoming/Cowboy Bebop - S01E{number:02d}.mkv"
            self.generate(path)
            files.append({"path": path, "seriesId": self.series["id"], "episodeIds": [episodes[number]["id"]],
                          "quality": {"quality": {"id": 5}, "revision": {"version": 1}},
                          "languages": [{"id": 1, "name": "English"}]})
        self.command({"name": "ManualImport", "files": files, "importMode": "copy"})
        self.until(lambda: all(self.sonarr("/episode/" + str(episodes[number]["id"]))["hasFile"] for number in numbers),
                   "Generated episodes were not imported")

    def count(self, library):
        query = urllib.parse.urlencode({"parentId": self.libraries[library], "recursive": "true", "includeItemTypes": "Episode"})
        return self.jf("/Items?" + query)["TotalRecordCount"]

    def state(self):
        with sqlite3.connect(self.root / "config" / "sonarr.db") as database:
            database.row_factory = sqlite3.Row
            return dict(database.execute("SELECT * FROM JellyfinRefreshStates WHERE NotificationId = ?",
                                         (self.definition["id"],)).fetchone())

    def verify(self):
        self.import_episodes([1])
        self.until(lambda: self.count("Anime") == 1, "Immediate refresh did not discover imported episode")
        require(self.count("Untouched") == 0, "Refresh reached unselected library")
        self.check("real import immediately refreshes only selected Jellyfin library")
        self.save_mode(1)
        self.import_episodes([2, 3])
        require(self.count("Anime") == 1, "Interval mode refreshed before its tick")
        before = self.state()
        self.docker("restart", self.container)
        self.wait_ready()
        require(self.state()["ImportedGeneration"] == before["ImportedGeneration"], "Pending imports lost at restart")
        self.until(lambda: self.count("Anime") == 3, "One-minute tick failed after restart", 120)
        require(self.count("Untouched") == 0, "Periodic scan reached unselected library")
        self.check("burst imports wait for one-minute tick and persist across restart")
        self.save_mode(2)
        self.import_episodes([4])
        self.until(lambda: self.count("Anime") == 4, "Combined mode immediate refresh failed")
        state = self.state()
        require(state["ImportedGeneration"] > state["TickGeneration"], "Immediate refresh consumed pending periodic tick")
        self.until(lambda: self.state()["TickGeneration"] == self.state()["ImportedGeneration"],
                   "Combined mode periodic refresh failed", 120)
        self.command({"name": "ProcessPendingJellyfinRefresh"})
        require(self.count("Untouched") == 0, "Idle processing scanned unselected library")
        self.check("combined mode retains separate periodic work; idle processing leaves other library untouched")
        self.save_mode(0)
        self.generate("/media/incoming/Cowboy Bebop - S01E05.mkv")
        self.docker("stop", self.jellyfin)
        # The generated source file already exists, so import while Jellyfin is offline.
        episode = next(item for item in self.sonarr(f"/episode?seriesId={self.series['id']}")
                       if item["seasonNumber"] == 1 and item["episodeNumber"] == 5)
        self.command({"name": "ManualImport", "importMode": "copy", "files": [{
            "path": "/media/incoming/Cowboy Bebop - S01E05.mkv", "seriesId": self.series["id"],
            "episodeIds": [episode["id"]], "quality": {"quality": {"id": 5}, "revision": {"version": 1}},
            "languages": [{"id": 1, "name": "English"}]}]})
        require(self.state()["ImportedGeneration"] > self.state()["ImmediateGeneration"], "Failure discarded pending refresh")
        self.docker("start", self.jellyfin)
        def restarted():
            try:
                return self.json_request(self.jellyfin_url, "/System/Info/Public")[0] == 200
            except (OSError, urllib.error.URLError):
                return False

        self.until(restarted, "Jellyfin restart timed out")
        self.command({"name": "ProcessPendingJellyfinRefresh"})
        self.until(lambda: self.count("Anime") == 5, "Retry failed to discover pending imported episode")
        require(self.count("Untouched") == 0, "Retry scanned unselected library")
        self.check("unavailable Jellyfin preserves pending work and retries successfully")
        self.set_field("libraryId", "00000000000000000000000000000001")
        status, _ = self.json_request(self.base_url, "/api/v3/notification/test", "POST", self.definition, self.api_key)
        require(status == 400, "Missing selected library accepted")
        self.check("stale library rejected without global refresh fallback")

    def cleanup(self):
        if self.jellyfin:
            self.docker("rm", "--force", self.jellyfin)
        super().cleanup()
        if self.network:
            self.docker("network", "rm", self.network)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True, help="Already built SonarrWeeb image")
    parser.add_argument("--jellyfin-image", default="jellyfin/jellyfin:12.2")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--report", type=Path)
    parser.set_defaults(keep=False, anilist=False)
    args = parser.parse_args()
    audit = JellyfinIntegration(args)
    failure = None
    try:
        audit.start_servers()
        audit.configure()
        audit.verify()
    except Exception as error:
        failure = audit.last_request + ": " + str(error)
        for token in (audit.api_key, audit.jellyfin_token, audit.jellyfin_password):
            if token:
                failure = failure.replace(token, "[REDACTED]")
        print("FAIL " + failure, file=sys.stderr)
    finally:
        report = {"image": args.image, "jellyfinImage": args.jellyfin_image, "checks": audit.results,
                  "failure": failure, "passed": failure is None}
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        audit.cleanup()
    return 1 if failure else 0


if __name__ == "__main__":
    sys.exit(main())
