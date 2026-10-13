# Upstream compatibility audit - October 13, 2026

This is a fresh read-only review for the Tanukarr 1.0.3 work. It compares public
GitHub branch heads, the accepted integration ledgers and Tanukarr history at
`a1652467ba0c2bf3a9c9d03147a6bee89aaa8c49`. It records source inspection, not a
new build, container run or live-library result.

## Accepted source heads

| Repository and branch | Public head reviewed | Result |
| --- | --- | --- |
| [Sonarr/Sonarr `develop`](https://github.com/Sonarr/Sonarr/tree/develop) | `57816c149d02f95543973fefb4780c4495f8121c` | Already a real ancestor of the Tanukarr checkpoint; no new default-branch commits |
| [KakarottoCake/Sonarr-Pro `main`](https://github.com/KakarottoCake/Sonarr-Pro/tree/main) | `c104bf96ffd2ebaa18999db29196160e4a7210b0` | Unchanged from the accepted integration snapshot |
| [gaboy498-byte/Sonarr-Pro `main`](https://github.com/gaboy498-byte/Sonarr-Pro/tree/main) | `cef1369f86b23910c606c76e0b20d2ed86809ac7` | Unchanged; the existing disposition of individual features still applies |
| [gaboy investigation branch](https://github.com/gaboy498-byte/Sonarr-Pro/tree/investigate/multiseason-and-fakefilter) | `14eaaebf6267791c60c50a01b6d3109e18ccb53e` | Unchanged from the adapted season-range/filter work |
| [ikatun/Sonarr-Pro `main`](https://github.com/ikatun/Sonarr-Pro/tree/main) | `d2e06dc0552db509db43ab347e30b4e10736680d` | Unchanged from the reviewed complete-series/pack integration |

The source forks were adapted onto the newer Sonarr application. Their branch
heads do not have to be ancestors of the consolidated Tanukarr commits. Missing
ancestry alone is therefore not evidence that their features are missing. The
[initial integration](integration-2026-10-12.md),
[gaboy disposition](gaboy-integration.md) and
[ikatun disposition](ikatun-integration-2026-10-12.md) remain the feature ledgers.

Sonarr's default branch is `develop`. Its stable `main` head at this checkpoint
is `bc54afbdd0e38bdb0fca4785fcf7d7a6af77bf9a`, on the v4 lineage; there is no
`master` branch. Some stable-branch changes have different cherry-pick hashes
from equivalent changes already in `develop`. Importing the older major branch
wholesale would replace the current v5/application and release assumptions.

## Quality and codec candidates

| Source work | Compatibility finding | Disposition for 1.0.3 |
| --- | --- | --- |
| [Quality size-limit proposal](https://github.com/Sonarr/Sonarr/compare/develop...quality-size-limits), head `974c4a601b6b6871f8283a1b9472a987d32ff924` | An older proposal, not a new default-branch update. Its server-side limits, validation and bulk-resource behavior are already inherited. The modern Quality Definitions UI still needed to present its save error. | Adapt the useful error feedback to the current React UI; preserve edits for retry and avoid logging raw error objects. Do not import the old class component. |
| [ImageSharp proposal](https://github.com/Sonarr/Sonarr/commit/720371c1fd3e7f072209835b6adb2734b27e53d7) | The 2025 branch proposes ImageSharp 3.1.11; this fork already uses 3.1.12. | Do not downgrade. This historical branch is not evidence that current dependency findings are fixed. |
| [H.266 release-title parsing](https://github.com/Sonarr/Sonarr/commit/547cd5b48363422093142fafce4deb9aa443052c), Bogdan | Already present in integrated `develop`. | Retain; no duplicate patch. |
| [EAC3 WEB-DL parsing](https://github.com/Sonarr/Sonarr/commit/6421bb62188c9025a5cb7f34f70f7186e675807e), Mark McDowall | Already present in integrated `develop`. | Retain; no duplicate patch. |
| Kakarotto per-series compression `1e0bc1698ece0cc1e9f2a49fa44ef83af4b9e6b1` and H.264 presets `5a756d49f4a630a37355f0fa77665c9185eaf548` | Already integrated; the optional worker deliberately skips existing HEVC/AV1 files. | Retain the separate worker behavior. Codec-aware acquisition sizing does not start transcoding. |

The save-error proposal is credited to **Mark McDowall**, author of
[`974c4a601`](https://github.com/Sonarr/Sonarr/commit/974c4a601b6b6871f8283a1b9472a987d32ff924).
Earlier size-limit proposal work is credited to **Robert Dailey**,
[`039d7775e`](https://github.com/Sonarr/Sonarr/commit/039d7775ed3203c9831988172243e596a36e4064).
The actual inherited backend behavior is retained rather than attributed as a
new local feature. The modern adaptation, requested sizing policy, regression
work and English documentation use **OpenAI Codex, based on GPT-6**.

## Compatibility boundaries

Compatible adaptations must retain each title's TheTVDB, TMDB, AniList or
MyAnimeList catalogue identity, including native non-TVDB IDs and negative TVDB
placeholders. They must retain native episode numbering, separately guarded
season/movie group slots, direct provider authentication and the current list
set rule: selected universe intersect included membership, minus excluded
membership. An excluded member still wins when both sets contain the title.

This audit does not authorize reintroducing Sonarr cloud services, changing
provider ownership by numeric-ID coincidence, merging independent catalogue
entries, weakening group uniqueness or adopting an old API/build layout. The
new codec-size policy is local requested work; it is not presented as a newly
published upstream feature.

## Evidence and limits

GitHub repository, branch and commit responses were recorded along with
sequential, branch-specific remote fetches. The comparison of the inherited
Quality Definitions UI files against `upstream/develop` was empty before local
1.0.3 edits, establishing that the save-error gap was inherited behavior.
Source authors and available co-author trailers were inspected directly; an AI
Git display name alone is not an exact model identification.

Ignored local receipts are in
`_artifacts/release-1.0.3-2026-10-13/upstream-audit`, including
`audit-summary.json`, the public API snapshots, source-author records,
`quality-codec-current-vs-develop-numstat.txt` and the optional-branch patches.
These artifacts are not promised in a normal clone. No accepted default head
had advanced at this checkpoint; later remote updates require another review.
Executed 1.0.3 checks belong to the
[release verification report](releases/1.0.3-verification.md).
