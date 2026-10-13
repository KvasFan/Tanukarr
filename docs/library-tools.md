# Library tools

Library tools provide optional search, import, storage and access controls.
Shared controls serve TV series and anime; options tied to AniList or anime-pack
searches retain their provider/content scope. Choose metadata per title using
the [metadata provider guide](metadata-providers.md).
The automation pause, hardlink-only imports, external-ID checks and anime-pack
preference start disabled. Retaining replaced versions is enabled per show.
For feature provenance and contributor credits, see
[CONTRIBUTORS.md](../CONTRIBUTORS.md); the [documentation index](README.md)
links the integration and validation reports.

| What you want to do | Where to find it |
| --- | --- |
| Select a drive and combine anime, monitored or missing filters | Library: **Filter by path** and **Combine filters** |
| Present related TV/anime entries as one title with seasons and movies | Add/edit a title's grouping controls; library toolbar **Group anime seasons**; see [Series, anime and movie groups](anime-season-groups.md) for 1.0.1 folders, naming and manual overrides |
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

[Anime season and movie groups](anime-season-groups.md) retain each member's native
provider identity, episodes and filenames. Newly generated member paths can
use a canonical root with a configured season, strict story-arc or movie leaf;
assigning a group to an existing series keeps
its explicit registered path. Use the normal series path/move workflow to
arrange an existing library deliberately. A group season is a display/folder
position, not an episode-number remapping for import matching or Jellyfin.

The five group/member folder expressions and optional group filename tokens
are under **Settings > Media Management > Episode Naming**. Existing stored
leaves do not change when a format changes. Native episode tokens stay in
filenames; a grouped movie can still use `S01E01` inside its movie-title or
`Film NN` folder. See the linked guide for manual style overrides and collision
checks.

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

## Nyaa searches

Add the native **Nyaa** indexer under **Settings → Indexers** and set its website
URL to `https://nyaa.si`. Enable interactive search to use the release-search
window. Anime series use absolute episode-number searches, including both `9`
and `09` for single-digit episodes. **Anime Standard Format Search** adds
`SxxExx` searches and permits season searches when no season-specific title alias
exists. Complete-series searches can query the series title without that option.

The default additional parameters are `&cats=1_0&filter=1`. Nyaa still accepts
these legacy names, alongside modern `c`, `f` and `q` parameters. Avoid a fixed
`q` or `term` in Additional Parameters when Tanukarr should supply each search
title: a fixed query can override the generated term. Tanukarr 1.0.0 includes the
correction that escapes reserved title characters such as `&`, `#` and literal `+`
while preserving configured category/filter values and special-episode word
separators. The immutable historical 0.1.0 release predates this correction.

An HTTP/RSS connection test and a release search only retrieve metadata.
Acquisition remains a separate operation. Search results can be present but
rejected by the quality profile, seed requirement, episode matching or other
configured rules. See the [Nyaa verification report](nyaa-verification-2026-10-13.md)
for the queries and checks actually executed.

Post-release `main` adds **Search Season Packs** on a native season and grouped
season entry. It uses the member's native series ID/season number and filters
interactive results to packs; regular interactive search still shows individual
episodes. A logical group season does not replace the native search number.
The owner's earlier deployed-image `Baki-dou` search-only checkpoint returned
11 complete-series releases in approximately 3.16 seconds and 17 episode-search
releases in approximately 2.27 seconds. Those RSS snapshots predate the new
pack-only filter; they did not acquire media or verify the later grouping build.

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

### Provider capabilities

Choose **Settings > Import Lists > Add Import List** and configure the selected
service. Catalogue search credentials and account-list credentials can differ.
The list editor groups account information, filters and list/tag selections,
while retaining manual input and the selected provider's available fields.
Use Test to check the current form, then Save when the selection is correct;
loading choices or testing does not itself save or start synchronization.

| Import-list provider | Included/excluded selection | Other implemented filters and access |
| --- | --- | --- |
| AniList | Native custom-list names; any included match selects, any excluded match vetoes | Six account states and five media release states; configured OAuth for account-list import |
| MyAnimeList | Exact account-list tags, not AniList-style custom lists; any excluded tag vetoes | Multiple official account states, three airing states and minimum score; registered Client ID for a named public user or direct OAuth for `@me`/private access |
| TMDB List | Positive list IDs; selected included lists are combined, excluded lists are subtracted | TMDB import-list Read Access Token; Account ID/list-name loading where supported by this provider |
| Trakt List | List slugs or IDs for the configured username; combined inclusions minus exclusions | Existing Trakt credentials and filters; list-name loading where available |
| MDBList | Complete validated HTTPS list URLs; combined inclusions minus exclusions | Configured MDBList API key; manual URL entry |
| SIMKL User | Existing selected Shows/Anime Watchlist bucket; optional V2 custom-list IDs restrict/subtract membership | V2 custom lists are a read-only beta requiring a separately registered AUTH V2 app and PRO/VIP account; V1 Watchlist behavior remains when this option is unset |
| Plex | Existing TV Watchlist | Plex authentication; named Plex Lists/library collections and account-status/custom-tag selectors are not implemented by this provider |

AniList/MAL empty included **membership filters** select all otherwise eligible
entries from that account. TMDB List, Trakt List and MDBList require at least one
included **source**; an empty selector does not mean every list in an account.
Older saved single-source settings are transferred into the corresponding
included selection rather than discarded. Exclusions use list membership before
optional content filters, so a filtered-out copy in an excluded list cannot
accidentally permit the same title from an included list.
Positive external-ID aliases are resolved together, so a TVDB/TMDB alias for the
same title cannot bypass an exclusion through a different supplied ID. Titles
are not used to guess membership.

These capabilities belong to the named provider types. Other account, discovery,
person, collection or RSS providers keep their own controls; they are not assumed
to expose custom lists or tag filters. Per-connection import-list selections are
also separate from Tanukarr's existing global import-list exclusion records.
Unknown required data, incomplete pages or a failed selected source retains the
previous successful list rather than authorizing partial replacement/cleanup.
The [1.0.2 verification report](releases/1.0.2-verification.md) distinguishes
source audit, disposable provider checks and any live account operation.

SIMKL's optional custom-list selection starts from its configured Watchlist
bucket: intersect any included custom lists when configured, then subtract
excluded membership. It does not reinterpret an item's `watching` aggregate as
the user's account status. Movie custom-list entries are ignored by this
TV/anime importer. Beta/authentication/permission/premium errors, including an
HTTP 200 `premium_only` body, or incomplete pagination retain the prior list.
V2 import first reads a complete all-status inventory, then uses validated
activity cursors and `date_from` deltas. Native-ID reconciliation handles
deletions. Its private inventory/cursor survives restart inside the configuration
cache; Test stages reads without saving that snapshot or provider settings.
The [official custom-list guide](https://api.simkl.org/guides/custom-lists)
describes the V2/PRO/VIP requirements; the older status-conventions page's
custom-list statement is superseded by this beta contract. No live premium
account verification is inferred from the documentation or synthetic checks.

Enter a separately registered AUTH V2 Client ID for optional SIMKL custom lists
and authorize that application. Its optional client secret follows the
registration's requirements; use the instance's `oauth.html` callback including
the URL base. Choose Shows or Anime and the account Watchlist status as before,
then enter positive included/excluded custom-list IDs or load supported name
choices. Keep those V2/custom selections empty to retain the existing V1 path.

The inherited Plex reader stopped after ten 100-entry pages. Version 1.0.2
instead validates body offsets/raw sizes and any pagination headers, follows
the actual returned raw count, and confirms completion from a consistent total
or a terminal empty raw page. Short pages or non-TV rows do not falsely end
pagination. The 1,000-page safety bound fails rather than silently accepting a
partial list. This preserves the TV Watchlist scope and does not add named Plex
Lists. The [official pagination reference](https://developer.plex.tv/pms/)
documents returned offsets/sizes and optional totals; new execution evidence
belongs to the release report. Sonarr remote, generic JSON and RSS
providers keep their actual remote-series/tag/profile/root-folder or feed
selectors; they do not expose invented AniList-style account membership.

### MyAnimeList account lists and Jikan metadata

Select **MyAnimeList** as an import-list provider to read its official account
API. For a named public user, enter that username and your registered
application's Client ID. For your own or private account data, use `@me`, enter
the registered Client ID and select **Authenticate with MyAnimeList**. A Client
Secret is optional and is needed only when the registered application requires
one. Register the callback for this instance, including its URL base:
`http://localhost:6970/oauth.html` for the default local address. Use the actual
origin/base path for a differently hosted instance.

The direct OAuth flow uses PKCE and the application's registered credentials;
it does not use Sonarr's cloud broker. Older saved bearer credentials remain
usable while valid. Renewal/new authorization needs the registered Client ID;
follow the connection's reconnect guidance rather than removing the saved list.
Account states, minimum score and catalogue release states apply before tag
selection. Enter each complete tag name and press Enter; names match exactly.
MAL's rewatching entries follow the Watching selection.
Included tags are alternatives, and matching an excluded tag always removes the
entry. Hidden legacy single-status values are transferred into the modern
status selection, without a database migration.

An unsaved token-renewing Test keeps replacement credentials in the running
process for up to one day. A subsequent saved Fetch can persist them without
saving that Test form's filters. This credential handoff does not survive a
restart; it is separate from SIMKL's persisted inventory cursor. If the service
invalidated the old refresh token before a discarded Test and restart, authorize
the registered application again.

Jikan supplies public anime **catalogue metadata** for per-title MyAnimeList
search/details. It does not authorize private account-list operations. Jikan's
old user-anime-list endpoint is deprecated/discontinued, so it is not used as a
replacement account-list source. API shape/credentials and service availability
are separate questions: a documented endpoint is not an uptime guarantee.
The direct account API and OAuth follow
[MyAnimeList's official list reference](https://myanimelist.net/apiconfig/references/api/v2#operation/users_user_id_animelist_get)
and [authorization reference](https://myanimelist.net/apiconfig/references/authorization).
Jikan's retirement is recorded in its
[official OpenAPI source](https://github.com/jikan-me/jikan-rest/blob/master/storage/api-docs/api-docs.json).

### AniList lists

Configure AniList under **Settings → Import Lists**, authenticate with AniList
and enter the target username. The multiple-list inclusion and exclusion controls
are included in **Tanukarr 1.0.0** and were introduced in historical 0.1.0;
the immutable historical 0.0.1 release predates them. These filters
describe different things and are combined.

Version 1.0.2 simplifies exclusions to **custom-list membership only**. The six
separate Exclude Watching/Planning/Completed/Dropped/Paused/Repeating controls
and their parser behavior have been removed. This change follows the immutable
Tanukarr 1.0.1 release; its original tag and images retain their previous settings.
Previously saved status-exclusion flags are ignored by the new typed settings
model. No list deletion, database migration or OAuth reset is needed.
The redundant **Custom List (Legacy)** textbox is also absent, even under Show
Advanced. An older saved single-list name appears as an ordinary **Included
Custom Lists** tag, preserving its inclusion restriction.
The [settings cleanup checkpoint](anilist-settings-cleanup-2026-10-13.md) records
the new source's actual checks and final-image verification state.

| Setting | What it selects |
| --- | --- |
| Import List Status | Enabled membership states: Watching, Planning, Completed, Dropped, Paused or Repeating |
| Import Media Release Status | The anime's release status: Finished, Releasing, Not Yet Released, Cancelled or Hiatus |
| Included Custom Lists | Membership in any named custom list; leave empty to select all otherwise eligible entries |
| Excluded Custom Lists | Membership in any named list always vetoes the import, including overlapping included memberships |

Choose at least one membership status and one release status. Watching and
Planning, together with Finished and Releasing, are the defaults. For example,
**Completed** does not mean the anime has finished airing: choose **Finished**
as well if that is required. AniList gives an entry one watching state, while
the same entry can belong to several custom lists independently.

Included custom-list names are alternatives: matching any one is sufficient.
Older saved single-list names are transferred into that same set rather than
discarded. The old scalar's boundary whitespace is trimmed as before; modern
tag values preserve their exact names, and only exact duplicates are merged.
Remove the transferred tag to remove that restriction. Reading or testing the
connection does not rewrite its stored settings; Save persists the normalized
tag representation and clears the legacy scalar. Leave **Included Custom Lists**
empty to accept every entry matching the enabled membership
and release states. Named-list inclusion does not bypass either state filter.

Custom-list exclusion always wins over custom-list inclusion. For example,
enable **Import Watching**, include `Seasonal`, and exclude `Do not download`:
an anime marked Watching and belonging to both custom lists is excluded.
An anime in `Seasonal` alone is eligible if its media release state is enabled.
If Included Custom Lists is empty, an otherwise eligible Watching entry in
`Do not download` is still excluded. To omit an account-list state,
leave its **Import** checkbox unchecked; inclusion by a custom name does not
bypass that selection. Excluded custom lists start empty. Release-state filtering
remains separate.

| Enabled account/media states | Included custom lists | Matching excluded list | Result |
| --- | --- | --- | --- |
| No | Any | Any | Skip; explicit inclusion does not bypass status filters |
| Yes | Matches an included list | No | Include |
| Yes | Matches an included list | Yes | Exclude |
| Yes | Configured but no name matches | Any | Skip; the inclusion condition is unmet |
| Yes | No included names configured | Yes | Exclude |
| Yes | No included names configured | No | Include |

The final 1.0.2 rule is: eligible entries intersect the union of included lists
(or all eligible entries when no included names are configured), minus the union
of excluded lists. This supersedes the intermediate inclusion-first development
checkpoint. Immutable releases and dated reports retain their original behavior;
the [1.0.2 verification report](releases/1.0.2-verification.md) records new checks.

Enter each complete custom-list name and press **Enter** to add it. Repeat for
more names. Spaces, commas and other punctuation are part of the name, so
`Seasonal, favorites` is one name, not two. The editor trims name boundaries;
direct API array values retain their spelling, including boundary whitespace.

Current `main` organizes the editor into account, membership statuses,
release statuses and custom lists. **Load custom lists**
reads the target account's defined list names and offers autocomplete for the
included/excluded tags. It does not save the connection or start a sync. Manual
typing, unknown saved names and OAuth controls remain
available. Enter the AniList username, rather than a custom-list name, in the
account field.

An exact custom-list name takes precedence. A case-insensitive fallback is
accepted only when it identifies a single list, so distinct names differing
only in case are not merged. Membership requires a `true` flag; a list name
whose flag is `false` does not match. If an excluded name has an ambiguous
case-insensitive match, use its exact spelling: the sync fails and retains the
previous successful list rather than guessing. Excluded membership is tracked
across pages by native media ID; a duplicate included copy cannot undo the veto.
Missing custom-list data while
custom-list exclusions are configured, pagination failures and GraphQL errors
retain the previous successful list unless complete validated collection data
can supply the missing membership.

The importer in 1.0.1 requests both AniList's normal membership map and the
documented `customLists(asArray: true)` shape. If the map is unavailable, only an
explicit, valid array of names and boolean membership flags can replace it;
an empty array means known empty membership. Null or missing data in both shapes,
malformed flags and GraphQL errors retain the previous successful list when a
membership-dependent filter is configured. This does not turn an unknown
membership response into an empty list. AniList documents the nullable
[MediaList field and array argument](https://github.com/AniList/docs/blob/master/docs/reference/object/medialist.md)
and [GraphQL errors](https://github.com/AniList/docs/blob/master/docs/guide/graphql/errors.md).

Version 1.0.2 can resolve a missing map and missing array from a complete
collection for the same user and authentication context. It requests this only
when an active custom-list filter needs membership, validates the owner and
definitions, follows bounded chunk pagination, and requires every sparse Page
entry to appear in that collection. The snapshot lasts for one fetch or Test and
uses the existing shared rate limits. Incomplete, mismatched or unavailable
collection data still retains the previous successful list. See the
[membership and priority checkpoint](anilist-membership-priority-2026-10-13.md)
for the earlier live investigation, and the
[1.0.2 verification report](releases/1.0.2-verification.md) for new checks.
The intermittent upstream
missing-field cause was not reproduced by the successful live samples.

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

Tanukarr 1.0.0 shares request pacing and cooldown between list
pages, connection tests, searches and metadata refreshes. It spaces requests at
least 2.1 seconds apart, observes AniList's retry/reset headers and retries a
temporary HTTP 429 at most three total attempts. A prolonged cooldown or repeated
failure remains an explicit error rather than a successful empty import.
Initial synchronization of a large list can therefore take several minutes.
The limit is shared within one application instance; another application using
the same public IP can still exhaust AniList's server allowance.

The immutable historical `v0.1.0` release predates this correction. Use Tanukarr
1.0.0 or another build containing the fix; repeated presses of **Test** or
**Refresh** do not bypass the server
cooldown. A request-limit error alone does not indicate an invalid custom-list
name or a need to replace OAuth credentials. See the
[request-limit verification report](anilist-rate-limit-verification-2026-10-13.md)
and AniList's [official rate-limit documentation](https://github.com/AniList/docs/blob/master/docs/guide/rate-limiting.md).

#### Pending additions and the development reliability update

The reliability update included in 1.0.1 preserves progress when metadata
retrieval reaches a provider's request limit. In an automatic list-add batch,
series with successfully prepared metadata are saved; HTTP 429 defers remaining
entries from that source for this batch while other sources can continue.
A transient metadata HTTP failure leaves that entry pending. The completion
message counts series actually added, rather than all candidates attempted.
Executed local checks and publication status are recorded in the
[reliability checkpoint](anilist-react-reliability-2026-10-13.md).

Version 1.0.1 applies the same progress-preserving principle when a prepared
group-season or movie slot becomes occupied before insertion. The conflicting
candidate stays pending while valid candidates persist; completion reports the
actual saved count. The complete list snapshot and existing group assignments
are retained, and retries do not overwrite or silently renumber the occupant.
Use the grouping controls to resolve a persistent same-kind slot conflict. See
the [release verification report](releases/1.0.1-verification.md) for the exact
SQLite reproduction and executed checks.

The normal **Import List Sync** task runs every five minutes. AniList's full
account-list refresh interval remains **12 hours**. After a complete successful
fetch witnessed in this running process, intervening ticks can retry pending
metadata additions from the saved list without requesting its private pages
again. This requires the same saved definition and settings, including account,
OAuth fields, statuses, custom-list filters, root folder and tags; the recorded
successful-sync timestamp must still match and the list must be enabled and
unblocked. Settings changes, a failed or partial later fetch, or a restart
invalidate this permission to resume. After restart, the saved membership stays
in the database but a new complete fetch is required before this optimization
can be used again.

Version 1.0.2 extends this guarded completed-fetch resume to MyAnimeList, TMDB
List, Trakt List, MDBList, Plex Watchlist and SIMKL User, each within its own
minimum refresh interval. A fetched item carrying season-monitor overrides
does not create a resume witness: those overrides are not persisted in the
saved membership rows. This optimization retries pending metadata additions;
it neither bypasses provider cooldowns nor treats a failed response as complete.

This never treats missing membership as empty or successful. Unknown inclusion
or exclusion membership, malformed flags, GraphQL errors and incomplete list
pagination retain the previous successful list. The five-minute retry is for
pending metadata additions after a known complete list; it does not bypass
account authorization, exclusions, the normal full-refresh interval or AniList's
server cooldown. A complete successful response containing no eligible entries
still updates the saved list normally. Existing Clean Library and automation
pause settings remain separate controls.

Public catalogue metadata is reused for up to five minutes and official
relations for up to 15 minutes. The first metadata query also supplies the
root's relations, and simultaneous requests for the same public item share one
fetch. These bounded memory caches contain public catalogue data, not private
account-list responses or OAuth credentials, and reset on restart. Pacing also
uses the remaining server allowance to spread requests across its reset window;
other applications using the same public IP can still cause HTTP 429. See
[Metadata providers](metadata-providers.md#anilist-request-reuse-and-pagination)
for cache and pagination limits.

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
