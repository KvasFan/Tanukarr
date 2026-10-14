import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import NumberInput from 'Components/Form/NumberInput';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { CODEC_FAMILIES } from 'Settings/Quality/Definition/QualityPresetCriteria';
import { Failure } from 'typings/pending';
import translate from 'Utilities/String/translate';
import styles from './QualityProfileCodecFactors.module.css';

interface QualityProfileCodecFactorsProps {
  value?: Record<string, number>;
  errors?: Failure[];
  onChange: (value: Record<string, number>) => void;
}

const LABELS: Record<string, string> = {
  MPEG2: 'MPEG-2 / H.262 / x262',
  H264: 'H.264 / AVC / x264',
  HEVC: 'H.265 / HEVC / x265',
  VVC: 'H.266 / VVC / x266',
};

function QualityProfileCodecFactors({
  value,
  errors,
  onChange,
}: QualityProfileCodecFactorsProps) {
  const { data: defaults } = useApiQuery<Record<string, number>>({
    path: '/qualitydefinition/codecdefaults',
  });
  const values = { ...defaults, ...value };
  const handleFactor = useCallback(
    ({ name, value: factor }: { name: string; value: number | null }) =>
      onChange({
        ...defaults,
        ...value,
        [name.slice('codecMultipliers.'.length)]: factor ?? 0,
      }),
    [defaults, value, onChange]
  );

  return (
    <div className={styles.factors}>
      <h3>{translate('QualityProfileCodecFactors')}</h3>
      {errors?.map((error, index) => (
        <Alert key={index} kind="danger">
          {error.errorMessage}
        </Alert>
      ))}
      <div className={styles.grid}>
        {CODEC_FAMILIES.map((codec) => (
          <label key={codec}>
            <span>{LABELS[codec] ?? codec}</span>
            <NumberInput
              name={`codecMultipliers.${codec}`}
              value={values[codec] ?? 1}
              isFloat={true}
              min={0.01}
              max={10}
              step={0.01}
              onChange={handleFactor}
            />
          </label>
        ))}
      </div>
    </div>
  );
}

export default QualityProfileCodecFactors;
