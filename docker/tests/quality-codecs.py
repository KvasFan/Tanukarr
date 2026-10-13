#!/usr/bin/env python3
"""Verify editable quality limits and codec decisions against disposable native RSS releases."""

import argparse
import copy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import sqlite3
import threading
import urllib.parse
from xml.sax.saxutils import escape

from smoke import require


SCRIPTS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("quality_native_packs", SCRIPTS / "series-packs.py")
packs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packs)
CODECS = {"x262": 0.50, "x264": 1.00, "x265": 1.67, "AV1": 2.22}
RELEASES = [
    (f"{packs.TITLE}.{part}.1080p.WEB-DL.{codec}-Fixture", codec, hours)
    for part, hours in (("S01E01", 1), ("S01", 2), ("S01-S03", 6))
    for codec in CODECS
]


def serve_fixture(root):
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_arguments):
            pass

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            query = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
            status = 200 if parsed.path == "/" and query.get("page") == ["rss"] and len(self.path) <= 2048 else 400
            items = []
            if status == 200:
                for index, (title, _codec, hours) in enumerate(RELEASES, 1):
                    items.append(
                        f"<item><title>{escape(title)}</title>"
                        f'<guid isPermaLink="true">http://fixture-rss:8989/view/{index}</guid>'
                        f"<link>http://fixture-rss:8989/download/{index}.torrent</link>"
                        "<pubDate>Tue, 13 Oct 2026 09:00:00 +0000</pubDate>"
                        f"<nyaa:size>{hours} GiB</nyaa:size><nyaa:infoHash>{index:040x}</nyaa:infoHash>"
                        "<nyaa:seeders>20</nyaa:seeders><nyaa:leechers>1</nyaa:leechers></item>")
            body = ('<?xml version="1.0"?><rss version="2.0" xmlns:nyaa="https://nyaa.si/xmlns/nyaa">'
                    '<channel><title>Native codec fixture</title><description>Synthetic metadata</description>'
                    '<link>http://fixture-rss:8989/</link>' + "".join(items) + "</channel></rss>").encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/rss+xml")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as output:
                output.write(json.dumps({"utc": packs.utc_now(), "path": parsed.path, "query": query, "status": status}) + "\n")

    print("Local native pack RSS fixture ready", flush=True)
    ThreadingHTTPServer(("0.0.0.0", 8989), Handler).serve_forever()


class QualityCodecs(packs.SeriesPacks):
    fixture_script = "/runner/quality-codecs.py"

    def __init__(self, args):
        super().__init__(args)
        self.observations = {}

    def seed(self):
        super().seed()
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            db.execute("UPDATE Series SET Runtime=60")
            db.execute("UPDATE Episodes SET Runtime=60")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()

    @staticmethod
    def leaves(items):
        return [leaf for item in items for leaf in ([item] if item.get("quality") else QualityCodecs.leaves(item["items"]))]

    def profiles(self):
        profiles = self.json_api("/api/v5/qualityprofile")
        for profile in profiles:
            for item in self.leaves(profile["items"]):
                for field in ("minSize", "preferredSize", "maxSize"):
                    item.setdefault(field, None)
        return profiles

    def definition(self, version="v5", quality_id=None):
        definitions = self.json_api(f"/api/{version}/qualitydefinition")
        for definition in definitions:
            for field in ("minSize", "preferredSize", "maxSize"):
                definition.setdefault(field, None)
        return next(item for item in definitions if item["quality"]["id"] == quality_id) if quality_id is not None else definitions

    def update(self, version, rows, expected=200):
        path = f"/api/{version}/qualitydefinition" + ("/update" if version == "v3" else "")
        status, _, body = self.raw(path, "PUT", rows)
        require(status == expected or expected == 200 and status == 202, f"{version} quality update returned HTTP {status}: {body[:500]!r}")

    def editable_limits(self):
        profiles = self.profiles()
        require(len(profiles) >= 2, "Expected multiple actual default profiles")
        for profile in profiles:
            require(self.leaves(profile["items"]), "Default profile has no leaves")
        target = self.leaves(profiles[0]["items"])[0]["quality"]["id"]
        for version in ("v3", "v5"):
            definition = self.definition(version, target)
            leaf = next(item for item in self.leaves(profiles[0]["items"]) if item["quality"]["id"] == target)
            require(all(definition[field] == leaf[field] and not definition[field + "IsMixed"] for field in ("minSize", "preferredSize", "maxSize")),
                    "Definition editor does not show real per-profile limits")
            require(not any("specified" in key.lower() for key in definition), "Internal presence flags leaked into API JSON")
            self.check(version + " quality definitions show actual editable profile sizes and keep presence flags private")

        first = profiles[0]
        leaf = next(item for item in self.leaves(first["items"]) if item["quality"]["id"] == target)
        leaf.update(minSize=0.5, preferredSize=10.0, maxSize=20.0)
        self.json_api(f"/api/v5/qualityprofile/{first['id']}", "PUT", first)
        mixed = self.definition("v5", target)
        require(all(mixed[field] is None and mixed[field + "IsMixed"] for field in ("minSize", "preferredSize", "maxSize")),
                "Differing profile values did not produce mixed markers")
        self.check("different real profiles are represented as Mixed instead of empty or fabricated common values")

        for version in ("v3", "v5"):
            before = self.profiles()
            definition = self.definition(version, target)
            self.update(version, [{"id": definition["id"], "title": definition["title"]}])
            require(self.profiles() == before, "Title-only update overwrote profile sizes")
            self.check(version + " title-only save preserves every profile and mixed size value")
            self.update(version, [definition])
            require(self.profiles() == before, "Echoed Mixed presentation nulls cleared real profile differences")
            self.check(version + " full GET/PUT echo preserves Mixed nulls when an older client edits the title")

        before = self.profiles()
        self.update("v5", [{"id": mixed["id"], "minSize": 1.0}])
        after = self.profiles()
        expected = copy.deepcopy(before)
        for profile in expected:
            next(item for item in self.leaves(profile["items"]) if item["quality"]["id"] == target)["minSize"] = 1.0
        require(after == expected, "Partial minimum update changed an untouched profile field")
        self.check("partial global save applies only the edited size across profiles, preserving untouched limits, groups and permissions")

        for version in ("v3", "v5"):
            before_profiles = self.profiles()
            before_definitions = self.definition()
            self.update(version, [None], expected=400)
            require(self.profiles() == before_profiles and self.definition() == before_definitions, "Null bulk entry changed stored settings")
        self.check("v3/v5 reject null bulk entries without a server exception or a partial settings write")

        for version in ("v3", "v5"):
            before_profiles = self.profiles()
            before_definitions = self.definition()
            other = next(item for item in before_definitions if item["id"] != mixed["id"])
            self.update(version, [{"id": other["id"], "title": "Must not persist invalid batch"},
                                  {"id": mixed["id"], "minSize": 30.0}], expected=400)
            require(self.profiles() == before_profiles and self.definition() == before_definitions, "Invalid merged batch partially persisted")
            self.check(version + " rejects invalid merged limits before any profile or title in the batch changes")

        self.update("v5", [{"id": mixed["id"], "preferredSize": None, "maxSize": None}])
        values = self.definition("v5", target)
        require(values["preferredSize"] is None and values["maxSize"] is None and not values["maxSizeIsMixed"],
                "Explicit null did not clear limits to Unlimited")
        self.check("explicit null clears preferred/maximum limits to Unlimited while omitted minimum remains saved")

        before = self.profiles()
        self.docker("restart", self.container)
        self.refresh_binding()
        self.wait_ready()
        require(self.profiles() == before and self.definition("v3", target)["maxSize"] is None,
                "Saved size changes did not survive container restart")
        self.check("actual profile size edits and explicit Unlimited survive a Docker restart")

        for version in ("v3", "v5"):
            catalog = self.json_api(f"/api/{version}/qualitydefinition/codecs")
            require(catalog["MPEG-2 / H.262 / x262"] == .5 and catalog["H.264 / AVC / x264"] == 1
                    and catalog["H.265 / HEVC / x265"] == 1.67 and catalog["AV1"] == 2.22, "Required codec policy differs")
        self.check("v3/v5 expose one backend codec policy with all four requested factors")

    def codec_decisions(self):
        profile = self.profiles()[0]
        for leaf in self.leaves(profile["items"]):
            leaf.update(minSize=768 / 60, preferredSize=1600 / 60, maxSize=2048 / 60)
        self.json_api(f"/api/v5/qualityprofile/{profile['id']}", "PUT", profile)
        episode_id = next(identity[0] for identity in self.native_episodes if identity[1:] == (1, 1))
        episode_titles = {title for title, _, hours in RELEASES if hours == 1}
        pack_titles = {title for title, _, hours in RELEASES if hours > 1}
        for version in ("v3", "v5"):
            evidence = {"episodeSearch": {"episodeId": episode_id, "expectedTitles": sorted(episode_titles)},
                        "completeSeriesSearch": {"expectedTitles": sorted(pack_titles), "excludedEpisodeTitles": sorted(episode_titles)},
                        "samples": []}
            self.observations[version] = evidence
            episode_rows = self.json_api(f"/api/{version}/release?episodeId={episode_id}")
            evidence["episodeSearch"]["returnedRows"] = episode_rows
            pack_rows = self.json_api(f"/api/{version}/release?seriesId=1&completeSeries=true")
            evidence["completeSeriesSearch"]["returnedRows"] = pack_rows
            episode_by_title = {row.get("release", row).get("title"): row for row in episode_rows}
            pack_by_title = {row.get("release", row).get("title"): row for row in pack_rows}
            require(episode_titles.issubset(episode_by_title), "Native episode search omitted codec variants")
            require(pack_titles.issubset(pack_by_title), "Native complete-series search omitted pack codec variants")
            require(episode_titles.isdisjoint(pack_by_title)
                    and all(row.get("parsedInfo", row).get("fullSeason") is True for row in pack_rows),
                    "Complete-series search returned an individual episode instead of only packs")
            samples = []
            by_title = dict(pack_by_title)
            by_title.update({title: episode_by_title[title] for title in episode_titles})
            for title, codec, hours in RELEASES:
                row = by_title[title]
                release = row.get("release", row)
                parsed = row.get("parsedInfo", row)
                rejections = row.get("rejections", row.get("decision", {}).get("rejections", []))
                detail = json.dumps(rejections).lower()
                require(release["size"] == hours * 1073741824, "Codec evaluation changed the physical RSS byte count")
                require(len(row.get("mappedEpisodeInfo", [])) == hours, "Native episode runtime mapping differs from pack duration")
                if codec == "x262":
                    require("smaller than minimum" in detail, "MPEG-2 .50 did not trigger normalized minimum rejection")
                elif codec == "AV1":
                    require("larger than maximum" in detail, "AV1 2.22 did not trigger normalized maximum rejection")
                else:
                    require("smaller than minimum" not in detail and "larger than maximum" not in detail,
                            "H.264/HEVC incorrectly failed normalized quality-size limits")
                samples.append({"title": title, "codecFactor": CODECS[codec], "actualBytes": release["size"],
                                "normalizedBytes": release["size"] * CODECS[codec], "runtimeMinutes": hours * 60,
                                "mappedEpisodeCount": len(row.get("mappedEpisodeInfo", [])), "rejections": rejections,
                                "fullSeason": parsed.get("fullSeason")})
                evidence["samples"] = samples
            self.check(version + " actual Nyaa RSS episode, season and multiseason releases apply codec factors once and keep physical bytes")
        require(self.episode_identity() == self.native_episodes, "Quality evaluation changed native episode identities")
        self.check("quality and codec decisions preserve every native TV season and episode identity")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:verified")
    parser.add_argument("--mock-image", default="python:3.13-alpine")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    require(args.report is not None, "--report is required")
    audit = QualityCodecs(args)
    error = None
    logs = ""
    requests = []
    try:
        audit.start()
        audit.seed()
        audit.editable_limits()
        audit.indexer()
        audit.codec_decisions()
        audit.no_acquisition()
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
        report = {"createdUtc": packs.utc_now(), "passed": error is None, "error": error,
                  "checks": audit.results, "image": args.image, "imageId": audit.image_id,
                  "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"),
                  "runtime": audit.runtime_metadata, "observations": audit.observations,
                  "fixtureRequests": requests, "mockImageId": audit.mock_image_id,
                  "fixtureConfigOwnership": audit.fixture_config_ownership,
                  "scope": {"mainConfigurationAccessed": False, "realLibraryTested": False,
                            "liveIndexerTested": False, "localRssOnly": True, "acquisitionTested": False},
                  "keptApp": audit.container if args.keep else None,
                  "keptMock": audit.mock if args.keep else None,
                  "keptNetwork": audit.network if args.keep else None,
                  "keptData": str(audit.root) if args.keep else None,
                  "cleanupComplete": error is None and not args.keep}
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    raise SystemExit(main())
