import React from 'react';
import Link from 'Components/Link/Link';
import getMetadataProvider from 'Series/getMetadataProvider';
import translate from 'Utilities/String/translate';
import styles from './MetadataAttribution.module.css';

export default function MetadataAttribution({
  metadataSource,
}: {
  metadataSource?: string;
}) {
  const provider = getMetadataProvider({ metadataSource });
  return (
    <div className={styles.container}>
      <Link className={styles.attribution} to="/settings/metadatasource">
        {translate('MetadataProvidedBy', { provider: provider.name })}
      </Link>
    </div>
  );
}
