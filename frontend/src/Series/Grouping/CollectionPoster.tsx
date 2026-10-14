import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useAppDimension } from 'App/appStore';
import Series, { Image } from 'Series/Series';
import SeriesPoster from 'Series/SeriesPoster';
import styles from './CollectionPage.module.css';

interface CollectionPosterProps {
  members: Series[];
  title: string;
  className?: string;
  style?: object;
  lazy?: boolean;
  preferredImages?: Image[];
}

function CollectionPoster({
  members,
  title,
  className,
  style,
  lazy = true,
  preferredImages,
}: CollectionPosterProps) {
  const isSmallScreen = useAppDimension('isSmallScreen');
  const candidates = useMemo(
    () =>
      [preferredImages ?? [], ...members.map((member) => member.images)].filter(
        (images) =>
          images.some((image) => image.coverType === 'poster' && !!image.url)
      ),
    [members, preferredImages]
  );
  const identity = candidates
    .map((images) => images.find((image) => image.coverType === 'poster')?.url)
    .join('|');
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [identity]);
  const onError = useCallback(() => setIndex((current) => current + 1), []);
  const candidate = candidates[index];

  return candidate ? (
    <SeriesPoster
      key={`${
        candidate.find((image) => image.coverType === 'poster')?.url
      }:${index}:${lazy && isSmallScreen ? 'window' : 'page'}`}
      className={className}
      style={style}
      images={candidate}
      title={title}
      size={lazy ? 250 : 500}
      lazy={lazy}
      overflow={!isSmallScreen}
      onError={onError}
    />
  ) : (
    <div
      className={`${styles.posterPlaceholder} ${className ?? ''}`}
      style={style}
      role="img"
      aria-label={title}
    >
      {title}
    </div>
  );
}

export default CollectionPoster;
