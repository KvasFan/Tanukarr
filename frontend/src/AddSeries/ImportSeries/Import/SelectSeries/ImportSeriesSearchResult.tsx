import classNames from 'classnames';
import React, { useCallback, useEffect, useRef } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import getMetadataProvider from 'Series/getMetadataProvider';
import Series from 'Series/Series';
import useExistingSeries from 'Series/useExistingSeries';
import ImportSeriesTitle from './ImportSeriesTitle';
import styles from './ImportSeriesSearchResult.module.css';

interface ImportSeriesSearchResultProps {
  id: string;
  series: Series;
  isHighlighted: boolean;
  onPress: (series: Series) => void;
}

function ImportSeriesSearchResult({
  id,
  series,
  isHighlighted,
  onPress,
}: ImportSeriesSearchResultProps) {
  const { title, year, network } = series;
  const provider = getMetadataProvider(series);
  const isExistingSeries = useExistingSeries(series);

  const rowRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (isHighlighted) {
      rowRef.current?.scrollIntoView({ block: 'nearest' });
    }
  }, [isHighlighted]);

  const handlePress = useCallback(() => {
    onPress(series);
  }, [series, onPress]);

  return (
    <div
      ref={rowRef}
      className={classNames(
        styles.container,
        isHighlighted ? styles.highlighted : undefined
      )}
      role="row"
      id={id}
      aria-selected={isHighlighted}
    >
      <div className={styles.seriesCell} role="gridcell">
        <Link className={styles.series} onPress={handlePress}>
          <ImportSeriesTitle
            title={title}
            year={year}
            network={network}
            isExistingSeries={isExistingSeries}
          />
        </Link>
      </div>

      <div className={styles.providerLinkCell} role="gridcell">
        {provider.url ? (
          <Link
            className={styles.providerLink}
            to={provider.url}
            aria-label={`${provider.name} ${provider.externalId}`}
          >
            {provider.name}
            <Icon
              className={styles.providerLinkIcon}
              name={icons.EXTERNAL_LINK}
              size={16}
            />
          </Link>
        ) : (
          provider.name
        )}
      </div>
    </div>
  );
}

export default ImportSeriesSearchResult;
