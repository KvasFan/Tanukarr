# Jellyfin watched status and list progress

Tanukarr 1.0.4 adds an optional watched-status reader to the existing dedicated
**Jellyfin** connection. It reads the selected Jellyfin user's state in one
library, records watched episodes locally and can unmonitor watched episodes.
An additional, separately enabled step advances supported source-account
progress for titles already present in an import list. Publication and executed
coverage are recorded in the [1.0.4 verification ledger](releases/1.0.4-verification.md).

## Configure watched synchronization

1. Open **Settings > Connect**, add or edit the dedicated **Jellyfin** connection
   and configure its host, port, API key and library. This is the same connection
   described in the [library-refresh guide](jellyfin.md).
2. Enable watched synchronization and select **one Jellyfin user**. The user's
   access to the chosen library is checked. Other users' playback state is not
   combined with this user's state.
3. Set the watched polling interval in minutes. The default is **5 minutes**;
   allowed values are **1 through 1,440**. The scheduler checks once a minute and
   polls when the configured interval has elapsed. Run **Sync Jellyfin Watched**
   under **System > Tasks** for an immediate poll.
4. Optionally enable **Unmonitor watched episodes**. Watched status and monitoring
   are separate: reading watched state alone does not change monitoring. The
   default is off. Optional **Remonitor unwatched episodes** restores monitoring
   only when this connection previously unmonitored that episode and its series
   and season are still monitored. It does not enable manually disabled episodes.
5. When Jellyfin and Tanukarr see different media paths, set both advanced path
   prefixes. Leave both empty when the paths are the same. Test and save the
   connection before enabling source-account writes.

Exactly one connection may enable watched synchronization. Additional enabled
connections cause the poll to retain the previous local state rather than mix
different users or targets. Connection tags limit the locally matched series.
Existing connections remain opted out after an upgrade.

Unmonitoring applies on a new played transition, or once when the option is
enabled for an already-watched episode. Subsequent manual monitoring edits are
preserved; an unchanged watched poll does not continually override them. Manual
unmonitor actions relinquish this connection's remonitoring ownership.

### Match physical media paths

The reader matches Jellyfin's physical file path to a registered Tanukarr
episode-file path. It does not guess from a title, series name, group folder or
another catalogue's numbering. This applies to TV, anime and movie members that
are represented by registered files and episodes in Tanukarr. A Jellyfin movie
without a matching local import is not added to the library.

For example, if Jellyfin sees `/library/tv/Example/Season 01/episode.mkv` and
Tanukarr sees `/media/tv/Example/Season 01/episode.mkv`, configure:

| Field | Value |
| --- | --- |
| Jellyfin path prefix | `/library/tv` |
| Tanukarr path prefix | `/media/tv` |

Both prefixes must be absolute paths without `..` segments. Translation matches
the directory boundary: `/library/tv` does not match `/library/tv-other`.
Ambiguous local file mappings are skipped. A file linked to several episodes
applies its played flag to those registered episodes; it cannot infer which
part of a combined video was watched.

Only Jellyfin's explicit **Played** flag is read. Starting playback or stopping
below Jellyfin's own played threshold does not establish that an episode was
watched. Mark the item played in Jellyfin when testing manually.

## Advance the originating list account

Enable list-progress synchronization in the Jellyfin connection, then enable
**Sync watched progress** in each supported import list that may receive writes.
Both controls default to off. Existing saved list membership must link the local
title to that list; merely adding a title manually does not establish its origin.
Each provider verifies the authenticated account before writing.
The import list must also be enabled with **Enable Automatic Add** on. Disabling
that list stops its subsequent account-write dispatches.

| List provider | Supported update and requirements |
| --- | --- |
| AniList | Existing list entry's episode `progress`, using valid OAuth for the same user configured on the import list. The local title must have the exact native AniList identity. |
| MyAnimeList | Existing own-account list entry's watched-episode count through the official MAL API and valid OAuth. The local title must have the exact native MAL identity. Jikan and public username-only imports cannot write. |
| Trakt User / Trakt List | Exact episode history for a native TheTVDB-owned series using official ordering, verified parent-show identity and the authenticated own account. Existing history is read before adding an episode. |
| SIMKL Watchlist | Explicit episode history through registered AUTH V2 with `media:write`. Enable the option and authorize again for the write scope. Matching native TheTVDB, AniList or MAL identities are required; an existing authenticated inventory entry must be present. Verified single-movie entries use the corresponding movie update. |
| TMDB lists, MDBList, Plex Watchlist and other public/foreign lists | No watched-account writeback is implemented. Their existing import features remain available. |

Progress uses a **continuous watched prefix**, starting at episode 1. Watching
episode 3 while episode 2 is unwatched does not advance a scalar counter to 3.
Specials, scene numbering and display-group season numbers do not contribute.
Custom orderings, missing native identity mappings and ambiguous episode gaps
are refused. AniList and MAL use the native single-season catalogue entry;
related seasons keep their separate provider identities.

AniList and MAL updates advance watched progress when it exceeds the remote
count observed during preflight. Their mutation payloads contain only progress;
ratings, custom-list membership and account list status are not requested edits.
Providers may apply their own status/preferences when accepting progress or
history; those server-side effects are outside the submitted Tanukarr payload.
Marking an episode unwatched in Jellyfin changes its local watched flag, but does
not request remote history removal or a progress decrement. Trakt checks history
again before retries and sends the episode's recorded watched timestamp in UTC,
using the current mark time only when that timestamp is unavailable. SIMKL uses
explicit episode updates without requesting a rewatch.

These provider APIs do not offer an atomic transaction spanning identity/list
preflight and mutation. Concurrent manual edits, another client's writes or a
list-entry deletion between those steps can race the guarded update. In
particular, MAL's update endpoint can recreate an entry deleted after preflight;
Trakt can acquire duplicate history from another simultaneous writer. The
implementation avoids lowering the remote progress it observed and avoids blind
history retries, while retaining these cross-service concurrency limitations.

The persistent queue stores pending and acknowledged progress, hashed target
identity and retry timing. It limits dispatch to **10 list/title targets per
minute**, uses bounded provider batches, and backs off on network errors or rate
limits. Local watched state remains saved when a provider write fails. Expired
tokens, foreign accounts or unavailable entries require corrected authorization;
they are not treated as successful remote updates. Polling an unchanged Jellyfin
snapshot still retries eligible queued writes. Disabling either write opt-in
prevents subsequent dispatches; it cannot cancel an already in-flight request
or undo previously confirmed remote state.

## API, persistence and privacy

Both `/api/v3/episode` and `/api/v5/episode` expose `watched` and, when present,
`watchedAtUtc`. These fields reflect the selected Jellyfin source; ordinary API
episode edits are not a way to assign a forged watched source. The private source
fingerprint and the record of which connection unmonitored an episode are not
episode API fields. Changing the configured user or target establishes a new
authority rather than reusing another user's monitoring ownership.

A complete, validated user/library snapshot is required before applying local
updates. Failed pages, missing played flags, conflicting states or changed
settings retain the previous state. A missing or unmatched remote item never
means that an existing local episode should become unwatched. Offline servers
retry on later ticks without erasing the last successful checkpoint.

Migrations **248** and **249** add local watched state and the list-progress
queue. They are included in the standard SQLite database backup; media files
are not. Preserve a consistent private configuration backup before upgrading.
Credentials remain in the connection/list settings. Queues and public episode
responses do not contain API tokens, Jellyfin usernames or raw HTTP bodies.

## Testing and source credit

The reproducible [Jellyfin watched fixture](../docker/tests/jellyfin-watched.py)
uses a disposable local Jellyfin server, generated media and an empty Tanukarr
configuration. It is separate from an owner's library and private provider
accounts. Provider response fixtures and a passing local played-state check do
not establish that an external account accepted a write. Current execution,
publication and security state belong to the
[release verification](releases/1.0.4-verification.md).

[Unmonitarr](https://github.com/baretsky/Unmonitarr) provided the requested
watched-to-monitoring concept. The reviewed source is Baretsky's
[`ec60a3084888e859e6c35fbac9891bcdfc011b95`](https://github.com/baretsky/Unmonitarr/commit/ec60a3084888e859e6c35fbac9891bcdfc011b95),
distributed under its [MIT license](https://github.com/baretsky/Unmonitarr/blob/ec60a3084888e859e6c35fbac9891bcdfc011b95/LICENSE).
Tanukarr's implementation uses authenticated polling and native-file matching;
no Unmonitarr source code is copied. Original Sonarr/provider authors and the
local **OpenAI Codex, based on GPT-6** implementation are credited in
[CONTRIBUTORS.md](../CONTRIBUTORS.md).

The provider contracts are documented in the official
[AniList mutation reference](https://github.com/AniList/docs/blob/master/docs/reference/mutation.md),
[MyAnimeList API v2](https://myanimelist.net/apiconfig/references/api/v2),
[Trakt sync contract](https://github.com/trakt/trakt-api/blob/master/projects/api/src/contracts/sync/index.ts)
and [SIMKL history API](https://api.simkl.org/api-reference/simkl/add-to-history).
