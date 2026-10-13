import React from 'react';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import LoadingMessage from 'Components/Loading/LoadingMessage';
import styles from './LoadingPage.module.css';

function LoadingPage() {
  return (
    <div className={styles.page}>
      <img
        className={styles.logoFull}
        src={`${window.Sonarr.urlBase}/Content/Images/logo.svg`}
        alt={window.Sonarr.productName}
      />
      <LoadingMessage />
      <LoadingIndicator />
    </div>
  );
}

export default LoadingPage;
