declare module '*.module.css';

interface Window {
  Sonarr: {
    apiKey: string;
    apiRoot: string;
    instanceName: string;
    productName: string;
    productVersion: string;
    theme: string;
    urlBase: string;
    version: string;
    isProduction: boolean;
  };
}

interface ImportMeta {
  glob: (pattern: string, options: { eager: true }) => unknown;
}
