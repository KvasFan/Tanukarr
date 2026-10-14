import { useEffect, useState } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { setTranslations } from 'Utilities/String/translate';

interface TranslationsResponse {
  strings: Record<string, string>;
}

export function useTranslations() {
  const { data, ...result } = useApiQuery<TranslationsResponse>({
    path: '/localization',
    queryOptions: {
      staleTime: Infinity,
      gcTime: Infinity,
    },
  });
  const [appliedData, setAppliedData] = useState<TranslationsResponse>();

  useEffect(() => {
    if (data) {
      setTranslations(data.strings);
      setAppliedData(data);
    }
  }, [data]);

  return {
    ...result,
    isFetched: result.isFetched && !!data && appliedData === data,
    data,
  };
}
