import React, { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import PageSectionContent from 'Components/Page/PageSectionContent';
import EditQualityProfileModal from 'Settings/Profiles/Quality/EditQualityProfileModal';
import { useQualityProfiles } from 'Settings/Profiles/Quality/useQualityProfiles';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import styles from './QualityProfileQualitySettings.module.css';

interface ProfileButtonProps {
  id: number;
  name: string;
  onPress: (id: number) => void;
}

function ProfileButton({ id, name, onPress }: ProfileButtonProps) {
  const handlePress = useCallback(() => onPress(id), [id, onPress]);
  return <Button onPress={handlePress}>{name}</Button>;
}

function QualityProfileQualitySettings() {
  const { data, error, isFetching, isFetched } = useQualityProfiles();
  const [searchParams, setSearchParams] = useSearchParams();
  const [profileId, setProfileId] = useState<number>();
  const [isOpen, setIsOpen] = useState(
    () => searchParams.get('addProfile') === 'true'
  );
  const handleClose = useCallback(() => {
    setProfileId(undefined);
    setIsOpen(false);

    if (searchParams.has('addProfile')) {
      const next = new URLSearchParams(searchParams);
      next.delete('addProfile');
      setSearchParams(next, { replace: true });
    }
  }, [searchParams, setSearchParams]);
  const handleOpen = useCallback((id: number) => {
    setProfileId(id);
    setIsOpen(true);
  }, []);
  const handleAdd = useCallback(() => {
    setProfileId(undefined);
    setIsOpen(true);
  }, []);

  return (
    <FieldSet legend={translate('QualityProfiles')}>
      <PageSectionContent
        errorMessage={translate('QualityProfilesLoadError')}
        error={error}
        isFetching={isFetching}
        isPopulated={isFetched}
      >
        <div className={styles.profiles}>
          <Button kind="primary" onPress={handleAdd}>
            {translate('AddQualityProfile')}
          </Button>
          {[...(data ?? [])].sort(sortByProp('name')).map((profile) => (
            <ProfileButton
              key={profile.id}
              id={profile.id}
              name={profile.name}
              onPress={handleOpen}
            />
          ))}
        </div>
        <EditQualityProfileModal
          id={profileId}
          isOpen={isOpen}
          qualitySettings={true}
          onModalClose={handleClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default QualityProfileQualitySettings;
