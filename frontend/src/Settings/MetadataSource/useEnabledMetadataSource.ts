import {
  setMetadataSourceOption,
  useMetadataSourceOption,
} from 'AddSeries/AddNewSeries/metadataSourceOptionsStore';
import { MetadataSource } from 'AddSeries/AddNewSeries/useAddSeries';
import { InputChanged } from 'typings/inputs';
import { orderedMetadataSources } from './metadataSources';
import { useMetadataSourceSettings } from './useMetadataSourceSettings';

export default function useEnabledMetadataSource() {
  const savedSource = useMetadataSourceOption();
  const { data: settings, isFetched } = useMetadataSourceSettings();
  const sources = orderedMetadataSources(
    settings?.metadataSourceOrder,
    settings?.disabledMetadataSources
  );
  const metadataSource =
    savedSource === 'all' || sources.some(({ key }) => key === savedSource)
      ? savedSource
      : 'all';
  return { metadataSource, sources, isFetched };
}

export function changeMetadataSource({ value }: InputChanged<string>) {
  setMetadataSourceOption(value as MetadataSource | 'all');
}
