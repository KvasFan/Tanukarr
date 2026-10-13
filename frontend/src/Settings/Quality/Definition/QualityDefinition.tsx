import React, { useCallback } from 'react';
import TextInput from 'Components/Form/TextInput';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import QualityDefinitionSizeInput from './QualityDefinitionSizeInput';
import { useManageQualityDefinitions } from './useQualityDefinitions';
import styles from './QualityDefinition.module.css';

interface QualityDefinitionProps extends QualityDefinitionModel {
  updateDefinition: ReturnType<
    typeof useManageQualityDefinitions
  >['updateDefinition'];
}

function QualityDefinition({
  id,
  quality,
  title,
  minSize,
  preferredSize,
  maxSize,
  minSizeIsMixed,
  preferredSizeIsMixed,
  maxSizeIsMixed,
  updateDefinition,
}: QualityDefinitionProps) {
  const handleTitleChange = useCallback(
    ({ value }: { value: string }) => {
      updateDefinition(id, 'title', value);
    },
    [id, updateDefinition]
  );

  const handleSizeChange = useCallback(
    (field: 'minSize' | 'preferredSize' | 'maxSize', value: number | null) => {
      if (value != null && !Number.isFinite(value)) return;
      updateDefinition(id, field, value == null ? null : value / 60);
    },
    [id, updateDefinition]
  );

  const sizes = [
    {
      field: 'minSize',
      label: 'Minimum',
      value: minSize,
      mixed: minSizeIsMixed,
    },
    {
      field: 'preferredSize',
      label: 'Preferred',
      value: preferredSize,
      mixed: preferredSizeIsMixed,
    },
    {
      field: 'maxSize',
      label: 'Maximum',
      value: maxSize,
      mixed: maxSizeIsMixed,
    },
  ] as const;

  return (
    <div className={styles.qualityDefinition}>
      <div className={styles.quality}>{quality.name}</div>

      <div className={styles.title}>
        <TextInput
          name={`${id}.${title}`}
          value={title}
          onChange={handleTitleChange}
        />
      </div>
      {sizes.map(({ field, label, value, mixed }) => (
        <QualityDefinitionSizeInput
          key={field}
          id={id}
          qualityName={quality.name}
          field={field}
          label={label}
          value={value}
          mixed={mixed}
          onChange={handleSizeChange}
        />
      ))}
    </div>
  );
}

export default QualityDefinition;
