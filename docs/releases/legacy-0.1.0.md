# Historical 0.1.0 release

> Historical source checkpoint: `961d8abe176008aa335bb539ae02ec2c1edd061f`.
> Results and assets on this page apply only to that source revision.

> Historical checkpoint: this project is now **Tanukarr**. See the [current documentation](../README.md) and [Tanukarr 1.0.0 verification](../releases/1.0.0-verification.md). Current product terminology is used in this archive. Numeric results, hashes, tags and original published assets retain their recorded identities; historical commands use placeholders for their original local names. This wording update does not repeat verification.

Historical 0.1.0 adds a dedicated Jellyfin library-refresh connection and AniList
inclusion/exclusion controls to the initial Tanukarr integration. It retains
the Sonarr, KakarottoCake, gaboy498-byte and ikatun features described in the
[0.0.1 release](https://github.com/KvasExpert/Tanukarr/releases/tag/v0.0.1).
The original release and its versioned image remain unchanged.

Source: [KvasExpert/Tanukarr](https://github.com/KvasExpert/Tanukarr).
Git tag: `v0.1.0`. Image: [the historical 0.1.0 image](https://github.com/KvasExpert/Tanukarr/releases/tag/v0.1.0).
Published release images support **Linux amd64**.

## Install or upgrade

Follow the [Docker installation guide](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/docs/docker-installation.md)
for Compose YAML, permissions, authentication and offline installation. Download
the release's [compose.yaml](https://github.com/KvasExpert/Tanukarr/releases/download/v0.1.0/compose.yaml)
into a separate installation directory, then run:

```sh
docker compose config
docker compose pull
docker compose up -d
```

The default address is **http://localhost:6970**. Existing installations can
change their image to [the historical 0.1.0 image](https://github.com/KvasExpert/Tanukarr/releases/tag/v0.1.0), keeping the same config
and media mounts. Stop the application and take a consistent, private backup of
the configuration before upgrading. Never share a live `/config` between
instances. Keep an original backup when importing an upstream configuration
into a separate Tanukarr config directory.

**0.1.0 is the product release version.** The inherited Sonarr core remains
`10.0.0.*`; legacy V3 and V5 API versions are unchanged. Migration **243** adds
the persistent Jellyfin refresh-state table. These additions do not rewrite
series metadata-source/foreign-ID ownership or turn native AniList IDs into TVDB
IDs. Use the original backup for a rollback that requires an older schema.

## Jellyfin selected-library refresh

Configure **Settings > Connect > + > Jellyfin**, enter the server and API key,
and select one library. Each connection supports:

- A refresh after every eligible imported file.
- A conditional interval refresh, batching imports since the previous successful
  tick and sending no request during idle intervals.
- Both immediate and periodic refreshes, with independent pending work.

The default interval is **60 minutes**, with a minimum of **1 minute**. Pending
imports survive restarts and failed requests are retried. Standard import,
upgrade and tag filters apply. The provider refreshes the selected library and
never falls back to a server-wide scan.

Jellyfin must first complete an initial scan for **every configured physical
media folder**. Until those folders are indexed, Tanukarr rejects Test/refresh
and retains pending imports. HTTP 204 confirms that Jellyfin accepted a refresh;
it does not confirm that its asynchronous scan completed. See the
[Jellyfin guide](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/docs/jellyfin.md)
for networking, triggers and recovery behavior.

## AniList inclusion and exclusion

Under **Settings > Import Lists > AniList**, **Included Custom Lists** selects
entries matching any named list. **Custom List (Legacy)** joins that same union
so saved restrictions continue to apply. Empty inclusion controls accept all
entries matching the enabled membership and anime release states.

**Excluded Custom Lists** and the six **Exclude List Status** choices always
take precedence: Watching, Planning, Completed, Dropped, Paused and Repeating.
For example, an anime in Watching and `Seasonal` is excluded when it also belongs
to the excluded `Do not download` list. An excluded ID remains excluded across
duplicates and pagination. These filters affect one saved connection; they do
not create a global blacklist or directly delete existing shows.

Enter each complete list name and press **Enter**; spaces and commas belong to
the name. Exact spelling wins, with a case-insensitive fallback only when
unique. Ambiguous excluded names, missing exclusion membership data, GraphQL
errors and pagination failures preserve the previous successful cached list.
Watching state, anime release state and custom-list membership remain distinct.
See the [AniList guide](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/docs/library-tools.md#anilist-lists).

Credential-bearing HTTP failures also honor provider logging suppression,
including response-body and redirect diagnostics.

## Release assets and verification

| Asset | Purpose |
| --- | --- |
| `compose.yaml` | Deployment using the versioned GHCR image |
| [0.1.0 image archive](https://github.com/KvasExpert/Tanukarr/releases/tag/v0.1.0) | Offline Docker image |
| [0.1.0 source archive](https://github.com/KvasExpert/Tanukarr/releases/tag/v0.1.0) | Corresponding source with license and contributor notices |
| [0.1.0 SBOM](https://github.com/KvasExpert/Tanukarr/releases/tag/v0.1.0) | CycloneDX component inventory |
| `SHA256SUMS` | Download checksums |
| `release-manifest.json` | Source commit, image identity, platform and runtime |
| `smoke.json` | Release-image startup, API/UI, permissions and persistence checks |
| `functional-security.json` | Release-image functional and security checks |

The tag workflow builds the image, runs its configured backend/frontend and
container checks, then publishes that exact tested image. Attached reports and
the manifest identify the actual release build. Checksums establish download
integrity; an SBOM is an inventory, not a vulnerability scan or certification.

Earlier verification remains scoped to its recorded source and image hashes:

- The [Jellyfin checkpoint](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/docs/jellyfin-verification-2026-10-12.md)
  records 124 focused cases within 7,368 Windows test passes, 6,455 Linux backend
  passes, and actual Jellyfin 12.2 scans of generated media in disposable libraries.
- The [AniList checkpoint](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/docs/anilist-filters-verification-2026-10-13.md)
  records 202 import-list cases, including 58 new cases, within 7,426 Windows
  passes; 6,513 Linux backend passes; and 19 published-container checks including
  six real Chromium scenarios.

Those full-suite counts are **prior checkpoints**, not claims that the version
bump repeated every suite. Their reports record skipped cases and platform
limits. Private AniList OAuth synchronization, real user downloads, hardware
VAAPI encoding and an arm64 runtime were not verified.

The earlier image scans reported **15 inherited CVEs: zero critical, three high,
nine medium and three low**, with zero discovered secrets. These changes do not
patch the affected ImageSharp/Ubuntu dependencies. Consult the exact checkpoint
and component versions rather than assuming a clean security scan for 0.1.0.

## Contributors and license

Credit remains with the Sonarr contributors, **Xabis**, **Private Contributor**,
**KakarottoCake**, **Matt Flowers / gaboy498-byte**, **Ivo Katunaric / ikatun**, the
recorded Codex source authors, Jellyfin/AniList API contributors, translators
and reviewers. Original feature commits and source-declared AI labels remain in
[CONTRIBUTORS.md](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/CONTRIBUTORS.md).
Local integration, these additions, verification, English documentation and
release work used **OpenAI Codex based on GPT-6**. Inherited Codex display names
do not establish an exact model; inherited Claude labels remain source
declarations.

Tanukarr is an independent derivative distributed under the
[GPL license](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/LICENSE.md), with
[copyright notices](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/COPYRIGHT.md)
and corresponding source retained. Report project issues in the
[Tanukarr tracker](https://github.com/KvasExpert/Tanukarr/issues); follow
[SECURITY.md](https://github.com/KvasExpert/Tanukarr/blob/v0.1.0/SECURITY.md)
for vulnerabilities.
