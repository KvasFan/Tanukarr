# Jellyfin integration verification — October 12, 2026

This report covers the dedicated Jellyfin connection added to development main
after the immutable SonarrWeeb 0.0.1 release. It extends the existing upstream
Emby / Jellyfin integration without changing that connection. Configuration and
trigger semantics are in the [Jellyfin guide](jellyfin.md); original Sonarr
authors, Jellyfin API contributors and OpenAI Codex based on GPT-6 are credited
in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

## Source and behavior

The new provider supports exactly one selected library per connection, immediate
refresh per eligible imported file, conditional interval ticks from one minute,
and both together. Migration 243 stores import generations, independent
immediate/tick watermarks, last tick, mode, target fingerprint and state epoch.
API keys are excluded from refresh state. Disabled/deleted connections clear
pending work; API-key rotation preserves it; target changes reset it.

The implementation uses the official `Library/VirtualFolders` and selected
`Items/{libraryId}/Refresh` APIs. It rejects stale IDs, redirects and responses
other than HTTP 204. Canonical `Authorization: MediaBrowser Token="..."`
authentication works when Jellyfin 12 disables legacy token headers. API-key
characters unsafe for a quoted header are rejected. HTTP error-response logging
now honors `LogHttpError=false`, including the shared trace representation, so
Jellyfin error bodies cannot reflect a token into that trace.

Library dropdown query keys include connection identity and server settings;
changing the host does not retain options belonging to another Jellyfin server.
The provider inherits Sonarr's import/upgrade/tag filters and does not subscribe
to import-complete events that would duplicate per-file notifications.

## Executed checks

The complete Windows Debug solution compiled with zero warnings and errors.
All **102 focused Jellyfin cases** passed, covering validation, actual Newtonsoft
HTTP responses, selected endpoints, authentication, redirects, status handling,
SQLite state, concurrency, retry, restart persistence, mode changes, actual
notification dispatch, tags/upgrades, and one-minute task registration.

Additional full-suite, container and real Jellyfin checks are being completed
for this checkpoint. Their results will be recorded here after execution; the
prior release reports do not establish verification of this new code.

## Limits

Jellyfin accepts refresh requests asynchronously. HTTP 204 verifies acceptance,
not successful scan completion. SonarrWeeb cannot retry a later server-internal
failure it does not observe. Transport timeouts can cause duplicate accepted
requests. Busy immediate requests may wait for the background retry task, while
retaining one request per pending import. Scheduler checks occur every 30 seconds;
the configured interval is approximate.

The user's production Jellyfin credentials and library have not been supplied.
Owned test servers and generated videos are distinct from verification of a
particular user-managed Jellyfin deployment. Existing-image vulnerability
findings remain documented in the
[release security checkpoint](releases/0.0.1-verification.md); this integration
does not claim to patch those dependencies.
