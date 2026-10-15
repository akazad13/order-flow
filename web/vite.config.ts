/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Same path prefixes as nginx.conf, so the app runs unchanged in dev and in docker compose.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Everything goes through the gateway (spec 023, `dotnet run` on 5105), which logs in and adds the token.
      // ws: live order updates over SignalR (spec 005).
      '/bff': { target: 'http://localhost:5105' },
      '/order-api': { target: 'http://localhost:5105', ws: true },
      '/notification-api': { target: 'http://localhost:5105' },
      '/inventory-api': { target: 'http://localhost:5105' },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
  },
});
