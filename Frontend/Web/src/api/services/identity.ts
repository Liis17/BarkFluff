// Обёртки над IdentityApi (сессии, 2FA). Login/refresh — см. AuthContext/refresh.
import { identityClient } from '../transport';
import { rawIdentityClient } from '../baseTransport';
import { buildHeaders } from '../metadata';
import {
  ConfirmAccountRequest,
  ConfirmOtpVerificationRequest,
  CreateAccountRequest,
  DisableOtpVerificationRequest,
  EnableOtpVerificationRequest,
  GetActiveSessionsRequest,
  ListOtpVerificationRequest,
  OtpTypeId,
  RemoveActiveSessionRequest,
  SetPasswordRequest,
} from '../../gen/identity_api_pb';

export async function getActiveSessions() {
  const resp = await identityClient.getActiveSessions(new GetActiveSessionsRequest({}));
  return resp.sessions;
}

export async function removeActiveSession(deviceId: string) {
  await identityClient.removeActiveSession(new RemoveActiveSessionRequest({ deviceId }));
}

export async function listOtpVerification() {
  const resp = await identityClient.listOtpVerification(new ListOtpVerificationRequest({}));
  return { authenticatorEnabled: resp.authenticatorEnabled, emailEnabled: resp.emailEnabled };
}

export async function enableAuthenticator() {
  const resp = await identityClient.enableOtpVerification(
    new EnableOtpVerificationRequest({ otpType: OtpTypeId.Authenticator }),
  );
  return { otpQr: resp.otpQr, otpCode: resp.otpCode };
}

export async function confirmOtpVerification(otpCode: string) {
  await identityClient.confirmOtpVerification(new ConfirmOtpVerificationRequest({ otpCode }));
}

export async function disableAuthenticator(otpCode: string) {
  await identityClient.disableOtpVerification(
    new DisableOtpVerificationRequest({ otpType: OtpTypeId.Authenticator, otpCode }),
  );
}

// --- Регистрация (pre-auth, через rawIdentityClient без интерсептора) ---

export async function createAccount(firstName: string, lastName: string, username: string, email: string) {
  const resp = await rawIdentityClient.createAccount(
    new CreateAccountRequest({ firstName, lastName, username, email }),
    { headers: buildHeaders() },
  );
  return resp.codeId;
}

export async function confirmAccount(codeId: string, codeValue: string) {
  const resp = await rawIdentityClient.confirmAccount(
    new ConfirmAccountRequest({ codeId, codeValue }),
    { headers: buildHeaders() },
  );
  return resp.refreshToken;
}

// SetPassword вызывается уже с токеном (после confirm) — через authed-клиент.
export async function setPassword(password: string) {
  await identityClient.setPassword(new SetPasswordRequest({ password, oldPassword: '' }));
}
