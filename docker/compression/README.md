# Per-show compression worker

Tanukarr exposes authenticated compression controls on each series page. The
optional worker runs as the media owner on a Linux host, using host-installed
FFmpeg and ffprobe. It is a separate service; the Tanukarr runtime image does not
include or start this worker. A private Unix socket carries control requests
from Tanukarr. The worker opens no TCP listener and accepts no SSH credential,
shell command or Docker socket.

See the [documentation index](../../docs/README.md) for feature guides and
[CONTRIBUTORS.md](../../CONTRIBUTORS.md) for feature provenance and AI attribution.

## Installation and permissions

Install Python 3, FFmpeg, ffprobe and the `nice`/`ionice` utilities on the Linux
host. Software modes require FFmpeg's `libx264` encoder. Set up the worker as the
same unprivileged media UID/GID used by Tanukarr, with access to the registered
media files. Paths returned by Tanukarr must also identify the same files on the
host: mount media at matching absolute paths or arrange equivalent host paths.
The worker checks that a file lies inside its registered series folder and one
configured media root.

Example worker configuration (keep outside the public repository):

```json
{
  "socket": "/home/media/tanukarr/compression-shared/worker.sock",
  "stateDirectory": "/home/media/tanukarr/compression-state",
  "sonarrConfig": "/home/media/tanukarr/config/config.xml",
  "sonarrUrl": "http://127.0.0.1:6970",
  "mediaRoots": ["/mnt/media", "/mnt/media2"],
  "scriptIndex": "/home/media/.ghost_index"
}
```

Run `python3 worker.py --config /path/to/worker-config.json` as an unprivileged
media user. Mount `compression-shared` into Tanukarr at `/compression` so the
default container-side socket is `/compression/worker.sock`. The socket is mode
`0660`; its parent directory must permit traversal by Tanukarr's media UID/GID.
Optionally set `SONARR_COMPRESSION_SOCKET` to another container-side socket path.
Keep that directory private to these services: filesystem permissions are the
worker socket's access control.

`sonarrConfig` is a host-readable `config.xml` containing the master API key, and
`sonarrUrl` must reach the corresponding Tanukarr instance from the worker. The
worker reads registered files and requests rescans through `/api/v5`. Protect
the JSON configuration, Tanukarr configuration and worker state from other users.
Do not put their credentials or contents in a public repository.

Use a systemd service with `KillMode=control-group`, `UMask=0007`,
`Restart=on-failure`, and the media user/group. Keep the worker state and
configuration private. Only one worker instance should use a socket/state
directory. Restarted jobs become interrupted; starting again skips files
already processed. A shared FileSaver/Phantom lock avoids concurrent script
encodes when the other tools honor the same advisory lock. The queue admits up
to ten active or queued shows and processes one show at a time. Only registered
episode files are eligible.

## Presets and file handling

Software presets use H.264 (x264) CRF 23 and fast/slow, at low process priority with at
most two encoding threads. Hardware fast/slow target Intel Haswell VAAPI H.264.
They are available only when the worker's startup encoding probes succeed for
both presets. For that setup, add the media user to the render group and install
the i965 VAAPI driver; the worker uses `/dev/dri/renderD128` and
`LIBVA_DRIVER_NAME=i965`. Hardware HEVC is not advertised on Haswell. FileSaver and Phantom
choices read the installed scripts' CRF/preset/QP and minimum-size settings and
use those settings through the same safer runner. Their CLI scripts remain
untouched, and their existing processed-file index is respected and updated.
These options do not invoke the scripts' Discord notifications.

Files retain their original filename/container. Eligible extensions are `.mkv`,
`.mp4` and `.m4v`. Audio, subtitles, attachments, chapters and stream dispositions
are copied; unsupported containers, HDR, high bit-depth, HEVC/AV1, multiple video
tracks or embedded cover art, and previously processed files are skipped.
Probing, encoding and validation restrict FFmpeg input to local files and the
Matroska and MOV/MP4 demuxers, so a disguised streaming playlist cannot request
network resources. Unsupported extensions are skipped before probing. Output must
save at least 2%. Validation checks the expected video codec, resolution,
duration, stream types, non-video codecs, languages, dispositions and chapter
count; the video and audio must decode successfully before an atomic
replacement. The source and its live Tanukarr association are
rechecked at replacement. File mode, owner/group and modification time are
preserved. Hard-linked torrent originals are preserved.
Cancellation keeps the active original; completed encodes remain. Completion
requests a Tanukarr rescan to refresh file sizes and codec information.

## Verification and limits

Run the safety suite from the repository root on Linux:

```sh
python3 docker/compression/test_worker.py
```

The generated-video integration cases require host FFmpeg/ffprobe and are
opt-in:

```sh
SONARR_COMPRESSION_INTEGRATION=1 python3 docker/compression/test_worker.py
```

These cases create disposable Matroska/MP4 videos, test software compression,
stream preservation and hardlink safety, and verify that a disguised DASH
playlist sends no request to a local fixture HTTP server. The normal suite skips
those cases when the environment variable is unset. Hardware presets are tested
only if the runtime probe marks them available.

The [2026-10-12 validation report](../../docs/security-functional-validation-2026-10-12.md)
records 29 passing worker tests with generated media. It did not test a real
library or VAAPI hardware. The demuxer/protocol restrictions address the
confirmed playlist network-access issue; they do not establish that FFmpeg has
no other vulnerabilities or constitute fuzzing of native decoders. Maintain the
host's FFmpeg packages as well as the Tanukarr image.
