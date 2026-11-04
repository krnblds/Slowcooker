import { reactive } from "vue";

export interface ConfirmRequest {
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
}

interface Pending extends ConfirmRequest {
  resolve: (ok: boolean) => void;
}

export const pending = reactive<{ current: Pending | null }>({ current: null });

/** Shows the app's confirm dialog (rendered once in App.vue) and resolves with the user's choice. */
export function confirm(request: ConfirmRequest): Promise<boolean> {
  pending.current?.resolve(false);
  return new Promise((resolve) => {
    pending.current = { ...request, resolve };
  });
}

export function settle(ok: boolean): void {
  pending.current?.resolve(ok);
  pending.current = null;
}
