import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Локальный BarkFluff.Web (YARP-прокси gRPC-Web ↔ gRPC). Kestrel слушает 7016
// (ConfigureKestrel.ListenAnyIP в Program.cs). Переопределяется через BF_PROXY.
const GRPC_TARGET = process.env.BF_PROXY ?? 'http://localhost:7016';

// gRPC-сервисы маршрутизируются прокси по корню /barkfluff.<pkg>.<Service>/...
const grpcProxy = (timeout?: number) => ({
  target: GRPC_TARGET,
  changeOrigin: true,
  ...(timeout !== undefined ? { timeout } : {}),
});

export default defineConfig({
  plugins: [react()],
  build: {
    // Собираем в локальный dist/. В образ BarkFluff.Web статику кладёт Dockerfile
    // (Node-стадия web-build → COPY dist → wwwroot). Так репозиторий остаётся чистым.
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Выносим тяжёлые зависимости в отдельный vendor-чанк для кэширования.
        manualChunks: {
          react: ['react', 'react-dom', 'react-router-dom'],
          grpc: ['@connectrpc/connect', '@connectrpc/connect-web', '@bufbuild/protobuf'],
        },
      },
    },
  },
  server: {
    proxy: {
      '/barkfluff.identity.IdentityApi': grpcProxy(),
      '/barkfluff.users.UsersApi': grpcProxy(),
      '/barkfluff.messages.MessagesApi': grpcProxy(),
      '/barkfluff.files.FilesApi': grpcProxy(),
      // Server-streaming — без таймаута на долгоживущее соединение.
      '/barkfluff.updates.UpdatesApi': grpcProxy(0),
      '/barkfluff.onliner.OnlinerApi': grpcProxy(0),
      '/api/files/upload': grpcProxy(),
    },
  },
});
