import React, { useCallback, useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import Series, { Season } from 'Series/Series';
import { useToggleSeasonMonitored } from 'Series/useSeries';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

interface CollectionSeasonSettingsModalProps {
  isOpen: boolean;
  member: Series;
  season: Season;
  title: string;
  onModalClose(): void;
  onEditEntry(): void;
}

function CollectionSeasonSettingsModal({
  isOpen,
  member,
  season,
  title,
  onModalClose,
  onEditEntry,
}: CollectionSeasonSettingsModalProps) {
  const [monitored, setMonitored] = useState(season.monitored);
  const {
    toggleSeasonMonitored,
    isTogglingSeasonMonitored,
    toggleSeasonMonitoredError,
  } = useToggleSeasonMonitored(member.id);
  useEffect(() => {
    if (isOpen) {
      setMonitored(season.monitored);
    }
  }, [isOpen, season.monitored]);
  const changeMonitored = useCallback(
    ({ value }: CheckInputChanged) => setMonitored(value),
    []
  );
  const close = useCallback(() => {
    if (!isTogglingSeasonMonitored) {
      onModalClose();
    }
  }, [isTogglingSeasonMonitored, onModalClose]);
  const save = useCallback(
    () =>
      toggleSeasonMonitored(
        { seasonNumber: season.seasonNumber, monitored },
        { onSuccess: onModalClose }
      ),
    [toggleSeasonMonitored, season.seasonNumber, monitored, onModalClose]
  );

  return (
    <Modal isOpen={isOpen} onModalClose={close}>
      <ModalContent onModalClose={close}>
        <ModalHeader>
          {title} · {translate('Settings')}
        </ModalHeader>
        <ModalBody>
          <CheckInput
            name="monitored"
            value={monitored}
            ariaLabel={translate('Monitored')}
            helpText={translate('Monitored')}
            isDisabled={isTogglingSeasonMonitored}
            onChange={changeMonitored}
          />
          <Button isDisabled={isTogglingSeasonMonitored} onPress={onEditEntry}>
            {translate('EditSeries')}
          </Button>
          {toggleSeasonMonitoredError ? (
            <Alert kind={kinds.DANGER}>
              {translate('SeasonSettingsSaveError')}
            </Alert>
          ) : null}
        </ModalBody>
        <ModalFooter>
          <Button isDisabled={isTogglingSeasonMonitored} onPress={onModalClose}>
            {translate('Cancel')}
          </Button>
          <SpinnerErrorButton
            isSpinning={isTogglingSeasonMonitored}
            error={toggleSeasonMonitoredError}
            isDisabled={monitored === season.monitored}
            onPress={save}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default CollectionSeasonSettingsModal;
