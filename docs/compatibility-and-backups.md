# Compatibility, migration and backups

**Tanukarr 1.0.0** continues the SonarrWeeb fork. The product name, supplied
Tanuki logos, GitHub repository and new release image namespace change; the
Sonarr-derived storage and API identifiers remain intentional compatibility
boundaries. This project is an independent GPL-licensed derivative of Sonarr,
with original source authors, copyright and license notices retained.

## API and internal names

The application supports **`/api/v3` and `/api/v5`** with its existing API-key
authentication. **`/api/v4` is not implemented.** API v3/v5 compatibility does
not mean that every downstream client or upstream endpoint has been tested.
Fork-specific settings and resource shapes may require a compatible client.
The corrected Tanukarr 1.0.0 disposable-container checks observed authenticated
v3/v5 responses and a missing v4 route. Both v3/v5 calendar feeds also denied
missing/wrong API keys with 401 and returned branded calendar data for a valid
key. See the [release verification](releases/1.0.0-verification.md) for the exact
tested image identity; no downstream-client compatibility claim is implied.

The product version is **1.0.0**. Sonarr's inherited internal assembly version
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

For an existing SonarrWeeb Docker deployment, stop the container, save a
consistent configuration backup and change the image to
`ghcr.io/kvasexert/tanukarr:1.0.0`. Preserve the original `/config` volume or bind
mount, media mounts, Compose project/volume identity and client categories.
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

For **PostgreSQL**, the native application backup does not include the external
database. Back up and restore that database separately with the PostgreSQL tools
appropriate to the deployed server, while maintaining a matching configuration
checkpoint.

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
`_artifacts/tanukarr-1.0.0/native-backup-restore.json`; historical SonarrWeeb
receipts remain under `_artifacts/backup-restore-2026-10-13/`. These files are not
part of a normal source clone.
