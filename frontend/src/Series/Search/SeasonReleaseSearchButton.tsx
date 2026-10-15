import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import { sizes } from 'Helpers/Props';
import { MediaType } from 'Series/Series';
import translate from 'Utilities/String/translate';
import SeasonInteractiveSearchModal from './SeasonInteractiveSearchModal';

interface SeasonReleaseSearchButtonProps {
  size?: React.ComponentProps<typeof Button>['size'];
  seriesId: number;
  seasonNumber: number;
  displaySeasonNumber?: number;
  episodeCount: number;
  mediaType?: MediaType;
}

function SeasonReleaseSearchButton({
  size = sizes.SMALL,
  seriesId,
  seasonNumber,
  displaySeasonNumber = seasonNumber,
  episodeCount,
  mediaType = 'series',
}: SeasonReleaseSearchButtonProps) {
  const [isOpen, setIsOpen] = useState(false);
  const onOpen = useCallback(() => {
    if (!isOpen && episodeCount > 0) {
      setIsOpen(true);
    }
  }, [episodeCount, isOpen]);
  const onClose = useCallback(() => setIsOpen(false), []);
  let label = translate('SearchSeasonPacksWithNumber', {
    season: displaySeasonNumber,
  });
  if (mediaType !== 'series')
    label = translate(mediaType === 'movie' ? 'SearchMovie' : 'SearchSpecial');

  return (
    <>
      <Button
        size={size}
        isDisabled={isOpen || episodeCount === 0}
        onPress={onOpen}
      >
        {label}
      </Button>
      <SeasonInteractiveSearchModal
        isOpen={isOpen}
        seriesId={seriesId}
        seasonNumber={seasonNumber}
        displaySeasonNumber={displaySeasonNumber}
        seasonPacksOnly={mediaType === 'series' ? true : undefined}
        mediaType={mediaType}
        episodeCount={episodeCount}
        onModalClose={onClose}
      />
    </>
  );
}

export default SeasonReleaseSearchButton;
