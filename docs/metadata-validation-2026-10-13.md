# TV/anime metadata validation — October 13, 2026

This checkpoint follows main source `08a5def3edc9bac5bb8e359a18c146e3e27fb260`.
It updates Tanukarr's positioning and English guidance for both TV series and
anime, audits the four metadata providers, and corrects a Series Type reset in
the add dialog. It does not replace the immutable Tanukarr 1.0.0 tag or assets.
The later anime-season grouping work has its own verification checkpoint.

## Confirmed metadata behavior

- Add New Series offers TheTVDB, TMDB, AniList and MyAnimeList/Jikan. TheTVDB
  is the initial source; a saved source choice is retained. Default series
  preferences use Standard TV conventions.
- TheTVDB and TMDB resolve their TV catalogue entries, including anime present
  in those catalogues. AniList and Jikan resolve anime entries. Providers are
  selected per title rather than automatically merged.
- Search passes the selected source to lookup. Add and refresh retain the
  source/native foreign ID, with compatibility TVDB IDs allocated separately.
  Refresh retains the user's Series Type; a provider outage does not switch
  to another registered catalogue automatically.
- TMDB metadata sends a configured v3 `api_key`. TheTVDB uses SkyHook without
  a personal key exposed in this fork. AniList/Jikan public catalogue requests
  do not use account OAuth; AniList account-list import has separate OAuth
  requirements. TMDB import-list Read Access Tokens are separate credentials.

See [Metadata providers](metadata-providers.md) for configuration and primary
provider references. These findings describe implemented routing and access;
they do not establish live SkyHook availability, keyed TMDB service access or
private-account OAuth synchronization at this checkpoint.

## Add-dialog correction and provenance

The inherited add dialog initializes a nonstandard provider recommendation,
then replaces it with the saved Series Type in a mount effect. With the usual
Standard preference, AniList/MyAnimeList Anime recommendations become Standard.
The same reset affects a Daily recommendation. Because the effect depends on a
new settings object, changing another option can overwrite a manual choice too.

The correction keeps the initial recommendation and applies an explicit Series
Type input to both local state and saved preferences. Other settings no longer
reset the field. Saved preferences still apply when the result recommends
Standard; the user can always change the final selection.

The original component was converted to TypeScript by **Mark McDowall** in
`87c840974b3350e60a06c70a36071103369470a1`. Comparison with upstream
`57816c149` confirms the reset predates this fork's changes. The audit, local
correction and English documentation used **OpenAI Codex based on GPT-6**;
[CONTRIBUTORS.md](../CONTRIBUTORS.md) preserves the broader source credits.

## Executed checks

| Check | Result and scope |
| --- | --- |
| Native solution Debug build | Passed, zero warnings and errors; solution build uses the required shared StyleCop settings. |
| Focused offline backend fixtures | 161 passed, zero failures/skips: 146 metadata, add/refresh and synthetic-ID cases, six provider migrations and nine localization cases. |
| Add-dialog React regressions | Ten passed. Real React mounts the production component and settings selector with mocked API/UI dependencies; assertions cover Anime/Daily recommendations, saved Standard preferences, explicit changes, unrelated rerenders and submitted provider/type fields. |
| Regression reproduction | Before the component correction, four of the ten cases failed because recommendations or selections were reset. |
| Complete frontend functional command | `yarn test:queue-details`: 26 passed, including the ten add-dialog cases; the same command runs in CI and Docker builds. |
| Frontend typecheck and changed-file ESLint | Passed; changed TypeScript/test files have zero lint errors and warnings. |
| Vite production build | Passed with inherited font, chunk-size and timing warnings. |

The earlier 146-case run is a baseline subset of the 161-case run and is not
counted again. Online SkyHook IntegrationTest fixtures were excluded. Tests
against an owner's real media library, live acquisition, and private accounts
were not run for this checkpoint.

Ignored local receipts are under `_artifacts/metadata-positioning-2026-10-13/`
and `_artifacts/metadata-audit-2026-10-13/`. They are not supplied by a normal
clone; reproducible frontend commands are documented in
[CONTRIBUTING.md](../CONTRIBUTING.md).

## Remaining integration limits

The separate Library Import workflow searches TheTVDB and has no four-provider
selector. Daily classification relies on an intentionally disabled Sonarr
cloud endpoint, so Daily must be selected manually for date-based releases.

TMDB episode groups are applied only by the TMDB provider. Other owners ignore
that ordering ID. IMDb's index/selector exists, but its `ApplyOrdering` method
has no production caller; choosing `imdb` on a TMDB-owned entry routes to a
TMDB episode-group request instead. The helper expects IMDb episode IDs while
TMDB episodes use native numeric IDs, so wiring a caller alone would not fix
that integration. Successful projector/index unit fixtures establish those
helpers' behavior rather than a complete ordering workflow.

The ordering/provider files are additions relative to the recorded upstream
base. These are inherited integration gaps, not regressions introduced by the
Tanukarr rename. Current maintained guidance says to use Default for non-TMDB
owners and avoid IMDb ordering choices. Dated reports retain their original
test results and source/image identities.

The add-dialog correction is a post-1.0.0 main-source change. An edge build
contains it only after the corresponding workflow succeeds; the stable 1.0.0
image retains the earlier component. No release retagging or artifact
replacement is implied by this documentation update.
