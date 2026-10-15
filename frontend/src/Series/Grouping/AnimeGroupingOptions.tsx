import React, { useCallback, useMemo } from 'react';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Series, {
  AnimeGroupingFields,
  AnimeGroupingMode,
  AnimeGroupKind,
  AnimeGroupMemberKind,
  AnimeGroupMovieFolderStyle,
} from 'Series/Series';
import useSeries from 'Series/useSeries';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  createManualGroupId,
  getAvailableGroupMemberNumber,
  groupAnimeSeries,
  hasAnimeGroup,
} from './animeGrouping';
import { AnimeGroupingPreview } from './useAnimeGroupingPreview';

interface AnimeGroupingOptionsProps {
  series: Series;
  value: AnimeGroupingFields;
  preview?: Readonly<AnimeGroupingPreview>;
  isFetching?: boolean;
  isEditing?: boolean;
  onChange: (value: AnimeGroupingFields) => void;
}

function AnimeGroupingOptions({
  series,
  value,
  preview,
  isFetching = false,
  isEditing = false,
  onChange,
}: AnimeGroupingOptionsProps) {
  const { data: existingSeries = [] } = useSeries();
  const newGroupId = useMemo(createManualGroupId, []);
  const mode = value.animeGroupingMode ?? 'automatic';
  const memberKind =
    value.animeGroupMemberKind ??
    preview?.animeGroupMemberKind ??
    series.animeGroupMemberKind ??
    'season';
  const groupKind =
    value.animeGroupKind ??
    preview?.animeGroupKind ??
    (memberKind === 'movie' ? 'movieSaga' : 'series');
  const groups = useMemo(
    () =>
      Array.from(
        groupAnimeSeries(existingSeries, true).groupsByRepresentativeId.values()
      ),
    [existingSeries]
  );
  const groupOptions = useMemo(() => {
    const values = [
      { key: newGroupId, value: translate('AnimeGroupNew') },
      ...groups.map((group) => ({ key: group.id, value: group.title })),
    ];

    if (
      value.animeGroupId &&
      !values.some((item) => item.key === value.animeGroupId)
    ) {
      values.push({
        key: value.animeGroupId,
        value: value.animeGroupTitle ?? value.animeGroupId,
      });
    }

    return values;
  }, [groups, newGroupId, value.animeGroupId, value.animeGroupTitle]);
  const automatic = isEditing && hasAnimeGroup(value) ? value : preview;
  const resolved = mode === 'manual' ? value : automatic;

  const handleModeChange = useCallback(
    ({ value: selected }: InputChanged<string>) => {
      const animeGroupingMode = selected as AnimeGroupingMode;
      onChange(
        animeGroupingMode === 'manual'
          ? {
              animeGroupingMode,
              animeGroupId:
                value.animeGroupId || preview?.animeGroupId || newGroupId,
              animeGroupTitle:
                value.animeGroupTitle ||
                preview?.animeGroupTitle ||
                series.title,
              animeGroupSeasonNumber:
                memberKind === 'season'
                  ? value.animeGroupSeasonNumber ||
                    preview?.animeGroupSeasonNumber ||
                    1
                  : null,
              animeGroupMovieNumber:
                memberKind === 'movie'
                  ? value.animeGroupMovieNumber ||
                    preview?.animeGroupMovieNumber ||
                    1
                  : null,
              animeGroupMemberKind: memberKind,
              animeGroupKind: groupKind,
              animeGroupMovieFolderStyle:
                value.animeGroupMovieFolderStyle ?? 'automatic',
            }
          : {
              animeGroupingMode,
              animeGroupId: null,
              animeGroupTitle: null,
              animeGroupSeasonNumber: null,
              animeGroupMovieNumber: null,
              animeGroupMemberKind: memberKind,
              animeGroupKind: groupKind,
              animeGroupMovieFolderStyle:
                value.animeGroupMovieFolderStyle ?? 'automatic',
            }
      );
    },
    [groupKind, memberKind, newGroupId, onChange, preview, series.title, value]
  );

  const handleGroupChange = useCallback(
    ({ value: id }: InputChanged<string>) => {
      const group = groups.find((item) => item.id === id);
      const number = getAvailableGroupMemberNumber(
        group?.members ?? [],
        memberKind,
        series.id
      );

      onChange({
        animeGroupingMode: 'manual',
        animeGroupId: id,
        animeGroupTitle: group?.title ?? series.title,
        animeGroupMemberKind: memberKind,
        animeGroupSeasonNumber: memberKind === 'season' ? number : null,
        animeGroupMovieNumber: memberKind === 'movie' ? number : null,
        animeGroupKind:
          memberKind === 'season'
            ? 'series'
            : group?.members[0].animeGroupKind ?? groupKind,
        animeGroupMovieFolderStyle:
          group?.members[0].animeGroupMovieFolderStyle ?? 'automatic',
      });
    },
    [groupKind, groups, memberKind, onChange, series.id, series.title]
  );

  const handleTitleChange = useCallback(
    ({ value: title }: InputChanged<string>) => {
      onChange({ ...value, animeGroupTitle: title });
    },
    [onChange, value]
  );
  const handleSeasonChange = useCallback(
    ({ value: season }: NumberInputChanged) => {
      onChange(
        memberKind === 'movie'
          ? {
              ...value,
              animeGroupMovieNumber: season,
              animeGroupSeasonNumber: null,
            }
          : {
              ...value,
              animeGroupSeasonNumber: season,
              animeGroupMovieNumber: null,
            }
      );
    },
    [memberKind, onChange, value]
  );
  const handleMemberKindChange = useCallback(
    ({ value: selected }: InputChanged<string>) => {
      const kind = selected as AnimeGroupMemberKind;
      const group = groups.find((item) => item.id === value.animeGroupId);
      const number = getAvailableGroupMemberNumber(
        group?.members ?? [],
        kind,
        series.id
      );
      onChange({
        ...value,
        animeGroupMemberKind: kind,
        animeGroupSeasonNumber: kind === 'season' ? number : null,
        animeGroupMovieNumber: kind === 'movie' ? number : null,
        animeGroupKind: kind === 'season' ? 'series' : groupKind,
      });
    },
    [groupKind, groups, onChange, series.id, value]
  );
  const handleGroupKindChange = useCallback(
    ({ value: selected }: InputChanged<string>) => {
      onChange({ ...value, animeGroupKind: selected as AnimeGroupKind });
    },
    [onChange, value]
  );
  const handleFolderStyleChange = useCallback(
    ({ value: selected }: InputChanged<string>) => {
      onChange({
        ...value,
        animeGroupMovieFolderStyle: selected as AnimeGroupMovieFolderStyle,
      });
    },
    [onChange, value]
  );

  let automaticHelp = translate(
    series.metadataSource === 'aniList' ||
      series.metadataSource === 'myAnimeList'
      ? 'AnimeGroupingStandalone'
      : 'AnimeGroupingNativeSeasons'
  );

  if (isFetching) {
    automaticHelp = translate('AnimeGroupingLoading');
  } else if (hasAnimeGroup(automatic ?? {})) {
    const member =
      automatic?.animeGroupMemberKind === 'movie'
        ? translate('AnimeGroupMovieLabel', {
            number: automatic?.animeGroupMovieNumber ?? 1,
          })
        : translate('AnimeGroupSeasonLabel', {
            season: automatic?.animeGroupSeasonNumber ?? 1,
          });
    automaticHelp = translate('AnimeGroupingMemberResolved', {
      title: automatic?.animeGroupTitle ?? '',
      member,
    });
  }

  let folderHelp: string | undefined = undefined;

  if (isEditing) {
    folderHelp = translate('AnimeGroupingEditHelp');
  } else if (mode !== 'disabled' && hasAnimeGroup(resolved ?? {})) {
    folderHelp =
      resolved?.animeGroupFolder && resolved?.animeGroupMemberFolder
        ? translate('AnimeGroupingMemberFolderHelp', {
            folder: `${resolved.animeGroupFolder}/${resolved.animeGroupMemberFolder}`,
          })
        : translate('AnimeGroupingGeneratedFolderHelp');
  }

  return (
    <>
      <FormRow>
        <FormLabel>{translate('AnimeGrouping')}</FormLabel>
        <FormInputHelpText text={translate('AnimeGroupingHelp')} />
        <SelectInput
          name="animeGroupingMode"
          value={mode}
          values={[
            { key: 'automatic', value: translate('AnimeGroupingAutomatic') },
            { key: 'manual', value: translate('AnimeGroupingManual') },
            { key: 'disabled', value: translate('AnimeGroupingDisabled') },
          ]}
          onChange={handleModeChange}
        />
      </FormRow>
      {mode === 'automatic' ? <FormInputHelpText text={automaticHelp} /> : null}
      {mode === 'manual' ? (
        <>
          <FormRow>
            <FormLabel>{translate('AnimeGroup')}</FormLabel>
            <SelectInput
              name="animeGroupId"
              value={value.animeGroupId || newGroupId}
              values={groupOptions}
              onChange={handleGroupChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>{translate('AnimeGroupTitle')}</FormLabel>
            <TextInput
              name="animeGroupTitle"
              value={value.animeGroupTitle ?? ''}
              onChange={handleTitleChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>{translate('AnimeGroupMemberKind')}</FormLabel>
            <SelectInput
              name="animeGroupMemberKind"
              value={memberKind}
              values={[
                { key: 'season', value: translate('AnimeGroupSeasonMember') },
                { key: 'movie', value: translate('AnimeGroupMovieMember') },
              ]}
              onChange={handleMemberKindChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>
              {translate(
                memberKind === 'movie'
                  ? 'AnimeGroupMovieNumber'
                  : 'AnimeGroupSeason'
              )}
            </FormLabel>
            <FormInputHelpText
              text={translate('AnimeGroupManualMemberRequired')}
            />
            <NumberInput
              name={
                memberKind === 'movie'
                  ? 'animeGroupMovieNumber'
                  : 'animeGroupSeasonNumber'
              }
              value={
                memberKind === 'movie'
                  ? value.animeGroupMovieNumber
                  : value.animeGroupSeasonNumber
              }
              min={1}
              max={999}
              onChange={handleSeasonChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>{translate('AnimeGroupKind')}</FormLabel>
            <SelectInput
              name="animeGroupKind"
              value={groupKind}
              values={[
                { key: 'series', value: translate('AnimeGroupSeries') },
                {
                  key: 'movieSaga',
                  value: translate('AnimeGroupMovieSaga'),
                  disabled: memberKind === 'season',
                },
              ]}
              onChange={handleGroupKindChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>
              {translate(
                memberKind === 'movie'
                  ? 'AnimeGroupMovieFolderStyle'
                  : 'AnimeGroupSeasonFolderStyle'
              )}
            </FormLabel>
            <SelectInput
              name="animeGroupMovieFolderStyle"
              value={value.animeGroupMovieFolderStyle ?? 'automatic'}
              values={[
                {
                  key: 'automatic',
                  value: translate('AnimeGroupFolderAutomatic'),
                },
                {
                  key: 'numbered',
                  value: translate(
                    memberKind === 'movie'
                      ? 'AnimeGroupFolderNumbered'
                      : 'AnimeGroupFolderNumberedSeason'
                  ),
                },
                {
                  key: 'title',
                  value: translate(
                    memberKind === 'movie'
                      ? 'AnimeGroupFolderTitle'
                      : 'AnimeGroupFolderArcTitle'
                  ),
                },
              ]}
              onChange={handleFolderStyleChange}
            />
          </FormRow>
        </>
      ) : null}
      {folderHelp ? (
        <FormRow>
          <FormInputHelpText text={folderHelp} />
        </FormRow>
      ) : null}
    </>
  );
}

export default AnimeGroupingOptions;
