import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CheckInput from 'Components/Form/CheckInput';
import Link from 'Components/Link/Link';
import Series from 'Series/Series';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { AnimeSeriesGroup } from './animeGrouping';
import CollectionPoster from './CollectionPoster';
import {
  getCollectionEntries,
  getCollectionLink,
} from './collectionPresentation';
import styles from './AnimeGroupSummary.module.css';

export interface AnimeGroupViewProps {
  groupsByRepresentativeId?: Map<number, AnimeSeriesGroup>;
}

interface AnimeGroupSummaryProps {
  group: AnimeSeriesGroup;
  view: 'posters' | 'overview' | 'table';
  isSelectMode: boolean;
  posterWidth?: number;
  posterHeight?: number;
}

function AnimeGroupSummary({
  group,
  view,
  isSelectMode,
  posterWidth = 100,
  posterHeight = 147,
}: AnimeGroupSummaryProps) {
  const { useSelectedIds, toggleSelected } = useSelect<Series>();
  const selected = useSelectedIds();
  const selectedCount = group.members.filter((member) =>
    selected.includes(member.id)
  ).length;
  let selectionValue: boolean | null = null;

  if (selectedCount === 0) {
    selectionValue = false;
  } else if (selectedCount === group.members.length) {
    selectionValue = true;
  }

  const entries = getCollectionEntries(group);
  const seasons = entries.filter((entry) => entry.section === 'seasons');
  const movies = entries.filter((entry) => entry.section === 'movies');
  const specials = entries.filter((entry) => entry.section === 'specials');
  const totalEpisodeCount = group.members.reduce(
    (sum, member) => sum + (member.statistics?.episodeCount ?? 0),
    0
  );
  const totalEpisodeFileCount = group.members.reduce(
    (sum, member) => sum + (member.statistics?.episodeFileCount ?? 0),
    0
  );
  const onSelect = useCallback(
    ({ value }: CheckInputChanged) => {
      group.members.forEach((member) =>
        toggleSelected({ id: member.id, isSelected: value, shiftKey: false })
      );
    },
    [group.members, toggleSelected]
  );
  const link = getCollectionLink(group.id);

  return (
    <div
      className={
        {
          posters: styles.poster,
          table: styles.table,
          overview: styles.overview,
        }[view]
      }
      data-anime-group={group.id}
    >
      {isSelectMode ? (
        <CheckInput
          containerClassName={
            view === 'posters' ? styles.posterSelection : undefined
          }
          name={`anime-group-${group.id}`}
          ariaLabel={translate('AnimeGroupSelect')}
          value={selectionValue}
          onChange={onSelect}
        />
      ) : null}
      {view === 'table' ? null : (
        <Link
          className={styles.posterButton}
          to={link}
          aria-label={group.title}
        >
          <CollectionPoster
            members={group.members}
            title={group.title}
            style={{ width: posterWidth, height: posterHeight }}
          />
        </Link>
      )}
      <div className={styles.details}>
        <Link className={styles.title} to={link}>
          {group.title}
        </Link>
        <div className={styles.count}>
          {translate('AnimeGroupMemberCounts', {
            seasons: seasons.length,
            movies: movies.length,
          })}
          {specials.length
            ? ` · ${translate('CountSpecials', { count: specials.length })}`
            : null}
        </div>
        <div className={styles.count}>
          {totalEpisodeFileCount}/{totalEpisodeCount} {translate('Episodes')}
        </div>
        {group.members.length === 1 && seasons.length === 1 ? (
          <span className={styles.singletonSeason}>
            {translate('AnimeGroupSeasonLabel', {
              season: seasons[0].displayNumber,
            })}
          </span>
        ) : null}
      </div>
    </div>
  );
}

export default AnimeGroupSummary;
