#!/usr/bin/env python3
"""Test Jellyfin refresh using disposable servers and generated videos only."""

import argparse
import json
from pathlib import Path
import secrets
import sqlite3
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from smoke import ContainerSmoke, SmokeFailure, require

PRODUCT_VERSION = (Path(__file__).resolve().parents[2] / "VERSION").read_text(encoding="utf-8").strip()


class JellyfinIntegration(ContainerSmoke):
    def __init__(self, args):
        super().__init__(args)
        self.jellyfin = None
        self.network = None
        self.jellyfin_url = None
        self.jellyfin_token = None
        self.jellyfin_password = secrets.token_urlsafe(32)
        self.sonarr_password = secrets.token_urlsafe(32)
        self.definition = None
        self.series = None
        self.libraries = {}
        self.last_request = "startup"

    def json_request(self, base, path, method="GET", data=None, token=None):
        self.last_request = f"{'Jellyfin' if base == self.jellyfin_url else 'Sonarr'} {method} {path.split('?')[0]}"
        headers = {"Content-Type": "application/json"}
        if base == self.jellyfin_url:
            headers["Authorization"] = (
                'MediaBrowser Client="Tanukarr integration test", Device="Generated fixture", '
                f'DeviceId="tanukarr-isolated-audit", Version="{PRODUCT_VERSION}"'
            )
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
            try:
                return error.code, json.loads(error.read())
            except ValueError:
                return error.code, None

    def sonarr(self, path, method="GET", data=None, version="v3"):
        status, resource = self.json_request(self.base_url, "/api/" + version + path, method, data, self.api_key)
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
        self.jf("/Auth/Keys?app=Tanukarr-isolated-test", "POST")
        self.jellyfin_token = next(key["AccessToken"] for key in self.jf("/Auth/Keys")["Items"]
                                   if key["AppName"] == "Tanukarr-isolated-test")
        for name in ("Anime", "Untouched"):
            folder = self.root / "media" / name
            folder.mkdir()
            folder.chmod(0o777)
            options = {"LibraryOptions": {"EnableRealtimeMonitor": False, "SaveLocalMetadata": False,
                       "MetadataSavers": [], "TypeOptions": [
                           {"Type": kind, "MetadataFetchers": [], "ImageFetchers": []}
                           for kind in ("Series", "Season", "Episode")]}}
            query = urllib.parse.urlencode({"name": name, "collectionType": "tvshows", "paths": "/media/" + name,
                                            "refreshLibrary": "false"})
            self.jf("/Library/VirtualFolders?" + query, "POST", options)
        self.libraries = {item["Name"]: item["ItemId"] for item in self.jf("/Library/VirtualFolders")}
        require(set(self.libraries) == {"Anime", "Untouched"}, "Unexpected isolated libraries")
        # Initialize physical folders on this fresh test server before testing
        # selected refreshes. Production connections never use this global endpoint.
        self.generate("/media/Untouched/Baseline Show/Season 01/Baseline Show - S01E01.mkv")
        self.generate("/media/Anime/Bootstrap Show/Season 01/Bootstrap Show - S01E01.mkv")
        self.jf("/Library/Refresh", "POST")
        self.until(lambda: self.count("Untouched") == 1 and self.count("Anime") == 1,
                   "Initial test-library scan failed", 120)
        self.until(lambda: all(task["State"] != "Running" for task in self.jf("/ScheduledTasks")
                               if task["Key"] == "RefreshLibrary"), "Initial library scan remained busy", 120)
        require(self.count("Anime") == 1, "Anime baseline episode is missing")
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
        if self.args.browser_only:
            return

        self.sonarr("/rootfolder", "POST", {"path": "/media/Anime"})
        profile = self.sonarr("/qualityprofile")[0]["id"]
        found = self.sonarr("/series/lookup?" + urllib.parse.urlencode({"term": "Cowboy Bebop", "metadataSource": "aniList"}), version="v5")
        item = next(item for item in found if item.get("metadataSource") == "aniList" and str(item.get("foreignId")) == "1")
        item.update(rootFolderPath="/media/Anime", qualityProfileId=profile, monitored=False, seasonFolder=True,
                    seriesType="anime", addOptions={"monitor": "none", "searchForMissingEpisodes": False})
        self.series = self.sonarr("/series", "POST", item, version="v5")
        self.until(lambda: len(self.sonarr(f"/episode?seriesId={self.series['id']}")) >= 26, "Fixture episodes unavailable")
        self.generate("/media/Untouched/Unscanned Show/Season 01/Unscanned Show - S01E01.mkv")

    def set_field(self, name, value):
        next(item for item in self.definition["fields"] if item["name"] == name)["value"] = value

    def browser(self):
        username = "isolated-sonarr-jellyfin-audit"
        status, settings = self.json_request(self.base_url, "/api/v5/settings/general", token=self.api_key)
        require(status == 200, "Could not read isolated browser authentication settings")
        settings.update(username=username, password=self.sonarr_password, passwordConfirmation=self.sonarr_password)
        settings["authenticationRequired"] = "enabled"
        status, validation = self.json_request(self.base_url, "/api/v5/settings/general/1", "PUT", settings, self.api_key)
        problems = "; ".join(item.get("errorMessage", "") for item in validation if isinstance(item, dict)) if isinstance(validation, list) else ""
        require(200 <= status < 300, f"Could not set isolated browser credentials (HTTP {status}): {problems}")

        def native_path(path):
            if self.args.browser_node.lower().endswith(".exe") and sys.platform != "win32":
                return subprocess.check_output(["wslpath", "-w", str(path)], text=True).strip()
            return str(path)

        artifacts = self.args.report.parent.resolve() if self.args.report else self.root / "browser-report"
        artifacts.mkdir(parents=True, exist_ok=True)
        payload = {"baseUrl": self.base_url, "apiKey": self.api_key, "username": username,
                   "password": self.sonarr_password, "libraryId": self.libraries["Anime"],
                   "playwrightModule": self.args.playwright_module, "artifactDirectory": native_path(artifacts)}
        script = Path(__file__).with_name("ui-jellyfin.cjs").resolve()
        result = subprocess.run([self.args.browser_node, native_path(script)], input=json.dumps(payload),
                                capture_output=True, text=True, encoding="utf-8", timeout=240)
        output = result.stdout + result.stderr
        for secret in (self.api_key, self.jellyfin_token, self.jellyfin_password, self.sonarr_password):
            output = output.replace(secret, "[REDACTED]")
        print(output.strip(), flush=True)
        require(result.returncode == 0, "Jellyfin UI checks failed; see ui-jellyfin.json")
        self.check("real Chromium Jellyfin settings, mode/library choices and server-change dropdown isolation")

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
        self.until(lambda: self.count("Anime") == 2, "Immediate refresh did not discover imported episode")
        require(self.count("Untouched") == 1, "Refresh reached unselected library")
        self.check("real import immediately refreshes only selected Jellyfin library")
        self.save_mode(1)
        self.import_episodes([2, 3])
        require(self.count("Anime") == 2, "Interval mode refreshed before its tick")
        before = self.state()
        self.docker("restart", self.container)
        self.refresh_binding()
        self.wait_ready()
        require(self.state()["ImportedGeneration"] == before["ImportedGeneration"], "Pending imports lost at restart")
        self.until(lambda: self.count("Anime") == 4, "One-minute tick failed after restart", 120)
        require(self.count("Untouched") == 1, "Periodic scan reached unselected library")
        self.check("burst imports wait for one-minute tick and persist across restart")
        self.save_mode(2)
        self.import_episodes([4])
        self.until(lambda: self.count("Anime") == 5, "Combined mode immediate refresh failed")
        state = self.state()
        require(state["ImportedGeneration"] > state["TickGeneration"], "Immediate refresh consumed pending periodic tick")
        self.until(lambda: self.state()["TickGeneration"] == self.state()["ImportedGeneration"],
                   "Combined mode periodic refresh failed", 120)
        self.command({"name": "ProcessPendingJellyfinRefresh"})
        require(self.count("Untouched") == 1, "Idle processing scanned unselected library")
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
        port = self.docker("port", self.jellyfin, "8096/tcp").rsplit(":", 1)[1]
        self.jellyfin_url = "http://127.0.0.1:" + port
        def restarted():
            try:
                return self.json_request(self.jellyfin_url, "/Library/VirtualFolders", token=self.jellyfin_token)[0] == 200
            except (OSError, urllib.error.URLError):
                return False

        self.until(restarted, "Jellyfin restart timed out")
        self.command({"name": "ProcessPendingJellyfinRefresh"})
        self.until(lambda: self.count("Anime") == 6, "Retry failed to discover pending imported episode")
        require(self.count("Untouched") == 1, "Retry scanned unselected library")
        self.check("unavailable Jellyfin preserves pending work and retries successfully")
        self.set_field("libraryId", "00000000000000000000000000000001")
        status, _ = self.json_request(self.base_url, "/api/v3/notification/test", "POST", self.definition, self.api_key)
        require(status == 400, "Missing selected library accepted")
        self.check("stale library rejected without global refresh fallback")
        cold = self.root / "media" / "Cold"
        cold.mkdir()
        cold.chmod(0o777)
        query = urllib.parse.urlencode({"name": "Cold", "collectionType": "tvshows", "paths": "/media/Cold",
                                        "refreshLibrary": "false"})
        self.jf("/Library/VirtualFolders?" + query, "POST", {"LibraryOptions": {"EnableRealtimeMonitor": False}})
        cold_id = next(item["ItemId"] for item in self.jf("/Library/VirtualFolders") if item["Name"] == "Cold")
        self.set_field("libraryId", cold_id)
        status, _ = self.json_request(self.base_url, "/api/v3/notification/test", "POST", self.definition, self.api_key)
        require(status == 400, "Uninitialized empty Jellyfin library accepted")
        require(self.count("Untouched") == 1, "Cold-library check scanned another library")
        self.check("uninitialized empty library rejected before a no-op refresh can discard pending imports")

    def cleanup(self):
        if self.args.keep:
            print(f"Kept isolated containers: {self.container}, {self.jellyfin}; network: {self.network}")
            print(f"Sonarr URL: {self.base_url}; runtime data: {self.root}")
            print("Temporary credentials remain in the isolated configuration; they are not printed.")
            return
        if self.jellyfin:
            self.docker("rm", "--force", self.jellyfin)
        super().cleanup()
        if self.network:
            self.docker("network", "rm", self.network)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True, help="Already built Tanukarr image")
    parser.add_argument("--jellyfin-image", default="jellyfin/jellyfin:12.2")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--browser-node", help="External Node executable for optional Playwright checks")
    parser.add_argument("--playwright-module", help="External Playwright module directory visible to Node")
    parser.add_argument("--browser-only", action="store_true", help="Only verify connection settings and browser UI; skip generated imports")
    parser.add_argument("--keep", action="store_true", help="Keep only this test's containers, network and temporary data for inspection")
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    require(bool(args.browser_node) == bool(args.playwright_module), "Provide both browser options together")
    require(not args.browser_only or bool(args.browser_node), "--browser-only requires both browser options")
    audit = JellyfinIntegration(args)
    failure = None
    try:
        audit.start_servers()
        audit.configure()
        if args.browser_node:
            audit.browser()
        if not args.browser_only:
            audit.verify()
    except Exception as error:
        failure = audit.last_request + ": " + str(error)
        for token in (audit.api_key, audit.jellyfin_token, audit.jellyfin_password, audit.sonarr_password):
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
