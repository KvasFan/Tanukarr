import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { MetadataSource } from './useAddSeries';

const { useOption, setOption } = createOptionsStore<{
  source: MetadataSource | 'all';
}>('metadata_source_options', () => ({ source: 'all' }));

export const useMetadataSourceOption = () => useOption('source');
export const setMetadataSourceOption = (source: MetadataSource | 'all') =>
  setOption('source', source);
