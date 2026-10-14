#!/usr/bin/env python3
"""Verify missing presentation recovery with verified IDs and isolated trusted catalogue/artwork TLS."""

import argparse
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import secrets
import ssl
import sys
import threading
import time
import urllib.parse

from smoke import require


_spec = importlib.util.spec_from_file_location("artwork_fixture_parent", Path(__file__).with_name("anilist-reliability.py"))
_parent = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_parent)
ANI_IDS = (970101, 970103, 970104, 970105, 970106, 970107, 970108)
MAL_ID = 970202
TRUSTED_IMAGES = ("cdn.myanimelist.net", "s4.anilist.co", "image.tmdb.org", "artworks.thetvdb.com")


def ani_resource(media_id, empty_owner=False):
    title = "Generated artwork anime " + str(media_id)
    complete = media_id == 970103 and not empty_owner
    resource = {
        "id": media_id, "idMal": media_id + 100, "type": "ANIME", "format": "TV",
        "title": {"english": title, "romaji": title, "userPreferred": title},
        "episodes": 2, "duration": 24, "status": "FINISHED", "genres": [], "synonyms": [],
        "startDate": {"year": 2024, "month": 1, "day": 1}, "endDate": {"year": 2024, "month": 1, "day": 8},
        "description": "Owner presentation" if complete else None,
        "coverImage": {"extraLarge": "", "large": "https://s4.anilist.co/owner.png"} if complete else {},
        "bannerImage": "https://s4.anilist.co/owner-backdrop.png" if complete else None,
        "studios": {"nodes": []}, "relations": {"edges": []},
        "airingSchedule": {"pageInfo": {"currentPage": 1, "hasNextPage": False},
                           "nodes": [{"episode": 1, "airingAt": 1704067200}, {"episode": 2, "airingAt": 1704672000}]},
    }
    if media_id in (970107, 970108):
        resource["idMal"] = None
        resource["title"] = {"english": "Generated canonical collection" if media_id == 970107 else "Generated collection season 2"}
        resource["description"] = "Whole canonical collection overview" if media_id == 970107 else "Second season overview"
        resource["relations"]["edges"] = [{"relationType": "SEQUEL" if media_id == 970107 else "PREQUEL",
                                           "node": {"id": 970108 if media_id == 970107 else 970107, "type": "ANIME", "format": "TV"}}]
        if media_id == 970107:
            resource["coverImage"] = {"large": "https://s4.anilist.co/root-poster.png"}
            resource["bannerImage"] = "https://s4.anilist.co/root-backdrop.png"
    return resource


def jikan_resource(media_id):
    return {"mal_id": media_id, "title": "Generated native MAL " + str(media_id), "type": "TV",
            "episodes": 2, "duration": "24 min", "status": "Finished Airing", "relations": [], "genres": [], "studios": [],
            "synopsis": None if media_id == MAL_ID else "Verified Jikan description",
            "images": {} if media_id == MAL_ID else {"jpg": {"large_image_url": "", "image_url": None},
                                                      "webp": {"large_image_url": "https://cdn.myanimelist.net/generated.webp"}}}


def skyhook_resource(media_id):
    with_images = media_id == 970302
    return {"tvdbId": media_id, "tmdbId": 970401 if media_id == 970301 else None,
            "title": "Generated native TV " + str(media_id), "slug": "generated-tv-" + str(media_id),
            "overview": "Verified TVDB description" if with_images else None, "status": "ended", "runtime": 24,
            "genres": [], "actors": [], "translations": [], "malIds": [], "aniListIds": [],
            "images": [{"coverType": kind, "url": "https://artworks.thetvdb.com/" + kind + ".png"}
                       for kind in ("poster", "fanart")] if with_images else [],
            "seasons": [{"seasonNumber": 1, "images": []}],
            "seasonTypes": [{"name": "Official", "type": "official", "seasonNumbers": [1], "episodeCount": 2}],
            "episodes": [{"tvdbId": media_id * 10 + number, "seasonNumber": 1, "episodeNumber": number,
                          "title": "Native TV episode " + str(number), "airDate": "2024-01-0" + str(number)}
                         for number in (1, 2)]}


def serve_fixture(root):
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def control(self):
            return json.loads((root / "control.json").read_text(encoding="utf-8"))

        def answer(self, status, value, record, content_type="application/json", retry_after=None):
            body = value if isinstance(value, bytes) else json.dumps(value).encode("utf-8")
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(dict(record, status=status)) + "\n")
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            if retry_after is not None:
                self.send_header("Retry-After", str(retry_after))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            parsed = urllib.parse.urlparse(self.path)
            host = self.headers.get("Host", "").split(":")[0]
            record = {"host": host, "path": parsed.path, "method": "GET"}
            try:
                require(not self.headers.get("Authorization"), "Private account credentials reached public presentation")
                if host in TRUSTED_IMAGES:
                    record["kind"] = "artwork"
                    self.answer(200, _parent.poster_png(), record, "image/png")
                    return
                if host == "api.jikan.moe" and parsed.path.startswith("/v4/anime/"):
                    pieces = parsed.path.split("/")
                    media_id = int(pieces[3])
                    record["mediaId"] = media_id
                    require(media_id in (MAL_ID, 970201, 970204, 970205, 970206), "Unknown generated Jikan ID")
                    if pieces[4:] == ["full"]:
                        record["kind"] = "jikan-full"
                        if media_id == 970205:
                            self.answer(429, {}, record, retry_after=600)
                        else:
                            self.answer(200, {"data": jikan_resource(970999 if media_id == 970204 else media_id)}, record)
                        return
                    if pieces[4:] == ["episodes"]:
                        require(media_id == MAL_ID, "Optional artwork lookup unexpectedly fetched episode pages")
                        record["kind"] = "jikan-episodes"
                        self.answer(200, {"data": [{"mal_id": number, "title": "Native MAL episode " + str(number)}
                                                  for number in (1, 2)], "pagination": {"has_next_page": False}}, record)
                        return
                if host == "api.themoviedb.org" and parsed.path.startswith("/3/tv/"):
                    pieces = parsed.path.split("/")
                    media_id = int(pieces[3])
                    require(media_id in (970401, 970402), "Unknown generated TMDB ID")
                    record.update(mediaId=media_id, kind="tmdb-full")
                    if pieces[4:] == ["season", "1"]:
                        require(media_id == 970402, "Optional presentation fetched a TMDB season")
                        record["kind"] = "tmdb-episodes"
                        self.answer(200, {"episodes": [{"id": media_id * 10 + number, "season_number": 1,
                                                      "episode_number": number, "name": "Native TMDB episode " + str(number),
                                                      "air_date": "2024-01-0" + str(number)} for number in (1, 2)]}, record)
                        return
                    with_images = media_id == 970401
                    self.answer(200, {"id": media_id, "name": "Generated TMDB " + str(media_id), "status": "Ended",
                                      "overview": "Verified TMDB description" if with_images else None,
                                      "poster_path": "/poster.png" if with_images else None,
                                      "backdrop_path": "/backdrop.png" if with_images else None,
                                      "external_ids": {"tvdb_id": 970302 if media_id == 970402 else 970301},
                                      "seasons": [{"id": media_id + 1, "season_number": 1, "episode_count": 2}],
                                      "genres": [], "networks": [], "episode_run_time": [24]}, record)
                    return
                if host == "skyhook.sonarr.tv" and parsed.path.startswith("/v2/tvdb/shows/"):
                    pieces = parsed.path.strip("/").split("/")
                    media_id = int(pieces[-2])
                    require(media_id in (970301, 970302), "Unknown generated TVDB ID")
                    record.update(mediaId=media_id, kind="tvdb-full")
                    self.answer(200, skyhook_resource(media_id), record)
                    return
                raise ValueError("Unexpected public presentation request")
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

        def do_POST(self):
            record = {"host": self.headers.get("Host", "").split(":")[0], "path": self.path, "method": "POST"}
            try:
                require(record["host"] == "graphql.anilist.co" and self.path == "/", "Unexpected GraphQL route")
                require(not self.headers.get("Authorization"), "Private account credentials reached public AniList metadata")
                length = int(self.headers.get("Content-Length", "0"))
                require(0 < length < 65536, "Unexpected generated GraphQL body size")
                request = json.loads(self.rfile.read(length))
                query, variables = request["query"], request["variables"]
                require("MediaListCollection" not in query and "search:" not in query, "Presentation fell back to list or title search")
                if variables.get("idMal") is not None:
                    require(variables["idMal"] == MAL_ID and "airingSchedule" not in query, "Unexpected native MAL cross mapping")
                    record.update(kind="anilist-presentation", mediaId=970102, malId=MAL_ID)
                    resource = {"id": 970102, "idMal": MAL_ID, "type": "ANIME", "description": "Verified AniList description",
                                "coverImage": {"large": "https://s4.anilist.co/alternative.png"},
                                "bannerImage": "https://s4.anilist.co/alternative-backdrop.png"}
                else:
                    media_id = int(variables["id"])
                    require(media_id in ANI_IDS, "Unknown generated AniList ID")
                    kind = "anilist-metadata" if "airingSchedule" in query else "anilist-relations" if "relations" in query else "anilist-presentation"
                    record.update(kind=kind, mediaId=media_id)
                    resource = ani_resource(media_id, self.control().get("emptyOwner", False))
                self.answer(200, {"data": {"Media": resource}}, record)
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local artwork presentation TLS fixture ready", flush=True)
    server.serve_forever()


class ArtworkReliability(_parent.AniListReliability):
    fixture_script = "artwork-reliability.py"
    fixture_ready_message = "Local artwork presentation TLS fixture ready"
    fixture_hosts = ("graphql.anilist.co", "api.jikan.moe", "api.themoviedb.org", "skyhook.sonarr.tv", *TRUSTED_IMAGES)
    fixture_startup_check = "isolated trusted TLS for all four public catalogues and expected artwork CDNs with no external routing"

    def __init__(self, args):
        super().__init__(args)
        self.control_state = {"emptyOwner": False}
        self.native_snapshots = {}
        self.cookie = None
        self.username = "generated-artwork-user"
        self.password = secrets.token_urlsafe(32)

    def ui(self, path, method="GET", data=None, cookie=True, headers=None):
        parsed = urllib.parse.urlsplit(self.base_url)
        connection = http.client.HTTPConnection(parsed.hostname, parsed.port, timeout=self.args.timeout)
        request_headers = dict(headers or {})
        if cookie and self.cookie:
            request_headers["Cookie"] = self.cookie
        try:
            connection.request(method, path, body=data, headers=request_headers)
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def forms_session(self):
        settings = self.api_json("/api/v5/settings/general")
        settings.update(username=self.username, password=self.password, passwordConfirmation=self.password)
        self.api_json("/api/v5/settings/general/1", "PUT", settings)
        form = urllib.parse.urlencode({"username": self.username, "password": self.password})
        status, headers, _ = self.ui("/login", "POST", form, cookie=False,
                                     headers={"Content-Type": "application/x-www-form-urlencoded"})
        require(status == 302 and headers.get("Set-Cookie"), "Synthetic Forms session did not initialize")
        self.cookie = headers["Set-Cookie"].split(";", 1)[0]
        require(self.ui("/")[0] == 200, "Authenticated browser UI route is unavailable")
        self.check("generated Forms session accesses browser artwork routes while API-key authentication stays separate")

    def add_native(self, source, native_id, grouping="disabled"):
        before = time.monotonic()
        result = self.api_json("/api/v5/series", "POST", {
            "title": "Generated presentation input", "metadataSource": source, "foreignId": str(native_id),
            "tvdbId": native_id if source == "tvdb" else 0, "rootFolderPath": "/media/library",
            "language": {"id": 1, "name": "English"}, "seasonType": "official",
            "qualityProfileId": self.profile_id, "monitored": False, "seasonFolder": True,
            "seriesType": "anime" if source in ("aniList", "myAnimeList") else "standard",
            "animeGroupingMode": grouping, "addOptions": {"monitor": "none", "searchForMissingEpisodes": False},
        })
        self.until(lambda: len(self.episodes(result)) == 2, "Native presentation fixture episodes did not initialize")
        require(result["metadataSource"] == source and result["foreignId"] == str(native_id), "Presentation fallback changed catalogue owner or native ID")
        require(all(episode["seasonNumber"] == 1 for episode in self.episodes(result)), "Presentation fallback renumbered episodes")
        snapshot = {"metadataSource": source, "foreignId": str(native_id), "path": result["path"],
                    "tvdbId": result["tvdbId"], "episodeIdentity": self.episode_identity(result)}
        self.native_snapshots[result["id"]] = snapshot
        self.observations.setdefault("addElapsedSeconds", {})[str(native_id)] = round(time.monotonic() - before, 3)
        return self.get(result)

    def covers(self, series, expected_types):
        current = self.get(series)
        images = {item["coverType"]: item for item in current["images"]}
        require(set(images) == set(expected_types), "Expected presentation artwork types were not retained")
        for kind in expected_types:
            self.until(lambda: (self.root / "config" / "MediaCover" / str(series["id"]) / (kind + ".jpg")).is_file(),
                       "Generated presentation image did not download")
            current = self.get(series)
            image = next(item for item in current["images"] if item["coverType"] == kind)
            parsed = urllib.parse.urlsplit(image["url"])
            require(not parsed.scheme and parsed.path.startswith("/MediaCover/"), "A registered image was not mapped to a local API URL")
            original = image["url"]
            require(self.raw(original)[0] in (302, 401, 403), "A UI cover unexpectedly accepted the API key without a Forms session")
            original_status, _headers, original_bytes = self.ui(original)
            require(original_status == 200 and original_bytes.startswith(b"\x89PNG"), "Original cached fixture artwork is not readable")
            size = 250 if kind == "poster" else 360
            status, _headers, body = self.ui(original.replace(kind + ".jpg", f"{kind}-{size}.jpg"))
            require(status == 200 and body.startswith((b"\x89PNG", b"\xff\xd8")), "Thumbnail or original fallback is not a readable image")
            api_path = parsed.path.replace("/MediaCover/", "/api/v5/mediacover/", 1)
            require(self.raw(api_path)[0] == 200 and self.raw(api_path, api_key=False)[0] == 401,
                    "API cover route does not require its own API-key authentication")
        return current

    def exercise(self):
        self.forms_session()
        settings = self.api_json("/api/v5/settings/metadata")
        settings["tmdbApiKey"] = "generated-presentation-tmdb-key"
        self.api_json("/api/v5/settings/metadata/1", "PUT", settings)

        ani = self.add_native("aniList", 970101)
        ani = self.covers(ani, ("poster",))
        require(ani["overview"] == "Verified Jikan description" and ani["images"][0]["remoteUrl"].startswith("https://cdn.myanimelist.net/"),
                "AniList owner did not recover missing description/poster from verified native MAL cross-ID")
        require(sum(row.get("kind") == "jikan-full" and row.get("mediaId") == 970201 for row in self.records()) == 1,
                "Optional Jikan presentation repeated public metadata")
        self.check("AniList uses verified idMal to recover missing synopsis and WebP poster without reading Jikan episode pages")

        mal = self.add_native("myAnimeList", MAL_ID)
        mal = self.covers(mal, ("poster", "fanart"))
        require(mal["overview"] == "Verified AniList description" and mal["malIds"] == [MAL_ID], "MAL presentation fallback replaced native owner IDs")
        require(any(row.get("kind") == "anilist-presentation" and row.get("malId") == MAL_ID for row in self.records()), "AniList did not confirm the native MAL mapping")
        require(all(episode["title"].startswith("Native MAL episode") for episode in self.episodes(mal)), "Presentation fallback replaced Jikan episode titles")
        self.check("MAL-owned TV entries receive AniList poster/backdrop through an exact idMal mapping while retaining native episode titles and IDs")

        owner = self.add_native("aniList", 970103)
        owner = self.covers(owner, ("poster", "fanart"))
        require(owner["overview"] == "Owner presentation", "Alternative presentation replaced valid owner description")
        require(not any(row.get("kind") == "jikan-full" and row.get("mediaId") == 970203 for row in self.records()), "Complete owner presentation consumed optional Jikan quota")
        self.check("valid owner overview/poster/backdrop are preserved and the empty extraLarge URL falls back to the actual large image")

        tvdb = self.add_native("tvdb", 970301)
        tvdb = self.covers(tvdb, ("poster", "fanart"))
        require(tvdb["overview"] == "Verified TMDB description" and tvdb["tvdbId"] == 970301, "Verified TMDB presentation changed TVDB identity")
        require(not any(row.get("kind") == "tmdb-episodes" and row.get("mediaId") == 970401 for row in self.records()), "Optional TMDB presentation read episode pages")
        self.check("TVDB-owned series recover artwork/description from their verified TMDB TV ID without changing seasons or fetching TMDB episodes")

        tmdb = self.add_native("tmdb", 970402)
        tmdb = self.covers(tmdb, ("poster", "fanart"))
        require(tmdb["overview"] == "Verified TVDB description" and tmdb["tvdbId"] == 970302, "Positive verified TVDB presentation changed TMDB owner")
        require(all(episode["title"].startswith("Native TMDB episode") for episode in self.episodes(tmdb)), "TVDB presentation replaced TMDB episode ordering/titles")
        self.check("TMDB-owned series recover presentation through their positive verified TVDB ID while retaining native TMDB episodes")

        child = self.add_native("aniList", 970108, "automatic")
        require(child["animeGroupId"] == "anilist:970107" and child["animeGroupSeasonNumber"] == 2,
                "Official root/child relation did not produce canonical group season two")
        require(child["overview"] == "Second season overview" and
                not any(item["foreignId"] == "970107" for item in self.api_json("/api/v5/series")),
                "Canonical root was added to the library or the child lost its native overview")
        root_values = []
        for version in ("v3", "v5"):
            path = f"/api/{version}/collection/presentation?groupId=anilist%3A970107"
            require(self.raw(path, api_key=False)[0] == 401, "Collection presentation did not require authentication")
            root = self.api_json(path)
            require(root["overview"] == "Whole canonical collection overview", "Collection page reused the second-season overview instead of verified canonical root")
            require({item["coverType"] for item in root["images"]} == {"poster", "fanart"}, "Canonical collection root omitted its own artwork")
            for image in root["images"]:
                url = urllib.parse.urlsplit(image["url"])
                require(not url.scheme and url.path.startswith("/MediaCoverProxy/"), "Canonical catalogue art was not registered through the local cover proxy")
                require(self.raw(image["url"])[0] in (302, 401, 403), "UI cover proxy accepted an API key without a browser session")
                status, _headers, body = self.ui(image["url"])
                require(status == 200 and body.startswith(b"\x89PNG"), "Canonical collection proxy did not serve actual root artwork")
            root_values.append(root["overview"])
        requests = sum(record.get("kind") == "anilist-presentation" and record.get("mediaId") == 970107 for record in self.records())
        require(requests == 1, "v3/v5 collection presentation did not share the bounded public root cache")
        require(self.raw("/api/v5/collection/presentation?groupId=anilist%3A979999")[0] == 404, "Unknown saved collection performed a catalogue request")
        self.observations["canonicalCollectionRoot"] = {"groupId": "anilist:970107", "savedMembers": [970108],
                                                       "wholeCollectionOverview": root_values[0], "rootPresentationRequests": requests,
                                                       "rootNotAddedToLibrary": True}
        self.check("a collection containing only native season two gets the unsaved verified canonical root synopsis/artwork through authenticated cached v3/v5 cover proxy")

        wrong = self.add_native("aniList", 970104)
        require(not wrong["images"] and not wrong.get("overview"), "Mismatched Jikan native ID attached artwork from a different anime")
        self.check("mismatched alternative catalogue IDs are rejected and unavailable presentation leaves an honest empty field")

        limited = self.add_native("aniList", 970105)
        following = self.add_native("aniList", 970106)
        require(not limited["images"] and not following["images"], "Quota-limited optional artwork manufactured a cover")
        require(sum(row.get("kind") == "jikan-full" and row.get("mediaId") == 970205 for row in self.records()) == 1,
                "Optional429 retried or exceeded its single-attempt allowance")
        require(not any(row.get("kind") == "jikan-full" and row.get("mediaId") == 970206 for row in self.records()), "Following entry ignored the shared optional provider cooldown")
        require(self.observations["addElapsedSeconds"]["970105"] < 15 and self.observations["addElapsedSeconds"]["970106"] < 15,
                "Optional artwork waited through the generated ten-minute shared provider cooldown")
        self.observations["optionalQuota"] = {"429Attempts": 1, "followingOptionalRequests": 0, "generatedRetryAfterSeconds": 600}
        self.check("optional429 uses one request and a shared cooldown while both complete native series imports succeed without waiting ten minutes")

        self.control(emptyOwner=True)
        self.docker("restart", self.container)
        self.wait_ready()
        self.command({"name": "RefreshSeries", "seriesId": owner["id"]})
        refreshed = self.covers(owner, ("poster", "fanart"))
        require(refreshed["overview"] == "Owner presentation", "Temporarily empty metadata refresh erased a valid saved overview")
        for series_id, snapshot in self.native_snapshots.items():
            current = self.api_json(f"/api/v5/series/{series_id}")
            require(all(current[name] == snapshot[name] for name in ("metadataSource", "foreignId", "path", "tvdbId")) and
                    self.episode_identity(current) == snapshot["episodeIdentity"], "Presentation enrichment changed saved native identity, folder or episode rows")
        self.observations["nativeSnapshots"] = self.native_snapshots
        self.check("a fresh-process refresh with empty owner presentation retains saved valid cover/overview and all native identity/path/episode rows")
        require(not any(record.get("host") in ("services.sonarr.tv", "sentry.sonarr.tv") for record in self.records()), "Presentation enrichment contacted blocked Sonarr infrastructure")
        require(not any(record["status"] == 400 for record in self.records()), "An unsupported or unexpected catalogue/artwork request occurred")
        self.check("all public catalogue/artwork requests stay within the trusted isolated mock and do not contact blocked Sonarr services")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=_parent.MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    audit = ArtworkReliability(args)
    error, records, logs, cleanup = None, [], "", False
    try:
        audit.start()
        audit.prepare()
        audit.exercise()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as caught:
        error = f"{audit.last_request}: {caught or type(caught).__name__}"
        for secret in (audit.api_key, audit.password, audit.cookie):
            if secret:
                error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, file=sys.stderr)
    finally:
        try:
            if audit.root:
                records = audit.records()
            if audit.container:
                logs = audit.docker("logs", audit.container)
                for secret in (audit.api_key, audit.password, audit.cookie):
                    if secret:
                        logs = logs.replace(secret, "[REDACTED]")
        except Exception as caught:
            error = error or "Evidence capture failed: " + str(caught)
        try:
            audit.cleanup()
            cleanup = not args.keep
        except Exception as caught:
            error = error or "Fixture cleanup failed: " + str(caught)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({"createdUtc": _parent._groups.utc_now(), "image": args.image,
                "imageId": audit.image_id, "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"),
                "runtime": audit.runtime_metadata, "mockImage": args.mock_image, "mockImageId": getattr(audit, "mock_id", None),
                "passed": error is None, "error": error, "checks": audit.results, "observations": audit.observations,
                "fixtureRequests": records, "cleanupComplete": cleanup,
                "keptData": str(audit.root) if args.keep and audit.root else None,
                "scope": {"trustedLocalCatalogueAndArtworkMock": True, "liveAlternativeCataloguesTested": False,
                          "privateAccountUsed": False, "realLibraryTested": False, "downloadsTested": False,
                          "mainConfigurationAccessed": False, "externalProviderRoutingEnabled": False,
                          "tlsValidationDisabled": False}}, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
