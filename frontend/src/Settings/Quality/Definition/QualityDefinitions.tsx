import { useQueryClient } from '@tanstack/react-query';
import React, { useEffect } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import PageSectionContent from 'Components/Page/PageSectionContent';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds } from 'Helpers/Props';
import {
  OnChildStateChange,
  SetChildSave,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';
import QualityCodecSizeFactors from './QualityCodecSizeFactors';
import QualityDefinition from './QualityDefinition';
import { useManageQualityDefinitions } from './useQualityDefinitions';
import styles from './QualityDefinitions.module.css';

interface QualityDefinitionsProps {
  isResettingQualityDefinitions: boolean;
  setChildSave: SetChildSave;
  onChildStateChange: OnChildStateChange;
}

function QualityDefinitions({
  isResettingQualityDefinitions,
  setChildSave,
  onChildStateChange,
}: QualityDefinitionsProps) {
  const queryClient = useQueryClient();
  const {
    items,
    isFetching,
    isFetched,
    isSaving,
    error,
    saveError,
    hasPendingChanges,
    updateDefinition,
    saveQualityDefinitions,
  } = useManageQualityDefinitions();

  const wasResettingQualityDefinitions = usePrevious(
    isResettingQualityDefinitions
  );

  useEffect(() => {
    setChildSave(() => {
      saveQualityDefinitions();
    });
  }, [saveQualityDefinitions, setChildSave]);

  useEffect(() => {
    onChildStateChange({
      isSaving,
      hasPendingChanges,
    });
  }, [hasPendingChanges, isSaving, onChildStateChange]);

  useEffect(() => {
    if (wasResettingQualityDefinitions && !isResettingQualityDefinitions) {
      queryClient.invalidateQueries({ queryKey: ['/qualitydefinition'] });
    }
  }, [
    isResettingQualityDefinitions,
    wasResettingQualityDefinitions,
    queryClient,
  ]);

  return (
    <FieldSet
      legend={translate('QualityDefinitions')}
      caption={translate('QualityDefinitionsCaption')}
    >
      <PageSectionContent
        errorMessage={translate('QualityDefinitionsLoadError')}
        isFetching={isFetching}
        isPopulated={isFetched}
        error={error}
      >
        <FormInputHelpText text={translate('QualityDefinitionsHourlyHelp')} />
        {saveError ? (
          <Alert kind={kinds.DANGER}>
            {translate('QualityDefinitionsSaveError')}
          </Alert>
        ) : null}
        <div className={styles.header}>
          <div className={styles.headerQuality}>{translate('Quality')}</div>
          <div className={styles.headerTitle}>{translate('Title')}</div>
          <div>{translate('Minimum')}</div>
          <div>{translate('Preferred')}</div>
          <div>{translate('Maximum')}</div>
        </div>

        <div className={styles.definitions}>
          {items.map((item) => {
            return (
              <QualityDefinition
                key={item.id}
                {...item}
                updateDefinition={updateDefinition}
              />
            );
          })}
        </div>
        <QualityCodecSizeFactors />
      </PageSectionContent>
    </FieldSet>
  );
}

export default QualityDefinitions;
