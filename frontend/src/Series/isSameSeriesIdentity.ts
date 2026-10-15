import Series from 'Series/Series';

export type SeriesIdentity = Partial<
  Pick<Series, 'metadataSource' | 'foreignId' | 'tvdbId'>
>;

export function getSeriesIdentityKey(identity: SeriesIdentity) {
  const source = (identity.metadataSource || 'tvdb').toLowerCase();

  if (source === 'tvdb') {
    return (identity.tvdbId ?? 0) > 0 ? `tvdb:${identity.tvdbId}` : undefined;
  }

  const foreignId = identity.foreignId?.trim();
  return foreignId ? `${source}:${foreignId}` : undefined;
}

export default function isSameSeriesIdentity(
  first: SeriesIdentity,
  second: SeriesIdentity
) {
  const key = getSeriesIdentityKey(first);
  return key != null && key === getSeriesIdentityKey(second);
}
