import loadTimeZoneData from './Language/loadTimeZoneData';

import './polyfills';
import 'Styles/globals.css';
import './index.css';

const initializeUrl = `${
  window.Sonarr.urlBase
}/initialize.json?t=${Date.now()}`;
const [response] = await Promise.all([
  fetch(initializeUrl),
  loadTimeZoneData(),
]);

window.Sonarr = await response.json();

const { bootstrap } = await import('./bootstrap');

await bootstrap();
