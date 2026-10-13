import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { usePendingItemsStore } from 'Helpers/Hooks/usePendingItemsStore';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import { useSaveSettings } from 'Settings/useSettings';

const PATH = '/qualitydefinition';
const DEFAULT_QUALITY_DEFINITIONS: QualityDefinitionModel[] = [];
const SIZE_FIELDS = ['minSize', 'preferredSize', 'maxSize'] as const;

export const useQualityCodecMultipliers = () =>
  useApiQuery<Record<string, number>>({ path: `${PATH}/codecs` });

type QualityDefinitionUpdate = Pick<
  QualityDefinitionModel,
  'id' | 'quality' | 'title' | 'weight'
> &
  Partial<Pick<QualityDefinitionModel, (typeof SIZE_FIELDS)[number]>>;

export const useQualityDefinitions = () => {
  const result = useApiQuery<QualityDefinitionModel[]>({
    path: PATH,
  });

  return {
    ...result,
    data: result.data ?? DEFAULT_QUALITY_DEFINITIONS,
  };
};

export const useSaveQualityDefinitions = (onSuccess?: () => void) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    QualityDefinitionModel[],
    QualityDefinitionModel[]
  >({
    path: PATH,
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updatedSettings: QualityDefinitionModel[]) => {
        queryClient.setQueryData<QualityDefinitionModel[]>(
          [PATH],
          updatedSettings
        );
        onSuccess?.();
      },
    },
  });

  return {
    save: mutate,
    isSaving: isPending,
    saveError: error,
  };
};

export const useManageQualityDefinitions = () => {
  const queryClient = useQueryClient();
  const { data, isFetching, isFetched, error } = useQualityDefinitions();
  const {
    store,
    setPendingItem,
    clearPendingItems,
    getItemsWithPendingChanges,
    hasPendingChanges,
  } = usePendingItemsStore<QualityDefinitionModel>();

  const handleSaved = useCallback(() => {
    clearPendingItems();
    queryClient.invalidateQueries({ queryKey: ['/qualityprofile'] });
  }, [clearPendingItems, queryClient]);

  const { save, isSaving, saveError } = useSaveSettings<
    QualityDefinitionUpdate[]
  >(PATH, handleSaved);

  const settings = useMemo(() => {
    return {
      items: getItemsWithPendingChanges(data),
      hasPendingChanges,
    };
  }, [data, getItemsWithPendingChanges, hasPendingChanges]);

  const saveQualityDefinitions = useCallback(() => {
    const pending = store.getState().pendingItems;
    const updatedSettings = data.flatMap((original) => {
      const changes = pending.get(original.id);
      if (!changes) return [];

      const updated = { ...original, ...changes };
      const request: QualityDefinitionUpdate = {
        id: updated.id,
        quality: updated.quality,
        title: updated.title,
        weight: updated.weight,
      };

      for (const field of SIZE_FIELDS) {
        if (Object.prototype.hasOwnProperty.call(changes, field)) {
          request[field] = updated[field];
        }
      }

      return [request];
    });
    save(updatedSettings);
  }, [data, store, save]);

  const updateDefinition = useCallback(
    <K extends keyof QualityDefinitionModel>(
      id: number,
      key: keyof QualityDefinitionModel,
      value: QualityDefinitionModel[K]
    ) => {
      const originalItem = data.find((def) => def.id === id);

      if (SIZE_FIELDS.some((field) => field === key)) {
        const mixedField = `${key}IsMixed` as
          | 'minSizeIsMixed'
          | 'preferredSizeIsMixed'
          | 'maxSizeIsMixed';
        const wasMixed = originalItem?.[mixedField] === true;
        setPendingItem(id, key, value, wasMixed ? undefined : originalItem);
        if (wasMixed) setPendingItem(id, mixedField, false, originalItem);
      } else {
        setPendingItem(id, key, value, originalItem);
      }
    },
    [data, setPendingItem]
  );

  return {
    ...settings,
    updateDefinition,
    saveQualityDefinitions,
    isFetching,
    isFetched,
    isSaving,
    error,
    saveError,
  };
};
