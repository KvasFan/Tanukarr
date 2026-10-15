import { useCallback } from 'react';
import { MetadataSource } from 'AddSeries/AddNewSeries/useAddSeries';
import {
  useManageSettings,
  useSaveSettings,
  useSettings,
} from 'Settings/useSettings';

export interface MetadataSourceSettingsModel {
  preferredMetadataLanguage: number;
  /**
   * Comes back masked once saved, never as the real key. Sending the mask back
   * leaves the stored key alone.
   */
  tmdbApiKey: string;
  tmdbApiKeyConfigured: boolean;
  metadataSourceOrder: MetadataSource[];
  disabledMetadataSources: MetadataSource[];
}

const PATH = '/settings/metadata';

export const useMetadataSourceSettingsValues = () => {
  const { data } = useSettings<MetadataSourceSettingsModel>(PATH);

  return data;
};

export const useMetadataSourceSettings = () => {
  return useSettings<MetadataSourceSettingsModel>(PATH);
};

export const useManageMetadataSourceSettings = () => {
  return useManageSettings<MetadataSourceSettingsModel>(PATH);
};

export const useSaveMetadataSourceSettings = (onSuccess?: () => void) => {
  const { data } = useSettings<MetadataSourceSettingsModel>(PATH);
  const { save, isSaving, saveError } =
    useSaveSettings<MetadataSourceSettingsModel>(PATH, onSuccess);

  const saveSettings = useCallback(
    (changes: Partial<MetadataSourceSettingsModel>) => {
      save({ ...data, ...changes });
    },
    [data, save]
  );

  return { saveSettings, isSaving, saveError };
};
