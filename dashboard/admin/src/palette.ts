import { ref } from "vue";

/** Whether the command palette is open. Opened with Ctrl/Cmd+K or "/" from anywhere, or from the sidebar. */
export const paletteOpen = ref(false);
