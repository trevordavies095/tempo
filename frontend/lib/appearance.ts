export const APPEARANCE_STORAGE_KEY = 'tempo-appearance';

export type AppearancePreference = 'system' | 'dark' | 'light';

export const DEFAULT_APPEARANCE: AppearancePreference = 'system';

export function isAppearancePreference(
  value: string | null
): value is AppearancePreference {
  return value === 'system' || value === 'dark' || value === 'light';
}

export function readStoredAppearance(): AppearancePreference {
  if (typeof window === 'undefined') {
    return DEFAULT_APPEARANCE;
  }

  try {
    const stored = localStorage.getItem(APPEARANCE_STORAGE_KEY);
    return isAppearancePreference(stored) ? stored : DEFAULT_APPEARANCE;
  } catch {
    return DEFAULT_APPEARANCE;
  }
}

export function resolveDark(
  preference: AppearancePreference,
  systemPrefersDark: boolean
): boolean {
  if (preference === 'dark') {
    return true;
  }
  if (preference === 'light') {
    return false;
  }
  return systemPrefersDark;
}

export function applyAppearance(preference: AppearancePreference): void {
  if (typeof document === 'undefined') {
    return;
  }

  const systemPrefersDark = window.matchMedia(
    '(prefers-color-scheme: dark)'
  ).matches;
  document.documentElement.classList.toggle(
    'dark',
    resolveDark(preference, systemPrefersDark)
  );
}

export function persistAppearance(preference: AppearancePreference): void {
  try {
    localStorage.setItem(APPEARANCE_STORAGE_KEY, preference);
  } catch {
    // Ignore quota / private-mode failures; class still applies.
  }
  applyAppearance(preference);
}

export const APPEARANCE_BOOTSTRAP_SCRIPT = `(function(){try{var s=localStorage.getItem('${APPEARANCE_STORAGE_KEY}');var p=(s==='dark'||s==='light'||s==='system')?s:'${DEFAULT_APPEARANCE}';var sys=window.matchMedia('(prefers-color-scheme: dark)').matches;var dark=p==='dark'||(p!=='light'&&sys);document.documentElement.classList.toggle('dark',dark);}catch(e){try{document.documentElement.classList.toggle('dark',window.matchMedia('(prefers-color-scheme: dark)').matches);}catch(e2){}}})();`;
