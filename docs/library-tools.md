# Library tools

Library tools provide optional search, import, storage and access controls.
The automation pause, hardlink-only imports, external-ID checks and anime-pack
preference start disabled. Retaining replaced versions is enabled per show.
For feature provenance and contributor credits, see
[CONTRIBUTORS.md](../CONTRIBUTORS.md); the [documentation index](README.md)
links the integration and validation reports.

| What you want to do | Where to find it |
| --- | --- |
| Select a drive and combine anime, monitored or missing filters | Library: **Filter by path** and **Combine filters** |
| Pause automatic searches, RSS grabs and list additions | **System → Library tools → Pause automation**; use the banner to resume |
| Understand why a search skipped releases | Interactive search score details, and **System → Library tools → Recent searches** |
| Test all indexers and see individual failures | **Settings → Indexers → Test All** |
| Recover genuinely stalled downloads | **Settings → Download Clients → Options → Stalled download timeout**; zero disables recovery |
| Estimate extra space while torrents seed | **System → Library tools → Storage outlook** |
| Require hardlinks rather than falling back to a full copy | **System → Library tools → Hardlink-only imports** |
| Check registered folders and missing files | **System → Library tools → Check library** |
| Preview an import list and add selected shows | **System → Library tools → Preview import lists** |
| Restore a retained deleted file | **System → Library tools → Recycle bin**; choose its show and season |
| Create an expiring key for a companion app | **System → Library tools → Companion app keys** |
| Retain replaced versions and switch between them | A show's **Files, naming and retained versions** panel |
| Set automatic renaming independently for a show | The same show panel; **Preview Rename** still works manually |
| Set optional season folder titles | The same show panel; use `{Season Title}` and `{Season Year}` in Media Management's folder format |
| Preserve modification dates during upgrades | **Settings → Media Management → File Date → Preserve Original** |
| Check indexer show IDs or search anime packs first | Optional switches in **System → Library tools** |
| Search missing episodes in Calendar's displayed dates | Calendar's missing-episode search button |

## Imports and existing files

**Pause automation** stops new automatic searches, RSS grabs and import-list
additions. Manual searches and imports, current downloads and compression
continue. Resume from the banner or the same setting.

Original release titles and pack sizes remain attached to imported files, so
compression cannot turn a large release into a preferred small-pack release.
The database upgrade recovers this information only where grab/import history
identifies the exact series and file.

Manual episode selections survive download monitoring and restart. Multi-season
files retain their season boundaries; ambiguous files still require Manual
Import. Anime bundle matching requires explicit episode ranges, a complete aired
catalogue and an exact series or season alias. It does not guess from a similar
name.

Folder moves show a pending destination until the move succeeds. Failures retain
the original registered path and report a failed task. Renames handle colliding
names without overwriting existing files. Subtitle imports inspect subfolders
and preserve language variants, accessibility tags and subtitle labels.

## Retained versions and recycle-bin restore

Enable retention separately for each show. Future upgrades store previous video
files beneath that show's `Plex Versions` folder; they consume additional disk
space. The active file is the version Sonarr tracks. Switching versions retains
the previous active file and keeps the selected archive. Different episode
coverage requires Manual Import instead of an automatic switch.

Restores and version changes run as queued file operations and show their task
status. Existing destination files are never overwritten by recycle-bin
restore. Wait for compression on that show to finish before restoring or
switching versions. Compression itself does not create recycle-bin copies.

## Lists, rules and keys

TMDB account, discovery, list and person providers use a TMDB API Read Access
Token directly. Account lists also require the account ID in advanced settings.
Trakt smart lists use the existing Trakt connection; MDBList needs its list URL
and API key. New providers require configuration before use.

List previews identify the metadata provider, existing shows and import-list
exclusions. Selected additions use the chosen registered root folder and quality
profile, are unmonitored by default, and do not initiate missing-episode or
cutoff-unmet searches. Up to 25 shows can be added per selection. A preview expires
after ten minutes; refresh it before adding shows if it expires. Incomplete
provider results cannot be used to add shows.

### AniList lists

Configure AniList under **Settings → Import Lists**, authenticate with AniList
and enter the target username. The multiple-list inclusion and exclusion controls
are included in **0.1.0**; the immutable 0.0.1 release predates them. These filters
describe different things and are combined:

| Setting | What it selects |
| --- | --- |
| Import List Status | Enabled membership states: Watching, Planning, Completed, Dropped, Paused or Repeating |
| Import Media Release Status | The anime's release status: Finished, Releasing, Not Yet Released, Cancelled or Hiatus |
| Included Custom Lists | Membership in any of the named custom lists owned by the selected user |
| Custom List (Legacy) | The original single-name setting under Show Advanced, retained for saved connections; its name joins Included Custom Lists |
| Excluded Custom Lists | Membership in any named exclusion list vetoes the import, even when another filter includes the anime |
| Exclude List Status | Explicit exclusions for Watching, Planning, Completed, Dropped, Paused and Repeating; exclusion wins over the corresponding import checkbox |

Choose at least one membership status and one release status. Watching and
Planning, together with Finished and Releasing, are the defaults. For example,
**Completed** does not mean the anime has finished airing: choose **Finished**
as well if that is required. AniList gives an entry one watching state, while
the same entry can belong to several custom lists independently.

Included custom-list names are alternatives: matching any one is sufficient.
The original **Custom List (Legacy)** name participates in that same union, so an existing
connection keeps its restriction until that name is cleared. Leave both
inclusion controls empty to accept every entry matching the enabled membership
and release states. Named-list inclusion does not bypass either state filter.

Exclusions always take precedence. For example, enable **Import Watching** and
include `Seasonal`, then exclude `Do not download`: an anime marked Watching
and belonging to both custom lists is excluded. Explicitly checking
**Exclude Watching** also blocks Watching entries even when **Import Watching**
is checked. All explicit exclusions start disabled; excluded custom lists start
empty. Release-state filtering remains separate.

Enter each complete custom-list name and press **Enter** to add it. Repeat for
more names. Spaces, commas and other punctuation are part of the name, so
`Seasonal, favorites` is one name, not two. The editor trims name boundaries;
direct API array values retain their spelling, including boundary whitespace.

An exact custom-list name takes precedence. A case-insensitive fallback is
accepted only when it identifies a single list, so distinct names differing
only in case are not merged. Membership requires a `true` flag; a list name
whose flag is `false` does not match. If an excluded name has an ambiguous
case-insensitive match, use its exact spelling: the sync fails and retains the
previous successful list rather than guessing. Missing custom-list data while
custom-list exclusions are configured, pagination failures and GraphQL errors
also retain the previous successful list. An excluded AniList media ID cannot be reintroduced
by a duplicate accepted entry on another response page.

These filters apply to this saved AniList connection and its previews. They do
not create a global exclusion for another import-list connection or prevent
manual additions. Changing a filter does not directly delete an existing show
or file. The existing **Clean Library** policy remains a separate import-list
setting and still governs what happens after a successful sync removes entries
from the cached list.

AniList imports, previews, duplicate checks and exclusions use the native
`(metadataSource, foreignId)` identity. The AniList media ID and optional
MyAnimeList ID stay separate. AniList titles are not silently reassigned to a
TVDB parent show. A negative compatibility `tvdbId` on a native series is an
internal placeholder, not a real TVDB identifier.

Private-account OAuth synchronization requires your own credentials. Public
lookup and isolated browser checks do not verify that private-account workflow.
See the [AniList filter verification report](anilist-filters-verification-2026-10-13.md)
for the checks actually executed for these controls. The API distinctions above
follow AniList's [MediaList reference](https://docs.anilist.co/reference/object/medialist)
and [list-query guide](https://docs.anilist.co/guide/graphql/queries/media-list).

#### AniList request limits and custom-list failures

If the log says `Found ... reports from AniList List` and then fails with
`429:TooManyRequests` inside `AniListProxy.GetSeriesInfo`, the custom list was
read successfully. The failure occurred while fetching the anime metadata needed
to add those entries. Testing a list while its sync is running consumes the same
AniList request allowance.

The post-0.1.0 source correction shares request pacing and cooldown between list
pages, connection tests, searches and metadata refreshes. It spaces requests at
least 2.1 seconds apart, observes AniList's retry/reset headers and retries a
temporary HTTP 429 at most three total attempts. A prolonged cooldown or repeated
failure remains an explicit error rather than a successful empty import.
Initial synchronization of a large list can therefore take several minutes.
The limit is shared within one application instance; another application using
the same public IP can still exhaust AniList's server allowance.

The immutable `v0.1.0` release predates this correction. Use a build containing
the fix; repeated presses of **Test** or **Refresh** do not bypass the server
cooldown. A request-limit error alone does not indicate an invalid custom-list
name or a need to replace OAuth credentials. See the
[request-limit verification report](anilist-rate-limit-verification-2026-10-13.md)
and AniList's [official rate-limit documentation](https://github.com/AniList/docs/blob/master/docs/guide/rate-limiting.md).

### Custom-format groups

Custom-format conditions with alternative group zero retain the existing shared
logic. Positive groups are alternatives: shared conditions AND (group 1 OR group
2 …). Conditions within each group retain their existing required/optional
behavior. Episode-title exclusion is opt-in.

### Companion keys

Create a key under **System → Library tools → Companion app keys**. Choose a name,
**Read library** or **Read library and add shows**, and an expiry of 1–365 days.
The default expiry is 90 days; at most 50 keys can be stored. Copy the token when
created: it is shown once, and only its SHA-256 hash is stored. Revoke it from the
same panel when it is no longer needed.

The allowlist applies to both `/api/v3` and `/api/v5`:

| Permission | Allowed requests |
| --- | --- |
| Read library | `GET` for `series`, `episode`, `episodefile`, `calendar`, `tag`, `rootfolder` and `qualityprofile`, including supported positive numeric resource IDs |
| Read library and add shows | The same reads, plus `POST /api/v3/series` or `POST /api/v5/series` |

Adding a show with a companion key requires a registered `rootFolderPath` and
Sonarr's generated series folder. Omit `path`; leave
`addOptions.searchForMissingEpisodes` and
`addOptions.searchForCutoffUnmetEpisodes` false or absent. Custom paths and
automatic search requests are rejected before the show is added.

Companion keys cannot edit or delete existing shows, read settings or logs, use
lookup subroutes, run commands or start compression. Use the `X-Api-Key` request
header to send a key. The existing master API key retains full access.

General functional and security checks for the October 12 checkpoint, including
their limits, are recorded in the
[functional and security validation report](security-functional-validation-2026-10-12.md).
