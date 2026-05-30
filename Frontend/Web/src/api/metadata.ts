// gRPC-Web метаданные, совместимые с серверными интерсепторами BarkFluff.Shared.Auth.
// Все значения, кроме x-auth-token, base64-кодируются. Порт wwwroot/js/app/metadata.js.
import { getAppName, getAppVersion, getBrowserName, getDeviceId, getOsName } from './device';

function toBase64(str: string): string {
  return btoa(unescape(encodeURIComponent(str)));
}

export function buildHeaders(token?: string | null): Headers {
  const h = new Headers({
    'x-device-id': toBase64(getDeviceId()),
    'x-device-name': toBase64(getBrowserName()),
    'x-os-name': toBase64(getOsName()),
    'x-app-name': toBase64(getAppName()),
    'x-app-version': toBase64(getAppVersion()),
    'x-ip-address': toBase64('0.0.0.0'),
  });
  if (token) h.set('x-auth-token', token);
  return h;
}
