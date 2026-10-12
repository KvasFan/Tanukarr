import Series from 'Series/Series';

export type SeriesIdentity = Partial<
  Pick<Series, 'metadataSource' | 'foreignId' | 'tvdbId'>
>;

export default function isSameSeriesIdentity(
  first: SeriesIdentity,
  second: SeriesIdentity
) {
  const source = (first.metadataSource || 'tvdb').toLowerCase();

  if (source !== (second.metadataSource || 'tvdb').toLowerCase()) {
    return false;
  }

  if (source === 'tvdb') {
    return (first.tvdbId ?? 0) > 0 && first.tvdbId === second.tvdbId;
  }

  const foreignId = first.foreignId?.trim();
  return !!foreignId && foreignId === second.foreignId?.trim();
}
