import { getSeriesIdentityKey } from 'Series/isSameSeriesIdentity';
import type { ImportSeriesItem } from './importSeriesStore';

type ImportSeriesEligibilityItem = Pick<
  ImportSeriesItem,
  'id' | 'selectedSeries'
>;

export type ImportSeriesEligibility =
  | 'ready'
  | 'unmatched'
  | 'existing'
  | 'duplicate';

export interface ImportSeriesEligibilityResult {
  eligibilityById: Map<string, ImportSeriesEligibility>;
  duplicateIdsByIdentity: Map<string, string[]>;
}

export interface ImportSeriesSelectionResult {
  importableIds: string[];
  duplicateIdsToDeselect: Set<string>;
}

export function getImportSeriesSelection(
  { eligibilityById, duplicateIdsByIdentity }: ImportSeriesEligibilityResult,
  selectedIds: string[]
): ImportSeriesSelectionResult {
  const selectedIdSet = new Set(selectedIds);
  const chosenDuplicateIds = new Set<string>();
  const duplicateIdsToDeselect = new Set<string>();

  duplicateIdsByIdentity.forEach((duplicateIds) => {
    let hasChosenId = false;

    duplicateIds.forEach((duplicateId) => {
      if (!selectedIdSet.has(duplicateId)) {
        return;
      }

      if (hasChosenId) {
        duplicateIdsToDeselect.add(duplicateId);
      } else {
        chosenDuplicateIds.add(duplicateId);
        hasChosenId = true;
      }
    });
  });

  const importableIds = selectedIds.filter((id) => {
    const status = eligibilityById.get(id);

    return status === 'ready' || chosenDuplicateIds.has(id);
  });

  return { importableIds, duplicateIdsToDeselect };
}

export function getImportSeriesEligibility(
  items: ImportSeriesEligibilityItem[],
  existingIdentities: Set<string>
): ImportSeriesEligibilityResult {
  const idsByIdentity = items.reduce<Map<string, string[]>>((ids, item) => {
    const identity = getSeriesIdentityKey(item.selectedSeries ?? {});

    if (identity != null) {
      const existingIds = ids.get(identity);

      if (existingIds) {
        existingIds.push(item.id);
      } else {
        ids.set(identity, [item.id]);
      }
    }

    return ids;
  }, new Map());

  const eligibilityById = items.reduce<Map<string, ImportSeriesEligibility>>(
    (eligibility, item) => {
      const identity = getSeriesIdentityKey(item.selectedSeries ?? {});
      let status: ImportSeriesEligibility = 'unmatched';

      if (identity != null && existingIdentities.has(identity)) {
        status = 'existing';
      } else if (
        identity != null &&
        (idsByIdentity.get(identity)?.length ?? 0) > 1
      ) {
        status = 'duplicate';
      } else if (identity != null) {
        status = 'ready';
      }

      eligibility.set(item.id, status);

      return eligibility;
    },
    new Map()
  );

  const duplicateIdsByIdentity = new Map<string, string[]>();

  idsByIdentity.forEach((ids, identity) => {
    if (ids.length > 1 && !existingIdentities.has(identity)) {
      duplicateIdsByIdentity.set(identity, ids);
    }
  });

  return {
    eligibilityById,
    duplicateIdsByIdentity,
  };
}
