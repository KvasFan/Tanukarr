import getMediaType from 'Series/getMediaType';
import Series, { Image, Season } from 'Series/Series';
import {
  AnimeSeriesGroup,
  compareAnimeGroupMembers,
  getAnimeGroupMemberNumber,
  hasAnimeGroup,
} from './animeGrouping';

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
    .sort(compareAnimeGroupMembers);

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
    const regularSeasons = member.seasons
      .filter((season) => season.seasonNumber > 0)
      .sort((a, b) => a.seasonNumber - b.seasonNumber);
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

        if (
          season.seasonNumber === 0 ||
          member.animeGroupMemberKind === 'special' ||
          getMediaType(member) === 'special'
        ) {
          section = 'specials';
        } else if (
          member.animeGroupMemberKind === 'movie' ||
          getMediaType(member) === 'movie'
        ) {
          section = 'movies';
        }

        let displayNumber =
          getAnimeGroupMemberNumber(member) ?? season.seasonNumber;
        if (season.seasonNumber === 0) displayNumber = 0;
        else if (section === 'seasons' && member.animeGroupSeasonNumber != null)
          displayNumber =
            member.animeGroupSeasonNumber +
            regularSeasons.findIndex(
              (item) => item.seasonNumber === season.seasonNumber
            );

        return {
          key: `${member.id}:${season.seasonNumber}`,
          member,
          season,
          displayNumber,
          section,
        };
      });
  });
}

export function getNativeSeasonLink(member: Series, seasonNumber: number) {
  return `/series/${member.titleSlug}?season=${seasonNumber}#season-${seasonNumber}`;
}
