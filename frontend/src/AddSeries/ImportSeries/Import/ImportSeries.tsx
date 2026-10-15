import React, { useCallback, useEffect, useMemo, useRef } from 'react';
import { useParams } from 'react-router';
import {
  setAddSeriesOption,
  useAddSeriesOption,
} from 'AddSeries/addSeriesOptionsStore';
import { SelectProvider } from 'App/Select/SelectContext';
import Alert from 'Components/Alert';
import SelectInput from 'Components/Form/SelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import useRootFolders, { useRootFolder } from 'RootFolder/useRootFolders';
import useEnabledMetadataSource, {
  changeMetadataSource,
} from 'Settings/MetadataSource/useEnabledMetadataSource';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import ImportSeriesFooter from './ImportSeriesFooter';
import ImportSeriesList from './ImportSeriesList';
import {
  clearImportSeries,
  resetUnmatchedSearches,
  useLookupQueueHasItems,
} from './importSeriesStore';
import styles from './ImportSeries.module.css';

function ImportSeries() {
  const {
    metadataSource,
    sources,
    isFetched: sourcesFetched,
  } = useEnabledMetadataSource();
  const isLookingUpSeries = useLookupQueueHasItems();
  const { rootFolderId: rootFolderIdString = '' } = useParams<{
    rootFolderId: string;
  }>();
  const rootFolderId = parseInt(rootFolderIdString, 10);

  const {
    isFetching: rootFoldersFetching,
    isFetched: rootFoldersFetched,
    error: rootFoldersError,
    data: rootFolders,
  } = useRootFolders();

  useRootFolder(rootFolderId, false);

  const { path, unmappedFolders } = useMemo(() => {
    const rootFolder = rootFolders.find((r) => r.id === rootFolderId);

    return {
      path: rootFolder?.path ?? '',
      unmappedFolders:
        rootFolder?.unmappedFolders.map((unmappedFolders) => {
          return {
            ...unmappedFolders,
            id: unmappedFolders.name,
          };
        }) ?? [],
    };
  }, [rootFolders, rootFolderId]);

  const qualityProfiles = useQualityProfilesData();

  const defaultQualityProfileId = useAddSeriesOption('qualityProfileId');

  const scrollerRef = useRef<HTMLDivElement>(null);
  const handleSourceChange = useCallback((change: InputChanged<string>) => {
    resetUnmatchedSearches();
    changeMetadataSource(change);
  }, []);

  const items = useMemo(() => {
    return unmappedFolders.map((unmappedFolder) => {
      return {
        ...unmappedFolder,
        id: unmappedFolder.name,
      };
    });
  }, [unmappedFolders]);

  useEffect(() => {
    return () => {
      clearImportSeries();
    };
  }, [rootFolderId]);

  useEffect(() => {
    if (
      !defaultQualityProfileId ||
      !qualityProfiles.some((p) => p.id === defaultQualityProfileId)
    ) {
      setAddSeriesOption('qualityProfileId', qualityProfiles[0].id);
    }
  }, [defaultQualityProfileId, qualityProfiles]);

  return (
    <SelectProvider items={items}>
      <PageContent title={translate('ImportSeries')}>
        <PageContentBody ref={scrollerRef}>
          <div className={styles.content}>
            <PageHeading
              scope={translate('Media')}
              title={translate('ImportSeries')}
              actions={
                <label className={styles.source}>
                  {translate('MetadataSource')}
                  <SelectInput
                    name="importMetadataSource"
                    value={metadataSource}
                    values={[
                      {
                        key: 'all',
                        value: translate('MetadataSourcesAll'),
                      },
                      ...sources,
                    ]}
                    isDisabled={!sourcesFetched || isLookingUpSeries}
                    onChange={handleSourceChange}
                  />
                </label>
              }
            />

            {rootFoldersFetching && !rootFoldersFetched ? (
              <LoadingIndicator />
            ) : null}

            {!rootFoldersFetching && !!rootFoldersError ? (
              <Alert kind={kinds.DANGER}>
                {translate('RootFoldersLoadError')}
              </Alert>
            ) : null}

            {!rootFoldersError &&
            !rootFoldersFetching &&
            rootFoldersFetched &&
            !unmappedFolders.length ? (
              <Alert kind={kinds.INFO}>
                {translate('AllSeriesInRootFolderHaveBeenImported', { path })}
              </Alert>
            ) : null}

            {!rootFoldersError &&
            rootFoldersFetched &&
            !!unmappedFolders.length &&
            scrollerRef.current ? (
              <ImportSeriesList items={items} scrollerRef={scrollerRef} />
            ) : null}
          </div>
        </PageContentBody>

        {!rootFoldersError && rootFoldersFetched && !!unmappedFolders.length ? (
          <ImportSeriesFooter />
        ) : null}
      </PageContent>
    </SelectProvider>
  );
}

export default ImportSeries;
