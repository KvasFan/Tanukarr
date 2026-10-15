import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import SeasonInteractiveSearchModalContent, {
  SeasonInteractiveSearchModalContentProps,
} from './SeasonInteractiveSearchModalContent';
import styles from './SeasonInteractiveSearchModalContent.module.css';

interface SeasonInteractiveSearchModalProps
  extends SeasonInteractiveSearchModalContentProps {
  isOpen: boolean;
}

function SeasonInteractiveSearchModal(
  props: SeasonInteractiveSearchModalProps
) {
  const {
    isOpen,
    episodeCount,
    seriesId,
    seasonNumber,
    displaySeasonNumber,
    seasonPacksOnly,
    initialSelectedFilterKey,
    mediaType,
    onModalClose,
  } = props;

  return (
    <Modal
      className={styles.modal}
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <SeasonInteractiveSearchModalContent
        episodeCount={episodeCount}
        seriesId={seriesId}
        seasonNumber={seasonNumber}
        displaySeasonNumber={displaySeasonNumber}
        seasonPacksOnly={seasonPacksOnly}
        initialSelectedFilterKey={initialSelectedFilterKey}
        mediaType={mediaType}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default SeasonInteractiveSearchModal;
