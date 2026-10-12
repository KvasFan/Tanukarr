import type Queue from 'typings/Queue';

export interface SeriesQueueDetails {
  count: number;
  episodesWithFiles: number;
}

type QueueEpisodeDetails = Pick<
  Queue,
  | 'seriesId'
  | 'trackedDownloadState'
  | 'episodeIds'
  | 'episodeIdsBySeason'
  | 'episodeIdsWithFiles'
>;

export default function getSeriesQueueDetails(
  queue: ReadonlyArray<QueueEpisodeDetails> | undefined,
  seriesId: number,
  seasonNumber?: number
): SeriesQueueDetails {
  const episodeIds = new Set<number>();
  const episodeIdsWithFiles = new Set<number>();

  const activeQueue = (queue ?? []).filter(
    (item) =>
      item.trackedDownloadState !== 'imported' && item.seriesId === seriesId
  );

  for (const item of activeQueue) {
    const matchingIds =
      seasonNumber == null
        ? item.episodeIds
        : item.episodeIdsBySeason[seasonNumber] ?? [];
    const existingIds = new Set(item.episodeIdsWithFiles);

    for (const episodeId of matchingIds) {
      episodeIds.add(episodeId);

      if (existingIds.has(episodeId)) {
        episodeIdsWithFiles.add(episodeId);
      }
    }
  }

  return {
    count: episodeIds.size,
    episodesWithFiles: episodeIdsWithFiles.size,
  };
}
