import Series, { Image, Season } from 'Series/Series';
import { AnimeSeriesGroup, hasAnimeGroup } from './animeGrouping';

export interface CollectionEntry {
  key: string;
  member: Series;
  season: Season;
  displayNumber: number;
  section: 'seasons' | 'movies' | 'specials';
}

export function getCollectionLink(groupId: string) {
  return `/collections/${encodeURIComponent(`group:${groupId}`)}`;
}

export function getSeriesLibraryLink(series: Series) {
  if (hasAnimeGroup(series)) {
    return getCollectionLink(series.animeGroupId!);
  }

  return series.seasons.filter((season) => season.seasonNumber > 0).length > 1
    ? `/collections/${encodeURIComponent(`series:${series.id}`)}`
    : `/series/${series.titleSlug}`;
}

export function resolveCollection(
  items: ReadonlyArray<Series>,
  collectionId?: string
) {
  if (!collectionId) {
    return undefined;
  }

  if (collectionId.startsWith('series:')) {
    const id = Number(collectionId.slice(7));
    const member = items.find((item) => item.id === id);
    return member
      ? {
          id: collectionId,
          title: member.collectionTitle?.trim() || member.title,
          members: [member],
        }
      : undefined;
  }

  if (!collectionId.startsWith('group:')) {
    return undefined;
  }

  const groupId = collectionId.slice(6);
  const members = items
    .filter((item) => hasAnimeGroup(item) && item.animeGroupId === groupId)
    .sort(
      (a, b) =>
        Number(a.animeGroupMemberKind === 'movie') -
          Number(b.animeGroupMemberKind === 'movie') ||
        (a.animeGroupSeasonNumber ?? a.animeGroupMovieNumber ?? 0) -
          (b.animeGroupSeasonNumber ?? b.animeGroupMovieNumber ?? 0) ||
        a.id - b.id
    );

  return members.length
    ? { id: groupId, title: members[0].animeGroupTitle!, members }
    : undefined;
}

export function getCollectionImages(group: AnimeSeriesGroup): Image[] {
  return (['poster', 'fanart', 'banner'] as const).flatMap((coverType) => {
    const image = group.members
      .flatMap((member) => member.images)
      .find((item) => item.coverType === coverType && !!item.url);
    return image ? [image] : [];
  });
}

export function getCollectionEntries(
  group: AnimeSeriesGroup
): CollectionEntry[] {
  return group.members.flatMap((member) => {
    const regularSeasons = member.seasons.filter(
      (season) => season.seasonNumber > 0
    );
    return member.seasons
      .filter(
        (season) =>
          season.seasonNumber > 0 ||
          (season.statistics?.totalEpisodeCount ??
            season.statistics?.episodeCount ??
            0) > 0
      )
      .map((season) => {
        let section: CollectionEntry['section'] = 'seasons';

        if (season.seasonNumber === 0) {
          section = 'specials';
        } else if (member.animeGroupMemberKind === 'movie') {
          section = 'movies';
        }

        return {
          key: `${member.id}:${season.seasonNumber}`,
          member,
          season,
          displayNumber:
            regularSeasons.length === 1
              ? member.animeGroupSeasonNumber ?? season.seasonNumber
              : season.seasonNumber,
          section,
        };
      });
  });
}

export function getNativeSeasonLink(member: Series, seasonNumber: number) {
  return `/series/${member.titleSlug}?season=${seasonNumber}#season-${seasonNumber}`;
}
