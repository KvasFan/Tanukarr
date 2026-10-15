import React, { useCallback, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import PageSectionContent from 'Components/Page/PageSectionContent';
import AddCard from 'Components/SettingsCard/AddCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import EditQualityProfileModal from './EditQualityProfileModal';
import QualityProfile from './QualityProfile';
import { useQualityProfiles } from './useQualityProfiles';

function QualityProfiles() {
  const navigate = useNavigate();
  const { data, error, isFetching, isFetched } = useQualityProfiles();

  // Sort the data by name
  const sortedItems = data ? [...data].sort(sortByProp('name')) : [];

  const [isQualityProfileModalOpen, setIsQualityProfileModalOpen] =
    useState(false);
  const [cloneProfileId, setCloneProfileId] = useState<number | null>(null);

  const handleAddQualityProfilePress = useCallback(() => {
    navigate('/settings/quality?addProfile=true');
  }, [navigate]);

  const handleAddQualityProfileClosePress = useCallback(() => {
    setCloneProfileId(null);
    setIsQualityProfileModalOpen(false);
  }, []);

  const handleCloneQualityProfilePress = useCallback((id: number) => {
    setCloneProfileId(id);
    setIsQualityProfileModalOpen(true);
  }, []);

  return (
    <PageSectionContent
      errorMessage={translate('QualityProfilesLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {sortedItems.map((item) => {
          return (
            <QualityProfile
              key={item.id}
              {...item}
              onCloneQualityProfilePress={handleCloneQualityProfilePress}
            />
          );
        })}

        <AddCard
          label={translate('AddQualityProfile')}
          onPress={handleAddQualityProfilePress}
        />
      </div>

      <EditQualityProfileModal
        isOpen={isQualityProfileModalOpen}
        cloneId={cloneProfileId ?? undefined}
        onModalClose={handleAddQualityProfileClosePress}
      />
    </PageSectionContent>
  );
}

export default QualityProfiles;
