import { toast } from "./toasts";

/** Copies text and confirms with a toast. Clipboard access needs a secure context (https or localhost). */
export async function copy(text: string, what = "Copied"): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
    toast("info", what);
  } catch {
    toast("error", "Could not access the clipboard");
  }
}
