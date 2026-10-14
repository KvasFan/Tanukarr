import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import useApiMutation, {
  getValidationFailures,
} from 'Helpers/Hooks/useApiMutation';
import QualityDefinitionModel from 'Quality/QualityDefinitionModel';
import translate from 'Utilities/String/translate';
import styles from './QualityPresetCriteria.module.css';

interface QualityPresetActionsProps {
  id: number;
  qualityId: number;
  retired?: boolean;
  disabled?: boolean;
  onEdit: () => void;
  onCopy: () => void;
}

function QualityPresetActions({
  id,
  qualityId,
  retired,
  disabled,
  onEdit,
  onCopy,
}: QualityPresetActionsProps) {
  const queryClient = useQueryClient();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const { mutate, isPending, error } = useApiMutation<
    QualityDefinitionModel,
    undefined
  >({
    path: retired
      ? `/qualitydefinition/${id}/restore`
      : `/qualitydefinition/${id}`,
    method: retired ? 'POST' : 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        setConfirmDelete(false);
        queryClient.invalidateQueries({ queryKey: ['/qualitydefinition'] });
        queryClient.invalidateQueries({ queryKey: ['/qualityprofile'] });
        queryClient.invalidateQueries({ queryKey: ['/qualityprofile/schema'] });
      },
    },
  });
  const failures = error ? getValidationFailures(error).errors : [];
  const handleMutation = useCallback(() => mutate(undefined), [mutate]);
  const handleDelete = useCallback(() => setConfirmDelete(true), []);
  const handleCancel = useCallback(() => setConfirmDelete(false), []);

  return (
    <div>
      <div className={styles.actions}>
        {retired ? null : (
          <Button onPress={onEdit}>
            {translate('QualityPresetEditCriteria')}
          </Button>
        )}
        <Button isDisabled={disabled} onPress={onCopy}>
          {translate('QualityPresetDuplicate')}
        </Button>
        {retired ? (
          <Button isDisabled={isPending || disabled} onPress={handleMutation}>
            {translate('QualityPresetRestore')}
          </Button>
        ) : null}
        {!retired && qualityId !== 0 && confirmDelete ? (
          <>
            <Button
              kind="danger"
              isDisabled={isPending || disabled}
              onPress={handleMutation}
            >
              {translate('QualityPresetConfirmDelete')}
            </Button>
            <Button isDisabled={isPending} onPress={handleCancel}>
              {translate('Cancel')}
            </Button>
          </>
        ) : null}
        {!retired && qualityId !== 0 && !confirmDelete ? (
          <Button isDisabled={disabled} onPress={handleDelete}>
            {translate('Delete')}
          </Button>
        ) : null}
      </div>
      {error ? (
        <Alert kind="danger">
          {failures.length
            ? failures.map((failure) => failure.errorMessage).join(' ')
            : translate('QualityPresetSaveError')}
        </Alert>
      ) : null}
    </div>
  );
}

export default QualityPresetActions;
