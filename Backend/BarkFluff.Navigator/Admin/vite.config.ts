import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  base: '/admin/',
  plugins: [react()],
  build: {
    outDir: '../wwwroot/admin',
    emptyOutDir: true,
  },
});
