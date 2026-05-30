import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './state/AuthContext';
import { RealtimeProvider } from './realtime/RealtimeProvider';
import { ThemeProvider } from './theme/ThemeProvider';
import { RequireAuth, PublicOnly } from './app/RequireAuth';
import { AppLayout } from './app/AppLayout';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterWizard } from './features/register/RegisterWizard';
import { ChatsPage } from './features/chats/ChatsPage';
import { SettingsPage } from './features/settings/SettingsPage';

export function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <RealtimeProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<PublicOnly><LoginPage /></PublicOnly>} />
            <Route path="/register" element={<PublicOnly><RegisterWizard /></PublicOnly>} />

            <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
              <Route path="/chats" element={<ChatsPage />} />
              <Route path="/chats/:chatId" element={<ChatsPage />} />
              <Route path="/settings" element={<Navigate to="/settings/profile" replace />} />
              <Route path="/settings/*" element={<SettingsPage />} />
            </Route>

            <Route path="*" element={<Navigate to="/chats" replace />} />
          </Routes>
        </BrowserRouter>
        </RealtimeProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
