import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import useApiMutation, {
  getValidationFailures,
} from 'Helpers/Hooks/useApiMutation';
import { QualitySource } from 'Quality/Quality';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import translate from 'Utilities/String/translate';
import QualityPresetCriteria from './QualityPresetCriteria';
import styles from './QualityPresetCriteria.module.css';

interface QualityPresetEditorProps {
  copy?: QualityDefinitionModel;
  onClose: () => void;
}

function QualityPresetEditor({ copy, onClose }: QualityPresetEditorProps) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState({
    title: copy
      ? `${copy.title} - ${translate('QualityPresetCopySuffix')}`
      : '',
    source: copy?.source ?? copy?.quality.source ?? QualitySource.Bluray,
    resolution: copy?.resolution ?? copy?.quality.resolution ?? 2160,
    codec: copy?.codec ?? '',
    titlePattern: copy?.titlePattern ?? '',
    copyFromId: copy?.id,
  });
  const { mutate, isPending, error } = useApiMutation<
    QualityDefinitionModel,
    typeof draft
  >({
    path: '/qualitydefinition',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/qualitydefinition'] });
        queryClient.invalidateQueries({ queryKey: ['/qualityprofile'] });
        queryClient.invalidateQueries({ queryKey: ['/qualityprofile/schema'] });
        onClose();
      },
    },
  });
  const failures = error ? getValidationFailures(error).errors : [];
  const handleTitle = useCallback(
    ({ value }: { value: string }) =>
      setDraft((current) => ({ ...current, title: value })),
    []
  );
  const handleCriteria = useCallback(
    (
      name: 'source' | 'resolution' | 'codec' | 'titlePattern',
      value: string | number
    ) => setDraft((current) => ({ ...current, [name]: value })),
    []
  );
  const handleSave = useCallback(() => mutate(draft), [mutate, draft]);

  return (
    <div className={styles.editor}>
      <h3>{translate(copy ? 'QualityPresetDuplicate' : 'QualityPresetAdd')}</h3>
      {error ? (
        <Alert kind="danger">
          {failures.length
            ? failures.map((failure) => failure.errorMessage).join(' ')
            : translate('QualityPresetSaveError')}
        </Alert>
      ) : null}
      <label className={styles.name}>
        {translate('Name')}
        <TextInput
          name="newQualityTitle"
          value={draft.title}
          autoFocus={true}
          onChange={handleTitle}
        />
      </label>
      <QualityPresetCriteria definition={draft} onChange={handleCriteria} />
      <div className={styles.actions}>
        <Button
          kind="primary"
          isDisabled={isPending || !draft.title.trim()}
          onPress={handleSave}
        >
          {translate('Save')}
        </Button>
        <Button isDisabled={isPending} onPress={onClose}>
          {translate('Cancel')}
        </Button>
      </div>
    </div>
  );
}

export default QualityPresetEditor;
