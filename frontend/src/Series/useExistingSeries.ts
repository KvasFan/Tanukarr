import { useMemo } from 'react';
import isSameSeriesIdentity, {
  SeriesIdentity,
} from 'Series/isSameSeriesIdentity';
import useSeries from 'Series/useSeries';

function useExistingSeries(identity: number | SeriesIdentity | undefined) {
  const { data: series = [] } = useSeries();

  return useMemo(() => {
    if (identity == null) {
      return false;
    }

    const candidate =
      typeof identity === 'number' ? { tvdbId: identity } : identity;
    return series.some((s) => isSameSeriesIdentity(s, candidate));
  }, [identity, series]);
}

export default useExistingSeries;
