import React, { useCallback } from 'react';
import { MetadataSource } from 'AddSeries/AddNewSeries/useAddSeries';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import { kinds } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { metadataSources, orderedMetadataSources } from './metadataSources';
import styles from './Sources.module.css';

interface SourcesProps {
  order?: MetadataSource[];
  disabled?: MetadataSource[];
  hasTmdbKey: boolean;
  hasOmdbKey?: boolean;
  onOrderChange: (order: MetadataSource[]) => void;
  onDisabledChange: (disabled: MetadataSource[]) => void;
}

interface SourceRowProps {
  source: (typeof metadataSources)[number];
  index: number;
  count: number;
  enabled: boolean;
  lastEnabled: boolean;
  hasTmdbKey: boolean;
  hasOmdbKey: boolean;
  onToggle: (change: CheckInputChanged) => void;
  onMove: (index: number, direction: number) => void;
}

function SourceRow({
  source,
  index,
  count,
  enabled,
  lastEnabled,
  hasTmdbKey,
  hasOmdbKey,
  onToggle,
  onMove,
}: SourceRowProps) {
  const handleUp = useCallback(() => onMove(index, -1), [index, onMove]);
  const handleDown = useCallback(() => onMove(index, 1), [index, onMove]);
  let status = translate('Disabled');

  if (enabled) {
    status =
      ((source.key === 'tmdb' || source.key === 'tmdbMovie') && !hasTmdbKey) ||
      (source.key === 'omdb' && !hasOmdbKey)
        ? translate('MetadataSourceNeedsApiKey')
        : translate('MetadataSourceReady');
  }

  return (
    <div className={styles.source}>
      <div className={styles.header}>
        <CheckInput
          containerClassName={styles.toggle}
          name={source.key}
          ariaLabel={`${translate('Enable')} ${source.value}`}
          value={enabled}
          isDisabled={enabled && lastEnabled}
          onChange={onToggle}
        />
        <a
          className={styles.name}
          href={source.url}
          target="_blank"
          rel="noreferrer"
        >
          {source.value}
        </a>
        <span className={styles.status}>{status}</span>
        <div className={styles.actions}>
          <Button
            title={`${translate('MetadataSourceMoveUp')} ${source.value}`}
            isDisabled={index === 0}
            onPress={handleUp}
          >
            ↑
          </Button>
          <Button
            title={`${translate('MetadataSourceMoveDown')} ${source.value}`}
            isDisabled={index === count - 1}
            onPress={handleDown}
          >
            ↓
          </Button>
        </div>
      </div>
      <div className={styles.description}>{translate(source.description)}</div>
    </div>
  );
}

function Sources({
  order,
  disabled = [],
  hasTmdbKey,
  hasOmdbKey = false,
  onOrderChange,
  onDisabledChange,
}: SourcesProps) {
  const sources = orderedMetadataSources(order);
  const handleMove = useCallback(
    (index: number, direction: number) => {
      const keys = sources.map(({ key }) => key);
      [keys[index], keys[index + direction]] = [
        keys[index + direction],
        keys[index],
      ];
      onOrderChange(keys);
    },
    [sources, onOrderChange]
  );
  const handleToggle = useCallback(
    ({ name, value }: CheckInputChanged) => {
      const key = name as MetadataSource;
      onDisabledChange(
        value ? disabled.filter((item) => item !== key) : [...disabled, key]
      );
    },
    [disabled, onDisabledChange]
  );
  const lastEnabled =
    sources.filter(({ key }) => !disabled.includes(key)).length === 1;

  return (
    <FieldSet legend={translate('MetadataSources')}>
      <Alert kind={kinds.INFO}>{translate('MetadataSourcesManageHelp')}</Alert>
      {sources.map((source, index) => (
        <SourceRow
          key={source.key}
          source={source}
          index={index}
          count={sources.length}
          enabled={!disabled.includes(source.key)}
          lastEnabled={lastEnabled}
          hasTmdbKey={hasTmdbKey}
          hasOmdbKey={hasOmdbKey}
          onToggle={handleToggle}
          onMove={handleMove}
        />
      ))}
      <div className={styles.attribution}>
        <InlineMarkdown
          data={translate('SeriesAndEpisodeInformationIsProvidedByTheTVDB', {
            url: 'https://www.thetvdb.com/subscribe',
          })}
        />
      </div>
    </FieldSet>
  );
}

export default Sources;
