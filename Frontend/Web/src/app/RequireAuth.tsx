import { Navigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from '../state/AuthContext';

// Приватные роуты: без авторизации уводим на /login.
export function RequireAuth({ children }: { children: ReactNode }) {
  const { isAuthed } = useAuth();
  if (!isAuthed) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

// Публичные роуты (login/register): авторизованного уводим в чаты.
export function PublicOnly({ children }: { children: ReactNode }) {
  const { isAuthed } = useAuth();
  if (isAuthed) return <Navigate to="/chats" replace />;
  return <>{children}</>;
}
