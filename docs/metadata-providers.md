# Metadata providers for TV series and anime

Tanukarr manages episodic **TV series and anime** with shared library,
monitoring, search, download-client and import workflows. Choose a metadata
catalogue for each title, then select an appropriate episode ordering where
alternatives are available. Provider choice, catalogue coverage and numbering
are related decisions with distinct responsibilities.

## Choose the source when adding a series

Open **Add New Series** and use the source dropdown above the search box.
Its four sources are **TheTVDB**, **TMDB**, **AniList** and **MyAnimeList**;
TheTVDB is the default. Search within the selected catalogue and confirm the
entry that represents your TV series, anime, edition, recut or special.
Set the root folder, quality profile and monitoring options before adding it.
Check **Series Type** as well: Standard uses season/episode release conventions,
Daily uses date-based conventions and Anime enables the corresponding anime
search/numbering behavior. A catalogue's genre and this parsing/search setting
are different concepts.

AniList and Jikan results default to Anime; TMDB results default to Standard,
including anime catalogue entries. TheTVDB normally defaults to Standard in
this fork. Choose Daily manually for date-based releases; automatic daily-series
classification is not a reliable setup step here. Existing user-selected Series
Type is retained during refresh.

The separate **Library Import** workflow currently searches TheTVDB and does
not offer this four-provider selector. For a folder whose intended metadata
owner is TMDB, AniList or MyAnimeList, use Add New Series, select the correct
catalogue entry and use the appropriate existing root/folder. Review the
generated path and episode matches before importing files. This is a per-series
workflow, not a claim of bulk import through all four providers.

## Missing artwork and descriptions

In 1.1.0, adding or refreshing a title can fill a missing cover, backdrop or
description from another supported catalogue through a verified provider ID.
AniList/MyAnimeList cross-references use the confirmed MAL ID. TheTVDB/TMDB
alternatives require an already known positive native ID; negative compatibility
placeholders are never sent as TheTVDB IDs. No title-similarity guess changes
the metadata owner, episode list, numbering or saved path.

Existing valid presentation from the same provider identity survives a
temporarily empty refresh response. Optional alternate lookups share the
AniList/Jikan rate coordinators, make at most two provider attempts, and use
short timeouts and cooldowns. Public presentation cache entries are bounded
and expire; they contain no list membership, OAuth tokens or account settings.
Unavailable alternatives leave the original import/refresh available.

Canonical linked-collection presentation is queried separately through the
verified AniList/MyAnimeList root ID, including when the root itself is not in
the library. Artwork URLs pass through the authenticated local cover proxy.
The frontend selects an existing thumbnail size for the display density and
tries the original image before showing a placeholder when a thumbnail fails.

Different catalogues can group seasons, recuts, specials or editions differently.
Check the selected entry's episode list and numbering against your releases.
A title missing from one catalogue may be available in another configured
source; no provider is guaranteed to cover every title or genre.

API clients must use `/api/v5/series/lookup` and its `metadataSource` query
parameter to select these catalogues. The legacy `/api/v3/series/lookup` remains
TheTVDB-only. Other supported v3 compatibility routes retain their own contracts;
see [API compatibility](compatibility-and-backups.md#api-and-internal-names).

## Catalogue coverage and current access requirements

| Source | Catalogue | Access used by this fork |
| --- | --- | --- |
| TheTVDB | TV series, including anime present in its catalogue | Metadata search/details use Sonarr's SkyHook TheTVDB proxy. This fork does not expose a personal TheTVDB API-key setting for those requests. |
| TMDB | TV catalogue, including listed anime; artwork, external IDs and episode groups where available | Configure a **TMDB v3 API key** under **Settings → Metadata Source**. The metadata client sends it as `api_key`; a TMDB API Read Access Token is a different credential. |
| AniList | Anime catalogue, alternative titles, release status and list membership | Public catalogue lookup uses AniList GraphQL without OAuth. Authenticated account-list import is a separate connection and requires its configured OAuth fields. |
| MyAnimeList | Anime catalogue, accessed through Jikan | Public metadata lookup uses Jikan's MyAnimeList API; the client does not require a MyAnimeList account or API key. Available episode detail depends on Jikan/MyAnimeList responses. |

The current metadata clients use `https://skyhook.sonarr.tv/v2/tvdb`,
`https://api.themoviedb.org/3`, `https://graphql.anilist.co` and
`https://api.jikan.moe/v4`. These describe the implemented access paths, not a
promise of uptime, exhaustive coverage or authorization for every account-list
feature. Metadata credentials do not configure indexers, download clients,
Jellyfin or other integrations.

The current access distinctions align with the official
[TMDB application-authentication reference](https://developer.themoviedb.org/docs/authentication-application),
[AniList authentication guide](https://docs.anilist.co/guide/auth/) and
[Jikan REST documentation](https://github.com/jikan-me/jikan-rest). In particular,
TMDB's v3 key and Read Access Token are different credential forms, AniList's
public data and account-authorized operations have different access rules, and
Jikan serves public metadata rather than authenticated MyAnimeList account
operations.

In 1.0.2, the separate **MyAnimeList import-list connection** reads MAL's official
account API. It supports public usernames with a registered Client ID, or direct
OAuth/PKCE for `@me` and authorized private data. Account-state, score, airing-state
and exact list-tag filters belong to that connection; they do not change Jikan's
public catalogue credentials. See
[account-list configuration and provider capabilities](library-tools.md#provider-capabilities)
for the implemented differences and
[1.0.2 verification](releases/1.0.2-verification.md) for actual execution scope.

The 1.0.2 Jikan catalogue client validates requested and returned positive MAL
identities, preserves TV/movie types and the source's episode numbers, and skips
duplicate episode rows. Missing episode numbers receive placeholders without
renumbering returned episodes. Shared HTTP 429 recovery allows at most three
attempts within thirty seconds; incomplete detail still uses the existing
placeholder fallback. These catalogue safeguards do not configure account-list
filters or establish live provider availability.

The TMDB import-list providers described in [Library tools](library-tools.md)
use a TMDB API Read Access Token. Keep that import-list token separate from the
v3 key used by the metadata/episode-group client.

## AniList request reuse and pagination

The reliability update included in 1.0.1 uses short-lived public
AniList caches: **five minutes for catalogue metadata** and **15 minutes for
official relations**. Concurrent requests for the same public key share one
fetch across provider instances, and each caller receives its own object graph.
The initial metadata query includes root relations; later airing-schedule pages
request the schedule rather than repeat the complete metadata selection.
This reduces duplicate requests when addition is followed by grouping and
refresh. Private account-list responses and OAuth credentials do not enter
these public caches. Executed local verification is recorded in the
[reliability checkpoint](anilist-react-reliability-2026-10-13.md).

The cache is bounded to 512 entries and 512 KiB per entry. Null/error responses
and incomplete metadata schedules are not retained as reusable metadata.
Pagination checks GraphQL errors, the requested media identity and the returned
page boundary. `hasNextPage` controls continuation; a conservative 50-node
fallback remains for older recorded responses that omit `pageInfo`. A supplied
page-info object with an unknown continuation flag is rejected. The existing
20-page airing-schedule cap remains; a result that reaches the cap before
completion is not stored in the metadata cache. These bounds do not promise
complete episode detail for every catalogue entry. AniList's nullable optional
airing-schedule field may legitimately be absent; that alone is not treated as
a malformed response or proof that the series was removed. AniList documents its
[pagination boundary](https://github.com/AniList/docs/blob/master/docs/guide/graphql/pagination.md).

Request pacing shares the allowance across public metadata, relations, searches
and account-list operations. It honors server retry/reset headers and spreads
calls when little allowance remains, using the observed window when a reset
header is absent. Caching and pacing reduce duplicate work; they do not guarantee
that the server will accept the next request. See AniList's
[rate-limit documentation](https://github.com/AniList/docs/blob/master/docs/guide/rate-limiting.md)
and [Library tools](library-tools.md#pending-additions-and-the-development-reliability-update)
for partial-progress and saved-list resume behavior.

In the current `main` import-list editor, account-list states and anime release
states remain their separate Import checkbox groups. Exclusion vetoes use only
**Excluded Custom Lists** and always veto a matching entry, including one also
selected by **Included Custom Lists**. Neither group bypasses the account/media
Import filters.
The six separate account-status exclusion flags and
their parser behavior were removed after the immutable 1.0.1 release. Public
catalogue lookup and these account-list filters are separate workflows. See
[AniList list configuration](library-tools.md#anilist-lists) for the simplified
controls and handling of older saved settings.
The duplicate legacy single-list textbox is not shown. Existing single-name
restrictions are transferred into Included Custom Lists tags, with modern
literal names preserved; removing a transferred tag does not reactivate a hidden
legacy filter.

## The selected provider owns the series

One metadata source owns each added series. Its provider/source and native
foreign ID form the identity used by refresh, import-list preview/sync,
existing-series matching, searches, exclusions and cleanup. Providers are
selected rather than automatically merged. A service outage does not silently
switch an existing series to another registered metadata provider.

External cross-reference IDs can link related catalogue entries without changing
that ownership. A negative compatibility `tvdbId` on a native TMDB/AniList/MAL
series is an internal placeholder, not a real TheTVDB entry. A missing entry in
one catalogue does not authorize replacing a separate recut or edition with a
parent entry from another catalogue.

Failed or incomplete list retrieval does not authorize destructive list cleanup.
AniList watching states, anime release states and custom-list membership remain
separate filters, with custom-list exclusions always removing matching included
entries in 1.0.2. See
[AniList list configuration](library-tools.md#anilist-lists).

Post-release [anime season and movie groups](anime-season-groups.md) can present related
AniList/MyAnimeList entries together using official prequel/sequel links or a
manual assignment. Manual controls are available for every owner; automatic
relations are limited to AniList/MyAnimeList. Each member keeps this same
provider/native-ID ownership. Season/movie labels and generated season, arc or
movie directory positions are separate
from the member's native episode numbers and configured filenames. Automatic
grouping does not merge owners or replace a TheTVDB/TMDB entry that already
contains multiple native seasons.

AniList/MyAnimeList anime movie entries can retain their own native episode
records inside a mixed anime group or numbered Movie Saga. This does not add
TMDB's movie API or a general movie catalogue to the TV integrations. Group
folder expressions and native filename tokens are documented in the linked
guide; an arc subtitle is a naming choice, rather than a metadata-owner change.

## Episode ordering is a separate choice

Metadata ownership identifies the catalogue entry. Episode ordering determines
the season/episode numbers stored for that series and used to match imports.
Where the add dialog offers alternatives, compare them with the release naming
you use. TV and anime releases can use seasonal, absolute, DVD, broadcast or
other available numbering; a genre alone does not determine the correct order.

The current backend applies **TMDB episode groups to TMDB-owned series only**.
Choose **Default** for TheTVDB, AniList and MyAnimeList owners. A TMDB group can
appear in a selector because an entry has a cross-reference ID, but choosing
that group for another metadata owner does not apply it.

**IMDb is not a fifth metadata provider and its ordering options are not
currently functional.** The inherited **Refresh Imdb Dataset** task can build
a local index and expose choices, but the production refresh path does not
apply its ordering, and the selector's episode request is not a working IMDb
ordering pipeline. Successful projector/index fixture tests do not establish
that this production path is wired. For a TMDB-owned title, choosing `imdb` can
also be sent to a TMDB episode-group endpoint and fail. Keep Default rather than
selecting IMDb.

The selected ordering is fixed when a series is added. Changing it later can
affect stored episode numbers, file/folder naming and matches; the current
workflow requires removing and adding the series again to choose another
ordering. Review existing files and backups before doing so.

## Current limitations and source checkpoints

- The four-provider selector belongs to Add New Series; Library Import remains
  TheTVDB-only.
- TMDB groups apply only to TMDB-owned titles. Non-TMDB group choices are
  ignored, and IMDb ordering choices are not applied by the current backend.
- Daily parsing should be selected manually for date-based releases.
- The published immutable 1.0.0 add dialog can reset a nonstandard recommended
  Series Type while mounting settings. Check the final field before saving.
  The main source correction preserves the metadata result's recommended type
  on initial mount and is covered by actual-component regression cases. Use a
  build containing the correction after its publishing workflow completes; it
  does not change the metadata owner,
  ordering implementation or immutable 1.0.0 image.

Current audit evidence is recorded in
[metadata validation](metadata-validation-2026-10-13.md). Historical release and
feature reports retain their original source/image identities and test results;
their focused tests are not proof of the incomplete ordering integration above.

## Shared tools and provider-specific features

Root folders, quality profiles, monitoring, indexers, download clients,
interactive searches, imports, retained versions and the selected-library
[Jellyfin refresh](jellyfin.md) serve TV and anime libraries. Configure those
services for the content and release formats you actually use.

AniList custom lists and Jikan metadata target anime catalogue entries. Nyaa's
anime searches and absolute-number matching address particular release formats;
they do not limit the whole application to anime. Provider-specific features
retain their catalogue and indexer limits.

The [fork design](../FORK.md) explains identity/numbering decisions;
[CONTRIBUTORS.md](../CONTRIBUTORS.md) preserves original authors and AI provenance.
The current English guide update used **OpenAI Codex based on GPT-6**. Dated
verification reports retain their original scope, and documentation positioning
does not establish new runtime coverage or change the 1.0.0 release artifacts.
