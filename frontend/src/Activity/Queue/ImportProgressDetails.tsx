import React, { useMemo } from 'react';
import Icon from 'Components/Icon';
import Tooltip from 'Components/Tooltip/Tooltip';
import Episode from 'Episode/Episode';
import { icons, kinds } from 'Helpers/Props';
import { ImportProgress, ImportProgressItem } from 'typings/Queue';
import translate from 'Utilities/String/translate';
import styles from './ImportProgressDetails.css';

interface ImportProgressDetailsProps {
  progress: ImportProgress;
  episodes: Episode[];
}

function formatEpisodeLabel(item: ImportProgressItem, episodes: Episode[]) {
  const mappedEpisodes = item.episodeIds
    .map((id) => episodes.find((episode) => episode.id === id))
    .filter((episode): episode is Episode => !!episode)
    .sort((left, right) => {
      return (
        left.seasonNumber - right.seasonNumber ||
        left.episodeNumber - right.episodeNumber
      );
    });

  if (!mappedEpisodes.length) {
    return item.episodeIds.join(', ');
  }

  const grouped = mappedEpisodes.reduce<Record<number, number[]>>(
    (result, episode) => {
      result[episode.seasonNumber] = result[episode.seasonNumber] || [];
      result[episode.seasonNumber].push(episode.episodeNumber);
      return result;
    },
    {}
  );

  return Object.entries(grouped)
    .map(([season, numbers]) => {
      const seasonLabel = season.padStart(2, '0');
      const episodeLabels = numbers
        .sort((left, right) => left - right)
        .map((number) => number.toString().padStart(2, '0'));

      if (episodeLabels.length === 1) {
        return `S${seasonLabel}E${episodeLabels[0]}`;
      }

      return `S${seasonLabel}E${episodeLabels[0]}-${
        episodeLabels[episodeLabels.length - 1]
      }`;
    })
    .join(', ');
}

function getState(item: ImportProgressItem) {
  switch (item.state) {
    case 'imported':
      return {
        icon: icons.CHECK,
        kind: kinds.SUCCESS,
        label: translate('Imported'),
        spinning: false,
      } as const;
    case 'importing':
      return {
        icon: icons.DOWNLOADING,
        kind: kinds.PRIMARY,
        label: translate('Importing'),
        spinning: true,
      } as const;
    case 'rejected':
      return {
        icon: icons.WARNING,
        kind: kinds.WARNING,
        label: translate('Rejected'),
        spinning: false,
      } as const;
    case 'failed':
      return {
        icon: icons.DANGER,
        kind: kinds.DANGER,
        label: translate('Failed'),
        spinning: false,
      } as const;
    default:
      return {
        icon: icons.PENDING,
        kind: kinds.DEFAULT,
        label: translate('WaitingToImport'),
        spinning: false,
      } as const;
  }
}

function ImportProgressDetails({
  progress,
  episodes,
}: ImportProgressDetailsProps) {
  const items = useMemo(() => progress.items, [progress.items]);

  return (
    <div className={styles.details}>
      {items.map((item) => {
        const state = getState(item);
        const sourceName = item.sourcePath?.split(/[\\/]/).pop();
        const reason = item.errors.length ? item.errors.join('; ') : undefined;

        return (
          <div key={item.id} className={styles.item}>
            <span className={styles.icon}>
              <Tooltip
                anchor={
                  <Icon
                    name={state.icon}
                    kind={state.kind}
                    isSpinning={state.spinning}
                  />
                }
                tooltip={reason || state.label}
                position="right"
              />
            </span>
            <span className={styles.episodes}>
              {formatEpisodeLabel(item, episodes)}
            </span>
            <span className={styles.state}>{state.label}</span>
            {sourceName ? (
              <span className={styles.source}>{sourceName}</span>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

export default ImportProgressDetails;
