# Jellyfin library refresh

The dedicated **Jellyfin** connection is included in **Tanukarr 1.0.1** and was
introduced in SonarrWeeb 0.1.0.
It is separate from the existing **Emby / Jellyfin**
connection, which reports changed media paths. The immutable SonarrWeeb `v0.0.1`
release does not contain this connection. Use the published 1.0.1 image, a verified
`edge` image or build the current source; check publication status as described
in [Docker installation](docker-installation.md).

## Configure the connection

1. In Jellyfin, make sure the library contains media and has completed an initial
   library scan for every configured media folder. A newly created empty library
   can lack an indexed physical folder; Jellyfin can then accept a selected
   refresh without scanning anything.
   Tanukarr checks this condition during Test and before each refresh, and
   retains pending imports until the library has been initialized in Jellyfin.
2. In Jellyfin, open **Dashboard → API Keys** and create a key for Tanukarr.
3. Open Tanukarr at `http://localhost:6970`, then **Settings → Connect → + →
   Jellyfin**.
4. Enter the Jellyfin **Host**, **Port**, optional **Use SSL** and **URL Base**,
   and the **API Key**. Host is a hostname or IP address without a scheme or path;
   the default port is `8096`. Use the URL base when Jellyfin is served under a
   reverse-proxy path such as `/jellyfin`.
5. Load the **Library** dropdown and select exactly one Jellyfin library.
6. Choose **Refresh Mode** and **Refresh Interval**, then enable **On Import**
   and optionally **On Upgrade**. Use tags to restrict which Tanukarr series
   trigger this connection. Click **Test**, then **Save**.

Create another Jellyfin connection to refresh another library, even if it uses
the same server. Each saved connection maintains its own pending imports and
schedule. Avoid enabling both the old Emby / Jellyfin library-update connection
and the new connection for the same series unless you want both requests.

Jellyfin API keys grant administrator access. Tanukarr masks the saved key in
settings responses and sends it in an HTTP header. Use HTTPS outside a trusted
network. Redirects are rejected so a redirected request cannot forward the key
to another host. The refresh-state table does not contain the key.

## Refresh modes

| Mode | Behavior |
| --- | --- |
| Every imported media file | Submit a refresh after each eligible imported file. A failed request stays pending and is retried by the one-minute background task. |
| At intervals when imports are pending | At each configured tick, submit one refresh if eligible files have been imported since the previous successful tick. Multiple imports are batched. Idle ticks send no request. |
| Every import and at intervals | Submit immediately and also keep those imports pending for the next periodic tick. An immediate success does not consume the periodic work. |

The default is **Interval**, every **60 minutes**. The minimum interval is
**1 minute**. For example, with a 60-minute interval, importing several episodes
during that hour produces one refresh at the next tick. An hour with no imports
produces none. Timing is approximate: Tanukarr checks scheduled tasks every
30 seconds, and an already running scan or server load can delay completion.

The interval begins when the connection is enabled, its target changes, or its
refresh mode changes. Changing only the interval length keeps the current tick
origin. Switching modes preserves failed work and avoids replaying imports
already acknowledged by the previous mode. Pending
work and the last tick are stored in Tanukarr's database and survive a restart.
Rotating the API key preserves pending work. Selecting a different server or
library clears work associated with the previous target. Disabling the import
trigger or deleting the connection clears its pending work.

An import means a new downloaded file or a manual import from outside the series
directory, using Sonarr's normal **On Import** trigger. Upgrades participate when
**On Upgrade** is enabled. Rescanning files already inside a series directory,
grabs, renames, metadata-only updates and existing-library discovery do not count
as new imported media. One file containing multiple episodes creates one import
trigger. This connection does not download media or synchronize watch status.

## Selected-library refresh

The connection enumerates libraries through `GET /Library/VirtualFolders` and
stores the selected `ItemId`. Each refresh checks that the library still exists
and that its configured physical folders are present in the selected library's
folder listing, then submits:

```http
POST /Items/{libraryId}/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=Default&replaceAllMetadata=false&replaceAllImages=false
Authorization: MediaBrowser Token="<Jellyfin API key>"
```

Jellyfin recursively validates the selected library's folders and discovers new
files there. The connection never calls the server-wide `/Library/Refresh`
endpoint. Existing metadata and images are not requested to be replaced.

**HTTP 204 means that Jellyfin accepted the refresh into its queue.** It does
not mean the scan has finished. Tanukarr acknowledges the request after this
response; failures inside Jellyfin's asynchronous scan must be diagnosed in
Jellyfin's dashboard and logs. A timeout can leave it unclear whether Jellyfin
accepted the request, so a retry may produce a duplicate scan request.

If a library is deleted or renamed and its ID changes, select it again and test
the connection. A missing library causes a failure; there is no fallback to
refreshing every library. Connection and HTTP failures retain pending imports.
Tanukarr logs a warning with the connection ID and retries without preventing
new imports from being recorded.

## Docker networking and paths

Inside a Tanukarr container, `localhost` refers to that container. For two
services on the same Compose network, use the Jellyfin service name, for example
`jellyfin`, as Host and its internal port `8096`. For Jellyfin on another machine,
use the reachable hostname or LAN IP. A Jellyfin port published only on the
host's loopback address may not be reachable from another container.

Jellyfin must already have access to the library's media mount. This integration
sends the library ID, so Tanukarr and Jellyfin can use different mount paths;
no path mapping is required. The refresh cannot expose files that Jellyfin
cannot read.

## Verification and provenance

See the [Jellyfin verification report](jellyfin-verification-2026-10-12.md) for
the historical checks and limitations. The corrected Tanukarr 1.0.0 image also
passes nine disposable Jellyfin 12.2 checks with real generated-video imports,
selected-library refresh modes, one-minute ticks, restart persistence and retry
recovery; see the [release verification](releases/1.0.0-verification.md).
The reproducible disposable-server test is
[docker/tests/jellyfin.py](../docker/tests/jellyfin.py); it uses generated videos,
two separate libraries and an isolated Tanukarr configuration.

The existing Sonarr MediaBrowser implementation provided the integration
patterns. Original authors and the OpenAI Codex / GPT-6 adaptation are credited
in [CONTRIBUTORS.md](../CONTRIBUTORS.md). No external plugin or community fork was
copied into the project.

Official sources checked for this implementation:

- [Sonarr MediaBrowser integration](https://github.com/Sonarr/Sonarr/blob/develop/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs).
- [Jellyfin item refresh API, v12.2](https://github.com/jellyfin/jellyfin/blob/v12.2/Jellyfin.Api/Controllers/ItemRefreshController.cs).
- [Jellyfin library enumeration and rename API](https://github.com/jellyfin/jellyfin/blob/v12.2/Jellyfin.Api/Controllers/LibraryStructureController.cs).
- [Selected-folder refresh implementation](https://github.com/jellyfin/jellyfin/blob/v12.2/MediaBrowser.Providers/Manager/ProviderManager.cs).
- [Recursive folder validation](https://github.com/jellyfin/jellyfin/blob/v12.2/MediaBrowser.Controller/Entities/Folder.cs).

The API contracts were reviewed for Jellyfin 10.10.7, 10.11.8 and 12.2; executed
server checks are limited to the version recorded in the verification report.
