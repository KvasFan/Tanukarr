import React, { useEffect } from 'react';
import {
  useLocation,
  useNavigate,
  useParams,
  useSearchParams,
} from 'react-router';
import NotFound from 'Components/NotFound';
import usePrevious from 'Helpers/Hooks/usePrevious';
import useSeries from 'Series/useSeries';
import getSeriesPath from 'Utilities/Series/getSeriesPath';
import translate from 'Utilities/String/translate';
import SeriesDetails from './SeriesDetails';

function SeriesDetailsPage() {
  const { data: allSeries } = useSeries();
  const { seriesId: seriesIdParam, titleSlug } = useParams<{
    seriesId?: string;
    titleSlug: string;
  }>();
  const [searchParams] = useSearchParams();
  const seasonParameter = searchParams.get('season');
  const requestedSeason =
    seasonParameter !== null && /^\d+$/.test(seasonParameter)
      ? Number(seasonParameter)
      : undefined;
  const navigate = useNavigate();
  const { hash } = useLocation();

  const series = seriesIdParam
    ? allSeries.find((series) => series.id === Number(seriesIdParam))
    : allSeries.find((series) => series.titleSlug === titleSlug);

  const seriesId = series?.id;
  const previousSeriesId = usePrevious(seriesId);
  const previousSeriesIdParam = usePrevious(seriesIdParam);
  const search = searchParams.toString();

  useEffect(() => {
    if (series) {
      if (!seriesIdParam || series.titleSlug !== titleSlug) {
        // Keep ?season= and #season-N so links into a collection season
        // survive the redirect from a slug-only URL.
        navigate(
          `${getSeriesPath(series)}${search ? `?${search}` : ''}${hash}`,
          {
            replace: true,
          }
        );
      }

      return;
    }

    if (
      previousSeriesId !== undefined &&
      seriesIdParam === previousSeriesIdParam
    ) {
      navigate('/');
    }
  }, [
    series,
    seriesIdParam,
    titleSlug,
    search,
    hash,
    previousSeriesId,
    previousSeriesIdParam,
    navigate,
  ]);

  if (!series) {
    return <NotFound message={translate('SeriesCannotBeFound')} />;
  }

  return (
    <SeriesDetails seriesId={series.id} requestedSeason={requestedSeason} />
  );
}

export default SeriesDetailsPage;
