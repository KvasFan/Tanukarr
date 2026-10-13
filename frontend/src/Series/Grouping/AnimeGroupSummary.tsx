import React, { useCallback, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import SeriesIndexOverview from 'Series/Index/Overview/SeriesIndexOverview';
import SeasonReleaseSearchButton from 'Series/Search/SeasonReleaseSearchButton';
import Series from 'Series/Series';
import SeriesPoster from 'Series/SeriesPoster';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { AnimeSeriesGroup } from './animeGrouping';
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
  const [isOpen, setIsOpen] = useState(false);
  const { useSelectedIds, toggleSelected } = useSelect<Series>();
  const selected = useSelectedIds();
  const selectedCount = group.members.filter((member) =>
    selected.includes(member.id)
  ).length;
  let selectionValue: boolean | null = selectedCount === 0 ? false : null;

  if (selectedCount === group.members.length) {
    selectionValue = true;
  }

  const representative = group.members[0];
  const seasons = group.members.filter(
    (member) => member.animeGroupMemberKind !== 'movie'
  );
  const movies = group.members.filter(
    (member) => member.animeGroupMemberKind === 'movie'
  );
  const memberLabel = (member: Series) =>
    member.animeGroupMemberKind === 'movie'
      ? `${translate('AnimeGroupMovieLabel', {
          number: member.animeGroupMovieNumber!,
        })} · ${member.title}`
      : translate('AnimeGroupSeasonLabel', {
          season: member.animeGroupSeasonNumber!,
        });
  const totals = group.members.reduce(
    (acc, member) => ({
      episodeCount: acc.episodeCount + (member.statistics?.episodeCount ?? 0),
      episodeFileCount:
        acc.episodeFileCount + (member.statistics?.episodeFileCount ?? 0),
    }),
    { episodeCount: 0, episodeFileCount: 0 }
  );
  const onSelect = useCallback(
    ({ value }: CheckInputChanged) => {
      group.members.forEach((member) =>
        toggleSelected({ id: member.id, isSelected: value, shiftKey: false })
      );
    },
    [group.members, toggleSelected]
  );
  const onOpen = useCallback(() => setIsOpen(true), []);
  const onClose = useCallback(() => setIsOpen(false), []);

  return (
    <>
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
          <Button className={styles.posterButton} onPress={onOpen}>
            <SeriesPoster
              images={representative.images}
              title={group.title}
              size={250}
              style={{ width: posterWidth, height: posterHeight }}
            />
          </Button>
        )}
        <div className={styles.details}>
          <Button className={styles.title} onPress={onOpen}>
            {group.title}
          </Button>
          <div className={styles.count}>
            {translate('AnimeGroupMemberCounts', {
              seasons: seasons.length,
              movies: movies.length,
            })}{' '}
            · {totals.episodeFileCount}/{totals.episodeCount}{' '}
            {translate('Episodes')}
          </div>
          {view === 'table' && group.members.length === 1 ? (
            <span className={styles.singletonSeason}>
              {memberLabel(representative)}
            </span>
          ) : null}
          {view === 'table' ? null : (
            <div className={styles.seasons}>
              {group.members.map((member) => (
                <span key={member.id}>{memberLabel(member)}</span>
              ))}
            </div>
          )}
        </div>
      </div>
      <Modal isOpen={isOpen} onModalClose={onClose}>
        <ModalContent onModalClose={onClose}>
          <ModalHeader>{group.title}</ModalHeader>
          <ModalBody>
            <p>{translate('AnimeGroupNativeEntryHelp')}</p>
            {[
              {
                key: 'seasons',
                label: translate('AnimeGroupSeasonsHeading'),
                members: seasons,
              },
              {
                key: 'movies',
                label: translate('AnimeGroupMovies'),
                members: movies,
              },
            ]
              .filter((section) => section.members.length > 0)
              .map((section) => (
                <div key={section.key} data-anime-group-section={section.key}>
                  <h2>{section.label}</h2>
                  {section.members.map((member) => (
                    <section
                      key={member.id}
                      className={styles.seasonEntry}
                      data-anime-group-series-id={member.id}
                      data-anime-group-member-kind={
                        member.animeGroupMemberKind ?? 'season'
                      }
                    >
                      <h3>
                        {memberLabel(member)}
                        {member.animeGroupMemberKind === 'movie'
                          ? null
                          : ` · ${member.title}`}
                      </h3>
                      <SeriesIndexOverview
                        seriesId={member.id}
                        sortKey="sortTitle"
                        posterWidth={100}
                        posterHeight={147}
                        rowHeight={225}
                        isSelectMode={isSelectMode}
                      />
                      {member.animeGroupMemberKind === 'movie' ? null : (
                        <div className={styles.searches}>
                          {member.seasons
                            ?.filter((season) => season.seasonNumber > 0)
                            .map((season, _index, nativeSeasons) => (
                              <SeasonReleaseSearchButton
                                key={season.seasonNumber}
                                seriesId={member.id}
                                seasonNumber={season.seasonNumber}
                                displaySeasonNumber={
                                  nativeSeasons.length === 1
                                    ? member.animeGroupSeasonNumber ??
                                      season.seasonNumber
                                    : season.seasonNumber
                                }
                                episodeCount={
                                  season.statistics?.totalEpisodeCount ??
                                  season.statistics?.episodeCount ??
                                  0
                                }
                              />
                            ))}
                        </div>
                      )}
                    </section>
                  ))}
                </div>
              ))}
          </ModalBody>
          <ModalFooter>
            <Button onPress={onClose}>{translate('Close')}</Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </>
  );
}

export default AnimeGroupSummary;
