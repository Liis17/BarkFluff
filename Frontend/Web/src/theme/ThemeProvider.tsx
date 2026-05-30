import { useEffect, type ReactNode } from 'react';
import { useThemeStore } from '../state/themeStore';

// Применяет выбранную тему к <html data-theme="...">.
export function ThemeProvider({ children }: { children: ReactNode }) {
  const theme = useThemeStore((s) => s.theme);
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
  }, [theme]);
  return <>{children}</>;
}
