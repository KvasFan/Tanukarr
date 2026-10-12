# Contributing to SonarrWeeb

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

## Compression and container tests

```sh
python3 docker/compression/test_worker.py
SONARR_COMPRESSION_INTEGRATION=1 python3 docker/compression/test_worker.py
docker build -t sonarr-weeb:local .
python3 docker/tests/smoke.py --image sonarr-weeb:local --report smoke-report.json
python3 docker/tests/functional-security.py --image sonarr-weeb:local --report functional-security-report.json
```

The second worker command requires FFmpeg/FFprobe and uses generated videos.
In PowerShell, set `$env:SONARR_COMPRESSION_INTEGRATION = '1'` before invoking
Python. Hardware VAAPI availability is separate from software-encoding coverage.
Container tests create disposable configuration and media. `--anilist` enables
public AniList checks; it does not use a private account or start a real download.

CI runs the solution build, offline Core/API/HTTP tests, cloud/log-sanitizer tests,
generated-video worker tests and frontend checks. The Docker workflow builds amd64
and runs both container scripts. SonarrWeeb 0.1.0 publishes Linux amd64; configured
support for another architecture is not an executed test or a published artifact.
A CI pass does not imply that every online or platform suite ran.

## Release and installation documentation

The public project is
[KvasExert/SonarrWeeb](https://github.com/KvasExert/SonarrWeeb). Release **0.1.0** uses
Git tag `v0.1.0` and container tag `ghcr.io/kvasexert/sonarrweeb:0.1.0`.
The [Docker installation guide](docs/docker-installation.md) contains complete
Compose YAML, GHCR pulls, storage, permissions, authentication, updates and the
release-image archive fallback. Maintain that guide and
[release notes](docs/releases/0.1.0.md) when changing the published deployment.
Preserve earlier release notes and dated verification reports as historical
checkpoints; do not apply their results to a new image without running checks.

Keep the SonarrWeeb product release version separate from the inherited Sonarr
core/API compatibility version. Preserve the GPL license, corresponding source
and original feature authors in every distribution. A release requires executed
build/container checks and an identified image digest; creating a Git tag or
writing a workflow alone does not verify the published image.

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

Report SonarrWeeb issues to
[this project's tracker](https://github.com/KvasExert/SonarrWeeb/issues). Include the
SonarrWeeb release version, commit/image digest, internal runtime version,
provider, series/release involved, expected behavior and sanitized logs. The
original [Sonarr Pro repository](https://github.com/KakarottoCake/Sonarr-Pro) is a
credited source fork; its releases do not identify this project's builds.

Use [SECURITY.md](SECURITY.md) for security reports. Contact upstream only when the
behavior also affects upstream Sonarr. This fork makes no response-time promise
on behalf of upstream maintainers.
