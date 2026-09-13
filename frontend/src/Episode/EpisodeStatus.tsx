import React from 'react';
import {
  useImportProgressForEpisode,
  useQueueItemForEpisode,
} from 'Activity/Queue/Details/QueueDetailsProvider';
import QueueDetails from 'Activity/Queue/QueueDetails';
import Icon from 'Components/Icon';
import ProgressBar from 'Components/ProgressBar';
import StatusIndicator from 'Components/StatusIndicator';
import useEpisode, { EpisodeEntity } from 'Episode/useEpisode';
import { useEpisodeFile } from 'EpisodeFile/EpisodeFileProvider';
import { icons, kinds, sizes } from 'Helpers/Props';
import isBefore from 'Utilities/Date/isBefore';
import translate from 'Utilities/String/translate';
import EpisodeQuality from './EpisodeQuality';
import styles from './EpisodeStatus.css';

function getImportProgressState(
  state: 'waiting' | 'importing' | 'rejected' | 'failed'
) {
  switch (state) {
    case 'importing':
      return {
        label: translate('Importing'),
        icon: icons.DOWNLOADING,
        kind: kinds.PRIMARY,
        spinning: true,
      } as const;
    case 'waiting':
      return {
        label: translate('WaitingToImport'),
        icon: icons.PENDING,
        kind: kinds.DEFAULT,
        spinning: false,
      } as const;
    case 'rejected':
      return {
        label: translate('Rejected'),
        icon: icons.DANGER,
        kind: kinds.WARNING,
        spinning: false,
      } as const;
    default:
      return {
        label: translate('Failed'),
        icon: icons.DANGER,
        kind: kinds.DANGER,
        spinning: false,
      } as const;
  }
}

interface EpisodeStatusProps {
  episodeId: number;
  episodeEntity?: EpisodeEntity;
  episodeFileId: number | undefined;
}

function EpisodeStatus({
  episodeId,
  episodeEntity = 'episodes',
  episodeFileId,
}: EpisodeStatusProps) {
  const episode = useEpisode(episodeId, episodeEntity);
  const queueItem = useQueueItemForEpisode(episodeId);
  const importProgressItem = useImportProgressForEpisode(episodeId);
  const episodeFile = useEpisodeFile(episodeFileId);

  const { airDateUtc, grabbed, monitored } = episode || {};
  const hasEpisodeFile = !!episodeFile;
  const isQueued = !!queueItem && importProgressItem?.state !== 'imported';
  const hasAired = isBefore(airDateUtc);

  if (!episode) {
    return null;
  }

  if (importProgressItem && importProgressItem.state !== 'imported') {
    const state = importProgressItem.state;
    const { label, icon, kind, spinning } = getImportProgressState(state);

    return (
      <StatusIndicator
        className={styles.center}
        label={label}
        title={importProgressItem.errors.join('; ') || label}
      >
        <Icon name={icon} kind={kind} isSpinning={spinning} />
      </StatusIndicator>
    );
  }

  if (importProgressItem?.state === 'imported' && !hasEpisodeFile) {
    const label = translate('Imported');

    return (
      <StatusIndicator className={styles.center} label={label} title={label}>
        <Icon name={icons.CHECK} kind={kinds.SUCCESS} />
      </StatusIndicator>
    );
  }

  if (isQueued) {
    const { sizeLeft, size } = queueItem;

    const progress = size ? 100 - (sizeLeft / size) * 100 : 0;

    return (
      <StatusIndicator
        className={styles.center}
        label={translate('EpisodeIsDownloading')}
      >
        <QueueDetails
          {...queueItem}
          progressBar={
            <ProgressBar
              progress={progress}
              kind={kinds.PURPLE}
              size={sizes.MEDIUM}
            />
          }
        />
      </StatusIndicator>
    );
  }

  if (grabbed) {
    const label = translate('EpisodeIsDownloading');

    return (
      <StatusIndicator className={styles.center} label={label} title={label}>
        <Icon name={icons.DOWNLOADING} />
      </StatusIndicator>
    );
  }

  if (hasEpisodeFile) {
    const quality = episodeFile.quality;
    const isCutoffNotMet = episodeFile.qualityCutoffNotMet;
    const label = translate('EpisodeDownloaded');

    return (
      <StatusIndicator className={styles.center} label={label}>
        <EpisodeQuality
          quality={quality}
          size={episodeFile.size}
          isCutoffNotMet={isCutoffNotMet}
          title={label}
        />
      </StatusIndicator>
    );
  }

  if (!airDateUtc) {
    const label = translate('Tba');

    return (
      <StatusIndicator className={styles.center} label={label} title={label}>
        <Icon name={icons.TBA} />
      </StatusIndicator>
    );
  }

  if (!monitored) {
    const label = translate('EpisodeIsNotMonitored');

    return (
      <StatusIndicator className={styles.center} label={label} title={label}>
        <Icon name={icons.UNMONITORED} kind={kinds.DISABLED} />
      </StatusIndicator>
    );
  }

  if (hasAired) {
    const label = translate('EpisodeMissingFromDisk');

    return (
      <StatusIndicator className={styles.center} label={label} title={label}>
        <Icon name={icons.MISSING} />
      </StatusIndicator>
    );
  }

  const label = translate('EpisodeHasNotAired');

  return (
    <StatusIndicator className={styles.center} label={label} title={label}>
      <Icon name={icons.NOT_AIRED} />
    </StatusIndicator>
  );
}

export default EpisodeStatus;
