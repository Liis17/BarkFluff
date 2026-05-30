// Тонкие обёртки над UsersApi (unary).
import { usersClient } from '../transport';
import { rawUsersClient } from '../baseTransport';
import { buildHeaders } from '../metadata';
import {
  ChangeBioRequest,
  ChangeNameRequest,
  ChangeUsernameRequest,
  CheckExistEmailRequest,
  CheckExistUsernameRequest,
  GetUserRequest,
  RenameDeviceRequest,
  SetProfilePictureRequest,
} from '../../gen/users_api_pb';
import type { User } from '../../gen/users_api_pb';

// Проверки существования (pre-auth, без интерсептора).
export async function checkUsernameExists(username: string): Promise<boolean> {
  const resp = await rawUsersClient.checkExistUsername(new CheckExistUsernameRequest({ username }), {
    headers: buildHeaders(),
  });
  return resp.exist;
}

export async function checkEmailExists(email: string): Promise<boolean> {
  const resp = await rawUsersClient.checkExistEmail(new CheckExistEmailRequest({ email }), {
    headers: buildHeaders(),
  });
  return resp.exist;
}

export async function getUser(userId: bigint | string): Promise<User | undefined> {
  const resp = await usersClient.getUser(
    new GetUserRequest({ userId: typeof userId === 'string' ? BigInt(userId) : userId }),
  );
  return resp.user;
}

export async function changeName(firstName: string, lastName: string) {
  await usersClient.changeName(new ChangeNameRequest({ firstName, lastName }));
}

export async function changeUsername(username: string) {
  await usersClient.changeUsername(new ChangeUsernameRequest({ username }));
}

export async function changeBio(bio: string) {
  await usersClient.changeBio(new ChangeBioRequest({ bio }));
}

export async function setProfilePicture(fileId: string) {
  await usersClient.setProfilePicture(new SetProfilePictureRequest({ fileId }));
}

export async function renameDevice(deviceId: string, customName: string) {
  await usersClient.renameDevice(new RenameDeviceRequest({ deviceId, customName }));
}
