import React, { ReactNode, useCallback } from 'react';
import Alert from 'Components/Alert';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { CODEC_LABELS } from './QualityPresetCriteria';
import { useManageCodecFactors } from './useQualityDefinitions';
import styles from './QualityDefinitions.module.css';

type QualityCodecSizeFactorsProps = Pick<
  ReturnType<typeof useManageCodecFactors>,
  'items' | 'error' | 'saveError' | 'setFactor'
>;

const NAME_PREFIX = 'codecFactor.';

function QualityCodecSizeFactors({
  items,
  error,
  saveError,
  setFactor,
}: QualityCodecSizeFactorsProps) {
  const handleChange = useCallback(
    ({ name, value }: NumberInputChanged) =>
      setFactor(name.slice(NAME_PREFIX.length), value),
    [setFactor]
  );

  let body: ReactNode = null;

  if (error) {
    body = (
      <FormInputHelpText
        text={translate('QualityDefinitionsCodecUnavailable')}
      />
    );
  } else if (items.length) {
    body = (
      <table className={styles.codecTable}>
        <thead>
          <tr>
            <th>{translate('VideoCodec')}</th>
            <th>{translate('QualityDefinitionsCodecFactor')}</th>
            <th>{translate('Default')}</th>
            <th>{translate('QualityDefinitionsCodecEquivalentSize')}</th>
          </tr>
        </thead>
        <tbody>
          {items.map(({ codec, factor, isMixed, defaultFactor }) => (
            <tr key={codec}>
              <td>{CODEC_LABELS[codec] ?? codec}</td>
              <td>
                <NumberInput
                  className={styles.codecInput}
                  name={`${NAME_PREFIX}${codec}`}
                  aria-label={`${CODEC_LABELS[codec] ?? codec}: ${translate(
                    'QualityDefinitionsCodecFactor'
                  )}`}
                  value={factor}
                  placeholder={isMixed ? translate('Mixed') : undefined}
                  min={0.01}
                  max={10}
                  step={0.01}
                  isFloat={true}
                  onChange={handleChange}
                />
              </td>
              <td>{defaultFactor.toFixed(2)}</td>
              <td>
                {isMixed || factor == null
                  ? translate('QualityDefinitionsMixedLimits')
                  : `${factor.toFixed(2)} GiB/${translate('HourShorthand')}`}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    );
  }

  return (
    <div className={styles.codecComparison}>
      <h3>{translate('QualityDefinitionsCodecTitle')}</h3>
      <FormInputHelpText text={translate('QualityDefinitionsCodecHelp')} />
      {saveError ? (
        <Alert kind={kinds.DANGER}>
          {translate('QualityDefinitionsCodecSaveError')}
        </Alert>
      ) : null}
      {body}
    </div>
  );
}

export default QualityCodecSizeFactors;
