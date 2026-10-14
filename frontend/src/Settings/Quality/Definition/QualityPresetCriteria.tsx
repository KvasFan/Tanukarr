import React, { useCallback } from 'react';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import { QualitySource } from 'Quality/Quality';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import translate from 'Utilities/String/translate';
import styles from './QualityPresetCriteria.module.css';

export const CODEC_FAMILIES = [
  'MPEG1',
  'MPEG2',
  'MPEG4ASP',
  'H263',
  'H264',
  'HEVC',
  'AV1',
  'VVC',
  'VC1',
  'WMV1',
  'WMV2',
  'WMV3',
  'VP6',
  'VP7',
  'VP8',
  'VP9',
  'Theora',
  'Unknown',
];

interface QualityPresetCriteriaProps {
  definition: Pick<
    QualityDefinitionModel,
    'source' | 'resolution' | 'codec' | 'titlePattern'
  >;
  onChange: (
    name: 'source' | 'resolution' | 'codec' | 'titlePattern',
    value: string | number
  ) => void;
}

function QualityPresetCriteria({
  definition,
  onChange,
}: QualityPresetCriteriaProps) {
  const handleSource = useCallback(
    ({ value }: { value: QualitySource }) => onChange('source', value),
    [onChange]
  );
  const handleResolution = useCallback(
    ({ value }: { value: string }) => onChange('resolution', Number(value)),
    [onChange]
  );
  const handleCodec = useCallback(
    ({ value }: { value: string }) => onChange('codec', value),
    [onChange]
  );
  const handlePattern = useCallback(
    ({ value }: { value: string }) => onChange('titlePattern', value),
    [onChange]
  );
  return (
    <div className={styles.criteria}>
      <label>
        {translate('QualityPresetSource')}
        <SelectInput<QualitySource>
          name="source"
          value={definition.source ?? QualitySource.Bluray}
          values={Object.values(QualitySource).map((source) => ({
            key: source,
            value: translate(`QualityPresetSource_${source}`),
          }))}
          onChange={handleSource}
        />
      </label>
      <label>
        {translate('QualityPresetResolution')}
        <SelectInput<string>
          name="resolution"
          value={definition.resolution ?? 2160}
          values={[0, 480, 576, 720, 1080, 2160].map((resolution) => ({
            key: resolution,
            value: resolution === 0 ? translate('Unknown') : `${resolution}p`,
          }))}
          onChange={handleResolution}
        />
      </label>
      <label>
        {translate('QualityPresetCodec')}
        <SelectInput<string>
          name="codec"
          value={definition.codec ?? ''}
          values={[
            { key: '', value: translate('Any') },
            ...CODEC_FAMILIES.map((codec) => ({ key: codec, value: codec })),
          ]}
          onChange={handleCodec}
        />
      </label>
      <label>
        {translate('QualityPresetTitlePattern')}
        <TextInput
          name="titlePattern"
          value={definition.titlePattern ?? ''}
          onChange={handlePattern}
        />
      </label>
    </div>
  );
}

export default QualityPresetCriteria;
