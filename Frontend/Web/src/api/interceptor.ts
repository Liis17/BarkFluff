// connect-интерсептор: подставляет device-метаданные + x-auth-token из getValidToken,
// и при ответе UNAUTHENTICATED делает один forced refresh + ретрай.
// Порт логики authCall/callWithToken из wwwroot/js/app/clients.js.
import { Code, ConnectError, type Interceptor } from '@connectrpc/connect';
import { buildHeaders } from './metadata';
import { getValidToken, refreshToken } from './refresh';

export const authInterceptor: Interceptor = (next) => async (req) => {
  const token = await getValidToken();
  // Подставляем актуальные заголовки (device + token).
  buildHeaders(token).forEach((value, key) => req.header.set(key, value));

  try {
    return await next(req);
  } catch (err) {
    // Только для unary: при UNAUTHENTICATED один раз рефрешим и повторяем.
    if (!req.stream && err instanceof ConnectError && err.code === Code.Unauthenticated) {
      const newToken = await refreshToken();
      if (!newToken) throw err;
      req.header.set('x-auth-token', newToken);
      return await next(req);
    }
    throw err;
  }
};
