import React, { useRef } from 'react';
import Alert from 'Components/Alert';
import { SelectedFilterKey } from 'Components/Filter/Filter';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import InteractiveSearch from 'InteractiveSearch/InteractiveSearch';
import InteractiveSearchPayload from 'InteractiveSearch/InteractiveSearchPayload';
import { useClearReleasesOnUnmount } from 'InteractiveSearch/useReleases';
import formatSeason from 'Season/formatSeason';
import { MediaType } from 'Series/Series';
import translate from 'Utilities/String/translate';
import styles from './SeasonInteractiveSearchModalContent.module.css';

export interface SeasonInteractiveSearchModalContentProps {
  episodeCount: number;
  seriesId: number;
  seasonNumber: number;
  displaySeasonNumber?: number;
  seasonPacksOnly?: true;
  initialSelectedFilterKey?: SelectedFilterKey;
  mediaType?: MediaType;
  onModalClose(): void;
}

function SeasonInteractiveSearchModalContent({
  episodeCount,
  seriesId,
  seasonNumber,
  displaySeasonNumber = seasonNumber,
  seasonPacksOnly,
  initialSelectedFilterKey,
  mediaType = 'series',
  onModalClose,
}: SeasonInteractiveSearchModalContentProps) {
  const searchPayload: InteractiveSearchPayload = {
    seriesId,
    seasonNumber,
    ...(seasonPacksOnly ? { seasonPacksOnly: true } : {}),
  };
  useClearReleasesOnUnmount(searchPayload);
  const modalBodyRef = useRef<HTMLDivElement>(null);
  let header =
    seasonNumber === null
      ? translate('InteractiveSearchModalHeader')
      : translate('InteractiveSearchModalHeaderSeason', {
          season: formatSeason(displaySeasonNumber) as string,
        });
  if (mediaType !== 'series')
    header = translate(mediaType === 'movie' ? 'SearchMovie' : 'SearchSpecial');

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader className={styles.modalHeader}>{header}</ModalHeader>

      <ModalBody ref={modalBodyRef} innerClassName={styles.modalBody}>
        {seasonPacksOnly ? (
          <Alert>{translate('SearchSeasonPacksHelpText')}</Alert>
        ) : null}
        <InteractiveSearch
          type="season"
          searchPayload={searchPayload}
          scrollerRef={modalBodyRef}
          initialSelectedFilterKey={
            initialSelectedFilterKey ??
            (mediaType === 'series' ? undefined : 'all')
          }
        />
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div>
          {mediaType === 'movie'
            ? translate('Movie')
            : translate('EpisodesInSeason', {
                episodeCount,
              })}
        </div>

        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default SeasonInteractiveSearchModalContent;
