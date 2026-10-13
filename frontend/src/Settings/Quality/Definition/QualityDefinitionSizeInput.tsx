import React, { useCallback } from 'react';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import Button from 'Components/Link/Button';
import translate from 'Utilities/String/translate';
import styles from './QualityDefinition.module.css';

interface QualityDefinitionSizeInputProps {
  id: number;
  qualityName: string;
  field: 'minSize' | 'preferredSize' | 'maxSize';
  label: string;
  value: number | null;
  mixed?: boolean;
  onChange(
    field: 'minSize' | 'preferredSize' | 'maxSize',
    value: number | null
  ): void;
}

function QualityDefinitionSizeInput({
  id,
  qualityName,
  field,
  label,
  value,
  mixed,
  onChange,
}: QualityDefinitionSizeInputProps) {
  const handleChange = useCallback(
    ({ value: updated }: NumberInputChanged) => {
      onChange(field, updated);
    },
    [field, onChange]
  );
  const handleClear = useCallback(
    () => onChange(field, null),
    [field, onChange]
  );
  const isUnlimited = value == null || (field === 'maxSize' && value === 0);
  let hourlyValue = isUnlimited ? null : value * 60;
  let human = isUnlimited
    ? translate('Unlimited')
    : `${((value * 60) / 1024).toFixed(2)} GiB/${translate('HourShorthand')}`;

  if (value == null && field === 'minSize') {
    hourlyValue = 0;
    human = `0 GiB/${translate('HourShorthand')}`;
  }

  if (mixed) {
    hourlyValue = null;
    human = translate('QualityDefinitionsMixedLimits');
  }

  return (
    <div className={styles.limit}>
      <span className={styles.limitCaption}>{translate(label)}</span>
      <div className={styles.limitField}>
        <NumberInput
          name={`${id}.${field}`}
          aria-label={`${qualityName}: ${translate(label)} (MiB/${translate(
            'HourShorthand'
          )})`}
          value={hourlyValue}
          placeholder={mixed ? translate('Mixed') : translate('Unlimited')}
          min={0}
          step={0.1}
          isFloat={true}
          emitChangeOnBlur={false}
          onChange={handleChange}
        />
        <span className={styles.unit}>MiB/{translate('HourShorthand')}</span>
      </div>
      <span className={styles.human}>{human}</span>
      {mixed ? (
        <Button onPress={handleClear}>
          {translate(
            field === 'minSize' ? 'QualityDefinitionsNoMinimum' : 'Unlimited'
          )}
        </Button>
      ) : null}
    </div>
  );
}

export default QualityDefinitionSizeInput;
