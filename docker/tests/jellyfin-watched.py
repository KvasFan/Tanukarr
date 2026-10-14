#!/usr/bin/env python3
"""Verify Jellyfin watched sync using generated videos and disposable accounts only."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request
import urllib.error

from jellyfin import JellyfinIntegration
from smoke import require


class JellyfinWatched(JellyfinIntegration):
    def __init__(self, args):
        super().__init__(args)
        self.selected_user = None
        self.other_user = None
        self.native_identities = []
        self.playback_observations = []
        self.watch_observations = []
        self.source_revision = None
        self.cleanup_confirmed = False
        self.provider_mock = None
        self.provider_mock_image_id = None
        self.provider_requests = []
        self.provider_observations = []

    def start_provider_mock(self):
        tls = self.root / "fixture"
        tls.mkdir(mode=0o700)
        (tls / "server.ext").write_text(
            "basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n"
            "extendedKeyUsage=serverAuth\nsubjectAltName=DNS:graphql.anilist.co\n", encoding="utf-8")
        commands = [
            [self.args.openssl, "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
             "-subj", "/CN=Tanukarr disposable watched CA", "-addext", "basicConstraints=critical,CA:TRUE",
             "-addext", "keyUsage=critical,keyCertSign,cRLSign", "-keyout", str(tls / "ca.key"), "-out", str(tls / "ca.crt")],
            [self.args.openssl, "req", "-new", "-newkey", "rsa:2048", "-nodes", "-subj", "/CN=graphql.anilist.co",
             "-keyout", str(tls / "server.key"), "-out", str(tls / "server.csr")],
            [self.args.openssl, "x509", "-req", "-days", "1", "-in", str(tls / "server.csr"),
             "-CA", str(tls / "ca.crt"), "-CAkey", str(tls / "ca.key"), "-CAcreateserial",
             "-extfile", str(tls / "server.ext"), "-out", str(tls / "server.crt")],
        ]
        for command in commands:
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            require(result.returncode == 0, "Could not generate the owned watched-progress certificate")
        (tls / "ca.key").chmod(0o600)
        (tls / "server.key").chmod(0o600)
        (tls / "ca.crt").chmod(0o644)
        self.provider_mock_image_id = self.docker("image", "inspect", "--format", "{{.Id}}", self.args.mock_image)
        self.provider_mock = self.docker("run", "--detach", "--network", self.network,
            "--network-alias", "graphql.anilist.co", "--label", "tanukarr.watched-progress-mock=true",
            "--env", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={Path(__file__).resolve().parent},target=/runner,readonly",
            "--mount", f"type=bind,source={tls},target=/fixture", self.args.mock_image,
            "python", "-u", "/runner/list-progress-mock.py", "--serve-fixture", "/fixture")
        self.until(lambda: "Local watched-progress provider TLS fixture ready" in self.docker("logs", self.provider_mock),
                   "Owned watched-progress TLS mock did not start")
        self.docker("cp", str(tls / "ca.crt"), self.container + ":/usr/local/share/ca-certificates/tanukarr-watched-fixture.crt")
        self.docker("exec", "--user", "0:0", self.container, "update-ca-certificates")
        networks = json.loads(self.docker("inspect", self.provider_mock))[0]["NetworkSettings"]["Networks"]
        address = next(network["IPAddress"] for network in networks.values() if network["NetworkID"] == self.network)
        require(address in self.docker("exec", self.container, "getent", "hosts", "graphql.anilist.co"),
                "Origin-account write routing escaped the owned fixture")
        require(not json.loads(self.docker("inspect", self.provider_mock))[0]["NetworkSettings"]["Ports"].get("443/tcp"),
                "Owned origin-progress mock was unexpectedly published to a host port")
        self.check("origin account writes use an owned certificate-validated AniList double without a public account or exposed mock port")

    def progress_state(self):
        path = self.root / "fixture" / "progress-state.json"
        return json.loads(path.read_text()) if path.exists() else {"anilist": 0, "mutations": []}

    @staticmethod
    def insert(db, table, values):
        for _, name, kind, required, default, primary in db.execute(f'PRAGMA table_info("{table}")'):
            if required and default is None and not primary and name not in values:
                values[name] = 0 if kind.upper() in ("INTEGER", "BOOLEAN") else ""
        columns = ",".join('"' + name + '"' for name in values)
        db.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({",".join("?" for _ in values)})', list(values.values()))

    def seed(self):
        self.start_provider_mock()
        labels = json.loads(self.docker("image", "inspect", "--format", "{{json .Config.Labels}}", self.args.image)) or {}
        self.source_revision = labels.get("org.opencontainers.image.revision")
        status = self.sonarr("/system/status", version="v5")
        self.runtime_metadata = {key: status[key] for key in (
            "appName", "productName", "productVersion", "version", "runtimeVersion", "migrationVersion") if key in status}
        require(status.get("migrationVersion", 0) >= 248, "Watched schema migration has not run")
        users = self.jf("/Users")
        self.selected_user = next(user["Id"] for user in users if user["Name"] == "isolated-jellyfin-audit")
        self.other_user = self.jf("/Users/New", "POST", {"Name": "other-disposable-watched-user"})["Id"]

        generated = {
            1: (1, 2, 1, "/media/Anime/Generated TV", "Season 02/Generated TV - S02E01.mkv"),
            2: (2, 1, 1, "/media/Anime/Generated Group", "Season 03/Generated Group - S03E01.mkv"),
            3: (2, 1, 2, "/media/Anime/Generated Group", "Season 03/Generated Group - S03E02.mkv"),
            4: (1, 2, 2, "/media/Anime/Generated TV", "Season 02/Generated TV - S02E02.mkv"),
            5: (3, 1, 1, "/media/Movies/Generated Saga", "Generated Film/Generated Film.mkv"),
        }
        movie_root = self.root / "media" / "Movies"
        movie_root.mkdir()
        movie_root.chmod(0o777)
        options = {"LibraryOptions": {"EnableRealtimeMonitor": False, "SaveLocalMetadata": False,
                   "MetadataSavers": [], "TypeOptions": [{"Type": "Movie", "MetadataFetchers": [], "ImageFetchers": []}]}}
        self.jf("/Library/VirtualFolders?" + urllib.parse.urlencode({"name": "Movies", "collectionType": "movies",
                "paths": "/media/Movies", "refreshLibrary": "false"}), "POST", options)
        self.libraries["Movies"] = next(item["ItemId"] for item in self.jf("/Library/VirtualFolders") if item["Name"] == "Movies")
        for _, _, _, parent, relative in generated.values():
            self.generate(parent + "/" + relative)
        self.jf("/Library/Refresh", "POST")
        self.until(lambda: self.count("Anime") == 5 and len(self.items("Movies")) == 1,
                   "Generated TV, grouped-anime and film scan failed", 150)

        self.sonarr("/rootfolder", "POST", {"path": "/media/Anime"})
        self.sonarr("/rootfolder", "POST", {"path": "/media/Movies"})
        profile = self.sonarr("/qualityprofile")[0]["id"]
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            for series_id, title, path, season, group_season in (
                    (1, "Generated TV", "/media/Anime/Generated TV", 2, None),
                    (2, "Generated Group Season 3", "/media/Anime/Generated Group", 1, 3),
                    (3, "Generated Film", "/media/Movies/Generated Saga", 1, None)):
                self.insert(db, "Series", dict(
                    Id=series_id, TvdbId=-(900000 + series_id) if series_id == 2 else 900000 + series_id,
                    Title=title, TitleSlug="isolated-watch-" + str(series_id),
                    CleanTitle=title.lower().replace(" ", ""), SortTitle=title.lower(), Path=path,
                    Images="[]", Monitored=1, SeasonFolder=1, Runtime=24, SeriesType=0, QualityProfileId=profile,
                    Tags="[]", Added="2026-10-14 00:00:00", Genres="[]", Actors="[]",
                    Seasons=json.dumps([{"seasonNumber": season, "monitored": True}]),
                    Ratings='{"votes":0,"value":0}', OriginalLanguage=1, Language=1,
                    MetadataSource=2 if series_id == 2 else 0,
                    ForeignId="424242" if series_id == 2 else str(900000 + series_id),
                    AniListIds="[424242]" if series_id == 2 else "[]", MalIds="[]", AlternateTitles="[]",
                    AnimeGroupId="fixture:group" if series_id == 2 else None,
                    AnimeGroupTitle="Generated Group" if series_id == 2 else None,
                    AnimeGroupSeasonNumber=group_season))
            for identity, (series_id, season, episode, parent, relative) in generated.items():
                physical = self.root / "media" / Path(parent).relative_to("/media") / relative
                self.insert(db, "EpisodeFiles", dict(
                    Id=identity, SeriesId=series_id, SeasonNumber=season, RelativePath=relative,
                    Size=physical.stat().st_size, DateAdded="2026-10-14 00:00:00",
                    Quality='{"quality":5,"revision":{"version":1}}', Languages="[1]"))
                self.insert(db, "Episodes", dict(
                    Id=identity, SeriesId=series_id, SeasonNumber=season, EpisodeNumber=episode,
                    Title="Generated watched fixture", EpisodeFileId=identity, Monitored=0 if identity == 4 else 1,
                    Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                    AirDate="2024-01-01", AirDateUtc="2024-01-01 00:00:00"))
            self.insert(db, "Episodes", dict(
                Id=6, SeriesId=2, SeasonNumber=1, EpisodeNumber=3, Title="Not imported generated episode",
                EpisodeFileId=0, Monitored=1, Runtime=24, Images="[]", Ratings='{"votes":0,"value":0}',
                AirDate="2024-01-01", AirDateUtc="2024-01-01 00:00:00"))
            self.insert(db, "ImportLists", dict(
                Id=1, Name="Generated watched origin", Implementation="AniListImport", ConfigContract="AniListSettings",
                EnableAutomaticAdd=1, SearchForMissingEpisodes=0, ShouldMonitor=0, MonitorNewItems=0,
                QualityProfileId=profile, SeriesType=1, Language=1, SeasonFolder=1, RootFolderPath="/media/Anime",
                Tags="[]", Settings=json.dumps({"baseUrl": "https://graphql.anilist.co",
                    "username": "generated-watched-owner", "accessToken": "generated-watched-access",
                    "refreshToken": "generated-watched-refresh", "expires": "2099-01-01T00:00:00Z",
                    "syncWatchedProgress": True, "importCurrent": True, "importPlanning": True,
                    "importReleasing": True, "importFinished": True})))
            self.insert(db, "ImportListItems", dict(
                Id=1, ImportListId=1, AniListId=424242, MalId=0, TvdbId=0, TmdbId=0, ImdbId="",
                Title="Generated Group Season 3"))
            db.execute("UPDATE ScheduledTasks SET LastExecution='2050-01-01 00:00:00', LastStartTime='2050-01-01 00:00:00'")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        self.native_identities = self.identities()
        require(self.native_identities == [(1, 1, 2, 1), (2, 2, 1, 1), (3, 2, 1, 2), (4, 1, 2, 2), (5, 3, 1, 1), (6, 2, 1, 3)],
                "Synthetic native episode identities changed during seeding")
        self.check("generated TV, grouped native Season 1 displayed in Season 03, and film match real Jellyfin physical imports")

    def items(self, library="Anime", user=None):
        query = urllib.parse.urlencode({"parentId": self.libraries[library], "recursive": "true",
                "includeItemTypes": "Episode,Movie", "fields": "Path", "enableUserData": "true"})
        return self.jf(f"/Users/{user or self.selected_user}/Items?" + query)["Items"]

    def find_item(self, suffix, library="Anime", user=None):
        matches = [item for item in self.items(library, user) if item.get("Path", "").endswith(suffix)]
        require(len(matches) == 1, "Generated Jellyfin path did not identify exactly one physical item")
        return matches[0]

    def identities(self):
        return sorted((episode["id"], episode["seriesId"], episode["seasonNumber"], episode["episodeNumber"])
                      for series_id in (1, 2, 3) for episode in self.sonarr("/episode?seriesId=" + str(series_id), version="v5"))

    def configure(self):
        self.definition = next(item for item in self.sonarr("/notification/schema") if item["implementation"] == "Jellyfin")
        self.definition.update(name="Disposable watched connection", onDownload=False, onUpgrade=False, tags=[])
        fields = {field["name"]: field for field in self.definition["fields"]}
        require(not fields["enableWatchedSync"].get("value") and not fields["unmonitorWatchedEpisodes"].get("value")
                and not fields["remonitorUnwatchedEpisodes"].get("value") and not fields["syncListProgress"].get("value"),
                "Watched synchronization or external account mutations were enabled by default")
        require(fields["watchedUserId"].get("selectOptionsProviderAction") == "users", "Selected-user discovery action missing")
        for name, value in {"host": "jellyfin-test", "port": 8096, "useSsl": False, "urlBase": "",
                            "apiKey": self.jellyfin_token, "libraryId": self.libraries["Anime"],
                            "enableWatchedSync": False, "watchedUserId": self.selected_user,
                            "watchedSyncIntervalMinutes": 1}.items():
            fields[name]["value"] = value
        users = self.sonarr("/notification/action/users", "POST", self.definition)["options"]
        require({option["value"] for option in users} == {self.selected_user, self.other_user}, "User discovery does not return actual isolated accounts")
        self.definition = self.sonarr("/notification", "POST", self.definition)
        self.set_field("apiKey", self.jellyfin_token)
        self.check("Connect discovers two actual users and keeps watch sync, monitoring changes and origin writes disabled by default")

    def save(self, **values):
        self.set_field("apiKey", self.jellyfin_token)
        for name, value in values.items():
            self.set_field(name, value)
        self.definition = self.sonarr("/notification/" + str(self.definition["id"]), "PUT", self.definition)
        self.set_field("apiKey", self.jellyfin_token)

    def episode(self, identity, version="v5"):
        return self.sonarr("/episode/" + str(identity), version=version)

    def watched_count(self, series):
        return sum(bool(episode.get("watched")) for episode in self.sonarr("/episode?seriesId=" + str(series), version="v5"))

    def play(self, suffix, played=True, library="Anime", user=None):
        user = user or self.selected_user
        before = self.find_item(suffix, library, user)
        self.jf(f"/Users/{user}/PlayedItems/{before['Id']}", "POST" if played else "DELETE")
        after = self.find_item(suffix, library, user)
        require(after["UserData"]["Played"] is played, "Jellyfin did not persist the actual user's played flag")
        if played:
            require(after["UserData"].get("PlayCount", 0) > before["UserData"].get("PlayCount", 0),
                    "Jellyfin playback count did not increment")
        self.playback_observations.append({"generatedItemId": before["Id"], "selectedUser": user == self.selected_user,
            "itemType": after["Type"], "beforePlayCount": before["UserData"].get("PlayCount", 0),
            "afterPlayCount": after["UserData"].get("PlayCount", 0), "played": played})

    def sync(self):
        self.command({"name": "SyncJellyfinWatched"})

    def browser(self):
        self.save(libraryId=self.libraries["Anime"], syncListProgress=False,
                  unmonitorWatchedEpisodes=False, remonitorUnwatchedEpisodes=False)
        self.sync()
        require(self.watched_count(2) == 2, "Owned browser fixture did not start with two watched anime episodes")
        username = "isolated-watched-browser"
        settings = self.sonarr("/settings/general", version="v5")
        settings.update(username=username, password=self.sonarr_password, passwordConfirmation=self.sonarr_password,
                        authenticationRequired="enabled")
        self.sonarr("/settings/general/1", "PUT", settings, version="v5")
        # The existing UI authorization middleware reads its initial requirement
        # at startup. Restart only this fixture before asserting a Forms challenge.
        self.docker("restart", self.container)
        self.refresh_binding()
        self.wait_ready()
        actual_auth = self.sonarr("/settings/general", version="v5")
        require(actual_auth["authenticationRequired"] == "enabled" and actual_auth["authenticationMethod"] == "forms",
                "Owned browser fixture did not persist required Forms authentication")

        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, request, response, code, message, headers, new_url):
                return None

        anonymous = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        try:
            with anonymous.open(self.base_url + "/", timeout=15) as response:
                status, location = response.status, response.headers.get("Location", "")
        except urllib.error.HTTPError as response:
            status, location = response.code, response.headers.get("Location", "")
        parsed_location = urllib.parse.urlsplit(location)
        same_origin = not parsed_location.netloc or parsed_location.netloc == urllib.parse.urlsplit(self.base_url).netloc
        require(status in (302, 303) and same_origin and parsed_location.path.startswith("/login"),
                f"Owned anonymous browser was not challenged for Forms login (HTTP {status}, path {parsed_location.path})")

        def native_path(path):
            if self.args.browser_node.lower().endswith(".exe") and sys.platform != "win32":
                return subprocess.check_output(["wslpath", "-w", str(path)], text=True).strip()
            return str(path)

        artifacts = self.args.browser_report_directory.resolve() if self.args.browser_report_directory else self.args.report.parent.resolve() / "watched-browser"
        artifacts.mkdir(parents=True, exist_ok=True)
        series = self.sonarr("/series/2", version="v5")
        payload = {"baseUrl": self.base_url, "apiKey": self.api_key, "username": username,
                   "password": self.sonarr_password, "playwrightModule": self.args.playwright_module,
                   "artifactDirectory": native_path(artifacts), "seriesId": 2, "titleSlug": series["titleSlug"],
                   "nativeSeasonNumber": 1, "expectedWatched": 2, "totalEpisodes": 3,
                   "jellyfinBaseUrl": self.jellyfin_url, "jellyfinToken": self.jellyfin_token,
                   "jellyfinItemId": self.find_item("Generated Group - S03E02.mkv")["Id"],
                   "selectedUserId": self.selected_user, "ownedFixture": True, "imageId": self.image_id}
        self.last_request = "Owned Chromium watched-count verification"
        result = subprocess.run([self.args.browser_node, native_path(Path(__file__).with_name("ui-jellyfin-watched.cjs"))],
                                input=json.dumps(payload), capture_output=True, text=True, encoding="utf-8", timeout=240)
        output = result.stdout + result.stderr
        for secret in (self.api_key, self.jellyfin_token, self.jellyfin_password, self.sonarr_password):
            output = output.replace(secret, "[REDACTED]")
        print(output.strip(), flush=True)
        require(result.returncode == 0, "Live browser watched-counter verification failed; see the separate browser report")
        require(self.watched_count(2) == 2, "Browser test did not restore its own generated watched state")

    def verify(self):
        first = "Generated Group - S03E01.mkv"
        second = "Generated Group - S03E02.mkv"
        self.play(first)
        self.sync()
        require(self.watched_count(2) == 0, "Disabled watched sync changed local state")
        self.check("real Jellyfin played counter increments while disabled synchronization preserves Tanukarr state")

        self.save(enableWatchedSync=True)
        self.sync()
        require(self.watched_count(2) == 1 and self.episode(2)["monitored"], "First actual watched episode did not increment the Tanukarr count or changed monitoring")
        require(self.episode(2, "v3")["watched"] and self.episode(2)["watchedAtUtc"], "v3/v5 watched fields disagree")
        require(self.identities() == self.native_identities, "Watched sync rewrote native seasons or episode IDs")
        self.watch_observations.append({"seriesId": 2, "watchedCount": 1, "nativeSeasonsUnchanged": True})
        self.check("actual Jellyfin watch updates v3/v5 watched count 0 to 1 while preserving native grouped Season 1 and monitoring")
        original_watched_at = self.episode(2)["watchedAtUtc"]
        for version in ("v3", "v5"):
            forged = self.episode(2, version)
            require("watchedSource" not in forged and "jellyfinUnmonitorOwner" not in forged,
                    "Private source or monitoring ownership leaked into the public episode DTO")
            forged.update(watched=False, watchedAtUtc="2099-01-01T00:00:00Z")
            self.sonarr("/episode/2", "PUT", forged, version=version)
            actual = self.episode(2, version)
            require(actual["watched"] and actual["watchedAtUtc"] == original_watched_at,
                    "A manual monitor endpoint overwrote read-only watched progress or its actual timestamp")
        self.check("v3/v5 monitoring API rejects forged watch state by preserving actual watched flags and timestamps; source ownership stays private")
        require(self.progress_state()["anilist"] == 0, "Origin progress advanced without the global write-back opt-in")
        self.check("per-list authorization alone does not mutate origin progress while Jellyfin origin write-back is disabled")

        self.play(second, user=self.other_user)
        self.sync()
        require(self.watched_count(2) == 1 and not self.episode(3)["watched"], "Another user's played flag contaminated selected-user progress")
        self.check("another Jellyfin user's actual played flag is excluded from the selected user's Tanukarr progress")

        self.save(syncListProgress=True)
        self.sync()
        self.until(lambda: self.progress_state()["anilist"] == 1,
                   "Real Jellyfin watched episode did not produce the owned AniList progress mutation", 45)
        self.provider_observations.append({"provider": "owned AniList TLS double", "progress": 1})
        self.check("real Jellyfin played episode advances only the verified existing origin-list entry to progress 1 via captured GraphQL")
        self.play(second)
        self.sync()
        require(self.watched_count(2) == 2, "Second actual watched episode did not increment Tanukarr progress")
        before = self.episode(2)
        self.sync()
        require(self.watched_count(2) == 2 and self.episode(2)["watchedAtUtc"] == before["watchedAtUtc"], "Repeated poll duplicated or rewrote watched progress")
        self.watch_observations.append({"seriesId": 2, "watchedCount": 2, "idempotent": True})
        self.check("selected user's second real watched item advances Tanukarr count 1 to 2 and repeat polls are idempotent")
        self.until(lambda: self.progress_state()["anilist"] == 2, "Owned origin-list progress did not advance to 2", 45)
        require(self.progress_state()["mutations"] == [{"provider": "anilist", "progress": 1}, {"provider": "anilist", "progress": 2}],
                "Idempotent watch sync duplicated or lowered origin mutations")
        self.provider_observations.append({"provider": "owned AniList TLS double", "progress": 2, "idempotent": True})
        self.check("second actual watched episode advances captured origin GraphQL progress 1 to 2 without duplicate mutation on repeated polls")

        self.docker("restart", self.container)
        self.refresh_binding()
        self.wait_ready()
        require(self.watched_count(2) == 2, "Watched state was lost at restart")
        self.check("actual watched flags and timestamps survive a Docker restart")

        self.docker("stop", self.jellyfin)
        self.sync()
        require(self.watched_count(2) == 2 and self.episode(2)["watchedAtUtc"] == before["watchedAtUtc"],
                "An actual Jellyfin outage discarded stored watched state")
        self.docker("start", self.jellyfin)
        self.jellyfin_url = "http://127.0.0.1:" + self.docker("port", self.jellyfin, "8096/tcp").rsplit(":", 1)[1]

        def jellyfin_ready():
            try:
                return self.json_request(self.jellyfin_url, "/Library/VirtualFolders", token=self.jellyfin_token)[0] == 200
            except OSError:
                return False

        self.until(jellyfin_ready, "Owned Jellyfin restart after outage did not become ready", 90)
        self.sync()
        require(self.watched_count(2) == 2, "Watch retry changed persisted progress after Jellyfin recovered")
        self.check("real Jellyfin outage retains local watched history and a subsequent successful poll retries safely")

        self.save(unmonitorWatchedEpisodes=True, remonitorUnwatchedEpisodes=True)
        self.sync()
        require(not self.episode(2)["monitored"] and not self.episode(3)["monitored"], "Enabled watch policy did not unmonitor watched episodes")
        self.play(second, played=False)
        self.sync()
        require(not self.episode(3)["watched"] and self.episode(3)["monitored"], "Integration-owned unmonitor did not reverse after actual unwatch")
        require(self.progress_state()["anilist"] == 2, "Unwatch lowered progress on the origin account")
        self.check("opt-in unmonitor watches and re-monitor restores only the integration's own previously monitored episode")

        self.play("Generated TV - S02E02.mkv")
        self.sync()
        self.play("Generated TV - S02E02.mkv", played=False)
        self.sync()
        require(not self.episode(4)["monitored"], "Integration re-monitored an episode manually unmonitored before sync")
        self.check("manually unmonitored native TV episode stays unmonitored after real watch and unwatch")

        self.play(second)
        self.sync()
        manually_disabled = self.episode(3)
        manually_disabled["monitored"] = False
        self.sonarr("/episode/3", "PUT", manually_disabled, version="v5")
        self.play(second, played=False)
        self.sync()
        require(not self.episode(3)["monitored"], "Repeating manual unmonitor failed to override prior integration ownership")
        self.check("manual unmonitor of an already unmonitored watched episode revokes integration re-monitor ownership")

        manually_enabled = self.episode(2)
        manually_enabled["monitored"] = True
        self.sonarr("/episode/2", "PUT", manually_enabled, version="v5")
        self.sync()
        require(self.episode(2)["watched"] and self.episode(2)["monitored"],
                "An unchanged remote watched flag overwrote a later manual monitor choice")
        self.check("manual re-monitor of an already watched episode survives unchanged real Jellyfin polls")

        # A real scheduled tick, rather than another manual command, must apply the
        # next user-data change. Move only this disposable task's cadence to now.
        self.save(unmonitorWatchedEpisodes=False, remonitorUnwatchedEpisodes=False)
        self.sync()
        self.play(second)
        self.docker("stop", self.container)
        self.prepare_config_for_host_sqlite()
        with sqlite3.connect(self.root / "config" / "sonarr.db") as db:
            db.execute("UPDATE ScheduledTasks SET LastExecution=?, LastStartTime=? WHERE TypeName=?",
                       (datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S"),
                        datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S"),
                        "NzbDrone.Core.Notifications.Jellyfin.SyncJellyfinWatchedCommand"))
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        self.until(lambda: self.watched_count(2) == 2, "Actual one-minute watched tick did not increment Tanukarr progress", 150)
        self.check("actual one-minute scheduled Jellyfin poll discovers new played state without a manual sync")

        # Explicitly switch the only watched connection to a separately initialized
        # movie library and prove the same exact-path mechanism accepts Movie items.
        film = self.find_item("Generated Film.mkv", "Movies")
        require(film["Type"] == "Movie", "Generated film did not enter the actual Jellyfin movie library")
        self.play("Generated Film.mkv", library="Movies")
        self.save(libraryId=self.libraries["Movies"])
        self.sync()
        require(self.episode(5)["watched"] and self.episode(5)["monitored"], "Actual movie watch did not map to the native film import")
        require(self.identities() == self.native_identities, "Movie sync changed native episode numbering")
        self.check("real Jellyfin Movie watch maps to the exact native film import without renumbering or monitoring changes")

        # A reachable but revoked key produces an HTTP failure; no old state is
        # cleared, and replacing the key restores the normal manual retry path.
        self.set_field("apiKey", "definitely-invalid-fixture-key")
        status, _ = self.json_request(self.base_url, "/api/v3/notification/test", "POST", self.definition, self.api_key)
        require(status == 400, "Revoked Jellyfin credentials passed the connection test")
        self.set_field("apiKey", self.jellyfin_token)
        require(self.episode(5)["watched"] and self.watched_count(2) == 2, "Failed connection test cleared historical watch state")
        self.check("revoked credential connection test fails safely without clearing saved TV, anime or film watched state")

        require(self.sonarr("/downloadclient") == [] and self.sonarr("/queue")["totalRecords"] == 0,
                "Watch integration configured acquisition or queued a download")
        require(len(self.sonarr("/importlist")) == 1 and self.progress_state()["anilist"] == 2,
                "Generated origin-list membership or advance-only progress changed unexpectedly")
        records = self.root / "fixture" / "progress-requests.jsonl"
        self.provider_requests = [json.loads(line) for line in records.read_text().splitlines() if line]
        require(self.provider_requests and all(record["provider"] == "graphql.anilist.co" and record["path"] == "/"
                and record["method"] == "POST" for record in self.provider_requests), "Origin write traffic escaped the owned fixture contract")
        require(self.count("Untouched") == 1 and self.identities() == self.native_identities,
                "Watch integration altered an unselected library or native episode identity")
        self.check("no download clients, queue entries, real origin account mutations, owner configuration access or unselected library changes")

    def cleanup(self):
        if self.args.keep:
            super().cleanup()
            return
        if self.provider_mock:
            self.docker("rm", "--force", self.provider_mock)
            self.provider_mock = None
        # Jellyfin runs as root in its image. Return only our guarded disposable
        # tree to the host UID before the shared fixture cleanup removes it.
        if self.root and os.name == "posix":
            owned = self.root.resolve()
            require(owned == self.root and owned.parent == Path(tempfile.gettempdir()).resolve()
                    and owned.name.startswith("sonarr-container-smoke-") and not owned.is_symlink(),
                    "Refusing ownership recovery outside this fixture's temporary tree")
            if self.jellyfin:
                self.docker("rm", "--force", self.jellyfin)
                self.jellyfin = None
            if self.container and self.running():
                self.docker("stop", self.container)
            self.docker("run", "--rm", "--network", "none", "--user", "0:0",
                "--label", "sonarr.watched-fixture-ownership=true", "--entrypoint", "/bin/chown",
                "--mount", f"type=bind,source={owned},target=/owned", self.args.image,
                "-R", "--no-dereference", f"{os.getuid()}:{os.getgid()}", "/owned")
        super().cleanup()
        self.cleanup_confirmed = self.container is None and self.root is None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    parser.add_argument("--jellyfin-image", default="jellyfin/jellyfin:12.2")
    parser.add_argument("--mock-image", default="python:3.13-alpine")
    parser.add_argument("--openssl", default="openssl")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--browser-node")
    parser.add_argument("--playwright-module")
    parser.add_argument("--browser-report-directory", type=Path)
    parser.set_defaults(anilist=False, browser_only=False)
    args = parser.parse_args()
    require(args.timeout > 0, "Timeout must be positive")
    require(bool(args.browser_node) == bool(args.playwright_module), "Provide both optional browser arguments together")
    require(not args.report.exists(), "Use a fresh report destination; previous test evidence is retained")
    audit = JellyfinWatched(args)
    error = None
    try:
        audit.start_servers()
        audit.seed()
        audit.configure()
        audit.verify()
        if args.browser_node:
            audit.browser()
    except (Exception, KeyboardInterrupt) as failure:
        error = audit.last_request + ": " + (str(failure) or type(failure).__name__)
        for secret in (audit.api_key, audit.jellyfin_token, audit.jellyfin_password, audit.sonarr_password):
            if secret:
                error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, flush=True)
    finally:
        try:
            audit.cleanup()
        except (Exception, KeyboardInterrupt) as failure:
            error = error or "Owned fixture cleanup failed: " + (str(failure) or type(failure).__name__)
            print("FAIL cleanup: " + error, flush=True)
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({
            "createdUtc": datetime.now(timezone.utc).isoformat(), "image": args.image, "imageId": audit.image_id,
            "sourceRevision": audit.source_revision, "runtime": audit.runtime_metadata,
            "jellyfinImage": args.jellyfin_image, "passed": error is None, "error": error,
            "checks": audit.results, "cleanupConfirmed": audit.cleanup_confirmed,
            "nativeEpisodeIdentities": audit.native_identities,
            "jellyfinPlaybackObservations": audit.playback_observations,
            "tanukarrWatchedObservations": audit.watch_observations,
            "originProgressObservations": audit.provider_observations,
            "originProgressRequests": audit.provider_requests,
            "providerMockImage": args.mock_image, "providerMockImageId": audit.provider_mock_image_id,
            "fixtureConfigOwnership": audit.fixture_config_ownership,
            "scope": {"realJellyfinTested": bool(audit.playback_observations), "generatedMediaOnly": True, "disposableUsersOnly": True,
                      "selectedSingleUser": True, "ownerConfigurationAccessed": False, "externalAccountMutations": False,
                      "originAccountApiTested": "owned TLS AniList double only, not a public account",
                      "nativeTvAnimeMovieImportsTested": error is None, "tanukarrImportedFileRowsSeededDirectly": True,
                      "downloadsStarted": False},
        }, indent=2) + "\n", encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())
