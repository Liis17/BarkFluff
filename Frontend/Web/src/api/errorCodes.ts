// Коды ошибок из трейлера x-error-code. Значения — из wwwroot/js/app/clients.js и памяти проекта.
import { ConnectError } from '@connectrpc/connect';

export const ErrorCodes = {
  OTP_REQUIRED: 'C1576884-12D8-4722-A7EE-9F9789AD1265',
  INVALID_OTP: '803B632C-4457-4B05-9435-9C3DD0F41E00',
  INVALID_CREDENTIALS: '21BFB9B5-C377-45D1-9B15-6B7F3432B397',
  INVALID_USERNAME_FORMAT: 'E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47',
} as const;

// Извлекает x-error-code из метаданных ConnectError (трейлер или заголовок).
export function extractErrorCode(err: unknown): string | null {
  if (err instanceof ConnectError) {
    return err.metadata.get('x-error-code') ?? null;
  }
  return null;
}
