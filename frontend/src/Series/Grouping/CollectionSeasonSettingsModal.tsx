import React from 'react';
import Modal from 'Components/Modal/Modal';
import EditSeriesModalContent from 'Series/Edit/EditSeriesModalContent';
import Series, { Season } from 'Series/Series';

interface CollectionSeasonSettingsModalProps {
  isOpen: boolean;
  member: Series;
  season: Season;
  title: string;
  onModalClose(): void;
  onDeleteSeriesPress(): void;
}

function CollectionSeasonSettingsModal({
  isOpen,
  member,
  season,
  title,
  onModalClose,
  onDeleteSeriesPress,
}: CollectionSeasonSettingsModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <EditSeriesModalContent
        seriesId={member.id}
        seasonNumber={season.seasonNumber}
        seasonTitle={title}
        onModalClose={onModalClose}
        onDeleteSeriesPress={onDeleteSeriesPress}
      />
    </Modal>
  );
}

export default CollectionSeasonSettingsModal;
