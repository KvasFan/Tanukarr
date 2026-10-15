import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import SelectInput from 'Components/Form/SelectInput';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import InteractiveSearch from 'InteractiveSearch/InteractiveSearch';
import { Release } from 'InteractiveSearch/useReleases';
import getMediaType from 'Series/getMediaType';
import { InputChanged } from 'typings/inputs';
import fetchJson from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import getQueryString from 'Utilities/Fetch/getQueryString';
import translate from 'Utilities/String/translate';
import { AnimeSeriesGroup } from './animeGrouping';
import styles from './CollectionPage.module.css';

interface CollectionSearchModalProps {
  isOpen: boolean;
  collection: AnimeSeriesGroup;
  onModalClose(): void;
}

interface SearchStatus {
  seriesId: number;
  title: string;
  state: 'pending' | 'searching' | 'complete' | 'failed';
  count: number;
}

function CollectionSearchContent({
  collection,
  onModalClose,
}: Omit<CollectionSearchModalProps, 'isOpen'>) {
  const queryClient = useQueryClient();
  const jobs = useRef(
    collection.members
      .filter((member) =>
        member.seasons.some(
          (season) =>
            (season.statistics?.totalEpisodeCount ??
              season.statistics?.episodeCount ??
              0) > 0
        )
      )
      .map((member, index, members) => ({
        seriesId: member.id,
        title: member.title,
        payload:
          getMediaType(member) === 'series' ||
          (getMediaType(member) === 'movie' &&
            members.findIndex((item) => getMediaType(item) === 'movie') ===
              index)
            ? { seriesId: member.id, completeSeries: true as const }
            : { seriesId: member.id, seasonNumber: 1 },
      }))
  ).current;
  const [statuses, setStatuses] = useState<SearchStatus[]>(() =>
    jobs.map((job) => ({ ...job, state: 'pending', count: 0 }))
  );
  const [selectedId, setSelectedId] = useState<number | null>(
    jobs[0]?.seriesId ?? null
  );
  const bodyRef = useRef<HTMLDivElement>(null);
  const changeSelection = useCallback(
    ({ value }: InputChanged<string>) => setSelectedId(Number(value)),
    []
  );

  useEffect(() => {
    const controller = new AbortController();
    const update = (
      seriesId: number,
      state: SearchStatus['state'],
      count = 0
    ) =>
      setStatuses((current) =>
        current.map((status) =>
          status.seriesId === seriesId ? { ...status, state, count } : status
        )
      );

    const run = async () => {
      for (const job of jobs) {
        if (controller.signal.aborted) {
          break;
        }

        const payload = job.payload;
        update(job.seriesId, 'searching');

        try {
          const releases = await fetchJson<Release[], unknown>({
            path: getQueryPath('/release') + getQueryString(payload),
            signal: controller.signal,
            headers: {
              'X-Api-Key': window.Sonarr.apiKey,
              'X-Sonarr-Client': 'Sonarr',
            },
          });

          if (controller.signal.aborted) {
            break;
          }

          queryClient.setQueryData(['/release', payload], releases);
          update(job.seriesId, 'complete', releases.length);
        } catch {
          if (!controller.signal.aborted) {
            update(job.seriesId, 'failed');
          }
        }
      }
    };

    run();

    return () => {
      controller.abort();
      jobs.forEach((job) =>
        queryClient.removeQueries({
          queryKey: ['/release', job.payload],
          exact: true,
        })
      );
    };
  }, [jobs, queryClient]);

  const completed = statuses.filter(
    (status) => status.state === 'complete' || status.state === 'failed'
  ).length;
  const isSearching = completed < jobs.length;
  const failed = statuses.filter((status) => status.state === 'failed');
  const selectedStatus = statuses.find(
    (status) => status.seriesId === selectedId
  );
  const selectedJob = jobs.find((job) => job.seriesId === selectedId);

  useEffect(() => {
    if (selectedStatus?.state === 'failed') {
      const successful = statuses.find((status) => status.state === 'complete');

      if (successful) {
        setSelectedId(successful.seriesId);
      }
    }
  }, [selectedStatus, statuses]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('SearchCollection')} · {collection.title}
      </ModalHeader>
      <ModalBody ref={bodyRef}>
        <div className={styles.searchProgress} role="status">
          {translate(
            isSearching
              ? 'CollectionSearchProgress'
              : 'CollectionSearchFinished',
            { count: completed, total: jobs.length }
          )}
        </div>
        <SelectInput
          name="collectionSearchSeries"
          value={selectedId ?? ''}
          values={[
            { key: '', value: translate('SelectSeries'), disabled: true },
            ...statuses.map((status) => ({
              key: status.seriesId,
              value: `${status.title}${
                status.state === 'complete' ? ` (${status.count})` : ''
              }`,
              disabled: status.state !== 'complete',
            })),
          ]}
          onChange={changeSelection}
        />
        {failed.map((status) => (
          <Alert key={status.seriesId} kind={kinds.DANGER}>
            {translate('CollectionSearchEntryError', { title: status.title })}
          </Alert>
        ))}
        {selectedId === null || selectedStatus?.state !== 'complete' ? null : (
          <InteractiveSearch
            key={selectedId}
            type="series"
            searchPayload={selectedJob!.payload}
            scrollerRef={bodyRef}
            initialSelectedFilterKey="all"
          />
        )}
      </ModalBody>
      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

function CollectionSearchModal({
  isOpen,
  collection,
  onModalClose,
}: CollectionSearchModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      onModalClose={onModalClose}
    >
      <CollectionSearchContent
        collection={collection}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default CollectionSearchModal;
