import { UndefinedInitialDataOptions, useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import fetchJson, {
  ApiError,
  FetchJsonOptions,
} from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import getQueryString, { QueryParams } from 'Utilities/Fetch/getQueryString';

export interface QueryOptions<T> extends FetchJsonOptions<unknown> {
  queryParams?: QueryParams;
  fetcher?: (options: FetchJsonOptions<unknown>) => Promise<Readonly<T>>;
  queryOptions?:
    | Omit<
        UndefinedInitialDataOptions<Readonly<T>, ApiError>,
        'queryKey' | 'queryFn'
      >
    | undefined;
}

const useApiQuery = <T>(options: QueryOptions<T>) => {
  const { queryKey, requestOptions } = useMemo(() => {
    const {
      path: path,
      queryOptions,
      queryParams,
      fetcher,
      ...otherOptions
    } = options;

    return {
      queryKey: queryParams ? [path, queryParams] : [path],
      requestOptions: {
        ...otherOptions,
        path: getQueryPath(path) + getQueryString(queryParams),
        headers: {
          ...options.headers,
          'X-Api-Key': window.Sonarr.apiKey,
          'X-Sonarr-Client': 'Sonarr',
        },
      },
    };
  }, [options]);

  return {
    queryKey,
    ...useQuery({
      ...options.queryOptions,
      queryKey,
      queryFn: async ({ signal }) =>
        (options.fetcher ?? fetchJson<Readonly<T>, unknown>)({
          ...requestOptions,
          signal,
        }),
    }),
  };
};

export default useApiQuery;
