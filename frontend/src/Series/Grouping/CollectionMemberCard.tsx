import React, { useCallback, useState } from 'react';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import DeleteSeriesModal from 'Series/Delete/DeleteSeriesModal';
import SeasonInteractiveSearchModal from 'Series/Search/SeasonInteractiveSearchModal';
import SeasonReleaseSearchButton from 'Series/Search/SeasonReleaseSearchButton';
import translate from 'Utilities/String/translate';
import CollectionPoster from './CollectionPoster';
import { CollectionEntry, getNativeSeasonLink } from './collectionPresentation';
import CollectionSeasonSettingsModal from './CollectionSeasonSettingsModal';
import styles from './CollectionPage.module.css';

interface CollectionMemberCardProps {
  entry: CollectionEntry;
}

function CollectionMemberCard({ entry }: CollectionMemberCardProps) {
  const { member, season, displayNumber, section } = entry;
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  let label = translate('AnimeGroupSeasonLabel', { season: displayNumber });

  if (section === 'movies') {
    label = translate('AnimeGroupMovieLabel', {
      number: member.animeGroupMovieNumber ?? 1,
    });
  } else if (section === 'specials') {
    label = translate('Specials');
  }

  const openSearch = useCallback(() => setIsSearchOpen(true), []);
  const closeSearch = useCallback(() => setIsSearchOpen(false), []);
  const openSettings = useCallback(() => setIsSettingsOpen(true), []);
  const closeSettings = useCallback(() => setIsSettingsOpen(false), []);
  const closeDelete = useCallback(() => setIsDeleteOpen(false), []);
  const deleteEntry = useCallback(() => {
    setIsSettingsOpen(false);
    setIsDeleteOpen(true);
  }, []);
  const episodeCount =
    season.statistics?.totalEpisodeCount ??
    season.statistics?.episodeCount ??
    0;
  const episodeFileCount = season.statistics?.episodeFileCount ?? 0;
  const link = getNativeSeasonLink(member, season.seasonNumber);

  return (
    <article
      className={styles.card}
      data-anime-group-series-id={member.id}
      data-anime-group-member-kind={member.animeGroupMemberKind ?? 'season'}
      data-native-season={season.seasonNumber}
    >
      <Link
        className={styles.cardPosterLink}
        to={link}
        aria-label={`${member.title} · ${label}`}
      >
        <CollectionPoster
          members={[member]}
          title={member.title}
          className={styles.cardPoster}
        />
      </Link>
      <div className={styles.cardInfo}>
        <span className={styles.cardLabel}>{label}</span>
        <h3>
          <Link to={link}>{season.title || member.title}</Link>
        </h3>
        <div className={styles.cardMetadata}>
          <span>
            {episodeFileCount}/{episodeCount} {translate('Episodes')}
          </span>
          <span>
            {translate(season.monitored ? 'Monitored' : 'Unmonitored')}
          </span>
          {member.year ? <span>{member.year}</span> : null}
        </div>
        {member.overview ? (
          <p className={styles.cardOverview}>{member.overview}</p>
        ) : null}
        <div className={styles.cardActions}>
          {season.seasonNumber > 0 ? (
            <SeasonReleaseSearchButton
              size="medium"
              seriesId={member.id}
              seasonNumber={season.seasonNumber}
              displaySeasonNumber={displayNumber}
              episodeCount={episodeCount}
            />
          ) : null}
          <Button isDisabled={!episodeCount} onPress={openSearch}>
            <Icon name={icons.INTERACTIVE} /> {translate('SearchEpisodes')}
          </Button>
          <Button onPress={openSettings}>
            <Icon name={icons.EDIT} />{' '}
            {translate(section === 'movies' ? 'Settings' : 'SeasonSettings')}
          </Button>
        </div>
      </div>
      <SeasonInteractiveSearchModal
        isOpen={isSearchOpen}
        seriesId={member.id}
        seasonNumber={season.seasonNumber}
        displaySeasonNumber={displayNumber}
        episodeCount={episodeCount}
        initialSelectedFilterKey="all"
        onModalClose={closeSearch}
      />
      <CollectionSeasonSettingsModal
        isOpen={isSettingsOpen}
        member={member}
        season={season}
        title={label}
        onModalClose={closeSettings}
        onDeleteSeriesPress={deleteEntry}
      />
      <DeleteSeriesModal
        isOpen={isDeleteOpen}
        seriesId={member.id}
        onModalClose={closeDelete}
      />
    </article>
  );
}

export default CollectionMemberCard;
