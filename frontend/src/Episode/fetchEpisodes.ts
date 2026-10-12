import fetchJson, { FetchJsonOptions } from 'Utilities/Fetch/fetchJson';
import Episode from './Episode';

// Keep even ten-digit IDs comfortably below HTTP request-line limits.
const BATCH_SIZE = 100;

async function fetchEpisodes(
  options: FetchJsonOptions<unknown>
): Promise<Episode[]> {
  const [path, query] = options.path.split('?');
  const params = new URLSearchParams(query);
  const ids = [...new Set(params.getAll('episodeIds'))];

  if (ids.length <= BATCH_SIZE) {
    return fetchJson<Episode[], unknown>(options);
  }

  params.delete('episodeIds');
  const episodes: Episode[] = [];

  // Sequential batches bound load and share the query's cancellation signal.
  // Return only after all succeed so the cache never receives partial results.
  for (let offset = 0; offset < ids.length; offset += BATCH_SIZE) {
    const batchParams = new URLSearchParams(params);
    ids.slice(offset, offset + BATCH_SIZE).forEach((id) => {
      batchParams.append('episodeIds', id);
    });

    const batch = await fetchJson<Episode[], unknown>({
      ...options,
      path: `${path}?${batchParams}`,
    });
    episodes.push(...batch);
  }

  return episodes;
}

export default fetchEpisodes;
