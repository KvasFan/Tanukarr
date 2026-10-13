import React from 'react';
import SeriesImage, { SeriesImageProps } from './SeriesImage';

interface SeriesBannerProps
  extends Omit<SeriesImageProps, 'coverType' | 'placeholder'> {
  size?: 35 | 70;
}

function SeriesBanner({ size = 70, ...otherProps }: SeriesBannerProps) {
  return (
    <SeriesImage
      {...otherProps}
      size={size}
      coverType="banner"
      placeholder={`${window.Sonarr.urlBase}/Content/Images/banner-dark.png`}
    />
  );
}

export default SeriesBanner;
