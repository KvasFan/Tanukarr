import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import CompleteSeriesSearchModalContent, {
  CompleteSeriesSearchModalContentProps,
} from './CompleteSeriesSearchModalContent';
import styles from './SeasonInteractiveSearchModalContent.module.css';

interface CompleteSeriesSearchModalProps
  extends CompleteSeriesSearchModalContentProps {
  isOpen: boolean;
}

function CompleteSeriesSearchModal({
  isOpen,
  seriesId,
  mediaType,
  onModalClose,
}: CompleteSeriesSearchModalProps) {
  return (
    <Modal
      className={styles.modal}
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <CompleteSeriesSearchModalContent
        seriesId={seriesId}
        mediaType={mediaType}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default CompleteSeriesSearchModal;
