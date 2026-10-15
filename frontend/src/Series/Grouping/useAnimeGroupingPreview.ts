import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { AnimeGroupingFields } from 'Series/Series';

export interface AnimeGroupingPreview extends AnimeGroupingFields {
  status:
    | 'resolved'
    | 'existing'
    | 'manual'
    | 'disabled'
    | 'unrelated'
    | 'unsupported'
    | 'ambiguous'
    | 'unavailable'
    | 'limitReached';
}

export default function useAnimeGroupingPreview(
  metadataSource: string | undefined,
  foreignId: string | undefined,
  enabled: boolean
) {
  return useApiQuery<AnimeGroupingPreview>({
    path: '/series/grouping',
    queryParams: { metadataSource, foreignId },
    queryOptions: {
      enabled:
        enabled &&
        (metadataSource === 'aniList' ||
          metadataSource === 'myAnimeList' ||
          metadataSource === 'tmdbMovie') &&
        !!foreignId,
      staleTime: 60000,
      retry: false,
    },
  });
}
