// src/config/api.config.ts (or wherever your current export lives)

const getBaseUrl = () => {
  if (typeof window === 'undefined') return 'http://localhost:5000';

  // Development: Vite Server (5173) -> Point to API on same host
  if (window.location.port === '5173') {
    // If we're on a secure context or user wants HTTPS, prefer 5001
    return `https://${window.location.hostname}:5001`;
  }

  // Production: Served by API -> Use relative path
  return '';
};

const API_BASE_URL = getBaseUrl();

export default API_BASE_URL;
