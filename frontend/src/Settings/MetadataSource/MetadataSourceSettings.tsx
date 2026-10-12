import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { LanguageSelectInputOnChangeProps } from 'Components/Form/Select/LanguageSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { inputTypes, kinds } from 'Helpers/Props';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import Sources from './Sources';
import {
  MetadataSourceSettingsModel,
  useManageMetadataSourceSettings,
} from './useMetadataSourceSettings';

function MetadataSourceSettings() {
  const {
    isFetching,
    isFetched,
    error,
    hasPendingChanges,
    hasSettings,
    settings,
    isSaving,
    validationErrors,
    validationWarnings,
    saveSettings,
    updateSetting,
  } = useManageMetadataSourceSettings();

  const handleInputChange = useCallback(
    ({ name, value }: LanguageSelectInputOnChangeProps) => {
      updateSetting(name as keyof MetadataSourceSettingsModel, value as number);
    },
    [updateSetting]
  );

  const handleSavePress = useCallback(() => {
    saveSettings();
  }, [saveSettings]);

  const handleTmdbApiKeyChange = useCallback(
    ({ value }: InputChanged<string>) => {
      updateSetting('tmdbApiKey', value);
    },
    [updateSetting]
  );

  return (
    <SettingsPage
      title={translate('MetadataSourceSettings')}
      hasPendingChanges={hasPendingChanges}
      isSaving={isSaving}
      onSavePress={handleSavePress}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading
            scope={translate('Settings')}
            title={translate('MetadataSource')}
          />

          {isFetching && !isFetched ? <LoadingIndicator /> : null}

          {!isFetching && error ? (
            <Alert kind={kinds.DANGER}>
              {translate('MetadataSourceSettingsLoadError')}
            </Alert>
          ) : null}

          {hasSettings && isFetched && !error ? (
            <Form
              id="metadataSourceSettings"
              validationErrors={validationErrors}
              validationWarnings={validationWarnings}
            >
              <FieldSet legend={translate('Options')}>
                <FormRow>
                  <FormLabel>
                    {translate('PreferredMetadataLanguage')}
                  </FormLabel>

                  <FormInputHelpText
                    text={translate('PreferredMetadataLanguageHelpText')}
                  />
                  <FormInput
                    type={inputTypes.LANGUAGE_SELECT}
                    name="preferredMetadataLanguage"
                    includeAny={false}
                    onChange={handleInputChange}
                    {...settings.preferredMetadataLanguage}
                  />
                </FormRow>
              </FieldSet>

              <FieldSet legend={translate('Tmdb')}>
                <FormRow>
                  <FormLabel>{translate('TmdbApiKey')}</FormLabel>

                  <FormInputHelpText
                    text={translate('TmdbApiKeyHelpText')}
                    link="https://www.themoviedb.org/settings/api"
                  />
                  <FormInput
                    type={inputTypes.PASSWORD}
                    name="tmdbApiKey"
                    onChange={handleTmdbApiKeyChange}
                    {...settings.tmdbApiKey}
                  />
                </FormRow>
              </FieldSet>
            </Form>
          ) : null}

          <Sources />
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default MetadataSourceSettings;
