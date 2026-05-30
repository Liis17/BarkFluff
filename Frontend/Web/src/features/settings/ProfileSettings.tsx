import { useEffect, useRef, useState } from 'react';
import { Avatar } from '../../components/Avatar';
import { Button } from '../../components/Button';
import { TextField } from '../../components/TextField';
import { useAuth } from '../../state/AuthContext';
import { changeBio, changeName, changeUsername, getUser, setProfilePicture } from '../../api/services/users';
import { uploadFile } from '../../api/upload';
import { UploadFileType } from '../../gen/files_api_pb';
import { ErrorCodes, extractErrorCode } from '../../api/errorCodes';

export function ProfileSettings() {
  const { currentUserId } = useAuth();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [username, setUsername] = useState('');
  const [bio, setBio] = useState('');
  const [picture, setPicture] = useState('');
  const initial = useRef({ firstName: '', lastName: '', username: '', bio: '' });
  const fileInput = useRef<HTMLInputElement>(null);
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (currentUserId == null) return;
    getUser(currentUserId)
      .then((u) => {
        if (!u) return;
        setFirstName(u.firstName);
        setLastName(u.lastName);
        setUsername(u.username);
        setBio(u.bio);
        setPicture(u.profilePicture);
        initial.current = { firstName: u.firstName, lastName: u.lastName, username: u.username, bio: u.bio };
      })
      .catch(() => {});
  }, [currentUserId]);

  async function onSave() {
    setStatus('saving');
    setError(null);
    try {
      const i = initial.current;
      if (firstName !== i.firstName || lastName !== i.lastName) await changeName(firstName.trim(), lastName.trim());
      if (username !== i.username) await changeUsername(username.trim());
      if (bio !== i.bio) await changeBio(bio);
      initial.current = { firstName, lastName, username, bio };
      setStatus('saved');
    } catch (err) {
      setStatus('error');
      setError(
        extractErrorCode(err) === ErrorCodes.INVALID_USERNAME_FORMAT
          ? 'Неверный формат username (3–32 символа: буквы, цифры, _)'
          : err instanceof Error
            ? err.message
            : 'Не удалось сохранить',
      );
    }
  }

  async function onPickAvatar(file: File) {
    setStatus('saving');
    try {
      const fileId = await uploadFile(file, UploadFileType.USER_AVATAR);
      await setProfilePicture(fileId);
      if (currentUserId != null) {
        const u = await getUser(currentUserId);
        if (u) setPicture(u.profilePicture);
      }
      setStatus('saved');
    } catch (err) {
      setStatus('error');
      setError(err instanceof Error ? err.message : 'Не удалось загрузить аватар');
    }
  }

  return (
    <div className="bf-setcard">
      <h2 className="bf-setcard__title">Профиль</h2>

      <div className="bf-profile__avatar">
        <Avatar name={`${firstName} ${lastName}`} src={picture || undefined} size={88} />
        <div>
          <Button variant="tonal" icon="photo_camera" onClick={() => fileInput.current?.click()}>
            Сменить аватар
          </Button>
          <input
            ref={fileInput}
            type="file"
            accept="image/*"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void onPickAvatar(f);
              e.target.value = '';
            }}
          />
        </div>
      </div>

      <div className="bf-set-fields">
        <TextField label="Имя" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
        <TextField label="Фамилия" value={lastName} onChange={(e) => setLastName(e.target.value)} />
        <TextField label="Имя пользователя" value={username} onChange={(e) => setUsername(e.target.value)} leadingIcon="alternate_email" />
        <TextField label="О себе" value={bio} onChange={(e) => setBio(e.target.value)} />
      </div>

      <div className="bf-set-actions">
        {status === 'saved' && <span className="bf-set-saved">Сохранено ✓</span>}
        {status === 'error' && error && <span className="bf-set-error">{error}</span>}
        <Button onClick={() => void onSave()} loading={status === 'saving'}>
          Сохранить
        </Button>
      </div>
    </div>
  );
}
