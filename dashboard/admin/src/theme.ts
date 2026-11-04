import { ref } from "vue";

export type Theme = "system" | "light" | "dark";
const KEY = "slowcooker:theme";
const current = ref<Theme>("system");

function read(): Theme {
  try {
    const v = localStorage.getItem(KEY);
    return v === "light" || v === "dark" ? v : "system";
  } catch {
    return "system";
  }
}

/** Applies the stored preference, or a one-off `?theme=light|dark|system` from the URL (handy for screenshots). */
export function applyStoredTheme(): void {
  const fromUrl = new URLSearchParams(location.search).get("theme");
  if (fromUrl === "light" || fromUrl === "dark" || fromUrl === "system") setTheme(fromUrl, false);
  else setTheme(read(), false);
}

export function setTheme(theme: Theme, persist = true): void {
  current.value = theme;
  if (theme === "system") document.documentElement.removeAttribute("data-theme");
  else document.documentElement.setAttribute("data-theme", theme);
  if (persist) {
    try {
      localStorage.setItem(KEY, theme);
    } catch {
      /* private mode etc. */
    }
  }
}

export function useTheme() {
  const cycle = () => setTheme(current.value === "system" ? "light" : current.value === "light" ? "dark" : "system");
  return { theme: current, setTheme, cycle };
}
