#!/usr/bin/env python3
"""Verify quality CRUD, profile codec factors, native release decisions and generated imports."""

import argparse
import copy
from concurrent.futures import ThreadPoolExecutor
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import time
import urllib.parse

from smoke import require


SCRIPTS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("custom_quality_codecs", SCRIPTS / "quality-codecs.py")
quality = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quality)
TITLE = quality.packs.TITLE
RELEASES = [
    (f"{TITLE}.S01E01.2160p.BDRip.x265-Fixture", "x265", 1),
    (f"{TITLE}.S01E01.2160p.Bluray.x264-Fixture", "x264", 1),
    (f"{TITLE}.S01E01.2160p.WEB-DL.x265-Fixture", "x265", 1),
    (f"{TITLE}.S01E01.1080p.BDRip.x265-Fixture", "x265", 1),
]


class CustomQualities(quality.QualityCodecs):
    fixture_script = "/runner/custom-qualities.py"

    def __init__(self, args):
        super().__init__(args)
        self.custom = None
        self.profile = None

    def command(self, payload):
        command = self.json_api("/api/v5/command", "POST", payload)
        deadline = time.monotonic() + self.args.timeout
        while time.monotonic() < deadline:
            state = self.json_api(f"/api/v5/command/{command['id']}")
            if state["status"] in ("completed", "failed", "aborted"):
                require(state["status"] == "completed", f"Generated {payload['name']} command failed")
                return
            time.sleep(.25)
        require(False, f"Generated {payload['name']} command timed out")

    def catalogue(self):
        before = self.definition()
        require(len(before) == 22 and all(not row["isCustom"] and not row["isRetired"] for row in before), "Built-in catalogue changed on a fresh install")
        for version in ("v3", "v5"):
            defaults = self.json_api(f"/api/{version}/qualitydefinition/codecdefaults")
            require(len(defaults) == 18 and defaults["MPEG2"] == .5 and defaults["H264"] == 1 and defaults["HEVC"] == 1.67 and defaults["AV1"] == 2.22 and defaults["Unknown"] == 1,
                    "Canonical codec families/defaults were renamed by API serialization")
            created = self.json_api(f"/api/{version}/qualitydefinition", "POST", {
                "title": f"Generated BdRip4k {version}", "source": "bluray", "resolution": 2160,
                "codec": "HEVC", "titlePattern": "BDRip"})
            require(created["quality"]["id"] >= 10000 and created["isCustom"] and not created["isRetired"], "Custom quality did not receive a stable independent identity")
            require(created["source"] == "bluray" and created["resolution"] == 2160 and created["codec"] == "HEVC" and created["titlePattern"] == "BDRip", "Custom parameters changed during creation")
            require(all(not next(leaf for leaf in self.leaves(profile["items"]) if leaf["quality"]["id"] == created["quality"]["id"])["allowed"] for profile in self.profiles()),
                    "Creating a custom quality silently enabled it in a profile")
            self.custom = created
            self.check(version + " creates a named 4K Blu-ray preset with separate codec/expression parameters and disabled profile leaves")
            before_invalid = self.definition()
            for invalid in ({"codec": "unsupported"}, {"resolution": 9999}, {"titlePattern": "["}, {"titlePattern": "a" * 257}):
                request = {"title": f"Generated invalid {version}", "source": "bluray", "resolution": 2160, **invalid}
                require(self.raw(f"/api/{version}/qualitydefinition", "POST", request)[0] == 400, "Invalid custom matching criteria were accepted")
            require(self.definition() == before_invalid, "Invalid custom creation changed stored definitions")
            self.check(version + " rejects unsupported codecs/resolutions and malformed or oversized expressions without writes")
        require([(row["id"], row["quality"]["id"], row["title"]) for row in self.definition() if not row["isCustom"]] ==
                [(row["id"], row["quality"]["id"], row["title"]) for row in before], "Creating presets rewrote built-in identities or titles")
        self.check("custom creation preserves every standard quality ID and name")

        self.profile = self.profiles()[0]
        leaf = next(item for item in self.leaves(self.profile["items"]) if item["quality"]["id"] == self.custom["quality"]["id"])
        leaf.update(allowed=True, minSize=0, maxSize=2048 / 60, preferredSize=1536 / 60)
        self.json_api(f"/api/v5/qualityprofile/{self.profile['id']}", "PUT", self.profile)
        self.custom["title"] = "Generated renamed BdRip4k"
        self.update("v5", [{"id": self.custom["id"], "title": self.custom["title"]}])
        fresh = self.json_api(f"/api/v3/qualityprofile/{self.profile['id']}")
        require(next(item for item in self.leaves(fresh["items"]) if item["quality"]["id"] == self.custom["quality"]["id"])["quality"]["name"] == self.custom["title"],
                "Renaming a custom quality did not reach profile/API labels")
        self.check("renamed quality labels update in existing profiles while preserving their numeric identity")

        profiles = self.profiles()
        copied = self.json_api("/api/v5/qualitydefinition", "POST", {
            "title": "Generated independent copy", "copyFromId": self.custom["id"], "source": "bluray",
            "resolution": 2160, "codec": "AV1", "titlePattern": "BDRip"})
        require(copied["quality"]["id"] != self.custom["quality"]["id"] and copied["codec"] == "AV1", "Copy reused an existing ID or ignored independent criteria")
        for profile in self.profiles():
            before_profile = next(item for item in profiles if item["id"] == profile["id"])
            original = next(item for item in self.leaves(before_profile["items"]) if item["quality"]["id"] == self.custom["quality"]["id"])
            clone = next(item for item in self.leaves(profile["items"]) if item["quality"]["id"] == copied["quality"]["id"])
            require(not clone["allowed"] and all(clone[field] == original[field] for field in ("minSize", "maxSize", "preferredSize")), "Copy lost an individual profile's limits or enabled itself")
        self.check("duplicating a preset copies each profile's size limits into an independently editable disabled identity")
        standard = self.definition("v5", 19)
        standard_copy = self.json_api("/api/v3/qualitydefinition", "POST", {
            "title": "Generated standard Blu-ray copy", "copyFromId": standard["id"], "source": standard["source"],
            "resolution": standard["resolution"], "codec": "H264", "titlePattern": ""})
        require(standard_copy["isCustom"] and standard_copy["quality"]["id"] != 19, "Standard quality could not be copied into an independent preset")
        self.update("v3", [{"id": standard["id"], "title": "Generated renamed standard", "source": "bluray", "resolution": 2160, "codec": "", "titlePattern": ""}])
        require(self.definition("v5", 19)["title"] == "Generated renamed standard", "Standard title/criteria edit did not persist")
        require(self.raw(f"/api/v3/qualitydefinition/{standard['id']}", "DELETE")[0] == 400, "A standard preset referenced by default profiles was deleted")
        require(not self.definition("v5", 19)["isRetired"], "Rejected standard deletion changed its identity")
        self.check("standard deletion rejects existing profile permissions or cutoff references without changing its identity")
        for profile in self.profiles():
            changed = False
            for item in profile["items"]:
                members = [item] if item.get("quality") else self.leaves(item["items"])
                if any(member["quality"]["id"] == 19 for member in members):
                    changed = changed or item["allowed"]
                    item["allowed"] = False
                    for member in members:
                        if member["quality"]["id"] == 19:
                            member["allowed"] = False
                    root_id = item["quality"]["id"] if item.get("quality") else item["id"]
                    if profile["cutoff"] == root_id:
                        replacement = next(candidate for candidate in profile["items"] if candidate["allowed"] and
                                           not any(member["quality"]["id"] == 19 for member in ([candidate] if candidate.get("quality") else self.leaves(candidate["items"]))))
                        profile["cutoff"] = replacement["quality"]["id"] if replacement.get("quality") else replacement["id"]
                        changed = True
            if changed:
                self.json_api(f"/api/v5/qualityprofile/{profile['id']}", "PUT", profile)
        require(self.raw(f"/api/v3/qualitydefinition/{standard['id']}", "DELETE")[0] == 204, "Standard preset with explicitly cleared owned profile references could not be deleted")
        require(self.definition("v5", 19)["isRetired"], "Delete removed or failed to retire the standard identity")
        self.json_api(f"/api/v5/qualitydefinition/{standard['id']}/restore", "POST", {})
        require(not self.definition("v3", 19)["isRetired"], "Standard preset restore failed")
        self.check("standard qualities support copy, edit, delete and restore while keeping their original numeric IDs")

    def profile_factors(self):
        profile_id = self.profile["id"]
        for version in ("v3", "v5"):
            before_other = [item for item in self.profiles() if item["id"] != profile_id]
            profile = self.json_api(f"/api/{version}/qualityprofile/{profile_id}")
            profile["codecMultipliers"]["HEVC"] = 2.35
            profile["codecMultipliers"]["Unknown"] = .9
            self.json_api(f"/api/{version}/qualityprofile/{profile_id}", "PUT", profile)
            require([item for item in self.profiles() if item["id"] != profile_id] == before_other, "Factors leaked into another quality profile")
            saved = self.json_api(f"/api/{version}/qualityprofile/{profile_id}")
            require(saved["codecMultipliers"]["HEVC"] == 2.35 and saved["codecMultipliers"]["Unknown"] == .9 and len(saved["codecMultipliers"]) == 18, "Canonical profile factors failed serialization or persistence")
            older = copy.deepcopy(saved)
            older.pop("codecMultipliers")
            self.json_api(f"/api/{version}/qualityprofile/{profile_id}", "PUT", older)
            require(self.json_api(f"/api/{version}/qualityprofile/{profile_id}")["codecMultipliers"] == saved["codecMultipliers"], "Older client's omitted factors reset saved values")
            self.check(version + " profile factors are isolated and survive older clients omitting the new field")
            for invalid in ({"HEVC": 0}, {"HEVC": 10.01}, {"Unsupported": 1}):
                request = copy.deepcopy(saved)
                request["codecMultipliers"] = invalid
                require(self.raw(f"/api/{version}/qualityprofile/{profile_id}", "PUT", request)[0] == 400, "Invalid per-profile factor accepted")
            require(self.json_api(f"/api/{version}/qualityprofile/{profile_id}")["codecMultipliers"] == saved["codecMultipliers"], "Rejected factors changed profile values")
            self.check(version + " rejects zero, excessive and unsupported factors before changing any saved profile")

        profile = self.json_api(f"/api/v5/qualityprofile/{profile_id}")
        profile["codecMultipliers"] = {}
        self.json_api(f"/api/v5/qualityprofile/{profile_id}", "PUT", profile)
        require(self.json_api(f"/api/v5/qualityprofile/{profile_id}")["codecMultipliers"]["HEVC"] == 1.67, "Explicit empty overrides did not restore defaults")
        self.check("explicit empty profile overrides restore backend codec defaults")

    def matching(self):
        episode_id = next(identity[0] for identity in self.native_episodes if identity[1:] == (1, 1))
        samples = []
        for version in ("v3", "v5"):
            for factor, rejected in ((2.5, True), (1.9, False)):
                profile = self.json_api(f"/api/v5/qualityprofile/{self.profile['id']}")
                profile["codecMultipliers"]["HEVC"] = factor
                self.json_api(f"/api/v5/qualityprofile/{profile['id']}", "PUT", profile)
                rows = self.json_api(f"/api/{version}/release?episodeId={episode_id}")
                by_title = {row.get("release", row)["title"]: row for row in rows}
                require(set(title for title, _, _ in RELEASES).issubset(by_title), "Native RSS search omitted matching or nonmatching control releases")
                for title, _codec, _hours in RELEASES:
                    row = by_title[title]
                    parsed = row.get("parsedInfo", row)
                    release = row.get("release", row)
                    quality_id = parsed["quality"]["quality"]["id"]
                    require((quality_id == self.custom["quality"]["id"]) == (title == RELEASES[0][0]), "Custom quality ignored source/resolution/codec/expression or matched a control release")
                    require(release["size"] == 1073741824, "Quality evaluation rewrote physical RSS size")
                target = by_title[RELEASES[0][0]]
                rejections = target.get("rejections", target.get("decision", {}).get("rejections", []))
                require(("larger than maximum" in json.dumps(rejections).lower()) == rejected, "Profile factor did not change the actual release-size decision exactly once")
                samples.append({"version": version, "factor": factor, "actualBytes": 1073741824, "normalizedBytes": 1073741824 * factor,
                                "qualityId": self.custom["quality"]["id"], "rejections": rejections})
            self.check(version + " native release evaluation selects only the custom 4K Blu-ray/HEVC expression match and uses its profile factor once")
        self.observations["releaseDecisions"] = samples

    def generated_import(self):
        incoming = self.root / "media" / "incoming"
        incoming.mkdir(mode=0o777)
        source = incoming / (RELEASES[0][0] + ".mkv")
        generated = subprocess.run([self.args.ffmpeg, "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=3840x2160:r=1",
                                    "-frames:v", "1", "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "pools=1:frame-threads=1:log-level=error",
                                    "-pix_fmt", "yuv420p", "-y", str(source)], capture_output=True, text=True, timeout=60)
        require(generated.returncode == 0 and source.is_file(), "Could not generate the owned 4K HEVC import sample")
        source.chmod(0o644)
        for version in ("v3", "v5"):
            preview = self.json_api(f"/api/{version}/manualimport?folder=" + urllib.parse.quote("/media/incoming") + "&filterExistingFiles=false")
            require(len(preview) == 1 and preview[0]["quality"]["quality"]["id"] == self.custom["quality"]["id"], "Automatic import preview did not match actual generated 4K HEVC media to the custom quality")
            require(preview[0]["qualityWeight"] >= 19, "Custom import preview did not use the dynamic quality catalogue weight")
            require(preview[0]["series"]["id"] == 1 and len(preview[0]["episodes"]) == 1, "Generated import preview did not map the native series/episode")
            self.check(version + " automatic import preview matches a generated 4K HEVC file to the same custom quality as its release")
        episode_id = next(identity[0] for identity in self.native_episodes if identity[1:] == (1, 1))
        self.command({"name": "ManualImport", "importMode": "copy", "files": [{
            "path": "/media/incoming/" + source.name, "seriesId": 1, "episodeIds": [episode_id], "quality": preview[0]["quality"],
            "languages": [{"id": 1, "name": "English"}]}]})
        episode = self.json_api(f"/api/v5/episode/{episode_id}")
        require(episode["hasFile"], "Generated custom-quality episode was not imported")
        imported = self.json_api(f"/api/v5/episodefile/{episode['episodeFileId']}")
        target = self.root / "media" / Path(imported["path"]).relative_to("/media")
        require(target.is_file() and target.parent.name == "Season 1", "Generated import did not use the configured series/season folders")
        require(imported["quality"]["quality"]["id"] == self.custom["quality"]["id"], "Import lost the selected custom quality identity")
        require(hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(source.read_bytes()).digest(), "Import changed physical media bytes")
        self.observations["generatedImport"] = {"episodeId": episode_id, "fileId": imported["id"], "qualityId": self.custom["quality"]["id"], "path": imported["path"], "sha256": hashlib.sha256(target.read_bytes()).hexdigest(), "actualBytes": target.stat().st_size}
        self.check("generated custom-quality import preserves physical bytes and writes the native series/season folder")
        self.command({"name": "RescanSeries", "seriesId": 1})
        require(self.json_api(f"/api/v5/episodefile/{imported['id']}")["quality"]["quality"]["id"] == self.custom["quality"]["id"], "Rescan reclassified an existing custom-quality file")
        self.check("rescan retains an existing file's custom quality and native episode identity")

    def retirement(self):
        quality_id = self.custom["quality"]["id"]
        require(self.raw(f"/api/v5/qualitydefinition/{self.custom['id']}", "DELETE")[0] == 400, "An enabled in-use quality was deleted")
        require(not self.definition("v5", quality_id)["isRetired"], "Rejected deletion changed the quality identity")
        self.check("deleting an enabled quality fails safely without changing profiles or library records")
        profile = self.json_api(f"/api/v5/qualityprofile/{self.profile['id']}")
        next(item for item in self.leaves(profile["items"]) if item["quality"]["id"] == quality_id)["allowed"] = False
        self.json_api(f"/api/v5/qualityprofile/{profile['id']}", "PUT", profile)
        require(self.raw(f"/api/v3/qualitydefinition/{self.custom['id']}", "DELETE")[0] == 204, "A disabled quality could not be deleted")
        require(self.definition("v5", quality_id)["isRetired"], "Delete did not preserve a retired quality identity")
        imported = self.observations["generatedImport"]
        require(self.json_api(f"/api/v5/episodefile/{imported['fileId']}")["quality"]["quality"]["id"] == quality_id, "Deleting a definition erased or remapped an existing file")
        self.check("deleting a disabled preset preserves its archived identity and existing file/history quality IDs")
        self.json_api(f"/api/v5/qualitydefinition/{self.custom['id']}/restore", "POST", {})
        require(not self.definition("v3", quality_id)["isRetired"], "Restoring the preset failed")
        races = []
        for _ in range(6):
            self.json_api(f"/api/v5/qualitydefinition/{self.custom['id']}/restore", "POST", {})
            disabled = self.json_api(f"/api/v5/qualityprofile/{self.profile['id']}")
            next(item for item in self.leaves(disabled["items"]) if item["quality"]["id"] == quality_id)["allowed"] = False
            self.json_api(f"/api/v5/qualityprofile/{disabled['id']}", "PUT", disabled)
            enabled = copy.deepcopy(disabled)
            next(item for item in self.leaves(enabled["items"]) if item["quality"]["id"] == quality_id)["allowed"] = True
            with ThreadPoolExecutor(max_workers=2) as workers:
                update = workers.submit(self.raw, f"/api/v5/qualityprofile/{enabled['id']}", "PUT", enabled)
                deletion = workers.submit(self.raw, f"/api/v5/qualitydefinition/{self.custom['id']}", "DELETE")
                update_status, delete_status = update.result()[0], deletion.result()[0]
            retired = self.definition("v5", quality_id)["isRetired"]
            active = next(item for item in self.leaves(self.json_api(f"/api/v5/qualityprofile/{enabled['id']}")["items"]) if item["quality"]["id"] == quality_id)["allowed"]
            require(update_status in (202, 400) and delete_status in (204, 400) and not (retired and active), "Concurrent profile enable/delete bypassed retirement safety")
            races.append({"updateHttp": update_status, "deleteHttp": delete_status, "retired": retired, "enabled": active})
        self.observations["concurrentEnableDelete"] = races
        self.check("concurrent profile-enable and preset-delete requests never produce an enabled retired quality")
        self.json_api(f"/api/v5/qualitydefinition/{self.custom['id']}/restore", "POST", {})
        self.docker("restart", self.container)
        self.refresh_binding()
        self.wait_ready()
        require(self.definition("v5", quality_id)["title"] == self.custom["title"], "Custom criteria or name did not survive restart")
        require(self.json_api(f"/api/v3/qualityprofile/{self.profile['id']}")["codecMultipliers"]["HEVC"] == 1.9, "Configured profile factors did not survive restart")
        require(self.json_api(f"/api/v5/episodefile/{imported['fileId']}")["quality"]["quality"]["id"] == quality_id, "Custom file identity did not survive restart")
        require(self.episode_identity() == self.native_episodes, "Quality operations changed native episode IDs or numbering")
        require(self.json_api("/api/v5/downloadclient") == [] and self.json_api("/api/v5/queue")["totalRecords"] == 0, "Fixture configured a client or acquired real media")
        self.check("restore and container restart preserve custom quality criteria, profile factors, imported files and native numbering")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:verified")
    parser.add_argument("--mock-image", default="python:3.13-alpine")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.serve_fixture:
        quality.RELEASES = RELEASES
        quality.serve_fixture(args.serve_fixture)
        return 0
    require(args.report is not None, "--report is required")
    audit = CustomQualities(args)
    error = None
    logs = ""
    requests = []
    try:
        audit.start()
        audit.seed()
        audit.catalogue()
        audit.profile_factors()
        audit.indexer()
        audit.matching()
        audit.generated_import()
        audit.retirement()
    except (Exception, KeyboardInterrupt) as failure:
        error = str(failure) or type(failure).__name__
        for secret in (audit.api_key, audit.metadata_key, audit.password):
            error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, flush=True)
    finally:
        if audit.container:
            try:
                logs = audit.docker("logs", audit.container)
                for secret in (audit.api_key, audit.metadata_key, audit.password):
                    logs = logs.replace(secret, "[REDACTED]")
                requests = audit.requests() if audit.fixture_root else []
            except Exception as failure:
                error = error or "Could not collect fixture evidence: " + type(failure).__name__
        try:
            audit.cleanup()
        except Exception as failure:
            error = error or "Owned fixture cleanup failed: " + type(failure).__name__
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
        report = {"createdUtc": quality.packs.utc_now(), "passed": error is None, "error": error,
                  "checks": audit.results, "image": args.image, "imageId": audit.image_id,
                  "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"),
                  "runtime": audit.runtime_metadata, "observations": audit.observations,
                  "fixtureRequests": requests, "mockImageId": audit.mock_image_id,
                  "fixtureConfigOwnership": audit.fixture_config_ownership,
                  "scope": {"mainConfigurationAccessed": False, "realLibraryTested": False,
                            "liveIndexerTested": False, "localRssOnly": True, "acquisitionTested": False,
                            "generated4kHevcImportTested": "generatedImport" in audit.observations},
                  "keptApp": audit.container if args.keep else None,
                  "keptMock": audit.mock if args.keep else None,
                  "keptNetwork": audit.network if args.keep else None,
                  "keptData": str(audit.root) if args.keep else None,
                  "cleanupComplete": error is None and not args.keep}
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    raise SystemExit(main())
