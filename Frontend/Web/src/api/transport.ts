// Транспорт с auth-интерсептором + типизированные клиенты сервисов.
// Unary-вызовы через эти клиенты автоматически получают токен и авто-refresh.
// Стримы (UpdatesApi) используют baseTransport напрямую — см. realtime/.
import { createClient } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { authInterceptor } from './interceptor';
import { IdentityApi } from '../gen/identity_api_connect';
import { UsersApi } from '../gen/users_api_connect';
import { MessagesApi } from '../gen/messages_api_connect';
import { FilesApi } from '../gen/files_api_connect';

const authTransport = createGrpcWebTransport({
  baseUrl: '/',
  interceptors: [authInterceptor],
});

export const identityClient = createClient(IdentityApi, authTransport);
export const usersClient = createClient(UsersApi, authTransport);
export const messagesClient = createClient(MessagesApi, authTransport);
export const filesClient = createClient(FilesApi, authTransport);
