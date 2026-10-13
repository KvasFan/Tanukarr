import React, { ReactNode } from 'react';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import translate from 'Utilities/String/translate';
import { useQualityCodecMultipliers } from './useQualityDefinitions';
import styles from './QualityDefinitions.module.css';

function QualityCodecSizeFactors() {
  const { data, error } = useQualityCodecMultipliers();
  const entries = Object.entries(data ?? {}).filter(
    ([, factor]) => Number.isFinite(factor) && factor > 0
  );

  let body: ReactNode = null;

  if (error) {
    body = (
      <FormInputHelpText
        text={translate('QualityDefinitionsCodecUnavailable')}
      />
    );
  } else if (entries.length) {
    body = (
      <table className={styles.codecTable}>
        <thead>
          <tr>
            <th>{translate('VideoCodec')}</th>
            <th>{translate('QualityDefinitionsCodecFactor')}</th>
            <th>{translate('QualityDefinitionsCodecEquivalentSize')}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map(([codec, factor]) => (
            <tr key={codec}>
              <td>{codec}</td>
              <td>{factor.toFixed(2)}</td>
              <td>
                {factor.toFixed(2)} GiB/{translate('HourShorthand')}
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
      {body}
    </div>
  );
}

export default QualityCodecSizeFactors;
