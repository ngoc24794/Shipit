import { ref } from 'vue'

export type Theme = 'dark' | 'light'

const THEME_STORAGE_KEY = 'shipit_theme'

function getInitialTheme(): Theme {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY)
    if (stored === 'light' || stored === 'dark') {
      return stored
    }
  } catch {
    // localStorage may be disabled
  }

  if (typeof window !== 'undefined' && window.matchMedia) {
    if (window.matchMedia('(prefers-color-scheme: light)').matches) {
      return 'light'
    }
  }

  // Default is dark according to design.md
  return 'dark'
}

const currentTheme = ref<Theme>(getInitialTheme())

export function applyTheme(theme: Theme): void {
  currentTheme.value = theme
  if (typeof document !== 'undefined') {
    const root = document.documentElement
    if (theme === 'dark') {
      root.classList.add('dark')
    } else {
      root.classList.remove('dark')
    }
  }
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // Ignore storage failure
  }
}

export function useTheme() {
  function toggleTheme(): void {
    applyTheme(currentTheme.value === 'dark' ? 'light' : 'dark')
  }

  function initTheme(): void {
    applyTheme(currentTheme.value)
  }

  return {
    theme: currentTheme,
    toggleTheme,
    applyTheme,
    initTheme
  }
}
