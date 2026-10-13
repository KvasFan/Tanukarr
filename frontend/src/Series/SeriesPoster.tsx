import React from 'react';
import SeriesImage, { SeriesImageProps } from './SeriesImage';

interface SeriesPosterProps
  extends Omit<SeriesImageProps, 'coverType' | 'placeholder'> {
  size?: 250 | 500;
}

function SeriesPoster({ size = 250, ...otherProps }: SeriesPosterProps) {
  return (
    <SeriesImage
      {...otherProps}
      size={size}
      coverType="poster"
      placeholder={`${window.Sonarr.urlBase}/Content/Images/poster-dark.png`}
    />
  );
}

export default SeriesPoster;
