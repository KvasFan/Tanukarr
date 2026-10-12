import Series from 'Series/Series';

type MetadataIdentity = Partial<
  Pick<Series, 'metadataSource' | 'foreignId' | 'tvdbId' | 'tmdbId' | 'imdbId'>
>;

export default function getMetadataProvider(series: MetadataIdentity) {
  const source = (series.metadataSource || 'tvdb').toLowerCase();
  const foreignId = series.foreignId?.trim();
  const positiveId = (value: string | number | undefined) =>
    value != null && /^[1-9]\d*$/.test(String(value))
      ? String(value)
      : undefined;
  const nativeId = positiveId(foreignId);

  switch (source) {
    case 'anilist':
      return {
        name: 'AniList',
        externalId: nativeId,
        url: nativeId ? `https://anilist.co/anime/${nativeId}` : undefined,
      };
    case 'myanimelist':
      return {
        name: 'MyAnimeList',
        externalId: nativeId,
        url: nativeId ? `https://myanimelist.net/anime/${nativeId}` : undefined,
      };

    case 'tmdb': {
      const id = nativeId ?? positiveId(series.tmdbId);
      return {
        name: 'TMDB',
        externalId: id,
        url: id ? `https://www.themoviedb.org/tv/${id}` : undefined,
      };
    }

    case 'imdb': {
      const id = foreignId ?? series.imdbId;
      const validId = id && /^tt\d+$/.test(id) ? id : undefined;
      return {
        name: 'IMDb',
        externalId: validId,
        url: validId ? `https://www.imdb.com/title/${validId}/` : undefined,
      };
    }

    case 'tvdb': {
      const id = positiveId(series.tvdbId);
      return {
        name: 'TheTVDB',
        externalId: id,
        url: id ? `https://www.thetvdb.com/?tab=series&id=${id}` : undefined,
      };
    }

    default:
      return { name: 'Unknown', externalId: undefined, url: undefined };
  }
}
