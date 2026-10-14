import React, { useCallback } from 'react';
import { SelectedFilterKey, SetFilter } from 'Components/Filter/Filter';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import InteractiveSearchType from 'InteractiveSearch/InteractiveSearchType';
import InteractiveSearchPayload from './InteractiveSearchPayload';
import { setReleaseOption } from './releaseOptionsStore';
import useReleases, { FILTER_BUILDER, Release } from './useReleases';

interface InteractiveSearchFilterModalProps extends FilterModalProps<Release> {
  type: InteractiveSearchType;
  searchPayload: InteractiveSearchPayload;
  localFilterKey?: SelectedFilterKey;
  onLocalFilterSelect?: (selectedFilterKey: SelectedFilterKey) => void;
}

export default function InteractiveSearchFilterModal({
  type,
  searchPayload,
  localFilterKey,
  onLocalFilterSelect,
  ...otherProps
}: InteractiveSearchFilterModalProps) {
  const { data } = useReleases(searchPayload, '', localFilterKey);

  const handleFilterSelect = useCallback(
    (selectedFilter: SetFilter) => {
      if (onLocalFilterSelect) {
        onLocalFilterSelect(selectedFilter.selectedFilterKey);
      } else if (type === 'episode') {
        setReleaseOption(
          'episodeSelectedFilterKey',
          selectedFilter.selectedFilterKey
        );
      } else {
        setReleaseOption(
          'seasonSelectedFilterKey',
          selectedFilter.selectedFilterKey
        );
      }
    },
    [onLocalFilterSelect, type]
  );

  return (
    <FilterModal
      {...otherProps}
      sectionItems={data}
      filterBuilderProps={FILTER_BUILDER}
      customFilterType="releases"
      dispatchSetFilter={handleFilterSelect}
    />
  );
}
