import React, { useCallback, useMemo, useState } from 'react';
import { useParams } from 'react-router';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds } from 'Helpers/Props';
import { CoverType, Image } from 'Series/Series';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import CollectionMemberCard from './CollectionMemberCard';
import CollectionPoster from './CollectionPoster';
import {
  getCollectionEntries,
  getCollectionImages,
  resolveCollection,
} from './collectionPresentation';
import CollectionSearchModal from './CollectionSearchModal';
import CollectionSettingsModal from './CollectionSettingsModal';
import styles from './CollectionPage.module.css';

interface CollectionPresentation {
  overview?: string | null;
  images?: Array<{
    coverType: CoverType;
    url?: string | null;
    remoteUrl?: string | null;
  }>;
}

function CollectionPage() {
  const { collectionId } = useParams<{ collectionId: string }>();
  const { data: series, isFetched, error } = useSeries();
  const collection = useMemo(
    () => resolveCollection(series, collectionId),
    [series, collectionId]
  );
  const { data: presentation } = useApiQuery<CollectionPresentation>({
    path: '/collection/presentation',
    queryParams: { groupId: collection?.id },
    queryOptions: {
      enabled:
        !!collection &&
        /^(anilist|mal):[1-9]\d*$/.test(collection.id) &&
        collectionId?.startsWith('group:') === true,
      staleTime: 15 * 60 * 1000,
      retry: false,
    },
  });
  const executeCommand = useExecuteCommand();
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const openSearch = useCallback(() => setIsSearchOpen(true), []);
  const closeSearch = useCallback(() => setIsSearchOpen(false), []);
  const openSettings = useCallback(() => setIsSettingsOpen(true), []);
  const closeSettings = useCallback(() => setIsSettingsOpen(false), []);
  const refresh = useCallback(() => {
    if (collection) {
      executeCommand({
        name: CommandNames.RefreshSeries,
        seriesIds: collection.members.map((member) => member.id),
      });
    }
  }, [collection, executeCommand]);

  if (!isFetched && !collection) {
    return (
      <PageContent title={translate('Collection')}>
        <LoadingIndicator />
      </PageContent>
    );
  }

  if (error) {
    return (
      <PageContent title={translate('Collection')}>
        <Alert kind={kinds.DANGER}>{translate('CollectionsLoadError')}</Alert>
      </PageContent>
    );
  }

  if (!collection) {
    return <NotFound message={translate('CollectionCannotBeFound')} />;
  }

  const entries = getCollectionEntries(collection);
  const rootImages: Image[] = (presentation?.images ?? []).flatMap((image) => {
    const url = image.url || image.remoteUrl;
    return url ? [{ ...image, url, remoteUrl: image.remoteUrl || url }] : [];
  });
  const images = [...rootImages, ...getCollectionImages(collection)];
  const backdrop =
    images.find((image) => image.coverType === 'fanart') ??
    images.find((image) => image.coverType === 'banner') ??
    images.find((image) => image.coverType === 'poster');
  const overview =
    presentation?.overview?.trim() ||
    collection.members.find((member) => member.overview?.trim())?.overview;
  const genres = [
    ...new Set(collection.members.flatMap((member) => member.genres ?? [])),
  ].slice(0, 6);
  const sections = [
    { key: 'seasons', title: translate('AnimeGroupSeasonsHeading') },
    { key: 'movies', title: translate('AnimeGroupMovies') },
    { key: 'specials', title: translate('Specials') },
  ];
  const hasEpisodes = entries.some(
    (entry) =>
      (entry.season.statistics?.totalEpisodeCount ??
        entry.season.statistics?.episodeCount ??
        0) > 0
  );

  return (
    <PageContent title={collection.title}>
      <PageContentBody
        innerClassName={styles.body}
        scrollPositionKey={`collection:${collection.id}`}
      >
        <header className={styles.hero} data-collection-id={collection.id}>
          {backdrop ? (
            <div
              className={styles.backdrop}
              style={{
                backgroundImage: `url(${JSON.stringify(backdrop.url)})`,
              }}
            />
          ) : null}
          <div className={styles.heroContent}>
            <CollectionPoster
              members={collection.members}
              title={collection.title}
              className={styles.heroPoster}
              lazy={false}
              preferredImages={rootImages}
            />
            <div className={styles.heroInfo}>
              <Link className={styles.backLink} to="/series">
                <Icon name={icons.PAGE_PREVIOUS} /> {translate('Series')}
              </Link>
              <span className={styles.eyebrow}>
                {translate(
                  collection.members[0].animeGroupKind === 'movieSaga'
                    ? 'AnimeGroupMovieSaga'
                    : 'Collection'
                )}
              </span>
              <h1 className={styles.title}>{collection.title}</h1>
              <div className={styles.tags}>
                {genres.map((genre) => (
                  <span key={genre}>{genre}</span>
                ))}
              </div>
              {overview ? <p className={styles.overview}>{overview}</p> : null}
              <nav
                className={styles.heroActions}
                aria-label={translate('CollectionActions')}
              >
                <Button
                  kind={kinds.PRIMARY}
                  isDisabled={!hasEpisodes}
                  onPress={openSearch}
                >
                  <Icon name={icons.SEARCH} /> {translate('SearchCollection')}
                </Button>
                <Button onPress={refresh}>
                  <Icon name={icons.REFRESH} /> {translate('RefreshAndScan')}
                </Button>
                <Button onPress={openSettings}>
                  <Icon name={icons.EDIT} /> {translate('CollectionSettings')}
                </Button>
              </nav>
            </div>
          </div>
        </header>
        <div className={styles.contents}>
          <nav
            className={styles.sectionLinks}
            aria-label={translate('CollectionContents')}
          >
            {sections.map((section) => {
              const count = entries.filter(
                (entry) => entry.section === section.key
              ).length;
              return count ? (
                <Link
                  key={section.key}
                  noRouter={true}
                  to={`#collection-${section.key}`}
                >
                  {section.title} <span>{count}</span>
                </Link>
              ) : null;
            })}
          </nav>
          {sections.map((section) => {
            const sectionEntries = entries.filter(
              (entry) => entry.section === section.key
            );
            return sectionEntries.length ? (
              <section
                key={section.key}
                id={`collection-${section.key}`}
                className={styles.section}
                data-anime-group-section={section.key}
              >
                <h2 className={styles.sectionTitle}>
                  {section.title}
                  <span>{sectionEntries.length}</span>
                </h2>
                <div className={styles.cards}>
                  {sectionEntries.map((entry) => (
                    <CollectionMemberCard key={entry.key} entry={entry} />
                  ))}
                </div>
              </section>
            ) : null;
          })}
          {entries.length ? null : (
            <Alert kind={kinds.INFO}>{translate('NoEpisodeInformation')}</Alert>
          )}
        </div>
      </PageContentBody>
      <CollectionSearchModal
        isOpen={isSearchOpen}
        collection={collection}
        onModalClose={closeSearch}
      />
      <CollectionSettingsModal
        isOpen={isSettingsOpen}
        collection={collection}
        isNativeCollection={collectionId?.startsWith('series:') ?? false}
        onModalClose={closeSettings}
      />
    </PageContent>
  );
}

export default CollectionPage;
