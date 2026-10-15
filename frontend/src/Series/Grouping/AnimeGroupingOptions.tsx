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
  getAnimeGroupMemberNumber,
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

function memberNumbers(kind: AnimeGroupMemberKind, number: number | null) {
  return {
    animeGroupSeasonNumber: kind === 'season' ? number : null,
    animeGroupMovieNumber: kind === 'movie' ? number : null,
    animeGroupSpecialNumber: kind === 'special' ? number : null,
  };
}

const MEMBER_FIELDS = {
  season: {
    number: 'animeGroupSeasonNumber',
    label: 'AnimeGroupSeason',
    resolved: 'AnimeGroupSeasonLabel',
    folder: 'AnimeGroupSeasonFolderStyle',
    numbered: 'AnimeGroupFolderNumberedSeason',
    title: 'AnimeGroupFolderArcTitle',
  },
  movie: {
    number: 'animeGroupMovieNumber',
    label: 'AnimeGroupMovieNumber',
    resolved: 'AnimeGroupMovieLabel',
    folder: 'AnimeGroupMovieFolderStyle',
    numbered: 'AnimeGroupFolderNumbered',
    title: 'AnimeGroupFolderTitle',
  },
  special: {
    number: 'animeGroupSpecialNumber',
    label: 'AnimeGroupSpecialNumber',
    resolved: 'AnimeGroupSpecialLabel',
    folder: 'AnimeGroupSpecialFolderStyle',
    numbered: 'AnimeGroupFolderNumberedSpecial',
    title: 'AnimeGroupFolderSpecialTitle',
  },
} as const;

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
  const fields = MEMBER_FIELDS[memberKind];
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
              ...memberNumbers(
                memberKind,
                getAnimeGroupMemberNumber(value) ||
                  getAnimeGroupMemberNumber(preview ?? {}) ||
                  1
              ),
              animeGroupMemberKind: memberKind,
              animeGroupKind: groupKind,
              animeGroupMovieFolderStyle:
                value.animeGroupMovieFolderStyle ?? 'automatic',
            }
          : {
              animeGroupingMode,
              animeGroupId: null,
              animeGroupTitle: null,
              ...memberNumbers(memberKind, null),
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
        series.id,
        memberKind === 'season'
          ? Math.max(
              1,
              series.seasons?.filter((season) => season.seasonNumber > 0)
                .length ?? 0
            )
          : 1
      );

      onChange({
        animeGroupingMode: 'manual',
        animeGroupId: id,
        animeGroupTitle: group?.title ?? series.title,
        animeGroupMemberKind: memberKind,
        ...memberNumbers(memberKind, number),
        animeGroupKind:
          memberKind === 'season'
            ? 'series'
            : group?.members[0].animeGroupKind ?? groupKind,
        animeGroupMovieFolderStyle:
          group?.members[0].animeGroupMovieFolderStyle ?? 'automatic',
      });
    },
    [
      groupKind,
      groups,
      memberKind,
      onChange,
      series.id,
      series.title,
      series.seasons,
    ]
  );

  const handleTitleChange = useCallback(
    ({ value: title }: InputChanged<string>) => {
      onChange({ ...value, animeGroupTitle: title });
    },
    [onChange, value]
  );
  const handleSeasonChange = useCallback(
    ({ value: season }: NumberInputChanged) => {
      onChange({ ...value, ...memberNumbers(memberKind, season) });
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
        series.id,
        kind === 'season'
          ? Math.max(
              1,
              series.seasons?.filter((season) => season.seasonNumber > 0)
                .length ?? 0
            )
          : 1
      );
      onChange({
        ...value,
        animeGroupMemberKind: kind,
        ...memberNumbers(kind, number),
        animeGroupKind: kind === 'season' ? 'series' : groupKind,
      });
    },
    [groupKind, groups, onChange, series.id, series.seasons, value]
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
    const number = getAnimeGroupMemberNumber(automatic ?? {}) ?? 1;
    const member = translate(
      MEMBER_FIELDS[automatic?.animeGroupMemberKind ?? 'season'].resolved,
      { number, season: number }
    );
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
                { key: 'special', value: translate('AnimeGroupSpecialMember') },
              ]}
              onChange={handleMemberKindChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>{translate(fields.label)}</FormLabel>
            <FormInputHelpText
              text={translate('AnimeGroupManualMemberRequired')}
            />
            <NumberInput
              name={fields.number}
              value={getAnimeGroupMemberNumber(value)}
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
            <FormLabel>{translate(fields.folder)}</FormLabel>
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
                  value: translate(fields.numbered),
                },
                {
                  key: 'title',
                  value: translate(fields.title),
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
