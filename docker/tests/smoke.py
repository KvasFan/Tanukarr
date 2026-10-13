#!/usr/bin/env python3
"""Verify a built Sonarr image using isolated, disposable runtime data."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request


class SmokeFailure(Exception):
    pass


def require(condition, message):
    if not condition:
        raise SmokeFailure(message)


class ContainerSmoke:
    def __init__(self, args):
        self.args = args
        self.container = None
        self.root = None
        self.base_url = None
        self.api_key = secrets.token_hex(16)
        self.metadata_key = secrets.token_hex(16)
        self.results = []
        self.image_id = None
        self.runtime_metadata = {}
        # These requests must remain local even when the host configures a proxy.
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def docker(self, *arguments, timeout=60):
        try:
            result = subprocess.run(
                [self.args.docker, *arguments], capture_output=True, text=True,
                encoding="utf-8", errors="replace", timeout=timeout,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise SmokeFailure(f"Docker invocation failed: {error}") from error
        if result.returncode:
            # No API key is passed as a Docker argument or environment variable.
            detail = result.stderr.strip() or result.stdout.strip()
            raise SmokeFailure(f"docker {arguments[0]} failed: {detail}")
        return result.stdout.strip()

    def check(self, name):
        self.results.append(name)
        print(f"PASS {name}", flush=True)

    def request(self, path, data=None, timeout=30):
        payload = None if data is None else json.dumps(data).encode("utf-8")
        request = urllib.request.Request(
            self.base_url + path, data=payload,
            method="GET" if data is None else "PUT",
            headers={"X-Api-Key": self.api_key, "Content-Type": "application/json"},
        )
        with self.http.open(request, timeout=timeout) as response:
            return response.read(), response.headers.get("Content-Type", "")

    def api(self, path, data=None, timeout=30):
        raw, content_type = self.request(path, data, timeout)
        require("json" in content_type.lower(), f"Expected JSON at {path}")
        try:
            return json.loads(raw)
        except json.JSONDecodeError as error:
            raise SmokeFailure(f"Invalid JSON at {path}") from error

    def running(self):
        return self.docker("inspect", "--format", "{{.State.Running}}", self.container) == "true"

    def wait_ready(self):
        deadline = time.monotonic() + self.args.timeout
        last_error = None
        next_progress = time.monotonic() + 30
        while time.monotonic() < deadline:
            require(self.running(), "Container exited before becoming ready")
            try:
                self.request("/ping", timeout=5)
                return
            except (OSError, urllib.error.URLError) as error:
                last_error = error
            if time.monotonic() >= next_progress:
                print("Waiting for the container HTTP server...", flush=True)
                next_progress += 30
            time.sleep(2)
        raise SmokeFailure(f"Container readiness timed out: {last_error}")

    def start(self):
        self.docker("info", "--format", "{{.OSType}}")
        self.image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.image)
        require(
            self.docker("image", "inspect", "--format", "{{.Os}}", self.args.image) == "linux",
            "The smoke test requires an already built Linux image",
        )
        self.root = Path(tempfile.mkdtemp(prefix="sonarr-container-smoke-")).resolve()
        config = self.root / "config"
        media = self.root / "media"
        config.mkdir()
        media.mkdir()
        # This directory contains no real media. Let UID 1000 write it even when
        # the test runner has a different UID. Generated file modes are checked below.
        media.chmod(0o777)
        (config / "config.xml").write_text(
            "<Config>"
            "<BindAddress>*</BindAddress><Port>8989</Port>"
            f"<ApiKey>{self.api_key}</ApiKey>"
            "<LaunchBrowser>False</LaunchBrowser>"
            "<AuthenticationMethod>Forms</AuthenticationMethod>"
            "<AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>"
            "<AnalyticsEnabled>False</AnalyticsEnabled>"
            "</Config>", encoding="utf-8",
        )
        name = "sonarr-smoke-" + secrets.token_hex(6)
        self.container = self.docker(
            "create", "--name", name,
            "--label", "sonarr.smoke-test=true",
            "--publish", "127.0.0.1::8989",
            "--env", "PUID=1000", "--env", "PGID=1000", "--env", "UMASK=002",
            "--mount", f"type=bind,source={config},target=/config",
            "--mount", f"type=bind,source={media},target=/media",
            self.args.image,
        )
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.check("isolated container startup and /ping")

    def refresh_binding(self):
        binding = json.loads(self.docker(
            "inspect", "--format", '{{json (index .NetworkSettings.Ports "8989/tcp")}}',
            self.container,
        ))
        require(len(binding) == 1 and binding[0]["HostIp"] == "127.0.0.1",
                "Container port must be published only on IPv4 loopback")
        self.base_url = f"http://127.0.0.1:{int(binding[0]['HostPort'])}"

    def check_ui(self):
        raw, content_type = self.request("/")
        require("text/html" in content_type.lower(), "Root did not serve UI HTML")
        html = raw.decode("utf-8")
        require('id="root"' in html, "UI root element is missing")
        scripts = re.findall(r'<script\b[^>]*\bsrc=[\"\']([^\"\']+)[\"\']', html, re.I)
        require(scripts, "UI HTML has no compiled script reference")
        checked = 0
        for source in scripts:
            parsed = urllib.parse.urlparse(source)
            require(not parsed.netloc and not parsed.scheme, "UI script unexpectedly points to another host")
            path = urllib.parse.urljoin("/", source)
            require(urllib.parse.urlparse(path).path.endswith(".js"), "UI references an uncompiled source script")
            asset, asset_type = self.request(path)
            require(len(asset) > 100 and "javascript" in asset_type.lower(),
                    f"Compiled UI JavaScript asset is missing: {path}")
            require(not asset.lstrip().lower().startswith(b"<!doctype"), "JavaScript request served an HTML fallback")
            checked += 1
        self.check(f"UI HTML and {checked} compiled JavaScript asset(s)")

    def ui_initialization(self):
        return self.api("/initialize.json")

    def check_runtime(self):
        status = self.api("/api/v5/system/status")
        require(status.get("isLinux") and status.get("isDocker"), "API does not report a Linux Docker runtime")
        require(status.get("appData") == "/config", "Application data is not in the mounted /config directory")
        require(status.get("version") and status.get("migrationVersion"), "Status lacks build/database information")
        release_version = (Path(__file__).resolve().parents[2] / "VERSION").read_text(encoding="utf-8").strip()
        for resource in (status, self.api("/api/v3/system/status"), self.ui_initialization()):
            require(resource.get("productName") == "Tanukarr", "Runtime does not identify the Tanukarr product")
            require(resource.get("productVersion") == release_version, "Runtime product version disagrees with VERSION")
            require(resource.get("version") == status["version"], "Internal version differs across API and frontend initialization")
        require(status.get("appName") == "Sonarr", "The compatible internal application name changed")
        self.runtime_metadata = {key: status[key] for key in (
            "appName", "productName", "productVersion", "version", "runtimeVersion", "migrationVersion"
        ) if key in status}
        self.check("Tanukarr release metadata agrees across v3/v5 APIs and frontend initialization")
        queue = self.api("/api/v5/queue")
        require(queue.get("totalRecords") == 0 and queue.get("records") == [], "Isolated queue was not empty")
        require(self.api("/api/v5/series") == [], "Isolated configuration contains existing series")
        self.check("Linux Docker status, initialized SQLite database, empty series and queue")
        probe = self.docker("exec", "--user", "1000:1000", self.container, "/app/ffprobe", "-version")
        require("ffprobe version" in probe, "Packaged native ffprobe did not execute")
        self.check("packaged native ffprobe executes as UID 1000")
        return status

    def check_permissions(self):
        # Docker exec starts a separate process; inspect PID 1 to verify the app's
        # credentials and inherited entrypoint umask before creating probe files.
        script = r"""
set -eu
test "$(id -u)" = 1000
test "$(id -g)" = 1000
test "$(awk '$1 == "Uid:" { print $2 }' /proc/1/status)" = 1000
test "$(awk '$1 == "Gid:" { print $2 }' /proc/1/status)" = 1000
mask=$(awk '$1 == "Umask:" { print $2 }' /proc/1/status)
test "$mask" = 0002
umask "$mask"
for parent in /config /media; do
  file="$parent/.smoke-file"
  directory="$parent/.smoke-directory"
  printf 'permission probe\n' > "$file"
  mkdir "$directory"
  test "$(stat -c '%u:%g:%a' "$file")" = 1000:1000:664
  test "$(stat -c '%u:%g:%a' "$directory")" = 1000:1000:775
  rm "$file"
  rmdir "$directory"
done
test -s /config/sonarr.db
"""
        self.docker("exec", "--user", "1000:1000", self.container, "sh", "-c", script)
        self.check("app UID/GID 1000, umask 002, writable config/media with file 664 and directory 775")

    def check_metadata(self):
        settings = self.api("/api/v5/settings/metadata")
        alias = self.api("/api/v5/settings/metadatasource")
        require(settings == alias, "Metadata settings route aliases disagree")
        require(settings.get("id") == 1, "Metadata settings ID is unexpected")
        settings.update(preferredMetadataLanguage=2, tmdbApiKey=self.metadata_key)
        saved = self.api("/api/v5/settings/metadata/1", settings)
        require(saved.get("tmdbApiKey") == "********" and saved.get("tmdbApiKeyConfigured") is True,
                "Saved metadata key is not masked")
        # A legacy save omitting the language must preserve the stored language.
        legacy = {"id": 1, "tmdbApiKey": "********"}
        self.api("/api/v5/settings/metadatasource/1", legacy)
        persisted = self.api("/api/v5/settings/metadata")
        require(persisted.get("preferredMetadataLanguage") == 2,
                "A legacy settings save reset the metadata language")
        require(persisted.get("tmdbApiKey") == "********" and persisted.get("tmdbApiKeyConfigured") is True,
                "A masked save lost or exposed the stored metadata key")
        self.check("metadata routes, masked key save and legacy language preservation")
        return persisted

    def check_anilist(self):
        query = urllib.parse.urlencode({"term": "Cowboy Bebop", "metadataSource": "aniList"})
        series = self.api("/api/v5/series/lookup?" + query, timeout=90)
        expected = [item for item in series if str(item.get("foreignId")) == "1"]
        require(expected and all(item.get("metadataSource") == "aniList" for item in expected),
                "AniList lookup did not retain its native provider identity")
        require(self.api("/api/v5/series") == [], "A lookup unexpectedly added a series")
        self.check("live AniList lookup retaining native identity without adding a series")

    def check_restart(self, metadata, first_status):
        sentinel = self.root / "media" / ".smoke-persistence"
        sentinel.write_text("isolated restart persistence\n", encoding="utf-8")
        initial_hash = hashlib.sha256(sentinel.read_bytes()).digest()
        self.docker("restart", "--time", "20", self.container)
        # An automatically assigned host port can change when Docker restarts
        # the container. Resolve its actual binding before checking readiness.
        self.refresh_binding()
        self.wait_ready()
        require(self.running(), "Container stopped after restart")
        status = self.check_runtime()
        require(status.get("startTime") != first_status.get("startTime"), "Application start time did not change")
        require(self.api("/api/v5/settings/metadata") == metadata, "Metadata settings did not survive restart")
        require(hashlib.sha256(sentinel.read_bytes()).digest() == initial_hash, "Media bind mount did not retain its data")
        require((self.root / "config" / "sonarr.db").is_file(), "SQLite database did not persist in the config bind mount")
        self.check("container restart and persisted database/settings/media")

    def run(self):
        self.start()
        self.check_ui()
        status = self.check_runtime()
        self.check_permissions()
        metadata = self.check_metadata()
        if self.args.anilist:
            self.check_anilist()
        self.check_restart(metadata, status)
        self.check_permissions()

    def cleanup(self):
        if self.args.keep and self.container:
            print(f"Kept isolated container: {self.container}")
            print(f"URL: {self.base_url}; runtime data: {self.root}")
            print("API credentials remain in the isolated config.xml; they are not printed.")
            return
        if self.container:
            # This is the exact ID returned by our own docker create, never a name
            # wildcard or a container discovered from another deployment.
            if self.running():
                # Allow a Linux host runner whose UID differs from 1000 to remove
                # these disposable bind mounts after the root entrypoint chowns them.
                self.docker("exec", "--user", "0:0", self.container, "sh", "-c",
                            "chmod -R a+rwX /config /media")
            self.docker("rm", "--force", "--volumes", self.container)
            self.container = None
        if self.root:
            expected_parent = Path(tempfile.gettempdir()).resolve()
            require(self.root.parent == expected_parent and self.root.name.startswith("sonarr-container-smoke-"),
                    "Refusing to remove a directory outside this test's temporary root")
            # Linux's root entrypoint can create files owned by UID 1000. Make our
            # own test directories removable without touching any unrelated data.
            def retry(function, path, error):
                os.chmod(path, 0o700)
                function(path)
            shutil.rmtree(self.root, onerror=retry)
            self.root = None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local", help="already built Linux image (default: %(default)s)")
    parser.add_argument("--docker", default="docker", help="Docker executable or absolute path")
    parser.add_argument("--timeout", type=int, default=180, help="startup/restart readiness timeout in seconds")
    parser.add_argument("--anilist", action="store_true", help="also perform one live AniList lookup (requires Internet)")
    parser.add_argument("--keep", action="store_true", help="keep only this test's container and temporary data for inspection")
    parser.add_argument("--report", type=Path, help="write a JSON verification report, containing no credentials")
    args = parser.parse_args()
    require(args.timeout > 0, "--timeout must be positive")
    smoke = ContainerSmoke(args)
    error = None
    try:
        smoke.run()
    except (SmokeFailure, urllib.error.URLError, OSError, ValueError, KeyboardInterrupt) as failure:
        error = str(failure) or type(failure).__name__
        print(f"FAIL {error}", file=sys.stderr)
    finally:
        try:
            smoke.cleanup()
        except (SmokeFailure, OSError) as failure:
            error = error or f"Cleanup failed: {failure}"
            print(f"FAIL cleanup: {failure}", file=sys.stderr)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({
                "image": args.image,
                "imageId": smoke.image_id,
                "runtime": smoke.runtime_metadata,
                "passed": error is None,
                "checks": smoke.results,
                "error": error,
                "liveAniListRequested": args.anilist,
                "realLibraryTested": False,
                "downloadsTested": False,
                "keptContainer": smoke.container if args.keep else None,
                "keptData": str(smoke.root) if args.keep and smoke.root else None,
            }, indent=2) + "\n", encoding="utf-8")
    if error:
        return 1
    print("Container smoke verification passed. No real library or download was used.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
