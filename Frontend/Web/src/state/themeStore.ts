import { create } from 'zustand';

export type Theme = 'light' | 'dark' | 'midnight';

const KEY = 'barkfluff_theme';

function initialTheme(): Theme {
  const saved = localStorage.getItem(KEY);
  if (saved === 'light' || saved === 'dark' || saved === 'midnight') return saved;
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

interface ThemeState {
  theme: Theme;
  setTheme: (t: Theme) => void;
}

export const useThemeStore = create<ThemeState>((set) => ({
  theme: initialTheme(),
  setTheme: (t) => {
    localStorage.setItem(KEY, t);
    set({ theme: t });
  },
}));
