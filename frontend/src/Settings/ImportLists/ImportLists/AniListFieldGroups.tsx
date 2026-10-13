import React, { useCallback, useEffect, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalSection from 'Components/ModalSection';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { ImportListModel } from './useImportLists';

interface AniListFieldGroupsProps {
  providerData: PendingSection<ImportListModel>;
  advancedSettings: boolean;
  onChange(change: EnhancedSelectInputChanged<unknown>): void;
}

interface CustomListOptions {
  options: { value: string; name: string }[];
}

const FIELD_GROUPS = [
  {
    title: 'AniListIncludeStatuses',
    names: [
      'importCurrent',
      'importPlanning',
      'importCompleted',
      'importDropped',
      'importPaused',
      'importRepeating',
    ],
  },
  {
    title: 'AniListReleaseStatuses',
    names: [
      'importFinished',
      'importReleasing',
      'importUnreleased',
      'importCancelled',
      'importHiatus',
    ],
  },
  {
    title: 'AniListCustomLists',
    names: ['includedCustomLists', 'excludedCustomLists', 'customList'],
  },
  {
    title: 'AniListExcludeStatuses',
    names: [
      'excludeCurrent',
      'excludePlanning',
      'excludeCompleted',
      'excludeDropped',
      'excludePaused',
      'excludeRepeating',
    ],
  },
];
const GROUPED_FIELDS = new Set(FIELD_GROUPS.flatMap((group) => group.names));

function AniListFieldGroups({
  providerData,
  advancedSettings,
  onChange,
}: AniListFieldGroupsProps) {
  const fields = providerData.fields ?? [];
  const username = fields.find((field) => field.name === 'username')?.value;
  const accessToken = fields.find(
    (field) => field.name === 'accessToken'
  )?.value;
  const [suggestions, setSuggestions] = useState<string[] | undefined>();
  const [loadFailed, setLoadFailed] = useState(false);
  const inFlight = useRef(false);
  const accountRevision = useRef(0);
  const { mutateAsync, isPending } = useApiMutation<
    CustomListOptions,
    Record<string, unknown>
  >({
    path: '/importlist/action/getCustomLists',
    method: 'POST',
  });

  useEffect(() => {
    accountRevision.current += 1;
    setSuggestions(undefined);
    setLoadFailed(false);
  }, [username, accessToken]);

  const onLoadCustomLists = useCallback(async () => {
    if (inFlight.current) {
      return;
    }

    inFlight.current = true;
    setLoadFailed(false);
    const revision = accountRevision.current;
    const requestBody = Object.fromEntries(
      Object.entries(providerData).map(([name, property]) => [
        name,
        property && typeof property === 'object' && 'value' in property
          ? property.value
          : property,
      ])
    );

    try {
      const result = await mutateAsync(requestBody);

      if (revision !== accountRevision.current) {
        return;
      }

      if (
        !Array.isArray(result.options) ||
        result.options.some(
          (option) => typeof option.value !== 'string' || !option.value.trim()
        )
      ) {
        throw new Error('Invalid custom list choices');
      }

      setSuggestions([
        ...new Set(result.options.map((option) => option.value)),
      ]);
    } catch {
      if (revision === accountRevision.current) {
        setLoadFailed(true);
      }
    } finally {
      inFlight.current = false;
    }
  }, [mutateAsync, providerData]);

  return (
    <>
      <ModalSection title={translate('AniListAccount')}>
        {fields
          .filter((field) => !GROUPED_FIELDS.has(field.name))
          .map((field) => (
            <ProviderFieldFormGroup
              key={field.name}
              advancedSettings={advancedSettings}
              provider="importList"
              providerData={providerData}
              {...field}
              onChange={onChange}
            />
          ))}
      </ModalSection>
      <Alert kind={kinds.INFO}>
        {translate('AniListExclusionPrecedenceHelp')}
      </Alert>
      {FIELD_GROUPS.map((group) => (
        <ModalSection key={group.title} title={translate(group.title)}>
          {group.title === 'AniListCustomLists' ? (
            <>
              <SpinnerButton isSpinning={isPending} onPress={onLoadCustomLists}>
                {translate('AniListLoadCustomLists')}
              </SpinnerButton>
              <FormInputHelpText
                text={translate('AniListCustomListChoicesHelp')}
              />
              {loadFailed ? (
                <Alert kind={kinds.WARNING}>
                  {translate('AniListCustomListsLoadError')}
                </Alert>
              ) : null}
              {suggestions?.length === 0 ? (
                <FormInputHelpText text={translate('AniListNoCustomLists')} />
              ) : null}
            </>
          ) : null}
          {group.names.map((name) => {
            const field = fields.find((candidate) => candidate.name === name);

            return field ? (
              <ProviderFieldFormGroup
                key={field.name}
                advancedSettings={advancedSettings}
                provider="importList"
                providerData={providerData}
                {...field}
                tagSuggestions={suggestions}
                onChange={onChange}
              />
            ) : null;
          })}
        </ModalSection>
      ))}
      <FormInputHelpText text={translate('AniListTestRetentionHelp')} />
    </>
  );
}

export default AniListFieldGroups;
