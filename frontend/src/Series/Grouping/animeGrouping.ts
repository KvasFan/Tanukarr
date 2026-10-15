import Series, {
  AnimeGroupingFields,
  AnimeGroupMemberKind,
} from 'Series/Series';

export interface AnimeSeriesGroup {
  id: string;
  title: string;
  members: Series[];
}

export function getAnimeGroupMemberNumber(series: AnimeGroupingFields) {
  switch (series.animeGroupMemberKind) {
    case 'movie':
      return series.animeGroupMovieNumber;
    case 'special':
      return series.animeGroupSpecialNumber;
    default:
      return series.animeGroupSeasonNumber;
  }
}

export function getAvailableGroupMemberNumber(
  members: Series[],
  kind: AnimeGroupMemberKind,
  excludedId?: number,
  slotCount = 1
) {
  const occupied = new Set(
    members
      .filter(
        (item) =>
          item.id !== excludedId &&
          (item.animeGroupMemberKind ?? 'season') === kind
      )
      .flatMap((item) => {
        const first = getAnimeGroupMemberNumber(item) ?? 0;
        const count =
          kind === 'season'
            ? Math.max(
                1,
                new Set(
                  item.seasons
                    ?.filter((season) => season.seasonNumber > 0)
                    .map((season) => season.seasonNumber)
                ).size
              )
            : 1;
        return Array.from({ length: count }, (_, index) => first + index);
      })
  );
  let number = 1;

  const isOccupied = (start: number) =>
    Array.from({ length: slotCount }, (_, index) => start + index).some(
      (slot) => occupied.has(slot)
    );

  while (isOccupied(number) && number < 999) {
    number++;
  }

  return number;
}

export function compareAnimeGroupMembers(a: Series, b: Series) {
  const order = { season: 0, movie: 1, special: 2 };
  return (
    order[a.animeGroupMemberKind ?? 'season'] -
      order[b.animeGroupMemberKind ?? 'season'] ||
    (getAnimeGroupMemberNumber(a) ?? 0) - (getAnimeGroupMemberNumber(b) ?? 0) ||
    a.id - b.id
  );
}

export function hasAnimeGroup(series: AnimeGroupingFields): boolean {
  const number = getAnimeGroupMemberNumber(series);
  return (
    series.animeGroupingMode !== 'disabled' &&
    !!series.animeGroupId &&
    !!series.animeGroupTitle?.trim() &&
    Number.isInteger(number) &&
    number! >= 1 &&
    number! <= 999
  );
}

// Apply after filtering and sorting. Never replace the native list used for bulk
// actions, and never infer membership from titles or provider foreign IDs.
export function groupAnimeSeries(
  items: ReadonlyArray<Series>,
  enabled: boolean
) {
  const displayItems: Series[] = [];
  const groupsByRepresentativeId = new Map<number, AnimeSeriesGroup>();
  const groups = new Map<string, AnimeSeriesGroup>();

  items.forEach((series) => {
    if (!enabled || !hasAnimeGroup(series)) {
      displayItems.push(series);
      return;
    }

    const id = series.animeGroupId!;
    const existing = groups.get(id);

    if (existing) {
      existing.members.push(series);
      return;
    }

    const group = { id, title: series.animeGroupTitle!, members: [series] };
    groups.set(id, group);
    groupsByRepresentativeId.set(series.id, group);
    displayItems.push(series);
  });

  groups.forEach((group) => {
    group.members.sort(compareAnimeGroupMembers);
  });

  return { displayItems, groupsByRepresentativeId };
}

export function canConfigureAnimeGrouping(series: Series) {
  // Every catalogue supports an explicit manual group. Automatic relation
  // previews remain limited to catalogues that expose the supported graph.
  return !!series;
}

export function createManualGroupId() {
  if (typeof crypto.randomUUID === 'function') {
    return `manual:${crypto.randomUUID()}`;
  }

  // LAN HTTP installations may lack randomUUID but still provide secure random
  // bytes. Never use a title-derived ID that could silently combine two groups.
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] % 16) + 64;
  bytes[8] = (bytes[8] % 64) + 128;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0'));
  return `manual:${hex.slice(0, 4).join('')}-${hex.slice(4, 6).join('')}-${hex
    .slice(6, 8)
    .join('')}-${hex.slice(8, 10).join('')}-${hex.slice(10).join('')}`;
}
