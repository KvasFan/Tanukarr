import { useMemo } from 'react';
import isSameSeriesIdentity, {
  SeriesIdentity,
} from 'Series/isSameSeriesIdentity';
import useSeries from 'Series/useSeries';

export function useExistingSeriesEntry(
  identity: number | SeriesIdentity | undefined
) {
  const { data: series = [] } = useSeries();

  return useMemo(() => {
    if (identity == null) {
      return undefined;
    }

    const candidate =
      typeof identity === 'number' ? { tvdbId: identity } : identity;
    return series.find((s) => isSameSeriesIdentity(s, candidate));
  }, [identity, series]);
}

function useExistingSeries(identity: number | SeriesIdentity | undefined) {
  return !!useExistingSeriesEntry(identity);
}

export default useExistingSeries;
