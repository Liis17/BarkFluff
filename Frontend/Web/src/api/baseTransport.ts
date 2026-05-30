// «Голый» gRPC-Web транспорт без auth-интерсептора.
// Используется для:
//  - login (токена ещё нет),
//  - refresh (CreateToken по refresh-токену),
//  - server-streaming (стримы сами управляют токеном в заголовках).
// Вынесен отдельно, чтобы разорвать цикл transport ↔ interceptor ↔ refresh.
//
// baseUrl: '/' — connect-web формирует URL {baseUrl}{pkg}.{Service}/{Method},
// что точно совпадает с YARP-маршрутами BarkFluff.Web (Program.cs).
import { createClient } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { IdentityApi } from '../gen/identity_api_connect';
import { UsersApi } from '../gen/users_api_connect';

export const baseTransport = createGrpcWebTransport({ baseUrl: '/' });

// Клиенты без интерсептора — для login/refresh и pre-auth проверок (регистрация).
export const rawIdentityClient = createClient(IdentityApi, baseTransport);
export const rawUsersClient = createClient(UsersApi, baseTransport);
