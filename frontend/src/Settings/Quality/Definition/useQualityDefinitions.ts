import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useRef, useState } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { usePendingItemsStore } from 'Helpers/Hooks/usePendingItemsStore';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import { useSaveSettings } from 'Settings/useSettings';

const PATH = '/qualitydefinition';
const DEFAULT_QUALITY_DEFINITIONS: QualityDefinitionModel[] = [];
const SIZE_FIELDS = ['minSize', 'preferredSize', 'maxSize'] as const;
const CRITERIA_FIELDS = [
  'source',
  'resolution',
  'codec',
  'titlePattern',
] as const;

const CODEC_FACTORS_PATH = `${PATH}/codecfactors`;
const DEFAULT_CODEC_FACTORS: QualityCodecFactor[] = [];

export interface QualityCodecFactor {
  codec: string;
  // Null when quality profiles use different factors for this codec family.
  factor: number | null;
  isMixed: boolean;
  defaultFactor: number;
}

export const useManageCodecFactors = () => {
  const queryClient = useQueryClient();
  const { data, error } = useApiQuery<QualityCodecFactor[]>({
    path: CODEC_FACTORS_PATH,
  });
  const [pending, setPending] = useState<Record<string, number>>({});
  const submitted = useRef<Record<string, number> | null>(null);

  const {
    mutate,
    isPending: isSaving,
    error: saveError,
  } = useApiMutation<QualityCodecFactor[], Record<string, number>>({
    path: CODEC_FACTORS_PATH,
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updated) => {
        queryClient.setQueryData([CODEC_FACTORS_PATH], updated);
        const sent = submitted.current;
        submitted.current = null;
        // A factor edited again while the request was in flight stays pending.
        setPending((current) =>
          Object.fromEntries(
            Object.entries(current).filter(
              ([codec, factor]) => sent?.[codec] !== factor
            )
          )
        );
        queryClient.invalidateQueries({ queryKey: ['/qualityprofile'] });
      },
    },
  });

  const items = useMemo(
    () =>
      (data ?? DEFAULT_CODEC_FACTORS).map((item) =>
        Object.prototype.hasOwnProperty.call(pending, item.codec)
          ? { ...item, factor: pending[item.codec], isMixed: false }
          : item
      ),
    [data, pending]
  );

  const setFactor = useCallback(
    (codec: string, factor: number | null) => {
      const saved = data?.find((item) => item.codec === codec);

      setPending((current) => {
        const { [codec]: _removed, ...rest } = current;

        // Clearing the field, or typing the saved value back, discards the edit.
        if (
          factor == null ||
          !Number.isFinite(factor) ||
          (saved && !saved.isMixed && saved.factor === factor)
        ) {
          return rest;
        }

        return { ...rest, [codec]: factor };
      });
    },
    [data]
  );

  const save = useCallback(() => {
    if (isSaving || !Object.keys(pending).length) {
      return;
    }

    submitted.current = pending;
    mutate(pending);
  }, [isSaving, mutate, pending]);

  return {
    items,
    error,
    saveError,
    isSaving,
    hasPendingChanges: Object.keys(pending).length > 0,
    setFactor,
    save,
  };
};

type QualityDefinitionUpdate = Pick<
  QualityDefinitionModel,
  'id' | 'quality' | 'title' | 'weight'
> &
  Partial<
    Pick<
      QualityDefinitionModel,
      (typeof SIZE_FIELDS)[number] | (typeof CRITERIA_FIELDS)[number]
    >
  >;

export const useQualityDefinitions = (enabled = true) => {
  const result = useApiQuery<QualityDefinitionModel[]>({
    path: PATH,
    queryOptions: { enabled },
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
    unsetPendingItem,
    getItemsWithPendingChanges,
    hasPendingChanges,
  } = usePendingItemsStore<QualityDefinitionModel>();
  const submittedChanges = useRef<Map<
    number,
    Partial<QualityDefinitionModel>
  > | null>(null);

  const applyChange = useCallback(
    (
      id: number,
      key: keyof QualityDefinitionModel,
      value: QualityDefinitionModel[keyof QualityDefinitionModel],
      baseline?: QualityDefinitionModel
    ) => {
      if (SIZE_FIELDS.some((field) => field === key)) {
        const mixedField = `${key}IsMixed` as
          | 'minSizeIsMixed'
          | 'preferredSizeIsMixed'
          | 'maxSizeIsMixed';
        // A value entered over a mixed limit is always an explicit change.
        const wasMixed = baseline?.[mixedField] === true;
        setPendingItem(id, key, value, wasMixed ? undefined : baseline);
        if (wasMixed) setPendingItem(id, mixedField, false, baseline);
      } else {
        setPendingItem(id, key, value, baseline);
      }
    },
    [setPendingItem]
  );

  const handleSaved = useCallback(() => {
    // Forget only the values that were sent; a field edited again while the
    // request was in flight keeps its newer pending value instead of being lost.
    const saved =
      queryClient.getQueryData<QualityDefinitionModel[]>([PATH]) ?? [];
    const submitted = submittedChanges.current;
    submittedChanges.current = null;

    store.getState().pendingItems.forEach((changes, id) => {
      const sent = submitted?.get(id);
      const savedItem = saved.find((definition) => definition.id === id);
      unsetPendingItem(id);

      (Object.keys(changes) as (keyof QualityDefinitionModel)[]).forEach(
        (key) => {
          if (!sent || !Object.is(changes[key], sent[key])) {
            applyChange(
              id,
              key,
              changes[key] as QualityDefinitionModel[typeof key],
              savedItem
            );
          }
        }
      );
    });

    queryClient.invalidateQueries({ queryKey: ['/qualityprofile'] });
  }, [applyChange, queryClient, store, unsetPendingItem]);

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
    // The toolbar button is disabled while saving but the Ctrl+S shortcut is
    // not; a second request in flight would make the sent snapshot ambiguous.
    if (isSaving) {
      return;
    }

    const pending = store.getState().pendingItems;
    // Entries are replaced, never mutated, so a shallow copy records what was sent.
    submittedChanges.current = new Map(pending);
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

      for (const field of CRITERIA_FIELDS) {
        if (Object.prototype.hasOwnProperty.call(changes, field)) {
          Object.assign(request, { [field]: updated[field] });
        }
      }

      return [request];
    });
    save(updatedSettings);
  }, [data, isSaving, store, save]);

  const updateDefinition = useCallback(
    <K extends keyof QualityDefinitionModel>(
      id: number,
      key: keyof QualityDefinitionModel,
      value: QualityDefinitionModel[K]
    ) => {
      const originalItem = data.find((def) => def.id === id);
      // While a save is in flight the catalogue still shows the pre-save values,
      // so compare against what was sent: reverting a submitted value is a change.
      const sent = isSaving ? submittedChanges.current?.get(id) : undefined;

      applyChange(
        id,
        key,
        value,
        originalItem && sent ? { ...originalItem, ...sent } : originalItem
      );
    },
    [applyChange, data, isSaving]
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
