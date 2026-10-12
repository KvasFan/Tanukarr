import React, { useRef } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import InteractiveSearch from 'InteractiveSearch/InteractiveSearch';
import { useClearReleasesOnUnmount } from 'InteractiveSearch/useReleases';
import translate from 'Utilities/String/translate';
import styles from './SeasonInteractiveSearchModalContent.module.css';

export interface CompleteSeriesSearchModalContentProps {
  seriesId: number;
  onModalClose(): void;
}

function CompleteSeriesSearchModalContent({
  seriesId,
  onModalClose,
}: CompleteSeriesSearchModalContentProps) {
  useClearReleasesOnUnmount({ seriesId, completeSeries: true });
  const modalBodyRef = useRef<HTMLDivElement>(null);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader className={styles.modalHeader}>
        {translate('SearchCompleteSeries')}
      </ModalHeader>

      <ModalBody ref={modalBodyRef} innerClassName={styles.modalBody}>
        <Alert kind={kinds.INFO}>
          {translate('SearchCompleteSeriesHelpText')}
        </Alert>
        <InteractiveSearch
          type="series"
          searchPayload={{ seriesId, completeSeries: true }}
          scrollerRef={modalBodyRef}
        />
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default CompleteSeriesSearchModalContent;
