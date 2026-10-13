#!/usr/bin/env python3
"""Exercise packaged TMDB, Trakt and MDBList providers against isolated TLS fixtures."""

import argparse
from contextlib import closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import sqlite3
import ssl
import sys
import urllib.parse

from smoke import require

_spec = importlib.util.spec_from_file_location("list_reliability", Path(__file__).with_name("anilist-reliability.py"))
_reliability = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_reliability)
MOCK_IMAGE = _reliability.MOCK_IMAGE
HOSTS = ("graphql.anilist.co", "api.themoviedb.org", "api.trakt.tv", "api.mdblist.com", "discover.provider.plex.tv", "plex.tv")
IDS = (960101, 960102, 960103, 960104)


def serve_fixture(root):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            control = json.loads((root / "control.json").read_text(encoding="utf-8"))
            host = self.headers.get("Host", "").split(":")[0]
            source = parsed.path.split("/")[-1] if host == "api.themoviedb.org" else parsed.path.split("/")[-3]
            if host == "api.trakt.tv":
                source = parsed.path.split("/")[4] if "/items/" in parsed.path else "choices"
            page = int(query.get("page", ["1"])[0])
            cursor = query.get("cursor", [""])[0]
            headers = {}
            source = {"11": "a", "12": "b", "13": "c"}.get(source, source)
            failed = control.get("failedSource") == source
            with (root / "requests.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps({"host": host, "path": parsed.path, "source": source, "page": page,
                                         "cursor": cursor, "queryKeys": sorted(query), "failed": failed}) + "\n")
            identities = {"a": list(IDS[:2]), "b": list(IDS[1:3]), "c": [IDS[1], IDS[3]]}.get(source, [])
            if host == "plex.tv" and parsed.path == "/api/v2/ping":
                value = {"ok": True}
            elif host == "discover.provider.plex.tv":
                offset = int(query.get("X-Plex-Container-Start", ["0"])[0])
                size = int(query.get("X-Plex-Container-Size", ["100"])[0])
                total = 1001
                rows = [{"type": "show", "title": "Generated Plex TV " + str(index), "year": 2024,
                         "Guid": [{"id": "tvdb://" + str(970000 + index)}, {"id": "tmdb://" + str(970000 + index)}]}
                        for index in range(offset, min(offset + size, total))]
                returned_offset = offset
                if control.get("plexMalformed") and offset >= 1000:
                    returned_offset = 0
                value = {"MediaContainer": {"offset": returned_offset, "size": len(rows), "totalSize": total, "Metadata": rows}}
                headers = {"X-Plex-Container-Start": str(returned_offset), "X-Plex-Container-Total-Size": str(total)}
            elif host == "api.themoviedb.org":
                if "/account/" in parsed.path:
                    value = {"page": page, "total_pages": 2, "total_results": 2,
                             "results": [{"id": 11 if page == 1 else 12, "name": "Source " + str(page), "number_of_items": 2}]}
                else:
                    # A movie-only first page must not terminate TV pagination.
                    rows = [{"id": IDS[0], "media_type": "movie", "title": "Movie sharing a TV numeric ID"}] if source == "a" and page == 1 else [
                        {"id": identity, "media_type": "tv", "name": "Generated TV " + str(identity)} for identity in identities]
                    value = {"page": page, "total_pages": 2 if source == "a" else 1, "results": rows}
                if failed:
                    value["total_pages"] = "unknown"
            elif host == "api.trakt.tv":
                if source == "choices":
                    rows = [{"name": "Source " + str(page), "ids": {"slug": "a" if page == 1 else "b"}}]
                    pages, count = 2, 2
                else:
                    pages, count = (2, len(identities) + 1) if source == "a" else (1, len(identities))
                    rows = [{"type": "movie", "movie": {"ids": {"tmdb": IDS[0]}}}] if source == "a" and page == 1 else [
                        {"type": "show", "show": {"title": "Generated TV " + str(identity), "year": 2024,
                                                   "ids": {"tvdb": identity, "tmdb": identity, "imdb": "tt" + str(identity)}}}
                        for identity in identities]
                headers = {"X-Pagination-Page": str(page), "X-Pagination-Page-Count": str(pages),
                           "X-Pagination-Item-Count": str(count), "X-Pagination-Limit": query.get("limit", ["100"])[0]}
                if failed:
                    headers.pop("X-Pagination-Page-Count")
                value = rows
            elif host == "api.mdblist.com":
                more = source == "a" and not cursor
                rows = [{"mediatype": "movie", "id": IDS[0], "title": "Movie sharing a TV numeric ID"}] if more else [
                    {"mediatype": "show", "tmdb_id": identity, "tvdb_id": identity, "imdb_id": "tt" + str(identity),
                     "title": "Generated TV " + str(identity)} for identity in identities]
                value = {"shows": rows, "pagination": {"has_more": more, "next_cursor": "second-page" if more else None}}
                headers = {"X-Has-More": "true" if more else "false"}
                if failed:
                    value = {"unavailable": True}
            else:
                self.send_error(404)
                return
            body = json.dumps(value).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            for key, value in headers.items():
                self.send_header(key, value)
            self.end_headers()
            self.wfile.write(body)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local list set-operation TLS fixture ready", flush=True)
    server.serve_forever()


class ListSets(_reliability.AniListReliability):
    fixture_script = "list-set-operations.py"
    fixture_ready_message = "Local list set-operation TLS fixture ready"
    fixture_hosts = HOSTS
    fixture_startup_check = "isolated trusted TLS providers, loopback-only API and no external provider routing"

    def prevent_metadata_acquisition(self):
        # A real ImportListSync must fetch and persist these lists. Global library
        # exclusions are evaluated after that persistence and before metadata
        # resolution, so they keep this provider-membership fixture self-contained.
        # The negative TVDB placeholder identifies a non-TVDB-owned exclusion;
        # its authoritative identity is the TMDB source and positive foreign ID.
        for identity in (IDS[0], IDS[2]):
            for source, tvdb_id in (("tvdb", identity), ("tmdb", -identity)):
                resource = {"tvdbId": tvdb_id, "title": "Generated membership-only " + str(identity),
                            "metadataSource": source, "foreignId": str(identity)}
                added = self.api_json("/api/v5/importlistexclusion", "POST", resource)
                require(added["tvdbId"] == tvdb_id and added["metadataSource"] == source
                        and added["foreignId"] == str(identity), "A generated metadata exclusion changed provider identity")
        for identity in range(970000, 971001):
            added = self.api_json("/api/v5/importlistexclusion", "POST", {
                "tvdbId": identity, "title": "Generated Plex membership-only " + str(identity),
                "metadataSource": "tvdb", "foreignId": str(identity)})
            require(added["tvdbId"] == identity, "A generated Plex metadata exclusion changed TVDB identity")
        require(self.api_json("/api/v5/series") == [], "Generated exclusion setup unexpectedly added a series")
        self.observations["metadataIsolation"] = {"generatedGlobalExclusions": 1005,
                                                  "exclusionsCreatedThroughApi": True, "databaseSeeded": False}
        self.check("owned API exclusions allow real list synchronization while preventing metadata acquisition for1001 Plex entries")

    def stored(self, definition):
        path = (self.root / "config" / "sonarr.db").resolve(strict=True)
        require(path.parent == self.root / "config", "Read-only inspection escaped the owned fixture")
        uri = "file:" + urllib.parse.quote(str(path), safe="/") + "?mode=ro"
        with closing(sqlite3.connect(uri, uri=True, timeout=10)) as connection:
            connection.execute("PRAGMA query_only = ON")
            return sorted(tuple(row) for row in connection.execute(
                "SELECT TvdbId,TmdbId,ImdbId FROM ImportListItems WHERE ImportListId = ?", (definition["id"],)))

    def exercise_provider(self, implementation, values, included, excluded, sources, legacy):
        schema = self.api_json("/api/v5/importlist/schema")
        resource = next(item for item in schema if item["implementation"] == implementation)
        for version in ("v3", "v5"):
            provider = next(item for item in self.api_json(f"/api/{version}/importlist/schema") if item["implementation"] == implementation)
            fields = {field["name"]: field for field in provider["fields"]}
            require(fields[included]["type"] == "tag" and fields[excluded]["type"] == "tag", "Modern list source controls are absent")
            require(all(fields[name].get("hidden") == "hidden" for name in legacy), "Legacy scalar source controls remain visible")
        self.check(implementation + " exposes modern inclusion/exclusion tags and hides obsolete scalar selectors in v3/v5")
        resource.update(name="Generated " + implementation, rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                        language={"id": 1, "name": "English"}, enableAutomaticAdd=True, searchForMissingEpisodes=False,
                        shouldMonitor="none", seriesType="standard", seasonFolder=True)
        values.update({included: sources[:2], excluded: sources[2:]})
        fields = {field["name"]: field for field in resource["fields"]}
        require(all(name in fields for name in values), implementation + " omitted a fixture setting")
        for name, value in values.items():
            fields[name]["value"] = value
        definition = self.api_json("/api/v5/importlist", "POST", resource)
        self.idle()
        self.command({"name": "ImportListSync", "definitionId": definition["id"]})
        self.idle()
        saved = self.stored(definition)
        source_ids = sorted(row[1] for row in saved)
        require(source_ids == [IDS[0], IDS[2]], implementation + " did not produce union(A,B) minus C by native TV identity")
        require(self.api_json("/api/v5/series") == [], implementation + " bypassed the owned metadata-acquisition exclusions")
        self.check(implementation + " reads all pages including a movie-only page, unions included sources and subtracts excluded members")
        self.control(failedSource="c")
        self.command({"name": "ImportListSync", "definitionId": definition["id"]})
        self.idle()
        require(self.stored(definition) == saved, implementation + " replaced authoritative membership after an excluded source failed")
        status, _ = self.raw("/api/v5/importlist/test", "POST", definition)
        require(status == 400, implementation + " Test accepted an incomplete excluded source")
        self.check(implementation + " rejects incomplete exclusion responses and retains the previous saved membership")
        self.control(failedSource=None)
        original = self.api_json("/api/v5/importlist/" + str(definition["id"]))
        for version in ("v3", "v5"):
            status, _ = self.raw(f"/api/{version}/importlist/test", "POST", original)
            require(status == (200 if version == "v3" else 204), implementation + " recovered Test failed")
        require(self.api_json("/api/v5/importlist/" + str(definition["id"])) == original, "Unsaved Test persisted settings")
        self.check(implementation + " recovered v3/v5 Test uses current fields without saving or acquiring media")
        if implementation in ("TmdbListImport", "TraktListImport"):
            action = "getAccountLists" if implementation == "TmdbListImport" else "getLists"
            options = self.api_json("/api/v5/importlist/action/" + action, "POST", original)["options"]
            require(len(options) == 2 and all(item.get("value") for item in options), "List choices were truncated or included a legacy None option")
            self.check(implementation + " list-name choices use complete provider pagination and real source identities")
        empty = json.loads(json.dumps(original))
        self.field(empty, included)["value"] = []
        status, _ = self.raw("/api/v5/importlist/test", "POST", empty)
        require(status == 400, "A remote-list provider invented an account universe without an included source")
        self.check(implementation + " requires an explicit included source instead of inventing an account-wide list")
        self.observations[implementation] = {"storedIdentities": saved, "exclusionFailureRetainedMembership": True,
                                            "unsavedTestPreservedSettings": True}

    def plex_pagination(self):
        resource = next(item for item in self.api_json("/api/v5/importlist/schema") if item["implementation"] == "PlexImport")
        resource.update(name="Generated Plex Watchlist", rootFolderPath="/media/library", qualityProfileId=self.profile_id,
                        language={"id": 1, "name": "English"}, enableAutomaticAdd=True, searchForMissingEpisodes=False,
                        shouldMonitor="none", seriesType="standard", seasonFolder=True)
        self.field(resource, "accessToken")["value"] = "generated-plex-token"
        definition = self.api_json("/api/v5/importlist", "POST", resource)
        self.idle()
        self.command({"name": "ImportListSync", "definitionId": definition["id"]})
        self.idle()
        saved = self.stored(definition)
        require(len(saved) == 1001 and [row[0] for row in saved] == list(range(970000, 971001)),
                "Plex Watchlist silently truncated entries beyond1000 or duplicated page identities")
        require(self.api_json("/api/v5/series") == [], "Plex membership synchronization bypassed global metadata exclusions")
        self.check("Plex Watchlist imports all1001 owned entries using validated raw offsets and complete provider totals")
        self.control(plexMalformed=True)
        self.command({"name": "ImportListSync", "definitionId": definition["id"]})
        self.idle()
        require(self.stored(definition) == saved, "A malformed later Plex page replaced complete membership")
        status, _ = self.raw("/api/v5/importlist/test", "POST", definition)
        require(status == 400, "Plex Test accepted a repeated nonterminal page")
        self.check("a malformed eleventh Plex page fails Test and retains all previously saved Watchlist entries")
        self.control(plexMalformed=False)
        before = self.api_json("/api/v5/importlist/" + str(definition["id"]))
        status, _ = self.raw("/api/v5/importlist/test", "POST", before)
        require(status == 204 and self.api_json("/api/v5/importlist/" + str(definition["id"])) == before,
                "Recovered Plex Test failed or saved the submitted form")
        self.check("Plex Watchlist recovers after a failed page without saving Test settings or contacting a real account")
        self.observations["PlexImport"] = {"storedCount": len(saved), "firstTvdbId": saved[0][0], "lastTvdbId": saved[-1][0],
                                            "failedLaterPageRetainedMembership": True, "unsavedTestPreservedSettings": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    audit = ListSets(args)
    error, logs, records, cleanup = None, "", [], False
    try:
        audit.start()
        audit.prepare()
        audit.prevent_metadata_acquisition()
        audit.exercise_provider("TmdbListImport", {"authToken": "generated-tmdb-token", "accountId": "fixture-account"},
                                "includedListIds", "excludedListIds", ["11", "12", "13"], ["listId", "accountListId"])
        audit.exercise_provider("TraktListImport", {"username": "fixture-user", "accessToken": "generated-trakt-token",
                                "refreshToken": "generated-trakt-refresh", "expires": "2099-01-01T00:00:00Z", "limit": 2,
                                "rating": "80-100", "genres": "animation", "years": "2024"},
                                "includedLists", "excludedLists", ["a", "b", "c"], ["listname"])
        audit.exercise_provider("MDBListImport", {"apiKey": "generated-mdblist-key"},
                                "includedListUrls", "excludedListUrls", ["https://mdblist.com/lists/fixture/" + value for value in ("a", "b", "c")], ["listUrl"])
        excluded = [record for record in audit.records() if record["host"] == "api.trakt.tv" and record["source"] == "c"]
        require(excluded and all(not set(record["queryKeys"]).intersection(("genres", "ratings", "years")) for record in excluded),
                "Exclusion membership was restricted by candidate content filters")
        audit.check("Trakt exclusion sources ignore candidate rating/year/genre filters, and the result limit applies after subtraction")
        audit.plex_pagination()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as caught:
        error = f"{audit.last_request}: {caught or type(caught).__name__}"
        print("FAIL " + error, file=sys.stderr)
    finally:
        try:
            if audit.root:
                records = audit.records()
            if audit.container:
                logs = audit.docker("logs", audit.container)
            audit.cleanup()
            cleanup = not args.keep
        except Exception as caught:
            error = error or "Fixture cleanup failed: " + str(caught)
        for secret in (audit.api_key, "generated-tmdb-token", "generated-trakt-token", "generated-trakt-refresh", "generated-mdblist-key", "generated-plex-token"):
            logs = logs.replace(secret, "[REDACTED]")
            if error:
                error = error.replace(secret, "[REDACTED]")
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({"createdUtc": _reliability._groups.utc_now(), "image": args.image, "imageId": audit.image_id,
                "sourceRevisionLabel": audit.image_labels.get("org.opencontainers.image.revision"), "runtime": audit.runtime_metadata,
                "passed": error is None, "checks": audit.results, "error": error, "observations": audit.observations,
                "fixtureRequests": records, "cleanupComplete": cleanup,
                "scope": {"trustedLocalProviderMocks": True, "liveProviderTested": False, "privateAccountUsed": False,
                          "realLibraryTested": False, "downloadsTested": False, "mainConfigurationAccessed": False,
                          "membershipSynchronizationEnabled": True, "metadataAcquisitionExcluded": True,
                          "databaseInspection": "read-only in the owned temporary fixture; synthetic global exclusions created through API",
                          "externalProviderRoutingEnabled": False, "tlsValidationDisabled": False}}, indent=2) + "\n", encoding="utf-8")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
