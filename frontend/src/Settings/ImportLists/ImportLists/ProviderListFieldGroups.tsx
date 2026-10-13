import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import Alert from 'Components/Alert';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import { TextTagSuggestion } from 'Components/Form/Tag/TextTagInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalSection from 'Components/ModalSection';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { ImportListModel } from './useImportLists';

interface ProviderListFieldGroupsProps {
  providerData: PendingSection<ImportListModel>;
  advancedSettings: boolean;
  onChange(change: EnhancedSelectInputChanged<unknown>): void;
}

interface ListChoices {
  options: { value: string; name: string }[];
}

const SOURCE_FIELDS = new Set([
  'includedListIds',
  'excludedListIds',
  'includedLists',
  'excludedLists',
  'includedListUrls',
  'excludedListUrls',
  'includedTags',
  'excludedTags',
  'includedCustomListIds',
  'excludedCustomListIds',
]);
const FILTER_FIELDS = new Set([
  'listStatuses',
  'showType',
  'listType',
  'importFinished',
  'importAiring',
  'importUnreleased',
  'minimumScore',
  'years',
  'genres',
  'ratings',
  'rating',
  'limit',
]);

function ProviderListFieldGroups({
  providerData,
  advancedSettings,
  onChange,
}: ProviderListFieldGroupsProps) {
  const fields = useMemo(
    () => providerData.fields ?? [],
    [providerData.fields]
  );
  const implementation = providerData.implementation?.value;
  const action = {
    TmdbListImport: 'getAccountLists',
    TraktListImport: 'getLists',
    SimklUserImport: 'getCustomLists',
  }[implementation ?? ''];
  const accountFingerprint = useMemo(
    () =>
      JSON.stringify(
        fields
          .filter(
            (field) =>
              (!SOURCE_FIELDS.has(field.name) &&
                !FILTER_FIELDS.has(field.name)) ||
              (implementation === 'SimklUserImport' &&
                field.name === 'showType')
          )
          .map((field) => [field.name, field.value])
      ),
    [fields, implementation]
  );
  const [suggestions, setSuggestions] = useState<
    TextTagSuggestion[] | undefined
  >();
  const [loadFailed, setLoadFailed] = useState(false);
  const inFlight = useRef(false);
  const revision = useRef(0);
  const { mutateAsync, isPending } = useApiMutation<
    ListChoices,
    Record<string, unknown>
  >({
    path: `/importlist/action/${action ?? 'getLists'}`,
    method: 'POST',
  });

  useEffect(() => {
    revision.current += 1;
    setSuggestions(undefined);
    setLoadFailed(false);
  }, [accountFingerprint, implementation]);

  const onLoadLists = useCallback(async () => {
    if (!action || inFlight.current) return;
    inFlight.current = true;
    setLoadFailed(false);
    const requestedRevision = revision.current;
    const requestBody: Record<string, unknown> = Object.fromEntries(
      Object.entries(providerData).map(([name, property]) => [
        name,
        property && typeof property === 'object' && 'value' in property
          ? property.value
          : property,
      ])
    );
    requestBody.fields = fields;

    try {
      const result = await mutateAsync(requestBody);
      if (requestedRevision !== revision.current) return;

      if (
        !Array.isArray(result.options) ||
        result.options.some(
          (option) =>
            typeof option.value !== 'string' ||
            !option.value.trim() ||
            typeof option.name !== 'string'
        )
      ) {
        throw new Error('Invalid remote list choices');
      }

      const choices = new Map(
        result.options.map((option) => [
          option.value,
          { value: option.value, name: `${option.name} (${option.value})` },
        ])
      );
      setSuggestions([...choices.values()]);
    } catch {
      if (requestedRevision === revision.current) setLoadFailed(true);
    } finally {
      inFlight.current = false;
    }
  }, [action, fields, mutateAsync, providerData]);

  const renderFields = (names: 'account' | 'filters' | 'sources') =>
    fields
      .filter((field) => {
        if (names === 'sources') return SOURCE_FIELDS.has(field.name);
        if (names === 'filters') return FILTER_FIELDS.has(field.name);
        return !SOURCE_FIELDS.has(field.name) && !FILTER_FIELDS.has(field.name);
      })
      .map((field) => (
        <ProviderFieldFormGroup
          key={field.name}
          advancedSettings={advancedSettings}
          provider="importList"
          providerData={providerData}
          {...field}
          tagSuggestions={
            SOURCE_FIELDS.has(field.name) ? suggestions : undefined
          }
          onChange={onChange}
        />
      ));

  return (
    <>
      <ModalSection title={translate('ImportListsAccount')}>
        {renderFields('account')}
      </ModalSection>
      <ModalSection title={translate('ImportListsSources')}>
        <Alert kind={kinds.INFO}>
          {translate('ImportListsSetDifferenceHelp')}
        </Alert>
        {action ? (
          <>
            <SpinnerButton isSpinning={isPending} onPress={onLoadLists}>
              {translate('ImportListsLoadLists')}
            </SpinnerButton>
            <FormInputHelpText text={translate('ImportListsLoadListsHelp')} />
            {loadFailed ? (
              <Alert kind={kinds.WARNING}>
                {translate('ImportListsLoadListsError')}
              </Alert>
            ) : null}
          </>
        ) : null}
        {renderFields('sources')}
      </ModalSection>
      <ModalSection title={translate('ImportListsFilters')}>
        {renderFields('filters')}
      </ModalSection>
      <FormInputHelpText text={translate('ImportListsTestRetentionHelp')} />
    </>
  );
}

export default ProviderListFieldGroups;
