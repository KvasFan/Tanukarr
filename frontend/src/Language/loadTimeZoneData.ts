import moment from 'moment-timezone';
import timeZoneDataUrl from 'moment-timezone/data/packed/latest.json?url';

export default async function loadTimeZoneData() {
  const response = await fetch(timeZoneDataUrl);

  if (!response.ok) {
    throw new Error('Unable to load time zone definitions');
  }

  moment.tz.load(await response.json());
}
