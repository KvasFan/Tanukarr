# Compatibility, migration and backups

**Tanukarr 1.0.4** continues the earlier releases. The Tanukarr name, supplied
Tanuki logos, GitHub repository and release image namespace introduced in 1.0.0
remain in use; the
Sonarr-derived storage and API identifiers remain intentional compatibility
boundaries. This project is an independent GPL-licensed derivative of Sonarr,
with original source authors, copyright and license notices retained.

## API and internal names

The application supports **`/api/v3` and `/api/v5`** with its existing API-key
authentication. **`/api/v4` is not implemented.** API v3/v5 compatibility does
not mean that every downstream client or upstream endpoint has been tested.
Fork-specific settings and resource shapes may require a compatible client.
For per-title catalogue selection, use `/api/v5/series/lookup` with
`metadataSource` set to `tvdb`, `tmdb`, `aniList` or `myAnimeList`. The legacy
`/api/v3/series/lookup` remains TheTVDB-only; supplying the v5 selector there does
not enable other catalogues. This endpoint difference does not remove the
supported v3 import-list and other compatibility routes. The
[v5 controller](../src/Sonarr.Api.V5/Series/SeriesLookupController.cs) and
[v3 controller](../src/Sonarr.Api.V3/Series/SeriesLookupController.cs) define the
implemented contract; the [1.0.2 report](releases/1.0.2-verification.md) records
the new packaged MAL/Jikan checks separately from historical API evidence.
The corrected Tanukarr 1.0.0 disposable-container checks observed authenticated
v3/v5 responses and a missing v4 route. Both v3/v5 calendar feeds also denied
missing/wrong API keys with 401 and returned branded calendar data for a valid
key. See the [release verification](releases/1.0.0-verification.md) for the exact
tested image identity; no downstream-client compatibility claim is implied.

The product version is **1.0.4**. Sonarr's inherited internal assembly version
remains `10.0.0.*`; it is not a second Tanukarr release number. The `Sonarr`
executable/core names, `config.xml`, SQLite `sonarr.db`, migration history,
provider identities and container port **8989** remain unchanged. The default
host port in Docker examples is **6970**.

General settings default to the **Instance Name** `Tanukarr`. Saved names must
start or end with `Tanukarr` or legacy `Sonarr` (for example `Tanukarr Home` or
`Anime Sonarr`). Existing custom names are preserved when read; arbitrary newly
saved names are not accepted. The validation keeps the inherited prefix/suffix
policy while admitting the new product name.

## Rename and move an existing installation

Renaming a GitHub repository or local checkout does not move configuration or
media. Existing WSL distribution names and host tool-installation directories
can remain unchanged. Review absolute paths used by scripts, bind mounts and
external services if moving an installation directory.

For an existing Tanukarr Docker deployment, stop the container, save a
consistent configuration backup and change the image to the newly verified
`ghcr.io/kvasexpert/tanukarr:1.0.4`. Its
[publication follow-up](releases/1.0.4-publication.md) records the verified
manifest and protected 35-to-37-table upgrade;
the [1.0.3 publication follow-up](releases/1.0.3-publication.md) retains the
preceding release's verified digest and deployment evidence. Preserve the
original `/config` volume or bind mount, media mounts, Compose project/volume
identity and client categories.
A new volume name selects new storage and can make the application appear empty.
Never let two running instances share one writable `/config`.

For an upstream Sonarr migration, copy the stopped source instance's
configuration into a separate Tanukarr location and keep the original backup.
Database migrations can make the copy unsuitable for an upstream downgrade.
Verify indexers, metadata providers, download clients and library permissions
before allowing automatic acquisition. See [Docker installation](docker-installation.md).

## What the native backup contains

The standard SQLite backup is a **ZIP archive** containing `config.xml`,
`sonarr.db` and backup information. It excludes the media library and does not
replace a media backup. Configuration and database files can contain API keys,
password hashes, OAuth tokens and other private settings; the archive is not
encrypted. Restrict its permissions and use secure storage.

The ZIP does not include optional compression-worker state or configuration
cache files. SIMKL V2's private inventory/cursor is a cache file; without it,
the provider must establish a new complete inventory before using deltas.
A consistent backup of the complete configuration mount preserves that cache
separately from the application's standard ZIP.

For **PostgreSQL**, the native application backup does not include the external
database. Back up and restore that database separately with the PostgreSQL tools
appropriate to the deployed server, while maintaining a matching configuration
checkpoint.

## Manual backup task timing

Starting **Backup** under **System > Tasks** creates the ordinary manual ZIP.
In 1.0.4 a successful completion also refreshes **Last Execution**, **Last
Duration** and **Next Execution** through the task API/SignalR update. The next
scheduled backup interval starts from that successful manual completion. Manual
archives still use their separate category and retention behavior; they are not
reclassified as scheduled archives.

A failed or incomplete Backup command keeps its error and the previous successful
timestamps. It does not advertise that a usable archive was created or defer a
due backup on the strength of a failed attempt. Check the command/log error and
the configured writable backup directory before retrying.

This fixes inherited behavior reproduced in a disposable published 1.0.3 instance:
a real valid ZIP was created while task timing stayed unchanged. The new
[backup fixture](../docker/tests/backup-tasks.py) verifies actual ZIP CRC/SQLite
integrity, V3/V5 timing, deliberate unwritable-folder failure, recovery and
scheduled/manual category separation. Its optional Chromium helper presses the
actual Tasks button and checks the refreshed cell without page reload. Newly
executed results belong to the [preparation ledger](releases/1.0.4-verification.md)
and [published-image follow-up](releases/1.0.4-publication.md).

Migrations 248/249 add Jellyfin watched state and the persistent originating-list
progress queue to the same SQLite database. A standard ZIP includes them with
the other database rows. It does not include the media or guarantee that a
separate external provider account has accepted a queued write. See
[Jellyfin watched status](jellyfin-watched.md).

## Verified restore behavior and remaining limitation

The corrected Tanukarr 1.0.0 image repeated the manual and scheduled SQLite
backup/restore checks in disposable instances. Stored ZIP restores through API
v3/v5 and an uploaded ZIP through API v5 recovered nine populated fixture tables,
including AniList settings/provider identities and the Jellyfin pending-refresh
queue. The application restarted and responded afterward. The fixture was seeded
directly in SQLite and used dummy credentials; this is not a restore of the
owner's production library or real account tokens. Eleven recorded checks include
successful ZIP paths and reproduction of the unsafe XML path described below.

**Use the application's standard ZIP backups. Do not restore a standalone XML
file.** The inherited raw-XML upload path remains unsafe: an accepted XML upload
can be scheduled as a database replacement at startup. Invalid database files
also lack a comprehensive preflight check. Those are outstanding limitations,
not a claim that all backup inputs are safe.

Before upgrading or restoring, retain a separate consistent filesystem/volume
backup of `/config` taken while the container is stopped. Restore into a
separate configuration location when validating a backup or rolling back a
migration; retain the original backup until validation succeeds.

The [Tanukarr 1.0.0 verification report](releases/1.0.0-verification.md) identifies
the exact tested image. New backup receipts are in the ignored
`_artifacts/tanukarr-1.0.0/native-backup-restore.json`; historical Tanukarr
receipts remain under `_artifacts/backup-restore-2026-10-13/`. These files are not
part of a normal source clone.
