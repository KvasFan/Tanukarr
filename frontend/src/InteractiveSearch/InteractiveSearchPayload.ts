interface EpisodeSearchPayload {
  episodeId: number;
}

interface SeasonSearchPayload {
  seriesId: number;
  seasonNumber: number;
}

interface CompleteSeriesSearchPayload {
  seriesId: number;
  completeSeries: true;
}

type InteractiveSearchPayload =
  | EpisodeSearchPayload
  | SeasonSearchPayload
  | CompleteSeriesSearchPayload;

export default InteractiveSearchPayload;
