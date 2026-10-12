# AniList inclusion and exclusion verification - October 13, 2026

This checkpoint covers the multiple custom-list inclusion/exclusion controls
and explicit watching-state exclusions added to development main after release
0.0.1. The immutable release remains unchanged. Configuration is described in
the [AniList section of the library tools guide](library-tools.md#anilist-lists).
Original contributors and the OpenAI Codex based on GPT-6 adaptation are
credited in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

## Behavior

Any matching excluded custom list or excluded watching state vetoes an entry,
even when Watching or another included list matches. Included custom lists
and the retained legacy single-list setting form an OR selection, combined
with the existing membership-state and anime release-state filters. Exclusions
also veto duplicate media IDs across response pages. The connection test checks
every page when exclusions are configured.

Actual `true` custom-list membership is required. Exact spelling takes priority;
a case-insensitive fallback is accepted only when unique. An ambiguous excluded
name or missing exclusion metadata fails the fetch and preserves the previous
cached list. The editor accepts complete names with spaces and commas using
Enter. Defaults retain the previous inclusion choices and enable no exclusions.

Filters are scoped to this saved AniList connection. They do not add a global
blacklist or directly remove existing series; the existing Clean Library policy
continues to apply independently.

## Executed checks

The complete Windows Debug solution compiled with zero warnings and errors.
All **202 import-list tests** passed with zero skips or failures, including
**59 new regression cases** for custom-list/status precedence, both duplicate
orders and later-page vetoes, missing/ambiguous membership, local status checks,
connection-test pagination, settings validation and legacy JSON compatibility.
Fixtures parse real JSON response bodies without private AniList credentials.

All 16 frontend tests, lint, stylelint, typecheck and the Vite production build
passed. The build retains the existing large-chunk warning.

Additional suite, container and browser results will be recorded after their
execution. Earlier release/Jellyfin reports do not establish verification of
this new code.

## Limits

No user-managed AniList account or OAuth credentials were supplied. JSON
fixtures and public metadata checks are distinct from private-account list
synchronization. The tests do not authorize or verify downloads of existing
user media. Inherited dependency vulnerabilities have not been patched by
these filter changes; prior findings are recorded in the
[Jellyfin checkpoint](jellyfin-verification-2026-10-12.md).
