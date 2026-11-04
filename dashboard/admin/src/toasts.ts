import { reactive } from "vue";

export interface Toast {
  id: number;
  kind: "success" | "error" | "info";
  text: string;
}

const list = reactive<Toast[]>([]);
let seq = 0;

export function toast(kind: Toast["kind"], text: string, ms = 4000): void {
  const id = ++seq;
  list.push({ id, kind, text });
  setTimeout(() => dismiss(id), ms);
}

export function dismiss(id: number): void {
  const i = list.findIndex((t) => t.id === id);
  if (i >= 0) list.splice(i, 1);
}

export function useToasts() {
  return { toasts: list, toast, dismiss };
}

/** Runs an action, reporting failure as a toast. Returns whether it succeeded. */
export async function attempt(action: () => Promise<unknown>, success?: string): Promise<boolean> {
  try {
    await action();
    if (success) toast("success", success);
    return true;
  } catch (e) {
    toast("error", e instanceof Error ? e.message : String(e), 7000);
    return false;
  }
}
