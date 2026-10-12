# Container verification

These reusable scripts verify disposable Linux containers. Feature attribution
is recorded in [CONTRIBUTORS.md](../../CONTRIBUTORS.md); the
[documentation index](../../docs/README.md) links the build, integration and
security reports.

## Startup smoke test

Build the image, then verify its runtime using Python 3 and the Docker CLI:

```sh
docker build --progress=plain -t sonarr-weeb:local .
python docker/tests/smoke.py --image sonarr-weeb:local --report smoke-report.json
```

The script needs Python 3 and a running Docker Engine configured for Linux
containers. Run it where temporary bind-mount paths are visible to that Docker
host. On Windows with Docker Desktop, use `--docker C:\path\to\docker.exe` if
the executable is not on PATH. For a Docker Engine installed inside WSL, run the
commands inside that WSL distribution with Linux Python and Docker; a Windows
temporary path is not automatically a valid mount source for that daemon.
It uses only Python's standard library and does not install or build anything.
Add `--anilist` to verify one live AniList lookup; it keeps the AniList identity
and checks that no series is added.

Each invocation creates its own new config and empty media directories under the
host's temporary directory. It seeds a random API key without printing it, mounts
those directories into a new container, and publishes an automatically assigned
port only on `127.0.0.1`. It never uses the Compose config or an existing library.
The temporary empty media directory is made writable so the test works when the
host UID differs from the container's UID 1000.

The checks cover HTTP readiness, the actual compiled UI JavaScript, V5 runtime
status, initialized SQLite, the packaged native ffprobe, an empty series list
and queue, UID/GID 1000 and the application's `002` umask, file/directory creation
in both mounts, metadata route
aliases and masked key saves, and persistence after a container restart. The
TMDB value is a synthetic test value; no TMDB request or real credential is used.
This verifies container startup and persistence, not a real library, indexer,
download client, download, import, or media transfer.

The default cleanup removes only the exact container ID created by the script
and its own temporary directory. `--keep` preserves those two resources for
inspection and prints their locations. Stop/remove the printed container before
removing the printed temporary directory. The retained `config.xml` contains
the temporary API key. A failure returns exit code 1 and can be saved using
`--report`; the JSON report contains no credentials.

The older `run-all.sh` and `mono/` scripts target historical Mono packages and
`_tests_linux`/`_output_linux` layouts. They are retained for upstream history;
they are not the .NET 10 runtime verification path and were not run in the
current validation.

## Functional and security checks

Run the isolated authentication, permissions, validation and persistence checks:

```sh
python3 docker/tests/functional-security.py --image sonarr-weeb:local \
  --report functional-security-report.json
```

Add `--anilist` to exercise a real public AniList lookup, native-series addition,
refresh, duplicate rejection and complete-series dry run against an empty local
Torznab fixture. Only the disposable instance is modified; no download is started.
The live path also verifies twelve rejected companion-key add requests across
v3/v5: custom paths, unregistered roots and implicit search flags must not change
the series database or schedule acquisition commands. The checks distinguish
membership status, anime airing status and custom lists.

```sh
python3 docker/tests/functional-security.py --image sonarr-weeb:local --anilist \
  --report functional-security-live-report.json
```

The default checks cover required Forms authentication, missing/incorrect API
keys, valid login, masked password, HttpOnly/SameSite cookie, login lockout,
invalid reset tokens and redirects. They also exercise explicit AllowedHosts,
DNS-rebinding Host variants, CORS, static-path traversal attempts, invalid JSON
and release-search scopes, companion endpoint allowlists and revocation, TMDB
key masking and route aliases, AniList schema fields, and restart persistence.
The restarted temporary database is checked for salted PBKDF2 password storage.

For a real browser run, install Playwright and its Chromium browser in an external
tool directory, then supply `--browser-node /absolute/path/to/node` and
`--playwright-module /absolute/path/to/node_modules/playwright`. Both arguments
are required together. For example, on a Linux host with Node/npm installed:

```sh
npm install --prefix /tmp/sonarr-ui-audit playwright@1.64.0
/tmp/sonarr-ui-audit/node_modules/.bin/playwright install chromium
python3 docker/tests/functional-security.py --image sonarr-weeb:local --anilist \
  --browser-node "$(command -v node)" \
  --playwright-module /tmp/sonarr-ui-audit/node_modules/playwright \
  --report /tmp/sonarr-ui-audit/reports/functional-security.json
```

Playwright 1.64.0 is the version recorded in the October 12 browser artifacts.
Chromium also requires the host's browser system libraries. These tools are
external test dependencies and are not added to the application package.
The Node helper
reads disposable credentials from stdin and redacts them from reports. It checks
login, navigation, provider attribution/links, native-series detail and lookup,
complete search and AniList list settings. The native-series/complete-search
scenario needs `--anilist`; the browser's lookup scenario requires network access
even if that flag is omitted. Browser reports and screenshots are
written alongside the specified report. These optional live checks require network
access. Docker CI runs both default scripts without live AniList or Playwright.

The harness also verifies that the app runs as UID 1000 with no effective Linux
capabilities and cannot write its executable, application directory or startup
script, with Docker's seccomp filter active and no privileged mode, Docker-socket
mount or host networking. The entrypoint initially uses root to assign volume
ownership before dropping privileges; the checks inspect the running application.
It does not use existing Compose configuration, real media, a download
client or a private AniList account. Cleanup removes only the instance and temporary
directories created by the invocation.

## Coverage and evidence

These scripts return exit code 1 on a failed check. `--report` records results and
the exercised options. Functional reports include the inspected image ID and
whether browser checks ran. `--keep` retains disposable resources and their
temporary credentials for debugging; handle them as private runtime data.
Screenshots are visual artifacts and should be reviewed before sharing.

The Dockerfile also runs frontend tests, lint, stylelint, typecheck and the Vite
build. The native amd64 backend stage runs Core/API/HTTP tests excluding
`IntegrationTest`, and the Common cloud-policy/log-sanitizer subset. These are
build checks, not every available .NET suite. Current whole-suite and platform
results are recorded in the
[functional and security validation report](../../docs/security-functional-validation-2026-10-12.md).

An image that passes these checks has demonstrated startup, isolated API/UI
behavior, permissions and persistence. Real downloads/imports, private AniList
OAuth synchronization, arbitrary decoder inputs, all Git history secrets and
VAAPI hardware remain outside this harness. The local validation used Linux
amd64; an arm64 build is not an arm64 runtime test.
