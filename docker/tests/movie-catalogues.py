#!/usr/bin/env python3
"""Exercise native movie catalogues and mixed collections through the packaged APIs."""

import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import secrets
import ssl
import subprocess
import sys
import threading
import urllib.parse

from smoke import require

_spec = importlib.util.spec_from_file_location("movie_catalogues_metadata", Path(__file__).with_name("metadata-sources.py"))
_metadata = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_metadata)
MOVIE_IDS = (970001, 970002)
COLLECTION_ID = 970000
COLLECTION_TITLE = "Generated Feature Collection"
OMDB_ID = "tt9700101"
ANIME_IDS = (970201, 970202, 970211, 970212)
TV_ID = 970301


def tmdb_movie(identity, revision=1):
    require(identity in MOVIE_IDS, "Unknown generated native movie")
    number = MOVIE_IDS.index(identity) + 1
    return {"id": identity, "title": "Generated Feature " + ("One" if number == 1 else "Two"),
            "release_date": "2019-01-01" if number == 1 else "2021-01-01", "runtime": 95 + number,
            "original_language": "en", "original_title": "Generated Original Feature " + str(number),
            "imdb_id": "tt" + str(9700000 + number), "status": "Released", "overview": "Generated movie revision " + str(revision),
            "vote_average": 8.1, "vote_count": 42, "genres": [{"id": 1, "name": "Drama"}],
            "belongs_to_collection": {"id": COLLECTION_ID, "name": COLLECTION_TITLE}}


def omdb_movie(identity=OMDB_ID, revision=1):
    require(identity == OMDB_ID, "Unknown generated IMDb movie")
    return {"Title": "Generated IMDb Feature", "Year": "2020", "Released": "01 Jan 2020", "Runtime": "103 min",
            "Type": "movie", "imdbID": identity, "Plot": "Generated OMDb revision " + str(revision), "Rated": "PG",
            "Genre": "Drama, Adventure", "imdbRating": "8.2", "imdbVotes": "12,345", "Response": "True", "Poster": "N/A"}


def anime_resource(identity):
    require(identity in ANIME_IDS, "Unknown generated anime")
    special = identity in ANIME_IDS[2:]
    title = ("Generated Standalone Special " if special else "Generated Anime Season ") + str(identity)
    return {"id": identity, "idMal": None, "type": "ANIME", "format": "OVA" if special else "TV",
            "title": {"english": title, "romaji": title, "userPreferred": title}, "episodes": 1,
            "duration": 24, "status": "FINISHED", "genres": [], "synonyms": [], "countryOfOrigin": "JP",
            "description": "Generated catalogue-owned member", "startDate": {"year": 2024, "month": 1, "day": 1},
            "endDate": {"year": 2024, "month": 1, "day": 1}, "coverImage": {}, "bannerImage": None,
            "studios": {"nodes": []}, "relations": {"edges": []}, "airingSchedule": {"nodes": [], "pageInfo": {"hasNextPage": False}}}


def serve_fixture(root):
    """Reusable HTTPS443 fixture; root contains server.crt/key and control.json."""
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            pass

        def answer(self, status, value, record):
            body = json.dumps(value).encode()
            with lock, (root / "requests.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(dict(record, status=status)) + "\n")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            host = self.headers.get("Host", "").split(":")[0]
            record = {"host": host, "path": parsed.path, "method": "GET"}
            try:
                control = json.loads((root / "control.json").read_text()) if (root / "control.json").exists() else {}
                revision = control.get("revision", 1)
                require(not self.headers.get("Authorization"), "Private credentials must not reach generated catalogues")
                if host == "api.themoviedb.org":
                    require(bool(query.get("api_key")), "TMDB requires the generated fixture key")
                    if parsed.path == "/3/search/movie":
                        record["kind"] = "tmdb-movie-search"
                        self.answer(200, {"results": [tmdb_movie(identity, revision) for identity in MOVIE_IDS]}, record)
                    elif parsed.path.startswith("/3/find/"):
                        require(query.get("external_source") == ["imdb_id"], "IMDb lookup must use the supported external-ID method")
                        imdb = parsed.path.rsplit("/", 1)[1]
                        identity = next(identity for identity in MOVIE_IDS if tmdb_movie(identity)["imdb_id"] == imdb)
                        record.update(kind="tmdb-imdb-find", nativeId=identity)
                        self.answer(200, {"movie_results": [tmdb_movie(identity, revision)], "tv_results": []}, record)
                    elif parsed.path.startswith("/3/movie/"):
                        identity = int(parsed.path.rsplit("/", 1)[1])
                        resource = tmdb_movie(identity, revision)
                        if control.get("malformed"):
                            resource["id"] = identity + 99
                        record.update(kind="tmdb-movie-detail", nativeId=identity)
                        self.answer(200, resource, record)
                    elif parsed.path == "/3/collection/" + str(COLLECTION_ID):
                        record["kind"] = "tmdb-movie-collection"
                        self.answer(200, {"id": COLLECTION_ID, "name": COLLECTION_TITLE,
                                          "parts": [tmdb_movie(identity, revision) for identity in reversed(MOVIE_IDS)]}, record)
                    else:
                        raise ValueError("Unexpected TMDB TV or telemetry route")
                elif host == "www.omdbapi.com":
                    require(query.get("apikey") and query.get("type") == ["movie"], "OMDb requires its own generated key and movie type")
                    record["kind"] = "omdb-movie"
                    if "s" in query:
                        self.answer(200, {"Response": "True", "Search": [omdb_movie()]}, record)
                    else:
                        require(query.get("i") == [OMDB_ID], "OMDb must retain the exact IMDb native ID")
                        self.answer(200, {"Response": "False"} if control.get("malformed") else omdb_movie(revision=revision), record)
                elif host == "api.tvmaze.com":
                    require(parsed.path.startswith("/shows/" + str(TV_ID)), "Unexpected native TV ID")
                    if parsed.path.endswith("/episodes"):
                        episodes = _metadata.episode_resources(TV_ID)
                        extra = dict(episodes[-1], id=TV_ID * 10 + 4, number=2, season=0)
                        extra["name"] = "Generated second native special"
                        self.answer(200, episodes + [extra], dict(record, kind="native-tv-episodes"))
                    else:
                        self.answer(200, _metadata.show_resource(TV_ID, revision), dict(record, kind="native-tv-show"))
                else:
                    raise ValueError("Unknown fixture catalogue")
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

        def do_POST(self):
            record = {"host": self.headers.get("Host", "").split(":")[0], "path": self.path, "method": "POST"}
            try:
                require(record["host"] == "graphql.anilist.co" and self.path == "/", "Unknown GraphQL fixture")
                length = int(self.headers.get("Content-Length", 0))
                require(0 < length < 65536 and not self.headers.get("Authorization"), "Unexpected private GraphQL request")
                body = json.loads(self.rfile.read(length))
                variables = body.get("variables", {})
                if "search" in variables:
                    identity = next(identity for identity in ANIME_IDS if anime_resource(identity)["title"]["english"] == variables["search"])
                    self.answer(200, {"data": {"Page": {"media": [anime_resource(identity)]}}}, dict(record, kind="anime-search", nativeId=identity))
                    return
                identity = int(variables.get("id", 0))
                resource = anime_resource(identity)
                record.update(kind="anime-native", nativeId=identity)
                self.answer(200, {"data": {"Media": resource}}, record)
            except Exception as error:
                self.answer(400, {"fixtureError": str(error)}, record)

    server = ThreadingHTTPServer(("0.0.0.0", 443), Handler)
    server.daemon_threads = True
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root / "server.crt", root / "server.key")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print("Local movie catalogues TLS fixture ready", flush=True)
    server.serve_forever()


class MovieCatalogues(_metadata.MetadataSources):
    fixture_script = "movie-catalogues.py"
    fixture_ready_message = "Local movie catalogues TLS fixture ready"
    fixture_hosts = ("api.themoviedb.org", "www.omdbapi.com", "graphql.anilist.co", "api.tvmaze.com")
    fixture_startup_check = "isolated trusted movie, anime and native TV catalogues with no public routing"

    def __init__(self, args):
        super().__init__(args)
        self.omdb_key = secrets.token_hex(16)
        self.private_config_values = []

    def create_tls(self):
        tls = super().create_tls()
        # This file belongs to the disposable instance and is written by start()
        # immediately before create_tls(). Enable Trace before the first launch,
        # including generated encryption defaults and subsequent provider keys.
        config = self.root / "config" / "config.xml"
        text = config.read_text(encoding="utf-8")
        require("<LogLevel>Debug</LogLevel>" in text, "Disposable logging configuration changed")
        config.write_text(text.replace("<LogLevel>Debug</LogLevel>",
                                      "<LogLevel>Trace</LogLevel><ConsoleLogLevel>Trace</ConsoleLogLevel>"), encoding="utf-8")
        return tls

    def verify_raw_logs(self):
        logging = self.api_json("/api/v3/config/host")
        require(logging["logLevel"].lower() == "trace" and logging["consoleLogLevel"].lower() == "trace",
                "Both application log sinks must be configured at Trace for the credential probe")
        values = self.read_database("SELECT Key, Value FROM Config WHERE lower(Key) IN ('hmacsalt', 'rijndaelsalt')")
        require({key.lower() for key, _value in values} == {"hmacsalt", "rijndaelsalt"}
                and all(value for _key, value in values), "Generated encryption configuration was not exercised")
        self.private_config_values = [value for _key, value in values]
        log_files = sorted((self.root / "config" / "logs").glob("*.txt"))
        require(log_files, "Application did not produce owned disk log evidence")
        disk = b"\n".join(path.read_bytes() for path in log_files)
        stdout = self.docker("logs", self.container).encode("utf-8")
        secrets_to_check = {"apiKey": self.api_key, "tmdbApiKey": self.metadata_key, "omdbApiKey": self.omdb_key,
                            **{key: value for key, value in values}}
        observations = {}
        self.observations["rawTraceLogProbe"] = {"traceEnabledBeforeStartup": True,
            "distinctProviderKeys": self.metadata_key != self.omdb_key, "encryptionDefaultsChecked": len(values),
            "diskLogFiles": len(log_files), "sinks": observations, "checkedBeforeRedaction": True}
        for name, content in (("disk", disk), ("stdout", stdout)):
            lowered = content.lower()
            write_markers = [b"writing setting to database. key:'" + key + b"'"
                             for key in (b"omdbapikey", b"tmdbapikey", b"hmacsalt", b"rijndaelsalt")]
            require(all(marker in lowered for marker in write_markers),
                    "Trace configuration writes were not observed in the " + name + " sink")
            leaks = []
            for label, secret in secrets_to_check.items():
                lines = [line.decode("utf-8", errors="replace") for line in content.splitlines() if secret.encode("utf-8") in line]
                if not lines:
                    continue
                contexts = []
                for line in lines[:2]:
                    for private_value in secrets_to_check.values():
                        line = line.replace(private_value, "[REDACTED]")
                    contexts.append(line[:512])
                leaks.append({"category": label, "occurrences": len(lines), "redactedContexts": contexts})
            observations[name] = {"byteCount": len(content), "sha256": hashlib.sha256(content).hexdigest(),
                                  "configurationWritesObserved": True, "generatedSecretsAbsent": not leaks,
                                  "leakedCategories": leaks}
            require(not leaks, "A raw " + name + " log contains a generated credential or encryption secret")
        self.check("raw application disk and stdout Trace logs omit distinct TMDB/OMDb keys, the API key and generated encryption salts before redaction")

    def exercise_encryption_defaults(self):
        before = self.read_database("SELECT Key FROM Config WHERE lower(Key) IN ('hmacsalt', 'rijndaelsalt')")
        # Saving the current UI resource uses SaveConfigDictionary, which reads
        # all configuration defaults, including the real generated private salts.
        settings = self.api_json("/api/v3/config/ui")
        self.api_json("/api/v3/config/ui/" + str(settings["id"]), "PUT", settings)
        self.observations["encryptionDefaultsPresentBeforeProbe"] = len(before)

    def setup_keys(self):
        for version in ("v3", "v5"):
            path = self.settings_path(version)
            settings = self.api_json(path)
            settings.update(tmdbApiKey=self.metadata_key, omdbApiKey=self.omdb_key,
                            disabledMetadataSources=["tvdb", "tmdb", "myAnimeList"])
            self.api_json(path + "/1", "PUT", settings)
            saved = self.api_json(path)
            require(saved["omdbApiKeyConfigured"] and saved["omdbApiKey"] == "********", "OMDb key was not masked")
            self.api_json(path + "/1", "PUT", saved)
            self.api_json(path + "/1", "PUT", {"id": 1})
            require(self.read_database("SELECT Value FROM Config WHERE lower(Key) = 'omdbapikey'")[0][0] == self.omdb_key,
                    "Masked or omitted OMDb API key changed its stored value")
            self.check(version + " masks OMDb credentials and preserves masked/omitted updates")

    def add_native(self, source, identity, version="v5", group=None, number=None, kind=None):
        prefix = {"tmdbMovie": "tmdbmovie:", "omdb": "imdb:", "tvMaze": "tvmaze:", "aniList": "anilist:"}[source]
        term = anime_resource(identity)["title"]["english"] if source == "aniList" else prefix + str(identity)
        query = urllib.parse.urlencode({"term": term, "metadataSource": source})
        lookup = self.api_json(f"/api/{version}/series/lookup?" + query)
        row = next(item for item in lookup if item["foreignId"] == str(identity) and item["metadataSource"] == source)
        row.pop("path", None)
        row.update(qualityProfileId=self.profile_id, rootFolderPath="/media/library", monitored=False, seasonFolder=False,
                   animeGroupingMode="automatic" if group is None else "manual", seriesType="standard",
                   addOptions={"monitor": "none", "searchForMissingEpisodes": False})
        if group:
            row.update(animeGroupId=group, animeGroupTitle=COLLECTION_TITLE, animeGroupKind="series", animeGroupMemberKind=kind)
            row[{"season": "animeGroupSeasonNumber", "movie": "animeGroupMovieNumber", "special": "animeGroupSpecialNumber"}[kind]] = number
        added = self.api_json(f"/api/{version}/series", "POST", row)
        self.until(lambda: bool(self.episodes(added)), "Provider-owned episodes did not initialize")
        return self.get(added)

    def catalogues(self):
        for version in ("v3", "v5"):
            query = urllib.parse.urlencode({"term": "imdb:tt9700001", "metadataSource": "tmdbMovie"})
            rows = self.api_json(f"/api/{version}/series/lookup?" + query)
            require(len(rows) == 1 and rows[0]["metadataSource"] == "tmdbMovie" and rows[0]["foreignId"] == "970001"
                    and rows[0]["mediaType"] == "movie" and rows[0]["tvdbId"] == 0, "IMDb find lost its movie namespace")
            self.check(version + " resolves explicit IMDb IDs through official TMDB movie results without TV substitution")
        for version, identity in zip(("v3", "v5"), MOVIE_IDS):
            movie = self.add_native("tmdbMovie", identity, version)
            require(movie["mediaType"] == "movie" and movie["tmdbId"] == identity and movie["foreignId"] == str(identity)
                    and movie["tvdbId"] < 0 and movie["animeGroupMovieNumber"] == MOVIE_IDS.index(identity) + 1,
                    "Movie addition lost native identity, media type or verified saga order")
            require(self.episode_identity(movie)[0][1] == str(identity), "Native movie feature ID was not persisted")
            self.series[identity] = movie
            self.check(version + " adds an ordinary native movie and one feature using actual provider JSON")
        group = self.series[MOVIE_IDS[0]]["animeGroupId"]
        imdb_movie = self.add_native("omdb", OMDB_ID, group=group, number=3, kind="movie")
        require(imdb_movie["foreignId"] == OMDB_ID and imdb_movie["imdbId"] == OMDB_ID and imdb_movie["tmdbId"] == 0
                and imdb_movie["tvdbId"] < 0, "OMDb IMDb identity was replaced by a TV or TMDB ID")
        self.series[OMDB_ID] = imdb_movie
        self.check("OMDb supplies complete feature metadata with an authoritative native IMDb ID")

    def mixed(self):
        group = self.series[MOVIE_IDS[0]]["animeGroupId"]
        tv = self.add_native("tvMaze", TV_ID, group=group, number=1, kind="season")
        require(sorted((row["seasonNumber"], row["episodeNumber"]) for row in self.episodes(tv)) == [(0, 1), (0, 2), (1, 1), (2, 1)],
                "Mixed grouping changed real native TV season/special numbering")
        for index, identity in enumerate(ANIME_IDS):
            special = index >= 2
            member = self.add_native("aniList", identity, group=group, number=index - 1 if special else index + 3,
                                     kind="special" if special else "season")
            require(member["mediaType"] == ("special" if special else "series"), "Provider media classification was lost")
            self.series[identity] = member
        self.series[TV_ID] = tv
        all_series = self.api_json("/api/v5/series")
        require(len(all_series) == 8 and len({row["path"] for row in all_series}) == 8, "Mixed member paths overlap")
        before = self.episode_identity(tv)
        conflict = dict(tv, animeGroupSeasonNumber=3)
        require(self.raw("/api/v5/series/" + str(tv["id"]), "PUT", conflict)[0] == 400,
                "Native multi-season member was allowed to overlap another logical season")
        require(self.episode_identity(tv) == before, "Rejected mixed grouping mutated native episodes")
        self.check("mixed catalogue collection keeps two native TV seasons, two anime entries, three films and independent standalone/native specials")

    def imports(self):
        movie = self.series[MOVIE_IDS[0]]
        destination = self.root / "media" / Path(movie["path"]).relative_to("/media")
        destination.mkdir(parents=True, exist_ok=True)
        path = destination / "Generated Feature One (2019) WEB-DL-720p.mkv"
        process = subprocess.run([self.args.ffmpeg, "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=1",
                                  "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000",
                                  "-t", "2", "-c:a", "aac", "-c:v", "libx264", "-preset", "ultrafast", "-y", str(path)], capture_output=True, text=True, timeout=60)
        require(process.returncode == 0 and path.is_file(), "Generated owned movie failed to encode")
        source_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        self.command({"name": "RescanSeries", "seriesId": movie["id"]})
        files = self.api_json("/api/v5/episodefile?seriesId=" + str(movie["id"]))
        require(len(files) == 1 and self.episodes(movie)[0]["episodeFileId"] == files[0]["id"], "Ordinary title/year movie filename did not import on scan")
        native_before = self.episode_identity(movie)
        self.command({"name": "RescanSeries", "seriesId": movie["id"]})
        require(self.episode_identity(movie) == native_before and self.api_json("/api/v5/episodefile/" + str(files[0]["id"]))["id"] == files[0]["id"],
                "Rescan changed the movie's native feature or file identity")
        require(hashlib.sha256(path.read_bytes()).hexdigest() == source_hash, "Movie scan changed generated media bytes")
        settings = self.api_json("/api/v5/settings/naming")
        settings["movieFormat"] = "{Series Title} ({Series Year}) {Quality Full}"
        self.api_json("/api/v5/settings/naming/1", "PUT", settings)
        example = self.api_json("/api/v5/settings/naming/examples")
        require(example.get("movieExample") and "S01E01" not in example["movieExample"] and "2010" in example["movieExample"],
                "Movie naming example still requires TV coordinates")
        self.observations["generatedMovie"] = {"sha256": source_hash, "episodeFileId": files[0]["id"], "nativeRows": native_before}
        self.check("actual generated title/year film imports and rescans with retained bytes/native identity and movie-specific naming")

    def refresh_movies(self):
        snapshots = {identity: (self.get(movie), self.episode_identity(movie)) for identity, movie in self.series.items() if identity in (*MOVIE_IDS, OMDB_ID)}
        self.control(revision=2)
        for identity, (movie, episodes) in snapshots.items():
            self.command({"name": "RefreshSeries", "seriesId": movie["id"]})
            saved = self.get(movie)
            require("revision 2" in saved["overview"] and self.episode_identity(movie) == episodes, "Movie refresh changed native identity or lost its new metadata")
        self.control(malformed=True, revision=3)
        for identity, (movie, episodes) in snapshots.items():
            command = self.api_json("/api/v5/command", "POST", {"name": "RefreshSeries", "seriesId": movie["id"]})
            self.until(lambda: self.api_json("/api/v5/command/" + str(command["id"]))["status"] in ("completed", "failed", "aborted"), "Malformed movie refresh did not end")
            require("revision 2" in self.get(movie)["overview"] and self.episode_identity(movie) == episodes,
                    "Malformed movie response overwrote existing metadata/episode rows")
        self.control(malformed=False)
        self.check("owning-provider refresh retains movie episode/file identity and malformed native responses preserve previous metadata")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--mock-image", default=_metadata._parent.MOCK_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--serve-fixture", type=Path, help=argparse.SUPPRESS)
    parser.set_defaults(anilist=False)
    args = parser.parse_args()
    if args.serve_fixture:
        serve_fixture(args.serve_fixture)
        return 0
    audit = MovieCatalogues(args)
    error, records, logs, cleanup = None, [], "", False

    def redact(value):
        for secret in (audit.api_key, audit.metadata_key, audit.omdb_key, *audit.private_config_values):
            value = value.replace(secret, "[REDACTED]")
        return value

    try:
        audit.start()
        audit.exercise_encryption_defaults()
        audit.prepare()
        audit.setup_keys()
        audit.catalogues()
        audit.mixed()
        audit.imports()
        audit.refresh_movies()
        audit.no_acquisition()
        audit.verify_raw_logs()
    except (Exception, KeyboardInterrupt) as caught:
        error = redact(f"{audit.last_request}: {caught or type(caught).__name__}")
        print("FAIL " + error, file=sys.stderr)
    finally:
        try:
            if audit.root:
                records = audit.records()
            if audit.container:
                # Keep the application's own log next to the report: a hosted failure such as
                # a SQLite error is otherwise unexplained once the container is removed.
                logs = redact(audit.docker("logs", audit.container))
        except Exception as caught:
            error = error or "Fixture request evidence failed: " + type(caught).__name__
        finally:
            try:
                audit.cleanup()
                cleanup = not args.keep
            except Exception as caught:
                error = error or "Cleanup failed: " + str(caught)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps({"createdUtc": _metadata._parent._groups.utc_now(), "image": args.image,
                "imageId": audit.image_id, "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"),
                "runtime": audit.runtime_metadata, "passed": error is None, "error": error, "checks": audit.results,
                "observations": audit.observations, "fixtureRequests": records, "cleanupComplete": cleanup,
                "keptData": str(audit.root) if args.keep else None,
                "scope": {"ownedGeneratedMedia": True, "publicProviderRequests": False, "realLibraryTested": False,
                          "privateAccountUsed": False, "acquisitionTested": False, "ownerConfigurationAccessed": False}}, indent=2) + "\n")
            args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
