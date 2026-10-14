import Series from './Series';

export default function mergeSeriesUpdate(
  series: Series,
  updated: Partial<Series>
): Series {
  return {
    ...series,
    ...updated,
    statistics: updated.statistics ?? series.statistics,
    seasons:
      updated.seasons?.map((season) => {
        const previous = series.seasons.find(
          (item) => item.seasonNumber === season.seasonNumber
        );
        return previous
          ? {
              ...previous,
              ...season,
              statistics: season.statistics ?? previous.statistics,
            }
          : season;
      }) ?? series.seasons,
  };
}
