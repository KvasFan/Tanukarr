import React, { lazy } from 'react';
import { Navigate, Route } from 'react-router-dom';
import NotFound from 'Components/NotFound';

const Blocklist = lazy(() => import('Activity/Blocklist/Blocklist'));
const History = lazy(() => import('Activity/History/History'));
const Queue = lazy(() => import('Activity/Queue/Queue'));
const AddNewSeries = lazy(() => import('AddSeries/AddNewSeries/AddNewSeries'));
const ImportSeries = lazy(
  () => import('AddSeries/ImportSeries/Import/ImportSeries')
);
const ImportSeriesSelectFolder = lazy(
  () => import('AddSeries/ImportSeries/SelectFolder/ImportSeriesSelectFolder')
);
const CalendarPage = lazy(() => import('Calendar/CalendarPage'));
const LibraryTools = lazy(() => import('LibraryTools/LibraryTools'));
const SeriesDetailsPage = lazy(
  () => import('Series/Details/SeriesDetailsPage')
);
const CollectionPage = lazy(() => import('Series/Grouping/CollectionPage'));
const SeriesIndex = lazy(() => import('Series/Index/SeriesIndex'));
const CustomFormatSettingsPage = lazy(
  () => import('Settings/CustomFormats/CustomFormatSettingsPage')
);
const DownloadClientSettings = lazy(
  () => import('Settings/DownloadClients/DownloadClientSettings')
);
const GeneralSettings = lazy(() => import('Settings/General/GeneralSettings'));
const ImportListSettings = lazy(
  () => import('Settings/ImportLists/ImportListSettings')
);
const IndexerSettings = lazy(() => import('Settings/Indexers/IndexerSettings'));
const MediaManagement = lazy(
  () => import('Settings/MediaManagement/MediaManagement')
);
const MetadataSettings = lazy(
  () => import('Settings/Metadata/MetadataSettings')
);
const MetadataSourceSettings = lazy(
  () => import('Settings/MetadataSource/MetadataSourceSettings')
);
const NotificationSettings = lazy(
  () => import('Settings/Notifications/NotificationSettings')
);
const Profiles = lazy(() => import('Settings/Profiles/Profiles'));
const Quality = lazy(() => import('Settings/Quality/Quality'));
const Settings = lazy(() => import('Settings/Settings'));
const TagSettings = lazy(() => import('Settings/Tags/TagSettings'));
const UISettings = lazy(() => import('Settings/UI/UISettings'));
const Statistics = lazy(() => import('Statistics/Statistics'));
const Backups = lazy(() => import('System/Backup/Backups'));
const LogsTable = lazy(() => import('System/Events/LogsTable'));
const AppLogFiles = lazy(() => import('System/Logs/App/AppLogFiles'));
const UpdateLogFiles = lazy(() => import('System/Logs/Update/UpdateLogFiles'));
const Status = lazy(() => import('System/Status/Status'));
const Tasks = lazy(() => import('System/Tasks/Tasks'));
const Updates = lazy(() => import('System/Updates/Updates'));
const CutoffUnmet = lazy(() => import('Wanted/CutoffUnmet/CutoffUnmet'));
const Missing = lazy(() => import('Wanted/Missing/Missing'));

export function appRouteElements() {
  return (
    <>
      {/*
        Home (v5) — TODO: swap to <HomePage /> when the homepage redesign lands.
        Until then, "/" redirects to SeriesIndex and the sidebar's "Home" nav is hidden.
      */}

      <Route path="/" element={<Navigate to="/series" replace={true} />} />

      {/*
        Series
      */}

      <Route path="/series" element={<SeriesIndex />} />

      <Route path="/add/new" element={<AddNewSeries />} />

      <Route path="/add/import" element={<ImportSeriesSelectFolder />} />

      <Route path="/add/import/:rootFolderId" element={<ImportSeries />} />

      <Route
        path="/serieseditor"
        element={<Navigate to="/series" replace={true} />}
      />

      <Route
        path="/seasonpass"
        element={<Navigate to="/series" replace={true} />}
      />

      <Route path="/series/:titleSlug" element={<SeriesDetailsPage />} />

      <Route path="/collections/:collectionId" element={<CollectionPage />} />

      <Route path="/statistics" element={<Statistics />} />
      <Route path="/system/library" element={<LibraryTools />} />

      {/*
        Calendar
      */}

      <Route path="/calendar" element={<CalendarPage />} />

      {/*
        Activity
      */}

      <Route path="/activity/history" element={<History />} />

      <Route path="/activity/queue" element={<Queue />} />

      <Route path="/activity/blocklist" element={<Blocklist />} />

      {/*
        Wanted
      */}

      <Route path="/wanted/missing" element={<Missing />} />

      <Route path="/wanted/cutoffunmet" element={<CutoffUnmet />} />

      {/*
        Settings
      */}

      <Route path="/settings" element={<Settings />} />

      <Route path="/settings/mediamanagement" element={<MediaManagement />} />

      <Route path="/settings/profiles" element={<Profiles />} />

      <Route path="/settings/quality" element={<Quality />} />

      <Route
        path="/settings/customformats"
        element={<CustomFormatSettingsPage />}
      />

      <Route path="/settings/indexers" element={<IndexerSettings />} />

      <Route
        path="/settings/downloadclients"
        element={<DownloadClientSettings />}
      />

      <Route path="/settings/importlists" element={<ImportListSettings />} />

      <Route path="/settings/connect" element={<NotificationSettings />} />

      <Route path="/settings/metadata" element={<MetadataSettings />} />

      <Route
        path="/settings/metadatasource"
        element={<MetadataSourceSettings />}
      />

      <Route path="/settings/tags" element={<TagSettings />} />

      <Route path="/settings/general" element={<GeneralSettings />} />

      <Route path="/settings/ui" element={<UISettings />} />

      {/*
        System
      */}

      <Route path="/system/status" element={<Status />} />

      <Route path="/system/tasks" element={<Tasks />} />

      <Route path="/system/backups" element={<Backups />} />

      <Route path="/system/updates" element={<Updates />} />

      <Route path="/system/events" element={<LogsTable />} />

      <Route path="/system/logs/files" element={<AppLogFiles />} />

      <Route path="/system/logs/files/update" element={<UpdateLogFiles />} />

      {/*
        Not Found
      */}

      <Route path="*" element={<NotFound />} />
    </>
  );
}
