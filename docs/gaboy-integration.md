# Additional features from the gaboy fork

Source: [gaboy498-byte/Sonarr-Pro](https://github.com/gaboy498-byte/Sonarr-Pro),
branch `investigate/multiseason-and-fakefilter`, reviewed at
`14eaaebf6267791c60c50a01b6d3109e18ccb53e`.

Its default `main` branch is an older copy of the original Sonarr Pro history.
The additional changes live on the investigation branch. The first six commits,
through `79ef6d2b17fe9dc985527f3062b0fa05363535a7`, are adapted here to the
current upstream APIs and the newer Kakarotto library behavior.

Matt Flowers / **gaboy498-byte** authored these additional commits. They build
on **KakarottoCake's Sonarr Pro** and the **upstream Sonarr contributors**.
Local adaptation and regression validation were performed with **OpenAI Codex
(GPT-6)**. See [contributor credits](../CONTRIBUTORS.md) for the full provenance,
including recorded historical AI attribution, and the
[documentation index](README.md) for related implementation and test reports.

## Multi-season downloads

- An explicit range such as `S01-S10` includes every intervening season.
  Separate season labels such as `S01 S10` include only the listed seasons.
  A `Complete Series` label retains the complete-range interpretation.
- Scene mappings are evaluated separately for each season. A search for a
  specific season keeps its requested episode selection.
- Import completion checks the expected episode IDs, preventing unrelated or
  duplicate imports from making a download appear complete.
- Legacy grabbed-episode membership can be reconciled for an explicitly mapped,
  complete, consecutive multi-season pack when the old IDs are a strict subset
  of the mapped IDs. It requires the same download and series. Partial packs,
  discrete season selections and saved manual episode choices are excluded.

The tracker reconstructs selected downloads from their recorded grabbed
episode IDs. Reconciliation does not broaden that selection or recover every
unselected season of an old pack automatically. If legacy history omitted wanted
episodes, select them explicitly through interactive import.

Automatic imports retain Kakarotto's protections: only grabbed episodes are
eligible; ambiguous files need interactive import; multi-season sources remain
available; qBittorrent downloads retain their isolated tracking tags.

## Import progress

The queue exposes waiting, importing, imported, rejected and failed states per
file and episode. Expand a queue row to inspect individual files and rejection
reasons. The queue progress bar shows the imported fraction once import starts;
episode status indicators show the corresponding import state.

Progress is keyed by download-client ID and download ID, so identical download
IDs on different clients do not share progress. It is held in memory and pruned
when the download leaves the queue. A restart reconstructs progress during the
next import attempt. Reporting failures cannot interrupt the import itself.

## Fake-release protection

When fake-release protection is enabled, executable or script extensions in a
release title are rejected before submission. An available torrent URL takes
priority over a magnet link so the file list can be inspected. A confirmed fake
release cannot fall back to a magnet. If the torrent cannot be fetched, an
available magnet remains a fallback and the skipped inspection is logged.

The investigation branch's final two commits, which retrieve magnet metadata,
are not adopted. They require `MonoTorrent 3.0.2-sonarrsecurity.1.rev0002`, which
was absent from the public NuGet feed at integration time. Official MonoTorrent
3.0.2 does not expose the required metadata-size limit or per-torrent settings
overload. Using it as a drop-in replacement would discard those safeguards.
This integration retains the existing official dependency and reports magnets
whose contents were not inspected.

## Commit provenance

All six adapted commits below were authored by Matt Flowers / gaboy498-byte.

| Commit | Adapted change |
| --- | --- |
| `9cd27f4` | Explicit multi-season ranges |
| `29c1872` | Safe multi-season mapping and import completion |
| `9dc7874` | Legacy grab membership reconciliation |
| `e37d5a2` | Per-episode import progress |
| `0d48e2e` | Multi-season size regression tests |
| `79ef6d2` | Fake-release submission routes and title checks |

Validation at the integration checkpoint is recorded in the
[first integration report](integration-2026-10-12.md). Later
[ikatun integration](ikatun-integration-2026-10-12.md) and
[functional/security validation](security-functional-validation-2026-10-12.md)
cover the combined behavior. These checks used test fixtures and isolated
instances, without live downloads, media moves or an existing production library.
