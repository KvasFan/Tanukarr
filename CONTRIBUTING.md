# Contributing to Tanukarr

Contributions, including AI-assisted work, are welcome. Describe the problem,
the resulting behavior and the verification you performed. Preserve the authors
and co-authors of adapted work. The [contributor register](CONTRIBUTORS.md) records
feature provenance, source commits and AI attribution.

## Before changing code

Read [AGENTS.md](AGENTS.md), [FORK.md](FORK.md) and the relevant
[feature guide](docs/README.md). Compare affected files with the recorded upstream
base before attributing a problem to the fork. Prefer changes maintainable alongside
upstream; report shared defects through the appropriate project.

Preserve native provider identities, explicit manual import choices, existing
library data and configured download clients. Use disposable fixtures for tests;
real-library downloads, moves or deletion require authorization.

## Tools and build

Use .NET SDK **10.0.401** from [global.json](global.json), Node.js **24**, Yarn
Classic **1.22**, Python 3, and Docker Engine with Linux containers. FFmpeg and
FFprobe are needed for generated-media worker integration tests.

Build the solution so SolutionDir supplies the shared StyleCop configuration:

```sh
dotnet build src/Sonarr.sln -c Debug
yarn install --frozen-lockfile
yarn test:queue-details
yarn lint
yarn stylelint
yarn typecheck
yarn build
```

`yarn build` creates the Vite production bundle; the obsolete webpack
`--env production` flag is not supported. Do not build while tests use the same
output directory. On Linux, prepare the packaged test probe:

```sh
chmod +x _tests/net10.0/ffprobe
```

`yarn test:queue-details` includes queue/episode/provider-identity cases and the
Add New Series modal regressions. To focus on recommended Series Type,
persisted defaults, explicit user selection and settings-rerender behavior, run:

```sh
yarn test:add-series-modal
```

The modal cases mount the actual React component with the real settings
selector and mocked API/UI dependencies; they do not contact metadata providers
or modify an existing library.

For the grouped library model, mounted Add/Edit controls and season/movie
selection cases, use `yarn test:anime-grouping`. These cases are also included
in `yarn test:queue-details`; they use mocked API dependencies. The generated
video Docker check in [Container verification](docker/tests/README.md#anime-season-grouping)
exercises actual backend add/edit/import/rescan behavior with a local trusted
catalogue fixture, separately from frontend component tests.

The main frontend command also includes naming-token/search payload and AniList
custom-list autocomplete cases. Those mocked component checks remain separate
from the real Docker API import/list action checks.

For 1.0.2 list changes, verify the actual provider capability rather than assuming
every service supports AniList custom lists. Included names form a union; matching
excluded names always remove entries from that selection, including overlaps
and duplicate pages. Account/media filters remain independent requirements.
Unknown required membership and incomplete pagination must retain the previous
successful list. See [Library tools](docs/library-tools.md#lists-rules-and-keys)
and the [1.0.2 verification report](docs/releases/1.0.2-verification.md) for
provider-specific limits and executed evidence.

For the 1.0.5 snapshot changes, verify complete pagination before
local filters, custom-only AniList groups, duplicate/conflicting identities,
failed later pages, account/grant isolation, bounded retention and scope cleanup
after success and exceptions. Saved membership and SIMKL's incremental baseline
must survive; temporary private responses must not become diagnostic exports.
Public metadata or relations outside the collection can still require requests.
Use the [snapshot guide](docs/import-list-snapshots.md) and the
[provider matrix](docs/library-tools.md#provider-capabilities) to distinguish
provider-specific boundaries.

Test the common metadata preference on both v3 and v5: Automatic defaults,
available positive native IDs, unavailable-preference fallback, unknown enum
rejection, older clients omitting the field and explicit resets. Changing a
preference must preserve existing source ownership, native episode numbering,
paths, tags and exclusions. Migration 250 adds only the default-Automatic
ImportLists column. Native AniList/MAL numbers must never become TVDB numbers.
An internal TVDB placeholder and a native provider ID are different values.

Run the solution build before focused tests, for example:

```sh
dotnet test src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj -c Debug --no-build --filter "FullyQualifiedName~ImportListIdentity|FullyQualifiedName~ImportListMetadataSource|FullyQualifiedName~ImportListSnapshot"
dotnet test src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj -c Debug --no-build --filter "FullyQualifiedName~ImportListMetadataSourceResourceFixture"
```

These commands describe reproducible checks; running them is required before
claiming results for another change. The prepared 1.0.5 source has its own
executed build, native/frontend, candidate runtime/browser and security evidence
in the [new ledger](docs/releases/1.0.5-verification.md). The 833 focused tests
overlap the 7,998 passing native cases; do not add those counts together.

Run the new complete-snapshot fixture against an explicitly selected built image:

```sh
python3 docker/tests/import-snapshots.py --image tanukarr:verified \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report import-snapshots-report.json
```

It uses owned generated AniList/MAL responses, not private accounts. Verify the
JSON's image ID, actual checks, failure and cleanup fields. Local candidates
do not establish hosted CI, public release assets or an owner deployment.

To reproduce the Docker workflow's new deterministic provider/search checks,
use an already built image and the pinned Python fixture image. On Debian/Ubuntu:

```sh
sudo apt-get update
sudo apt-get install --yes --no-install-recommends openssl ffmpeg
docker pull python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a
python3 docker/tests/anime-groups.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --timeout 300 --report groups-report.json
python3 docker/tests/nyaa-search.py --image tanukarr:local --fixture \
  --report nyaa-fixture-report.json
python3 docker/tests/series-packs.py --image tanukarr:local \
  --mock-image python:3.13-alpine@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a \
  --report series-packs-report.json
```

These scripts own disposable data and use synthetic local AniList/RSS responses;
mandatory CI does not need a live public catalogue/indexer response or private
provider credentials. OpenSSL and FFmpeg with `libx264` support the generated
grouping catalogue/media test; Python 3 and Docker support the pack fixture.
The separate multi-season fixture verifies v3/v5 complete and per-season
pack-only results without downloads. See the
[container prerequisites and scope](docker/tests/README.md#docker-workflow-fixture-prerequisites).

## Backend tests

Run tests after a solution build, using `--no-build`. For example:

```sh
dotnet test src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj -c Debug --no-build --filter "TestCategory!=IntegrationTest"
dotnet test src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj -c Debug --no-build
dotnet test src/Sonarr.Http.Test/Sonarr.Http.Test.csproj -c Debug --no-build
dotnet test src/NzbDrone.Common.Test/Sonarr.Common.Test.csproj -c Debug --no-build --filter "FullyQualifiedName~SonarrCloudRequestPolicyFixture|FullyQualifiedName~CleanseLogMessageFixture"
```

Other projects cover Common, Host, Update, Libraries, Windows, Mono, Integration
and Automation. Removing Core's category filter runs its online integration tests;
Common also contains online HTTP tests. Record unavailable services, ignored cases
and platform exclusions separately from failures. The Integration project starts
instances with temporary configuration and allocated ports. Review PostgreSQL
environment settings before running database tests so they use an owned server.

Automation needs Chrome and a matching ChromeDriver. The project-pinned driver can
differ from the installed browser; the October 12 run supplied an external matching
ChromeDriver 154. Browser tests use their own instances. Optional Playwright checks
are described in the [container test guide](docker/tests/README.md).

For [anime season groups](docs/anime-season-groups.md), exercise relationship
resolution, stored membership and generated paths after the solution build:

```sh
dotnet test src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj -c Debug --no-build --filter "FullyQualifiedName~AnimeGroup|FullyQualifiedName~BuildFilePathFixture"
```

Include mixed TV/movie chains, movie-only sagas, branches/cycles, unsupported
formats, request failures, manual and Disabled modes, same-kind duplicate slots
and generated-path overlaps. Exercise all five naming expressions, strict arc
subtitle selection, style overrides, malformed tokens and native filenames that
must not parse a fabricated logical season. API checks must
cover both v3 and v5 and older clients that omit grouping fields. Native
catalogue IDs, episode counts/numbers and existing explicit paths must survive
refresh and edits. A grouped-view test is separate from proving that native
filenames import and rescan correctly under generated season and movie leaves;
test legacy naming fields and typed custom-list membership through the real
unsaved AniList Test/action routes without saving a provider. Record
which kind actually ran in the
[grouping verification checkpoint](docs/anime-season-groups-verification-2026-10-13.md).

## Compression and container tests

For 1.0.4, run [backup/task verification](docker/tests/README.md#backup-task-timestamps)
and the [real local Jellyfin watched fixture](docker/tests/README.md#jellyfin-watched-state)
against the exact candidate image. Verify explicit Played increments, V3/V5
watched resources, native file matching, selected user/library isolation,
monitoring ownership, opt-out and persistence. Separate controlled provider
writer tests from private external-account execution. A fake successful mutation
response does not establish a live account write. The new database-backed backup
fixture is `FullyQualifiedName~BackupTaskFixture`; its cases must pass after a
solution build.

For the 1.0.3 quality/codec work, test actual profile-backed global summaries,
mixed versus unlimited values, omitted versus explicit-null updates and invalid
batch retention on both API versions. Codec regressions must preserve actual
bytes and physical indexer limits while normalizing profile decisions only once.
Mounted UI checks should exercise hourly conversion, mixed placeholders,
clearing/validation, failed save with retained edits and successful retry. Use
the [quality/codec container fixture](docker/tests/README.md#editable-quality-limits-and-codec-size-decisions)
on the exact image and keep its receipt separate from frontend test results.

```sh
python3 docker/compression/test_worker.py
SONARR_COMPRESSION_INTEGRATION=1 python3 docker/compression/test_worker.py
docker build -t tanukarr:local .
python3 docker/tests/smoke.py --image tanukarr:local --report smoke-report.json
python3 docker/tests/functional-security.py --image tanukarr:local --report functional-security-report.json
```

The second worker command requires FFmpeg/FFprobe and uses generated videos.
In PowerShell, set `$env:SONARR_COMPRESSION_INTEGRATION = '1'` before invoking
Python. Hardware VAAPI availability is separate from software-encoding coverage.
Container tests create disposable configuration and media. `--anilist` enables
public AniList checks; it does not use a private account or start a real download.

CI runs the solution build, offline Core/API/HTTP tests, cloud/log-sanitizer tests,
generated-video worker tests and frontend checks. The Docker workflow builds amd64
and runs the required disposable runtime fixtures, including quality/codec,
backup/task integrity and actual local Jellyfin watched checks. Thirteen reports
identify one tested image before publication. Published architectures and actually executed
runtime checks are recorded in the release verification report; configured
support for another architecture is not an executed test or a published artifact.
A CI pass does not imply that every online or platform suite ran.

## Release and installation documentation

The public project is
[KvasExpert/Tanukarr](https://github.com/KvasExpert/Tanukarr). The **1.0.5** target
uses Git tag `v1.0.5` and container tag `ghcr.io/kvasexpert/tanukarr:1.0.5` once
verified publication completes. Its [verification ledger](docs/releases/1.0.5-verification.md)
records pending and executed checks separately. The preceding
[1.0.4 publication follow-up](docs/releases/1.0.4-publication.md) retains that
release's actual hosted checks, artifact identities and remaining findings.
The [Docker installation guide](docs/docker-installation.md) contains complete
Compose YAML, GHCR pulls, storage, permissions, authentication, updates and the
release-image archive fallback. Maintain that guide and
[prepared release notes](docs/releases/1.0.5.md) when changing the published deployment.
Preserve earlier release notes and dated verification reports as historical
checkpoints; do not apply their results to a new image without running checks.

Keep the Tanukarr product release version separate from the inherited Sonarr
core/API compatibility version. Preserve the GPL license, corresponding source
and original feature authors in every distribution. A release requires executed
build/container checks and an identified image digest; creating a Git tag or
writing a workflow alone does not verify the published image.

Tanukarr continues the earlier releases. Archive prose uses the current product
name, while published artifact filenames and numeric evidence retain their
original identities in linked manifests and release pages. The rename does not authorize changes to Sonarr's
assembly names, database/configuration paths, migration IDs or v3/v5 API routes.
Keep original authorship, copyright and GPL notices in source and image assets.
When changing Tanuki logo assets, retain the supplied artwork's recorded
provenance and document any artist/license information actually provided.

## Documentation and attribution

Write repository documentation in English. Preserve proper names, quoted titles,
licenses and localization data. Update the [documentation index](docs/README.md)
and feature guide when behavior changes; keep dated reports as historical evidence.

For adapted features, name the source repository, original commit, authors and
co-authors, then describe local changes. Fork ownership and commit authorship are
different roles. Preserve Co-Authored-By trailers and update
[CONTRIBUTORS.md](CONTRIBUTORS.md) when rebase or consolidation obscures source
authorship.

For AI work, record the provider, tool and model identifier when known from the
session or source declaration. If a model was not recorded, say so; do not infer
it from a Codex or Claude display name. Historical model labels preserve
attribution and do not establish which runtime model executed a session.

State what was verified: **compiled only**, **tests passed**, or **observed against
an authorized real library**. Include commands, failures/skips and limitations.
Do not describe an unpatched dependency as fixed because its known attack path
is excluded, or apply an old report's results to an untested new build.

## Reports and suggestions

Report Tanukarr issues to
[this project's tracker](https://github.com/KvasExpert/Tanukarr/issues). Include the
Tanukarr release version, commit/image digest, internal runtime version,
provider, series/release involved, expected behavior and sanitized logs. The
original [Sonarr Pro repository](https://github.com/KakarottoCake/Sonarr-Pro) is a
credited source fork; its releases do not identify this project's builds.

Use [SECURITY.md](SECURITY.md) for security reports. Contact upstream only when the
behavior also affects upstream Sonarr. This fork makes no response-time promise
on behalf of upstream maintainers.
