import Series, { MediaType } from 'Series/Series';

export default function getMediaType(
  series: Partial<
    Pick<Series, 'mediaType' | 'metadataSource' | 'animeGroupMemberKind'>
  >
): MediaType {
  if (series.mediaType) return series.mediaType;

  const source = series.metadataSource?.toLowerCase();

  if (source === 'tmdbmovie' || source === 'omdb') return 'movie';
  if (series.animeGroupMemberKind === 'movie') return 'movie';
  if (series.animeGroupMemberKind === 'special') return 'special';

  return 'series';
}
