import React from 'react';
import Label from 'Components/Label';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface EpisodeWatchedStatusProps {
  watched?: boolean;
  watchedAtUtc?: string;
}

function EpisodeWatchedStatus({
  watched,
  watchedAtUtc,
}: EpisodeWatchedStatusProps) {
  if (!watched) {
    return null;
  }

  const date = watchedAtUtc ? new Date(watchedAtUtc) : null;
  const title =
    date && Number.isFinite(date.getTime())
      ? translate('EpisodeWatchedAt', { date: date.toLocaleString() })
      : translate('EpisodeWatchedSource');

  return (
    <Label kind={kinds.SUCCESS} dot={false} icon={icons.VIEW} title={title}>
      {translate('Watched')}
    </Label>
  );
}

export default EpisodeWatchedStatus;
