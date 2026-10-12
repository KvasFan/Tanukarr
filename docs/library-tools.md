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
and enter the target username. These filters describe different things and are
combined:

| Setting | What it selects |
| --- | --- |
| Import List Status | Your membership status: Watching, Planning, Completed, Dropped, Paused or Repeating |
| Import Media Release Status | The anime's release status: Finished, Releasing, Not Yet Released, Cancelled or Hiatus |
| Custom List | Membership in one named custom list owned by the selected user; leave blank to use all matching entries |

Choose at least one membership status and one release status. Watching and
Planning, together with Finished and Releasing, are the defaults. For example,
**Completed** does not mean the anime has finished airing: choose **Finished**
as well if that is required. A named custom list further restricts those matches;
it does not replace either status filter.

Custom-list names are trimmed. An exact name takes precedence; a
case-insensitive match is accepted only when it identifies a single list, so
distinct names differing only in case are not merged. A missing membership flag
does not count as membership. Pagination or GraphQL failures retain the previous
successful list rather than treating the response as an empty list.

AniList imports, previews, duplicate checks and exclusions use the native
`(metadataSource, foreignId)` identity. The AniList media ID and optional
MyAnimeList ID stay separate. AniList titles are not silently reassigned to a
TVDB parent show. A negative compatibility `tvdbId` on a native series is an
internal placeholder, not a real TVDB identifier.

Private-account OAuth synchronization requires your own credentials. The public
lookup and isolated browser checks described in the validation report do not
verify that private-account workflow.

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

The latest executed checks and their limits are recorded in the
[functional and security validation report](security-functional-validation-2026-10-12.md).
