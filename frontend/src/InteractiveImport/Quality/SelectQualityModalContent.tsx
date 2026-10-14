import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import { QualityModel } from 'Quality/Quality';
import { useQualityProfileSchema } from 'Settings/Profiles/Quality/useQualityProfiles';
import { useQualityDefinitions } from 'Settings/Quality/Definition/useQualityDefinitions';
import { InputChanged } from 'typings/inputs';
import getQualities from 'Utilities/Quality/getQualities';
import translate from 'Utilities/String/translate';

interface SelectQualityModalContentProps {
  qualityId: number;
  proper: boolean;
  real: boolean;
  modalTitle: string;
  onQualitySelect(quality: QualityModel): void;
  onModalClose(): void;
}

function SelectQualityModalContent(props: SelectQualityModalContentProps) {
  const { modalTitle, onQualitySelect, onModalClose } = props;

  const [qualityId, setQualityId] = useState(props.qualityId);
  const [proper, setProper] = useState(props.proper);
  const [real, setReal] = useState(props.real);

  const { schema, isSchemaLoading, isSchemaFetched, schemaError } =
    useQualityProfileSchema(true);

  const schemaItems = useMemo(() => getQualities(schema.items), [schema]);
  const requiresHistoricalDefinition =
    isSchemaFetched &&
    props.qualityId >= 10000 &&
    !schemaItems.some((item) => item.id === props.qualityId);
  const {
    data: definitions,
    isFetched: definitionsFetched,
    error: definitionError,
  } = useQualityDefinitions(requiresHistoricalDefinition);
  const items = useMemo(() => {
    const available = schemaItems.filter(
      (item) => !item.isRetired || item.id === props.qualityId
    );
    const historical = definitions.find(
      (definition) => definition.quality.id === props.qualityId
    );
    if (
      historical &&
      !available.some((item) => item.id === historical.quality.id)
    )
      available.push(historical.quality);
    return available;
  }, [schemaItems, definitions, props.qualityId]);
  const loaded =
    isSchemaFetched && (!requiresHistoricalDefinition || definitionsFetched);
  const loadError =
    schemaError || (requiresHistoricalDefinition ? definitionError : undefined);

  const qualityOptions = useMemo(() => {
    return items.map(({ id, name }): EnhancedSelectInputValue<number> => {
      return {
        key: id,
        value: name,
      };
    });
  }, [items]);

  const onQualityChange = useCallback(
    ({ value }: { value: number }) => {
      setQualityId(value);
    },
    [setQualityId]
  );

  const onProperChange = useCallback(
    ({ value }: InputChanged<boolean>) => {
      setProper(value);
    },
    [setProper]
  );

  const onRealChange = useCallback(
    ({ value }: InputChanged<boolean>) => {
      setReal(value);
    },
    [setReal]
  );

  const onQualitySelectWrapper = useCallback(() => {
    const quality = items.find((item) => item.id === qualityId);
    if (!quality) return;

    const revision = {
      version: proper ? 2 : 1,
      real: real ? 1 : 0,
      isRepack: false,
    };

    onQualitySelect({
      quality,
      revision,
    });
  }, [items, qualityId, proper, real, onQualitySelect]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{modalTitle} - Select Quality</ModalHeader>

      <ModalBody>
        {isSchemaLoading ||
        (requiresHistoricalDefinition && !definitionsFetched) ? (
          <LoadingIndicator />
        ) : null}

        {!isSchemaLoading && loadError ? (
          <Alert kind={kinds.DANGER}>{translate('QualitiesLoadError')}</Alert>
        ) : null}

        {loaded && !loadError ? (
          <Form>
            <FormRow>
              <FormLabel>{translate('Quality')}</FormLabel>
              <FormInput
                type={inputTypes.SELECT}
                name="quality"
                value={qualityId}
                values={qualityOptions}
                onChange={onQualityChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('Proper')}</FormLabel>
              <FormInput
                type={inputTypes.CHECK}
                name="proper"
                value={proper}
                onChange={onProperChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('Real')}</FormLabel>
              <FormInput
                type={inputTypes.CHECK}
                name="real"
                value={real}
                onChange={onRealChange}
              />
            </FormRow>
          </Form>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>Cancel</Button>

        <Button
          kind={kinds.SUCCESS}
          isDisabled={
            !loaded ||
            Boolean(loadError) ||
            !items.some((item) => item.id === qualityId)
          }
          onPress={onQualitySelectWrapper}
        >
          {translate('SelectQuality')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default SelectQualityModalContent;
