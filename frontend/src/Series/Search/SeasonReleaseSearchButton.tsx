import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import { sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import SeasonInteractiveSearchModal from './SeasonInteractiveSearchModal';

interface SeasonReleaseSearchButtonProps {
  seriesId: number;
  seasonNumber: number;
  displaySeasonNumber?: number;
  episodeCount: number;
}

function SeasonReleaseSearchButton({
  seriesId,
  seasonNumber,
  displaySeasonNumber = seasonNumber,
  episodeCount,
}: SeasonReleaseSearchButtonProps) {
  const [isOpen, setIsOpen] = useState(false);
  const onOpen = useCallback(() => {
    if (!isOpen && episodeCount > 0) {
      setIsOpen(true);
    }
  }, [episodeCount, isOpen]);
  const onClose = useCallback(() => setIsOpen(false), []);

  return (
    <>
      <Button
        size={sizes.SMALL}
        isDisabled={isOpen || episodeCount === 0}
        onPress={onOpen}
      >
        {translate('SearchSeasonPacksWithNumber', {
          season: displaySeasonNumber,
        })}
      </Button>
      <SeasonInteractiveSearchModal
        isOpen={isOpen}
        seriesId={seriesId}
        seasonNumber={seasonNumber}
        displaySeasonNumber={displaySeasonNumber}
        seasonPacksOnly={true}
        episodeCount={episodeCount}
        onModalClose={onClose}
      />
    </>
  );
}

export default SeasonReleaseSearchButton;
