import { MetadataSource } from 'AddSeries/AddNewSeries/useAddSeries';

export const metadataSources: {
  key: MetadataSource;
  value: string;
  url: string;
  description: string;
}[] = [
  {
    key: 'tvdb',
    value: 'TheTVDB',
    url: 'https://www.thetvdb.com',
    description: 'MetadataSourceTvdbDescription',
  },
  {
    key: 'tmdb',
    value: 'TMDB',
    url: 'https://www.themoviedb.org',
    description: 'MetadataSourceTmdbDescription',
  },
  {
    key: 'aniList',
    value: 'AniList',
    url: 'https://anilist.co',
    description: 'MetadataSourceAniListDescription',
  },
  {
    key: 'myAnimeList',
    value: 'MyAnimeList',
    url: 'https://myanimelist.net',
    description: 'MetadataSourceMyAnimeListDescription',
  },
  {
    key: 'tvMaze',
    value: 'TVmaze',
    url: 'https://www.tvmaze.com/api',
    description: 'MetadataSourceTvMazeDescription',
  },
];

export function orderedMetadataSources(
  order: MetadataSource[] = [],
  disabled: MetadataSource[] = []
) {
  return [...new Set([...order, ...metadataSources.map(({ key }) => key)])]
    .filter((key) => !disabled.includes(key))
    .flatMap((key) => metadataSources.filter((source) => source.key === key));
}
