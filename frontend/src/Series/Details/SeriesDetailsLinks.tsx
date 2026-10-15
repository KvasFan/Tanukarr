import React, { useMemo } from 'react';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import getMetadataProvider from 'Series/getMetadataProvider';
import Series from 'Series/Series';
import translate from 'Utilities/String/translate';
import styles from './SeriesDetailsLinks.module.css';

type SeriesDetailsLinksProps = Pick<
  Series,
  'tvdbId' | 'tvMazeId' | 'imdbId' | 'tmdbId' | 'metadataSource' | 'foreignId'
>;

interface SeriesDetailsLink {
  externalId?: string | number;
  name: string;
  url: string;
}

function SeriesDetailsLinks(props: SeriesDetailsLinksProps) {
  const { tvdbId, tvMazeId, imdbId, tmdbId, metadataSource, foreignId } = props;

  const links = useMemo(() => {
    const validLinks: SeriesDetailsLink[] = [];

    const provider = getMetadataProvider({
      metadataSource,
      foreignId,
      tvdbId,
      tmdbId,
      imdbId,
    });

    if (provider.url && metadataSource && metadataSource !== 'tvdb') {
      validLinks.push({
        name: provider.name,
        externalId: provider.externalId,
        url: provider.url,
      });
    }

    if (tvdbId > 0) {
      validLinks.push({
        externalId: tvdbId,
        name: 'The TVDB',
        url: `https://www.thetvdb.com/?tab=series&id=${tvdbId}`,
      });
    }

    if (tvMazeId > 0 && !validLinks.some((link) => link.name === 'TVmaze')) {
      validLinks.push({
        externalId: tvMazeId,
        name: 'TV Maze',
        url: `https://www.tvmaze.com/shows/${tvMazeId}/_`,
      });
    }

    if (imdbId) {
      validLinks.push(
        {
          externalId: imdbId,
          name: 'IMDB',
          url: `https://imdb.com/title/${imdbId}/`,
        },
        {
          name: 'Trakt',
          url: `https://trakt.tv/shows/${imdbId}`,
        },
        {
          name: 'MDBList',
          url: `https://mdblist.com/show/${imdbId}`,
        }
      );
    }

    if (tmdbId > 0 && !validLinks.some((link) => link.name === 'TMDB')) {
      validLinks.push({
        externalId: tmdbId,
        name: 'TMDB',
        url: `https://www.themoviedb.org/tv/${tmdbId}`,
      });
    }

    return validLinks.sort(
      (a, b) => Number(!a.externalId) - Number(!b.externalId)
    );
  }, [tvdbId, tvMazeId, imdbId, tmdbId, metadataSource, foreignId]);

  return (
    <div className={styles.links}>
      {links.map((link) => (
        <div key={link.name} className={styles.linkBlock}>
          <Link className={styles.link} to={link.url}>
            {link.name}
          </Link>

          {link.externalId ? (
            <ClipboardButton
              className={styles.copyButton}
              value={`${link.externalId}`}
              title={translate('CopyToClipboard')}
              kind={kinds.DEFAULT}
              size={sizes.SMALL}
              label={link.externalId}
            />
          ) : null}
        </div>
      ))}
    </div>
  );
}

export default SeriesDetailsLinks;
