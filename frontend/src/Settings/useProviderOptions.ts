import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import Field, { FieldSelectOption } from 'typings/Field';
import fetchJson, { ApiError, urlBase } from 'Utilities/Fetch/fetchJson';
import { ModelBaseSetting } from 'Utilities/selectSettings';

export interface ProviderOptions extends ModelBaseSetting {
  fields?: Field[];
}

export interface ProviderOptionsDevice {
  id: string;
  name: string;
}

export interface ProviderOptionsParams {
  provider: string;
  action: string;
  providerData: ProviderOptions;
}

const importantFieldNames = ['baseUrl', 'apiPath', 'apiKey', 'authToken'];
const libraryFieldNames = [
  ...importantFieldNames,
  'host',
  'port',
  'useSsl',
  'urlBase',
];

function isConnectionReady(providerData: ProviderOptions, action: string) {
  if (
    providerData.implementation?.value !== 'Jellyfin' ||
    !['libraries', 'users'].includes(action)
  ) {
    return true;
  }

  const field = (name: string) =>
    providerData.fields?.find((item) => item.name === name)?.value;
  const host = field('host');
  const apiKey = field('apiKey');
  const port = Number(field('port'));

  return (
    typeof host === 'string' &&
    host.trim().length > 0 &&
    typeof apiKey === 'string' &&
    apiKey.trim().length > 0 &&
    Number.isInteger(port) &&
    port > 0 &&
    port <= 65535
  );
}

// Generates a query key from provider fields that affect the available options.
function getProviderDataKey(providerData: ProviderOptions, action: string) {
  if (!providerData || !providerData.fields) {
    return null;
  }

  if (action === 'libraries' || action === 'users') {
    return {
      id: providerData.id?.value ?? null,
      implementation: providerData.implementation?.value ?? null,
      fields: libraryFieldNames.map((name) => ({
        name,
        value:
          providerData.fields?.find((field) => field.name === name)?.value ??
          null,
      })),
    };
  }

  const fields = providerData.fields
    .filter((f) => importantFieldNames.includes(f.name))
    .map((f) => f.value);

  return fields;
}

function flattenProviderData(providerData: ProviderOptions) {
  return Object.keys(providerData).reduce<Record<string, unknown>>(
    (acc, key) => {
      const property = providerData[key];

      if (key === 'fields') {
        acc[key] = property;
      } else {
        acc[key] = property.value;
      }

      return acc;
    },
    {}
  );
}

export default function useProviderOptions({
  provider,
  action,
  providerData,
}: ProviderOptionsParams) {
  const flattenedData = useMemo(
    () => flattenProviderData(providerData),
    [providerData]
  );

  // TODO: This should be updated to use `useApiQuery`
  const result = useQuery<{ options?: FieldSelectOption<unknown>[] }, ApiError>(
    {
      queryKey: [
        `/${provider}/action/${action}`,
        getProviderDataKey(providerData, action),
      ],
      enabled: !!(
        provider &&
        action &&
        providerData &&
        isConnectionReady(providerData, action)
      ),
      queryFn: async ({ signal }) => {
        return fetchJson<
          { options?: FieldSelectOption<unknown>[] },
          typeof flattenedData
        >({
          path: `${urlBase}/api/v3/${provider}/action/${action}`,
          method: 'POST',
          body: flattenedData,
          headers: {
            'Content-Type': 'application/json',
            'X-Api-Key': window.Sonarr.apiKey,
            'X-Sonarr-Client': 'Sonarr',
          },
          signal,
        });
      },
      placeholderData: ['libraries', 'users'].includes(action)
        ? undefined
        : keepPreviousData,
    }
  );

  return {
    ...result,
    data: result.data?.options || [],
  };
}
