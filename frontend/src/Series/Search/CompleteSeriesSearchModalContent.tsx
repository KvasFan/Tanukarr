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
import { MediaType } from 'Series/Series';
import translate from 'Utilities/String/translate';
import styles from './SeasonInteractiveSearchModalContent.module.css';

export interface CompleteSeriesSearchModalContentProps {
  seriesId: number;
  mediaType?: MediaType;
  onModalClose(): void;
}

function CompleteSeriesSearchModalContent({
  seriesId,
  mediaType = 'series',
  onModalClose,
}: CompleteSeriesSearchModalContentProps) {
  const searchPayload =
    mediaType === 'series'
      ? { seriesId, completeSeries: true as const }
      : { seriesId, seasonNumber: 1 };
  useClearReleasesOnUnmount(searchPayload);
  const modalBodyRef = useRef<HTMLDivElement>(null);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader className={styles.modalHeader}>
        {translate(
          {
            series: 'SearchCompleteSeries',
            movie: 'SearchMovie',
            special: 'SearchSpecial',
          }[mediaType]
        )}
      </ModalHeader>

      <ModalBody ref={modalBodyRef} innerClassName={styles.modalBody}>
        {mediaType === 'series' ? (
          <Alert kind={kinds.INFO}>
            {translate('SearchCompleteSeriesHelpText')}
          </Alert>
        ) : null}
        <InteractiveSearch
          type="series"
          searchPayload={searchPayload}
          initialSelectedFilterKey={mediaType === 'series' ? undefined : 'all'}
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
