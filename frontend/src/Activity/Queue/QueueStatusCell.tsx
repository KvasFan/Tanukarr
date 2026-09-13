import React from 'react';
import IconButton from 'Components/Link/IconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
  StatusMessage,
} from 'typings/Queue';
import translate from 'Utilities/String/translate';
import QueueStatus from './QueueStatus';
import styles from './QueueStatusCell.css';

interface QueueStatusCellProps {
  sourceTitle: string;
  status: string;
  trackedDownloadStatus?: QueueTrackedDownloadStatus;
  trackedDownloadState?: QueueTrackedDownloadState;
  statusMessages?: StatusMessage[];
  errorMessage?: string;
  hasImportProgress?: boolean;
  isImportProgressExpanded?: boolean;
  onImportProgressExpand?: () => void;
}

function QueueStatusCell(props: QueueStatusCellProps) {
  const {
    sourceTitle,
    status,
    trackedDownloadStatus = 'ok',
    trackedDownloadState = 'downloading',
    statusMessages,
    errorMessage,
    hasImportProgress = false,
    isImportProgressExpanded = false,
    onImportProgressExpand,
  } = props;

  return (
    <TableRowCell className={styles.status}>
      <QueueStatus
        sourceTitle={sourceTitle}
        status={status}
        trackedDownloadStatus={trackedDownloadStatus}
        trackedDownloadState={trackedDownloadState}
        statusMessages={statusMessages}
        errorMessage={errorMessage}
        position="right"
      />
      {hasImportProgress ? (
        <IconButton
          name={isImportProgressExpanded ? icons.COLLAPSE : icons.EXPAND}
          title={translate(
            isImportProgressExpanded ? 'HideEpisodes' : 'ShowEpisodes'
          )}
          aria-label={translate(
            isImportProgressExpanded ? 'HideEpisodes' : 'ShowEpisodes'
          )}
          onPress={onImportProgressExpand}
        />
      ) : null}
    </TableRowCell>
  );
}

export default QueueStatusCell;
