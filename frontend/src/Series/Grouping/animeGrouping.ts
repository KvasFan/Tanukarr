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
  return series.animeGroupMemberKind === 'movie'
    ? series.animeGroupMovieNumber
    : series.animeGroupSeasonNumber;
}

export function getAvailableGroupMemberNumber(
  members: Series[],
  kind: AnimeGroupMemberKind,
  excludedId?: number
) {
  const occupied = new Set(
    members
      .filter(
        (item) =>
          item.id !== excludedId &&
          (item.animeGroupMemberKind ?? 'season') === kind
      )
      .map(getAnimeGroupMemberNumber)
  );
  let number = 1;

  while (occupied.has(number) && number < 999) {
    number++;
  }

  return number;
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
    group.members.sort(
      (a, b) =>
        Number(a.animeGroupMemberKind === 'movie') -
          Number(b.animeGroupMemberKind === 'movie') ||
        (getAnimeGroupMemberNumber(a) ?? 0) -
          (getAnimeGroupMemberNumber(b) ?? 0) ||
        a.id - b.id
    );
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
