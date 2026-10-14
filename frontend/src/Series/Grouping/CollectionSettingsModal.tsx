import React, { useCallback, useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { AnimeGroupKind, AnimeGroupMovieFolderStyle } from 'Series/Series';
import { useSaveSeriesEditor } from 'Series/useSeries';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { AnimeSeriesGroup } from './animeGrouping';

interface CollectionSettingsModalProps {
  isOpen: boolean;
  collection: AnimeSeriesGroup;
  isNativeCollection: boolean;
  onModalClose(): void;
}

function CollectionSettingsModal({
  isOpen,
  collection,
  isNativeCollection,
  onModalClose,
}: CollectionSettingsModalProps) {
  const [title, setTitle] = useState(collection.title);
  const [kind, setKind] = useState<AnimeGroupKind>(
    collection.members[0].animeGroupKind ?? 'series'
  );
  const [folderStyle, setFolderStyle] = useState<AnimeGroupMovieFolderStyle>(
    collection.members[0].animeGroupMovieFolderStyle ?? 'automatic'
  );
  const [monitored, setMonitored] = useState('');
  const [seasonFolder, setSeasonFolder] = useState('');
  const [profile, setProfile] = useState('');
  const profiles = useQualityProfilesData();
  const { saveSeriesEditor, isSavingSeriesEditor, saveSeriesEditorError } =
    useSaveSeriesEditor();
  useEffect(() => {
    if (isOpen) {
      setTitle(collection.title);
      setKind(collection.members[0].animeGroupKind ?? 'series');
      setFolderStyle(
        collection.members[0].animeGroupMovieFolderStyle ?? 'automatic'
      );
      setMonitored('');
      setSeasonFolder('');
      setProfile('');
    }
  }, [isOpen, collection]);
  const changedGroup =
    !isNativeCollection &&
    (title.trim() !== collection.title ||
      kind !== (collection.members[0].animeGroupKind ?? 'series') ||
      folderStyle !==
        (collection.members[0].animeGroupMovieFolderStyle ?? 'automatic'));
  const changedNativeTitle =
    isNativeCollection && title.trim() !== collection.title;
  const hasChanges =
    changedGroup ||
    changedNativeTitle ||
    monitored !== '' ||
    seasonFolder !== '' ||
    profile !== '';
  const unchanged = { key: '', value: translate('NoChange') };
  const changeTitle = useCallback(
    ({ value }: InputChanged<string>) => setTitle(value),
    []
  );
  const changeKind = useCallback(
    ({ value }: InputChanged<string>) => setKind(value as AnimeGroupKind),
    []
  );
  const changeFolderStyle = useCallback(
    ({ value }: InputChanged<string>) =>
      setFolderStyle(value as AnimeGroupMovieFolderStyle),
    []
  );
  const changeMonitored = useCallback(
    ({ value }: InputChanged<string>) => setMonitored(value),
    []
  );
  const changeProfile = useCallback(
    ({ value }: InputChanged<string>) => setProfile(value),
    []
  );
  const changeSeasonFolder = useCallback(
    ({ value }: InputChanged<string>) => setSeasonFolder(value),
    []
  );
  const close = useCallback(() => {
    if (!isSavingSeriesEditor) {
      onModalClose();
    }
  }, [isSavingSeriesEditor, onModalClose]);
  const save = useCallback(
    () =>
      saveSeriesEditor(
        {
          seriesIds: collection.members.map((member) => member.id),
          ...(changedNativeTitle ? { collectionTitle: title.trim() } : {}),
          ...(changedGroup
            ? {
                animeGroupTitle: title.trim(),
                animeGroupKind: kind,
                animeGroupMovieFolderStyle: folderStyle,
              }
            : {}),
          ...(monitored === '' ? {} : { monitored: monitored === 'true' }),
          ...(seasonFolder === ''
            ? {}
            : { seasonFolder: seasonFolder === 'true' }),
          ...(profile === '' ? {} : { qualityProfileId: Number(profile) }),
        },
        { onSuccess: onModalClose }
      ),
    [
      saveSeriesEditor,
      collection.members,
      changedNativeTitle,
      changedGroup,
      title,
      kind,
      folderStyle,
      monitored,
      seasonFolder,
      profile,
      onModalClose,
    ]
  );

  return (
    <Modal isOpen={isOpen} onModalClose={close}>
      <ModalContent onModalClose={close}>
        <ModalHeader>{translate('CollectionSettings')}</ModalHeader>
        <ModalBody>
          <Form>
            {isNativeCollection ? (
              <FormRow>
                <FormLabel>{translate('Name')}</FormLabel>
                <TextInput
                  name="collectionTitle"
                  value={title}
                  hasError={!title.trim()}
                  onChange={changeTitle}
                />
              </FormRow>
            ) : null}
            {isNativeCollection ? null : (
              <>
                <FormRow>
                  <FormLabel>{translate('Name')}</FormLabel>
                  <TextInput
                    name="animeGroupTitle"
                    value={title}
                    hasError={!title.trim()}
                    onChange={changeTitle}
                  />
                </FormRow>
                <FormRow>
                  <FormLabel>{translate('AnimeGroupKind')}</FormLabel>
                  <SelectInput
                    name="animeGroupKind"
                    value={kind}
                    values={[
                      { key: 'series', value: translate('AnimeGroupSeries') },
                      {
                        key: 'movieSaga',
                        value: translate('AnimeGroupMovieSaga'),
                      },
                    ]}
                    onChange={changeKind}
                  />
                </FormRow>
                <FormRow>
                  <FormLabel>
                    {translate('AnimeGroupMovieFolderStyle')}
                  </FormLabel>
                  <SelectInput
                    name="animeGroupMovieFolderStyle"
                    value={folderStyle}
                    values={[
                      {
                        key: 'automatic',
                        value: translate('AnimeGroupFolderAutomatic'),
                      },
                      {
                        key: 'title',
                        value: translate('AnimeGroupFolderTitle'),
                      },
                      {
                        key: 'numbered',
                        value: translate('AnimeGroupFolderNumbered'),
                      },
                    ]}
                    onChange={changeFolderStyle}
                  />
                </FormRow>
              </>
            )}
            <FormRow>
              <FormLabel>{translate('Monitored')}</FormLabel>
              <SelectInput
                name="monitored"
                value={monitored}
                values={[
                  unchanged,
                  { key: 'true', value: translate('Monitored') },
                  { key: 'false', value: translate('Unmonitored') },
                ]}
                onChange={changeMonitored}
              />
            </FormRow>
            <FormRow>
              <FormLabel>{translate('QualityProfile')}</FormLabel>
              <SelectInput
                name="qualityProfileId"
                value={profile}
                values={[
                  unchanged,
                  ...profiles.map((item) => ({
                    key: String(item.id),
                    value: item.name,
                  })),
                ]}
                onChange={changeProfile}
              />
            </FormRow>
            <FormRow>
              <FormLabel>{translate('SeasonFolder')}</FormLabel>
              <SelectInput
                name="seasonFolder"
                value={seasonFolder}
                values={[
                  unchanged,
                  { key: 'true', value: translate('Yes') },
                  { key: 'false', value: translate('No') },
                ]}
                onChange={changeSeasonFolder}
              />
            </FormRow>
          </Form>
          {saveSeriesEditorError ? (
            <Alert kind={kinds.DANGER}>
              {translate('CollectionSettingsSaveError')}
            </Alert>
          ) : null}
        </ModalBody>
        <ModalFooter>
          <Button isDisabled={isSavingSeriesEditor} onPress={onModalClose}>
            {translate('Cancel')}
          </Button>
          <SpinnerErrorButton
            isSpinning={isSavingSeriesEditor}
            error={saveSeriesEditorError}
            isDisabled={!hasChanges || !title.trim()}
            onPress={save}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default CollectionSettingsModal;
