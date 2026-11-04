import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { effectScope, nextTick } from "vue";
import { usePolling } from "./usePolling";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

async function settle() {
  // Let resolved promises run their continuations.
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await nextTick();
}

describe("usePolling", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("fetches immediately, then on the interval, and stops when the scope is disposed", async () => {
    let n = 0;
    const fetcher = vi.fn(async () => ++n);
    const scope = effectScope();
    const polling = scope.run(() => usePolling(fetcher, { intervalMs: 1000, pauseWhenHidden: false }))!;

    await settle();
    expect(polling.data.value).toBe(1);
    expect(polling.loading.value).toBe(false);

    await vi.advanceTimersByTimeAsync(1000);
    expect(polling.data.value).toBe(2);

    scope.stop();
    await vi.advanceTimersByTimeAsync(5000);
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it("keeps the last good data and exposes the error when a fetch fails", async () => {
    const results = [() => Promise.resolve("ok"), () => Promise.reject(new Error("down")), () => Promise.resolve("back")];
    const polling = effectScope().run(() => usePolling(() => results.shift()!(), { intervalMs: 1000, pauseWhenHidden: false }))!;

    await settle();
    expect(polling.data.value).toBe("ok");

    await vi.advanceTimersByTimeAsync(1000);
    expect(polling.data.value).toBe("ok");
    expect(polling.error.value?.message).toBe("down");

    await vi.advanceTimersByTimeAsync(1000);
    expect(polling.data.value).toBe("back");
    expect(polling.error.value).toBeUndefined();
  });

  it("coalesces concurrent refreshes", async () => {
    const pending = deferred<number>();
    const fetcher = vi.fn(() => pending.promise);
    const polling = effectScope().run(() => usePolling(fetcher, { intervalMs: 0, immediate: false }))!;

    const a = polling.refresh();
    const b = polling.refresh();
    expect(polling.loading.value).toBe(true);
    expect(fetcher).toHaveBeenCalledTimes(1);

    pending.resolve(7);
    await Promise.all([a, b]);
    expect(polling.data.value).toBe(7);
    expect(polling.loading.value).toBe(false);
  });

  it("pause stops the timer and resume fetches again", async () => {
    const fetcher = vi.fn(async () => Date.now());
    const polling = effectScope().run(() => usePolling(fetcher, { intervalMs: 1000, pauseWhenHidden: false }))!;
    await settle();

    polling.pause();
    await vi.advanceTimersByTimeAsync(5000);
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(polling.paused.value).toBe(true);

    polling.resume();
    await settle();
    expect(fetcher).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1000);
    expect(fetcher).toHaveBeenCalledTimes(3);
  });

  it("intervalMs 0 means manual refresh only", async () => {
    const fetcher = vi.fn(async () => 1);
    effectScope().run(() => usePolling(fetcher, { intervalMs: 0 }));
    await settle();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
});
