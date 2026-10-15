import React, { useCallback, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import translate from 'Utilities/String/translate';
import QualityDefinitionSizeInput from './QualityDefinitionSizeInput';
import QualityPresetActions from './QualityPresetActions';
import QualityPresetCriteria from './QualityPresetCriteria';
import { useManageQualityDefinitions } from './useQualityDefinitions';
import styles from './QualityDefinition.module.css';

interface QualityDefinitionProps extends QualityDefinitionModel {
  updateDefinition: ReturnType<
    typeof useManageQualityDefinitions
  >['updateDefinition'];
  onCopy?: (id: number) => void;
  actionsDisabled?: boolean;
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
  source,
  resolution,
  codec,
  titlePattern,
  isRetired,
  onCopy,
  actionsDisabled,
  updateDefinition,
}: QualityDefinitionProps) {
  const [editingCriteria, setEditingCriteria] = useState(false);
  const handleEdit = useCallback(
    () => setEditingCriteria((current) => !current),
    []
  );
  const handleCopy = useCallback(() => onCopy?.(id), [id, onCopy]);
  const handleCriteria = useCallback(
    (
      key: 'source' | 'resolution' | 'codec' | 'titlePattern',
      value: string | number
    ) => updateDefinition(id, key, value),
    [id, updateDefinition]
  );
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
    <div className={styles.qualityDefinition} data-quality-definition-id={id}>
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
      <div className={styles.details}>
        {isRetired ? <span>{translate('QualityPresetDeleted')}</span> : null}
        {onCopy ? (
          <QualityPresetActions
            id={id}
            qualityId={quality.id}
            retired={isRetired}
            disabled={actionsDisabled}
            onCopy={handleCopy}
            onEdit={handleEdit}
          />
        ) : null}
        {editingCriteria && !isRetired ? (
          <QualityPresetCriteria
            definition={{
              source: source ?? quality.source,
              resolution: resolution ?? quality.resolution,
              codec,
              titlePattern,
            }}
            onChange={handleCriteria}
          />
        ) : null}
      </div>
    </div>
  );
}

export default QualityDefinition;
