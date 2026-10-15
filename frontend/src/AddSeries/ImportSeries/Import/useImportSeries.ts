import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import getMediaType from 'Series/getMediaType';
import isSameSeriesIdentity, {
  getSeriesIdentityKey,
} from 'Series/isSameSeriesIdentity';
import Series from 'Series/Series';
import {
  getImportSeriesItems,
  removeImportSeriesItemByPath,
} from './importSeriesStore';

export const useImportSeries = () => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<Series[], Series[]>({
    path: '/series/import',
    method: 'POST',
    mutationOptions: {
      onSuccess: (data, newSeries) => {
        queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });
        queryClient.setQueryData<Series[]>(['/series'], (oldSeries) => {
          if (!oldSeries) {
            return data;
          }

          return [...oldSeries, ...data];
        });

        newSeries.forEach((series) => {
          removeImportSeriesItemByPath(series.path);
        });
      },
    },
  });

  const importSeries = useCallback(
    (ids: string[]) => {
      const items = getImportSeriesItems(ids);
      const addedIds: string[] = [];

      const allNewSeries = ids.reduce<Series[]>((acc, id) => {
        const item = items.find((i) => i.id === id);
        const selectedSeries = item?.selectedSeries;

        // Make sure we have a selected series and the same series hasn't been added yet.
        if (
          selectedSeries &&
          getSeriesIdentityKey(selectedSeries) &&
          !acc.some((a) => isSameSeriesIdentity(a, selectedSeries))
        ) {
          const isMovie = getMediaType(selectedSeries) === 'movie';
          const newSeries: Series = {
            ...selectedSeries,
            monitored: true,
            monitorNewItems: 'all',
            qualityProfileId: item.qualityProfileId,
            path: item.path,
            seriesType: isMovie ? selectedSeries.seriesType : item.seriesType,
            language: item.language,
            seasonType: isMovie ? selectedSeries.seasonType : item.seasonType,
            seasonFolder: isMovie ? false : item.seasonFolder,
            addOptions: {
              monitor:
                isMovie && item.monitor !== 'none' ? 'all' : item.monitor,
              searchForMissingEpisodes: false,
              searchForCutoffUnmetEpisodes: false,
            },
            tags: [],
          };

          newSeries.path = item.path;

          addedIds.push(id);
          acc.push(newSeries);
        }

        return acc;
      }, []);

      if (allNewSeries.length > 0) {
        mutate(allNewSeries);
      }
    },
    [mutate]
  );

  return {
    isImporting: isPending,
    importError: error,
    importSeries,
  };
};
