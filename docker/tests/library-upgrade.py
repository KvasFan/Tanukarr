#!/usr/bin/env python3
"""Upgrade an owned populated schema252 library and preserve existing anime movie files."""

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import secrets
import sys

from smoke import require


SCRIPTS = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("upgrade_client_fixture", SCRIPTS / "download-client-folders.py")
_client = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_client)
BASE_IMAGE = "ghcr.io/kvasexpert/tanukarr@sha256:74b13c7a6fd59222a498cb8bbb14be149a2d505846d684a8aa2e78de280dd9bd"


class LibraryUpgrade(_client.ClientFolders):
    def __init__(self, args):
        super().__init__(args)
        self.base_image_id = None
        self.base_source = None
        self.base_runtime = {}
        self.before = {}
        self.after = {}
        self.candidate_image = args.image

    def docker(self, *arguments, timeout=60):
        if arguments and arguments[0] == "create" and "fixture-app" in arguments:
            arguments = (arguments[0], "--label", "sonarr.smoke-test=true", *arguments[1:])
        return super().docker(*arguments, timeout=timeout)

    def snapshot(self):
        observations = {}
        for identity, saved in self.series.items():
            row = self.get(saved)
            fields = ("id", "metadataSource", "foreignId", "tvdbId", "tmdbId", "imdbId", "path", "monitored",
                      "seasonFolder", "seriesType", "qualityProfileId", "tags", "animeGroupingMode", "animeGroupId",
                      "animeGroupTitle", "animeGroupSeasonNumber", "animeGroupMovieNumber", "animeGroupMemberKind",
                      "animeGroupKind", "animeGroupFolder", "animeGroupMemberFolder")
            episodes = []
            for item in self.episodes(row):
                episode = {key: item.get(key) for key in ("id", "seriesId", "seasonNumber", "episodeNumber", "monitored", "episodeFileId", "hasFile")}
                if item["hasFile"]:
                    resource = self.api_json(f"/api/v5/episodefile/{item['episodeFileId']}")
                    local = self.root / "media" / Path(resource["path"]).relative_to("/media")
                    require(local.is_file(), "Populated upgrade fixture file is missing")
                    episode.update(path=resource["path"], sha256=hashlib.sha256(local.read_bytes()).hexdigest(), size=local.stat().st_size)
                episodes.append(episode)
            observations[str(identity)] = {"series": {key: row.get(key) for key in fields}, "episodes": episodes}
        return observations

    def seed_old_library(self):
        self.args.image = self.args.base_image
        try:
            self.start()
            self.prepare()
            self.base_image_id = self.image_id
            self.base_source = self.image_labels.get("org.opencontainers.image.revision")
            self.base_runtime = dict(self.runtime_metadata)
            require(self.base_runtime.get("migrationVersion") == 252, "Upgrade base must actually run schema252")
            self.create_members()
            self.import_more()
            self.before = self.snapshot()
            imported = [item for group in self.before.values() for item in group["episodes"] if item["hasFile"]]
            require(len(imported) == 5 and all("S01E01" in item["path"] for item in imported),
                    "Upgrade fixture did not create the five original native filenames")
            self.check("immutable prior source populates mixed TV/anime/movie collections with five native S01E01 files")
        finally:
            self.args.image = self.candidate_image

    def upgrade(self):
        candidate = json.loads(self.docker("image", "inspect", self.args.image))[0]
        self.image_id = candidate["Id"]
        self.image_labels = candidate["Config"].get("Labels", {}) or {}
        self.docker("stop", self.container)
        self.docker("rm", "--volumes", self.container)
        self.container = None
        startup = ("set -eu; cp /fixture-ca.crt /usr/local/share/ca-certificates/tanukarr-anime-group-fixture.crt; "
                   "update-ca-certificates >/dev/null 2>&1; exec /entrypoint.sh /app/Sonarr -nobrowser -data=/config")
        self.container = self.docker(
            "create", "--name", "tanukarr-populated-upgrade-" + secrets.token_hex(6),
            "--label", "sonarr.smoke-test=true", "--label", "tanukarr.library-upgrade-test=true",
            "--network", self.network, "--network-alias", "fixture-app",
            "--env", "PUID=1000", "--env", f"PGID={self.fixture_gid}", "--env", "UMASK=002",
            "--env", "HTTP_PROXY=", "--env", "HTTPS_PROXY=", "--env", "ALL_PROXY=",
            "--mount", f"type=bind,source={self.root / 'config'},target=/config",
            "--mount", f"type=bind,source={self.root / 'media'},target=/media",
            "--mount", f"type=bind,source={self.root / 'fixture/ca.crt'},target=/fixture-ca.crt,readonly",
            "--entrypoint", "/bin/sh", self.args.image, "-c", startup)
        self.docker("start", self.container)
        self.wait_ready()
        status = self.api_json("/api/v5/system/status")
        self.runtime_metadata = {key: status.get(key) for key in ("productName", "productVersion", "version", "runtimeVersion", "migrationVersion")}
        require(self.runtime_metadata["migrationVersion"] >= 253, "Candidate did not apply the additive movie migration")
        require(self.snapshot() == self.before, "Migration changed existing native identities, paths, monitored states or media bytes")
        self.check("candidate schema upgrade preserves all existing native catalogue/episode/file IDs, monitoring, paths and bytes")
        for identity, row in self.series.items():
            self.command({"name": "RescanSeries", "seriesId": row["id"]})
            require(self.snapshot() == self.before, f"Rescan changed prior native media for {identity}")
        self.check("normal rescan preserves every original anime movie S01E01 filename and file identity after upgrade")
        for identity, row in self.series.items():
            self.command({"name": "RefreshSeries", "seriesId": row["id"]})
            require(self.snapshot() == self.before, f"Refresh changed prior native media for {identity}")
        self.after = self.snapshot()
        self.check("owning catalogue refresh retains mixed collection membership and all original episode/media identities")
        self.docker("restart", self.container)
        self.wait_ready()
        require(self.snapshot() == self.before, "Candidate restart changed the upgraded library")
        require(self.api_json("/api/v5/downloadclient") == [] and self.api_json("/api/v5/indexer") == [],
                "Upgrade fixture unexpectedly configured acquisition")
        self.check("upgraded library persists across restart without acquisition or owner-library access")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--base-image", default=BASE_IMAGE)
    parser.add_argument("--mock-image", default=_client.MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    require(args.timeout > 0, "--timeout must be positive")
    require(args.report is None or not args.report.exists(), "Use a fresh report path")
    audit = LibraryUpgrade(args)
    error, logs = None, ""
    try:
        audit.seed_old_library()
        audit.upgrade()
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
            report = {"createdUtc": datetime.now(timezone.utc).isoformat(), "image": args.image, "imageId": audit.image_id,
                      "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"), "runtime": audit.runtime_metadata,
                      "baseImage": args.base_image, "baseImageId": audit.base_image_id, "baseSourceRevision": audit.base_source,
                      "baseRuntime": audit.base_runtime, "passed": error is None, "error": error, "checks": audit.results,
                      "before": audit.before, "after": audit.after, "cleanupComplete": audit.cleaned,
                      "scope": {"populatedDisposableLibrary": True, "generatedMediaOnly": True,
                                "actualMigration": True, "ownerConfigurationAccessed": False, "externalProviderRouting": False}}
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(audit.redact(json.dumps(report, indent=2)) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
