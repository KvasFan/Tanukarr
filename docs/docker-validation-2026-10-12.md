# Initial verified Docker build and deployment — October 12, 2026

This historical report records the first actual Docker build and deployment,
at `130831c3b15ebca879d78fe30904b5840b3d6a3d`. The image and deployment after the
security corrections are documented in
[functional/security validation](security-functional-validation-2026-10-12.md).
The original export with the checksum below is preserved as
`_artifacts/sonarr-weeb-local-linux-amd64-build-130831c3b.tar`; the filename without
a commit identifier holds the final image.

The repository was built into `sonarr-weeb:local` and run as a Linux `amd64`
container. At this checkpoint, the Compose deployment `sonarr-pro` was running
at [http://localhost:6970](http://localhost:6970), also accessible from Windows.
Local configuration and media directories were `config/` and `media/`, initially
empty. No real library or download client was used.

See the [documentation index](README.md) for current operation instructions.
The Docker/CI behavior combines the work of KakarottoCake, Codex source commits
from the ikatun fork, Ivo Katunaric / ikatun, and local OpenAI Codex (GPT-6)
packaging/deployment validation. Upstream Sonarr and the other feature
contributors retain their authorship; complete credits and recorded AI model
labels are in [CONTRIBUTORS.md](../CONTRIBUTORS.md).

## Image and environment

- Branch: `integration/sonarr-latest-anilist`; functional integration starting
  point `e12e717e2`, followed by the packaging and deployment-tool corrections
  recorded here.
- Image ID: `sha256:570b34e50e28ee6c65336387ac4467b4f32764553fe467055f97d4959b8ead6f`.
- Runtime version: `10.0.0.30175`; SQLite migration `242`.
- .NET SDK `10.0.401`, Node 24/Vite frontend, Docker Engine `29.9.0`,
  Compose `v5.6.0`.
- Docker was installed in the dedicated WSL 2 distribution `SonarrWeebBuild`,
  Ubuntu `24.04.5`. Docker Desktop was not installed.
- Exported archive:
  `_artifacts/sonarr-weeb-local-linux-amd64-build-130831c3b.tar`,
  127,636,992 bytes.
- Archive SHA-256:
  `08f7676f65cc544c97a60bd17a84d7fd5e898c5609d4bb01ac7a325bea3bf938`.

To import this historical image on another Linux `amd64` host:

```sh
docker load -i sonarr-weeb-local-linux-amd64-build-130831c3b.tar
```

## Checks performed

| Check | Result |
| --- | --- |
| `docker compose --progress plain build sonarr-pro` | Passed; frontend and Release/Posix backend published |
| Core in the Docker build | 6,096 passed, 27 skipped, 0 failed |
| API in the Docker build | 56 passed, 0 failed |
| HTTP in the Docker build | 57 passed, 0 failed |
| Shared Sonarr cloud policy in the Docker build | 12 passed, 0 failed |
| Frontend tests in the Docker build | 11 passed, 0 failed |
| Lint, Stylelint, typecheck and Vite in the Docker build | Passed |
| Compression worker, Python on Linux | 25 passed, 1 skipped; no real media conversion |
| Isolated container, `/ping`, HTML and compiled JavaScript | Passed |
| V5 API, new SQLite, initially empty series and queue | Passed |
| Native `/app/ffprobe` executable as UID 1000 | Passed |
| UID/GID 1000, `002` umask, writable volumes | Passed; files `664`, directories `775` on a Linux filesystem |
| Masked synthetic TMDB key, API alias and language on a legacy save | Passed using only temporary data |
| Live AniList lookup from the container | AniList identity retained; no series added |
| Container restart, database/settings/media persistence | Passed |
| Included Compose deployment, access from Windows | Passed on `127.0.0.1:6970` |
| WSL distribution and Docker service restart | Image retained; container restarted automatically |
| Availability after idle time without new WSL commands | Verified from Windows; same instance and start time |

Runtime-test data and their containers were removed. The Compose deployment
remained available with new data and no saved synthetic TMDB keys. Linux
filesystem checks verify POSIX permissions; bind mounts on Windows also follow
host filesystem ACLs.

## Required corrections

The NuGet package ships Linux `ffprobe` without its executable bit. Docker build
and CI make it executable before tests. Frontend instructions use Vite, and
startup uses the locally integrated image.

Compose uses concrete local directories instead of `/path/to/media`, binds only
to loopback by default, and permits configuring paths, UID/GID, timezone and
umask through `SONARR_PRO_*` variables. Configuration, media, `.env` files and
Python caches are excluded from Git and the Docker build context.

The smoke test resolves the dynamic port again after a restart because Docker
can assign a different port. The Docker workflow uses the full smoke test,
rather than only `/ping`; external AniList lookup remains optional in CI.
Later security validation added the isolated functional/security harness.

In WSL, containerd's persistent storage path was aligned between the original
startup and the systemd service. Images and deployment survive restart.
Systemd services alone do not keep a WSL distribution running: the launcher
opens a hidden foreground session to keep the service available. This WSL
behavior is described in the
[Microsoft documentation](https://learn.microsoft.com/en-us/windows/wsl/systemd/).

## Operation commands

On this Windows host, from the repository directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docker/start-wsl.ps1
```

The launcher reuses the existing WSL session. Add `-Build` to rebuild the image
before startup. Automatic startup at Windows login was not configured; run the
launcher after a PC reboot.

On a host with the Docker CLI:

```sh
mkdir -p media
docker compose up -d --build
python3 docker/tests/smoke.py --image sonarr-weeb:local --anilist
```

To stop the local deployment:

```powershell
wsl.exe -d SonarrWeebBuild --cd /mnt/c/Users/tirael/Documents/SonarrWeeb --exec docker compose stop
```

The `/media` volume was empty at validation. For a real library, set
`SONARR_PRO_MEDIA_PATH` to the desired path and configure root folders, clients
and categories in the application. Downloads, media imports, private lists and
ARM64 were not tested in this deployment.

Full logs and reports are in `%LOCALAPPDATA%\SonarrWeebTools`:
`docker-build-final.log`, `docker-smoke-report-final.json`,
`docker-deployment-report.json` and `compression-linux-tests.log`.
No image was published to a registry. Consult the
[final validation report](security-functional-validation-2026-10-12.md) before
using this historical image; that later image includes the security corrections.
