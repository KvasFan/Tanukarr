import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useMemo, useState } from 'react';
import Button from 'Components/Link/Button';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useRootFolders from 'RootFolder/useRootFolders';
import { useImportLists } from 'Settings/ImportLists/ImportLists/useImportLists';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import styles from './LibraryTools.module.css';

interface PreviewItem {
  key: number;
  title: string;
  year: number;
  alreadyAdded: boolean;
  excluded: boolean;
  supported: boolean;
  metadataSource: string | null;
  foreignId: string | null;
}
interface Preview {
  token: string;
  partial: boolean;
  items: PreviewItem[];
}

function getMetadataProviderName(source: string) {
  switch (source.toLowerCase()) {
    case 'tvdb':
      return 'TheTVDB';
    case 'tmdb':
      return 'TMDB';
    case 'anilist':
      return 'AniList';
    case 'myanimelist':
      return 'MyAnimeList';
    default:
      return source;
  }
}

function getPreviewItemStatus(item: PreviewItem) {
  if (item.alreadyAdded) {
    return 'Already in your library';
  }

  if (item.excluded) {
    return 'Excluded from import lists';
  }

  if (!item.supported) {
    return 'No supported show ID; add through Search instead';
  }

  return '';
}

export default function ListPreview() {
  const client = useQueryClient();
  const lists = useImportLists();
  const roots = useRootFolders();
  const profiles = useApiQuery<
    Array<{
      id: number;
      name: string;
    }>
  >({
    path: '/qualityprofile',
  });
  const [listId, setListId] = useState(0);
  const [selection, setSelection] = useState({
    token: '',
    keys: [] as number[],
  });
  const [folderId, setFolderId] = useState(0);
  const [profileId, setProfileId] = useState(0);
  const [monitored, setMonitored] = useState(false);
  const preview = useApiQuery<Preview>({
    path: `/librarytools/lists/${listId}`,
    queryOptions: {
      enabled: listId > 0,
      refetchOnWindowFocus: false,
    },
  });
  const refresh = useApiMutation<void, undefined>({
    path: `/librarytools/lists/${listId}/preview`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        setSelection({ token: '', keys: [] });
        preview.refetch();
      },
    },
  });
  const add = useApiMutation<
    Array<{
      key: number;
      title: string;
      status: string;
    }>,
    {
      token: string;
      keys: number[];
      rootFolderId: number;
      qualityProfileId: number;
      monitored: boolean;
    }
  >({
    path: `/librarytools/lists/${listId}/add`,
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        setSelection({ token: '', keys: [] });
        preview.refetch();
        client.invalidateQueries({ queryKey: ['/series'] });
      },
    },
  });
  const list = lists.data.find((item) => {
    return item.id === listId;
  });
  const rootFolderId =
    folderId ||
    roots.data.find((root) => {
      return root.path === list?.rootFolderPath;
    })?.id ||
    roots.data[0]?.id ||
    0;
  const qualityProfileId =
    profileId || list?.qualityProfileId || profiles.data?.[0]?.id || 0;
  const selected = useMemo(
    () =>
      selection.token === preview.data?.token
        ? selection.keys.filter((key) =>
            preview.data?.items.some(
              (item) =>
                item.key === key &&
                item.supported &&
                !item.alreadyAdded &&
                !item.excluded
            )
          )
        : [],
    [preview.data, selection]
  );
  const isBusy = preview.isFetching || refresh.isPending || add.isPending;
  const error =
    preview.error ??
    add.error ??
    refresh.error ??
    profiles.error ??
    lists.error ??
    roots.error;
  const handleSelection = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      if (!preview.data || preview.data.partial || isBusy) {
        return;
      }

      const currentPreview = preview.data;
      const token = currentPreview.token;
      const key = Number(event.currentTarget.value);
      const isChecked = event.currentTarget.checked;

      setSelection((previous) => {
        const keys =
          previous.token === token
            ? previous.keys.filter((selectedKey) =>
                currentPreview.items.some(
                  (item) =>
                    item.key === selectedKey &&
                    item.supported &&
                    !item.alreadyAdded &&
                    !item.excluded
                )
              )
            : [];

        return {
          token,
          keys: isChecked
            ? Array.from(new Set([...keys, key])).slice(0, 25)
            : keys.filter((selectedKey) => selectedKey !== key),
        };
      });
    },
    [isBusy, preview.data]
  );
  const handleChange1 = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setListId(Number(event.target.value));
      setSelection({ token: '', keys: [] });
      setFolderId(0);
      setProfileId(0);
      setMonitored(false);
      add.reset();
      refresh.reset();
    },
    [add, refresh]
  );
  const handlePress2 = useCallback(() => refresh.mutate(undefined), [refresh]);
  const handleChange3 = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) =>
      setFolderId(Number(event.target.value)),
    [setFolderId]
  );
  const handleChange4 = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) =>
      setProfileId(Number(event.target.value)),
    [setProfileId]
  );
  const handleChange5 = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) =>
      setMonitored(event.target.checked),
    [setMonitored]
  );
  const handlePress7 = useCallback(() => {
    if (
      !preview.data ||
      preview.data.partial ||
      isBusy ||
      !selected.length ||
      !rootFolderId ||
      !qualityProfileId
    ) {
      return;
    }

    add.mutate({
      token: preview.data.token,
      keys: selected,
      rootFolderId,
      qualityProfileId,
      monitored,
    });
  }, [
    add,
    isBusy,
    monitored,
    preview.data,
    qualityProfileId,
    rootFolderId,
    selected,
  ]);
  return (
    <section>
      <h2>Preview import lists</h2>
      <p>
        Choose shows before adding them. Saving a list with automatic add off
        lets you use it here. Preview and adding selected shows do not start
        downloads.
      </p>
      <p>
        Import lists choose which shows to add. The metadata provider shown for
        each show supplies its series and episode information.
      </p>
      <label className={styles.form}>
        List
        <select
          value={listId}
          disabled={refresh.isPending || add.isPending}
          onChange={handleChange1}
        >
          <option value={0}>Choose a list</option>
          {lists.data.map((item) => {
            return (
              <option key={item.id} value={item.id}>
                {item.name} ({item.implementationName})
              </option>
            );
          })}
        </select>
      </label>
      {listId > 0 ? (
        <Button
          isDisabled={preview.isFetching || refresh.isPending || add.isPending}
          onPress={handlePress2}
        >
          Refresh preview
        </Button>
      ) : null}
      {list ? <p>Import source: {list.implementationName}</p> : null}
      {error ? (
        <p role="alert">{getErrorMessage(error, error.message)}</p>
      ) : null}
      {preview.isFetching ? <p role="status">Loading list…</p> : null}
      {preview.data ? (
        <>
          {preview.data.partial ? (
            <p role="alert">
              The list provider returned incomplete results or an error. Review
              these entries, test the list in Settings, then refresh the
              preview. Adding is available after a complete preview succeeds.
            </p>
          ) : null}
          {!preview.data.partial && preview.data.items.length === 0 ? (
            <p role="status">No shows found in this list.</p>
          ) : null}
          <div className={styles.form}>
            <label>
              Library folder
              <select
                value={rootFolderId}
                disabled={add.isPending}
                onChange={handleChange3}
              >
                {roots.data.map((root) => {
                  return (
                    <option key={root.id} value={root.id}>
                      {root.path}
                    </option>
                  );
                })}
              </select>
            </label>
            <label>
              Quality profile
              <select
                value={qualityProfileId}
                disabled={add.isPending}
                onChange={handleChange4}
              >
                {profiles.data?.map((profile) => {
                  return (
                    <option key={profile.id} value={profile.id}>
                      {profile.name}
                    </option>
                  );
                })}
              </select>
            </label>
          </div>
          <label className={styles.setting}>
            <input
              type="checkbox"
              checked={monitored}
              disabled={add.isPending}
              onChange={handleChange5}
            />
            <span>Monitor added shows for future releases</span>
          </label>
          <div className={styles.previewItems}>
            {preview.data.items.map((item) => {
              return (
                <label key={item.key} className={styles.setting}>
                  <input
                    type="checkbox"
                    checked={selected.includes(item.key)}
                    disabled={
                      isBusy ||
                      preview.data.partial ||
                      item.alreadyAdded ||
                      item.excluded ||
                      !item.supported ||
                      (!selected.includes(item.key) && selected.length >= 25)
                    }
                    value={item.key}
                    onChange={handleSelection}
                  />
                  <span>
                    {item.title}
                    {item.year ? ` (${item.year})` : ''}
                    <small>{getPreviewItemStatus(item)}</small>
                    {item.metadataSource ? (
                      <small>
                        Metadata: {getMetadataProviderName(item.metadataSource)}
                        {item.foreignId ? ` · ID ${item.foreignId}` : ''}
                      </small>
                    ) : null}
                  </span>
                </label>
              );
            })}
          </div>
          <div className={styles.selectionFooter}>
            <span>{selected.length} selected · up to 25 per batch</span>
            <Button
              isDisabled={
                selected.length === 0 ||
                isBusy ||
                preview.data.partial ||
                !rootFolderId ||
                !qualityProfileId
              }
              onPress={handlePress7}
            >
              {add.isPending ? 'Adding…' : 'Add selected shows'}
            </Button>
          </div>
          {add.data?.map((item) => {
            return (
              <p key={item.key} role="status">
                {item.title}: {item.status}
              </p>
            );
          })}
        </>
      ) : null}
    </section>
  );
}
