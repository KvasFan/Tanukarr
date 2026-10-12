# Container smoke verification

Build the image, then verify its runtime using Python 3 and the Docker CLI:

```sh
docker build --progress=plain -t sonarr-weeb:local .
python docker/tests/smoke.py --image sonarr-weeb:local --report smoke-report.json
```

The script needs a running Docker Engine configured for Linux containers. On
Windows, use `--docker C:\path\to\docker.exe` if the executable is not on PATH.
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
status, initialized SQLite, the packaged native FFprobe, an empty series list and queue, UID/GID 1000 and the
application's `002` umask, file/directory creation in both mounts, metadata route
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

The older `run-all.sh` and `mono/` scripts verify packaging-specific regressions
and are separate from this runtime smoke test.
