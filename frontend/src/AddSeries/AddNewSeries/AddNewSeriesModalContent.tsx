import React, { useCallback, useEffect, useMemo, useState } from 'react';
import AddSeries from 'AddSeries/AddSeries';
import {
  AddSeriesOptions,
  setAddSeriesOption,
  useAddSeriesOptions,
} from 'AddSeries/addSeriesOptionsStore';
import SeriesMonitoringOptionsPopoverContent from 'AddSeries/SeriesMonitoringOptionsPopoverContent';
import SeriesTypePopoverContent from 'AddSeries/SeriesTypePopoverContent';
import { useAppDimension } from 'App/appStore';
import CheckInput from 'Components/Form/CheckInput';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { LanguageSelectInputOnChangeProps } from 'Components/Form/Select/LanguageSelectInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Popover from 'Components/Tooltip/Popover';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { icons, inputTypes, kinds, tooltipPositions } from 'Helpers/Props';
import Language, { DEFAULT_LANGUAGE } from 'Language/Language';
import { useLanguageById } from 'Language/useLanguages';
import getMediaType from 'Series/getMediaType';
import {
  canConfigureAnimeGrouping,
  hasAnimeGroup,
} from 'Series/Grouping/animeGrouping';
import AnimeGroupingOptions from 'Series/Grouping/AnimeGroupingOptions';
import useAnimeGroupingPreview from 'Series/Grouping/useAnimeGroupingPreview';
import { AnimeGroupingFields, SeriesType } from 'Series/Series';
import SeriesPoster from 'Series/SeriesPoster';
import { useMetadataSourceSettingsValues } from 'Settings/MetadataSource/useMetadataSourceSettings';
import { useIsWindows } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import selectSettings from 'Utilities/selectSettings';
import translate from 'Utilities/String/translate';
import { useAddSeries } from './useAddSeries';
import { useEpisodeOrderings } from './useEpisodeOrderings';
import styles from './AddNewSeriesModalContent.module.css';

export interface AddNewSeriesModalContentProps {
  series: AddSeries;
  initialSeriesType: SeriesType;
  onModalClose: () => void;
}

function AddNewSeriesModalContent({
  series,
  initialSeriesType,
  onModalClose,
}: AddNewSeriesModalContentProps) {
  const isMovie = getMediaType(series) === 'movie';
  const {
    title: lookupTitle,
    originalTitle,
    year,
    overview: lookupOverview,
    images,
    seasonTypes,
    translations,
    folder: originalFolder,
    folders,
  } = series;
  const options = useAddSeriesOptions();
  const { preferredMetadataLanguage } = useMetadataSourceSettingsValues();
  const resolvedLanguage = useLanguageById(preferredMetadataLanguage);
  const [language, setLanguage] = useState<Language>(
    resolvedLanguage ?? DEFAULT_LANGUAGE
  );

  useEffect(() => {
    if (resolvedLanguage) {
      setLanguage(resolvedLanguage);
    }
  }, [resolvedLanguage]);

  const handleLanguageChange = useCallback(
    ({ value }: LanguageSelectInputOnChangeProps) => {
      setLanguage(value as Language);
    },
    []
  );

  const isSmallScreen = useAppDimension('isSmallScreen');
  const isWindows = useIsWindows();

  const { isAdding, addError, addSeries } = useAddSeries();

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return {
      ...selectSettings(options, {}),
      ...getValidationFailures(addError),
    };
  }, [options, addError]);

  const [seriesType, setSeriesType] = useState<SeriesType>(
    !isMovie && initialSeriesType === 'standard'
      ? settings.seriesType.value
      : initialSeriesType
  );
  const [seasonType, setSeasonType] = useState(
    () =>
      seasonTypes.find((s) => s.type === 'official')?.type ??
      seasonTypes[0]?.type ??
      'official'
  );

  // An empty string is the default ordering. The select cannot carry null, so it is
  // translated back on submit.
  const [orderingId, setOrderingId] = useState('');
  const [grouping, setGrouping] = useState<AnimeGroupingFields>({
    animeGroupingMode: series.animeGroupingMode ?? 'automatic',
    animeGroupId: series.animeGroupId,
    animeGroupTitle: series.animeGroupTitle,
    animeGroupSeasonNumber: series.animeGroupSeasonNumber,
    animeGroupMemberKind: series.animeGroupMemberKind,
    animeGroupMovieNumber: series.animeGroupMovieNumber,
    animeGroupSpecialNumber: series.animeGroupSpecialNumber,
    animeGroupKind: series.animeGroupKind,
    animeGroupMovieFolderStyle: series.animeGroupMovieFolderStyle,
  });
  const canGroup = canConfigureAnimeGrouping({ ...series, seriesType });
  const { data: groupingPreview, isFetching: isGroupingFetching } =
    useAnimeGroupingPreview(
      series.metadataSource,
      series.foreignId,
      canGroup && grouping.animeGroupingMode === 'automatic'
    );
  let resolvedGrouping: AnimeGroupingFields | undefined =
    grouping.animeGroupingMode === 'automatic' ? groupingPreview : undefined;

  if (grouping.animeGroupingMode === 'manual') {
    resolvedGrouping = grouping;
  }

  const isGrouped = canGroup && hasAnimeGroup(resolvedGrouping ?? {});
  const generatesGroupedPath = isGrouped && !series.path?.trim();

  const { data: orderings } = useEpisodeOrderings(
    series.tmdbId,
    series.imdbId,
    !isMovie
  );

  const orderingOptions = useMemo(() => {
    return orderings.map((ordering) => ({
      key: ordering.id ?? '',
      value: ordering.isDefault
        ? ordering.name
        : `${ordering.name} (${ordering.episodeCount} ${translate(
            'Episodes'
          )})`,
    }));
  }, [orderings]);

  const handleOrderingChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setOrderingId(value);
    },
    []
  );

  const {
    monitor,
    qualityProfileId,
    rootFolderPath,
    searchForCutoffUnmetEpisodes,
    searchForMissingEpisodes,
    seasonFolder,
    seriesType: seriesTypeSetting,
    tags,
  } = settings;

  const translation = translations.find((t) => t.language.id === language.id);
  const title = translation?.title ?? originalTitle ?? lookupTitle;
  const overview = translation?.overview ?? lookupOverview;
  const folder =
    folders.find((f) => f.language.id === language.id)?.folder ??
    originalFolder;
  const folderPreview = generatesGroupedPath
    ? resolvedGrouping?.animeGroupFolder &&
      resolvedGrouping?.animeGroupMemberFolder &&
      `${resolvedGrouping.animeGroupFolder}${isWindows ? '\\' : '/'}${
        resolvedGrouping.animeGroupMemberFolder
      }`
    : folder;

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged<string | number | boolean | number[]>) => {
      if (name === 'seriesType') {
        setSeriesType(value as SeriesType);
      }

      setAddSeriesOption(name as keyof AddSeriesOptions, value);
    },
    []
  );

  const handleSeasonTypeChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setSeasonType(value);
    },
    []
  );

  const handleQualityProfileIdChange = useCallback(
    ({ value }: InputChanged<string | number>) => {
      setAddSeriesOption('qualityProfileId', value as number);
    },
    []
  );

  const handleAddSeriesPress = useCallback(() => {
    addSeries({
      ...series,
      ...(canGroup ? grouping : {}),
      rootFolderPath: rootFolderPath.value,
      addOptions: {
        monitor: isMovie && monitor.value !== 'none' ? 'all' : monitor.value,
        searchForMissingEpisodes: searchForMissingEpisodes.value,
        searchForCutoffUnmetEpisodes: searchForCutoffUnmetEpisodes.value,
      },
      qualityProfileId: qualityProfileId.value,
      seriesType,
      seasonType,
      language,
      seasonFolder:
        isMovie || generatesGroupedPath ? false : seasonFolder.value,
      tags: tags.value,
      orderingId: orderingId || null,
    });
  }, [
    series,
    canGroup,
    grouping,
    generatesGroupedPath,
    isMovie,
    seriesType,
    seasonType,
    orderingId,
    rootFolderPath,
    language,
    monitor,
    qualityProfileId,
    seasonFolder,
    searchForMissingEpisodes,
    searchForCutoffUnmetEpisodes,
    tags,
    addSeries,
  ]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {title}

        {!title.includes(String(year)) && year ? (
          <span className={styles.year}>({year})</span>
        ) : null}
      </ModalHeader>

      <ModalBody>
        <div className={styles.container}>
          {isSmallScreen ? null : (
            <SeriesPoster
              className={styles.poster}
              images={images}
              size={250}
              lazy={false}
              title={title}
            />
          )}

          <div className={styles.info}>
            {overview ? <p className={styles.intro}>{overview}</p> : null}

            <Form
              validationErrors={validationErrors}
              validationWarnings={validationWarnings}
            >
              <FormRow>
                <FormLabel>{translate('RootFolder')}</FormLabel>
                <FormInputHelpText
                  text={
                    folderPreview
                      ? translate('AddNewSeriesRootFolderHelpText', {
                          folder: folderPreview,
                        })
                      : undefined
                  }
                />
                <FormInput
                  type={inputTypes.ROOT_FOLDER_SELECT}
                  name="rootFolderPath"
                  valueOptions={{
                    seriesFolder: folderPreview,
                    isWindows,
                  }}
                  selectedValueOptions={{
                    seriesFolder: folderPreview,
                    isWindows,
                  }}
                  onChange={handleInputChange}
                  {...rootFolderPath}
                />
              </FormRow>

              <FormRow>
                <FormLabel>{translate('Tags')}</FormLabel>

                <FormInput
                  type={inputTypes.TAG}
                  name="tags"
                  onChange={handleInputChange}
                  {...tags}
                />
              </FormRow>

              <FormRow>
                <FormLabel>
                  {translate('Monitor')}

                  <Popover
                    anchor={
                      <Icon className={styles.labelIcon} name={icons.INFO} />
                    }
                    title={translate('MonitoringOptions')}
                    body={
                      isMovie ? (
                        translate('MovieMonitoringHelp')
                      ) : (
                        <SeriesMonitoringOptionsPopoverContent />
                      )
                    }
                    position={tooltipPositions.RIGHT}
                  />
                </FormLabel>

                {isMovie ? (
                  <FormInput
                    type={inputTypes.SELECT}
                    name="monitor"
                    values={[
                      { key: 'all', value: translate('Monitored') },
                      { key: 'none', value: translate('Unmonitored') },
                    ]}
                    onChange={handleInputChange}
                    {...monitor}
                    value={monitor.value === 'none' ? 'none' : 'all'}
                  />
                ) : (
                  <FormInput
                    type={inputTypes.MONITOR_EPISODES_SELECT}
                    name="monitor"
                    onChange={handleInputChange}
                    {...monitor}
                  />
                )}
              </FormRow>

              {orderingOptions.length > 1 ? (
                <FormRow>
                  <FormLabel>{translate('EpisodeOrdering')}</FormLabel>
                  <FormInputHelpText
                    text={translate('EpisodeOrderingHelpText')}
                  />
                  <FormInput
                    type={inputTypes.SELECT}
                    name="orderingId"
                    values={orderingOptions}
                    value={orderingId}
                    onChange={handleOrderingChange}
                  />
                </FormRow>
              ) : null}

              {isMovie ? null : (
                <FormRow>
                  <FormLabel>{translate('SeasonFolder')}</FormLabel>

                  <FormInput
                    type={inputTypes.CHECK}
                    name="seasonFolder"
                    onChange={handleInputChange}
                    {...seasonFolder}
                    value={generatesGroupedPath ? false : seasonFolder.value}
                    isDisabled={generatesGroupedPath}
                  />
                </FormRow>
              )}

              <FormRow>
                <FormLabel>{translate('QualityProfile')}</FormLabel>

                <FormInput
                  type={inputTypes.QUALITY_PROFILE_SELECT}
                  name="qualityProfileId"
                  onChange={handleQualityProfileIdChange}
                  {...qualityProfileId}
                />
              </FormRow>

              {isMovie ? null : (
                <FormRow>
                  <FormLabel>
                    {translate('SeriesType')}

                    <Popover
                      anchor={
                        <Icon className={styles.labelIcon} name={icons.INFO} />
                      }
                      title={translate('SeriesTypes')}
                      body={<SeriesTypePopoverContent />}
                      position={tooltipPositions.RIGHT}
                    />
                  </FormLabel>
                  <FormInputHelpText text={translate('SeriesTypesHelpText')} />
                  <FormInput
                    type={inputTypes.SERIES_TYPE_SELECT}
                    name="seriesType"
                    onChange={handleInputChange}
                    {...seriesTypeSetting}
                    value={seriesType}
                  />
                </FormRow>
              )}

              {canGroup ? (
                <AnimeGroupingOptions
                  series={series}
                  value={grouping}
                  preview={groupingPreview}
                  isFetching={isGroupingFetching}
                  onChange={setGrouping}
                />
              ) : null}

              <FormRow>
                <FormLabel>{translate('Language')}</FormLabel>

                <FormInputHelpText text={translate('SeriesLanguageHelpText')} />
                <FormInput
                  type={inputTypes.LANGUAGE_SELECT}
                  name="language"
                  value={language}
                  includeAny={false}
                  onChange={handleLanguageChange}
                />
              </FormRow>

              {isMovie ? null : (
                <FormRow>
                  <FormLabel>{translate('SeasonType')}</FormLabel>

                  <FormInputHelpText text={translate('SeasonTypeHelpText')} />
                  <FormInput
                    type={inputTypes.SEASON_TYPE_SELECT}
                    name="seasonType"
                    value={seasonType}
                    seasonTypes={seasonTypes}
                    includeNoChange={false}
                    includeMixed={false}
                    onChange={handleSeasonTypeChange}
                  />
                </FormRow>
              )}
            </Form>
          </div>
        </div>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.searchOptions}>
          <span className={styles.searchLabel}>{translate('Search')}</span>

          <CheckInput
            containerClassName={styles.searchOption}
            name="searchForMissingEpisodes"
            helpText={translate('Missing')}
            onChange={handleInputChange}
            {...searchForMissingEpisodes}
          />

          <CheckInput
            containerClassName={styles.searchOption}
            name="searchForCutoffUnmetEpisodes"
            helpText={translate('CutoffUnmet')}
            onChange={handleInputChange}
            {...searchForCutoffUnmetEpisodes}
          />
        </div>

        <div className={styles.actions}>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerButton
            className={styles.addButton}
            kind={kinds.PRIMARY}
            isSpinning={isAdding}
            onPress={handleAddSeriesPress}
          >
            {translate('AddSeriesWithTitle', { title })}
          </SpinnerButton>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddNewSeriesModalContent;
