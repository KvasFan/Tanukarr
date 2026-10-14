import React, { useEffect } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import NotFound from 'Components/NotFound';
import usePrevious from 'Helpers/Hooks/usePrevious';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import SeriesDetails from './SeriesDetails';

function SeriesDetailsPage() {
  const { data: allSeries } = useSeries();
  const { titleSlug } = useParams<{ titleSlug: string }>();
  const [searchParams] = useSearchParams();
  const seasonParameter = searchParams.get('season');
  const requestedSeason =
    seasonParameter !== null && /^\d+$/.test(seasonParameter)
      ? Number(seasonParameter)
      : undefined;
  const navigate = useNavigate();

  const seriesIndex = allSeries.findIndex(
    (series) => series.titleSlug === titleSlug
  );

  const previousIndex = usePrevious(seriesIndex);

  useEffect(() => {
    if (
      seriesIndex === -1 &&
      previousIndex !== -1 &&
      previousIndex !== undefined
    ) {
      navigate('/');
    }
  }, [seriesIndex, previousIndex, navigate]);

  if (seriesIndex === -1) {
    return <NotFound message={translate('SeriesCannotBeFound')} />;
  }

  return (
    <SeriesDetails
      seriesId={allSeries[seriesIndex].id}
      requestedSeason={requestedSeason}
    />
  );
}

export default SeriesDetailsPage;
