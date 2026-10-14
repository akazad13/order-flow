/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Same path prefixes as nginx.conf, so the app runs unchanged in dev and in docker compose.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // ws: live order updates over SignalR (spec 005).
      '/order-api': { target: 'http://localhost:5101', ws: true, rewrite: (path) => path.replace(/^\/order-api/, '') },
      '/notification-api': { target: 'http://localhost:5104', rewrite: (path) => path.replace(/^\/notification-api/, '') },
      // Admins manage stock (spec 017).
      '/inventory-api': { target: 'http://localhost:5102', rewrite: (path) => path.replace(/^\/inventory-api/, '') },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
  },
});
