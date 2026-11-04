import { describe, expect, it, vi } from "vitest";
import { createClient, SlowcookerApiError } from "./client";

function fakeFetch(status: number, body?: unknown) {
  const calls: { url: URL; init: RequestInit }[] = [];
  const fetch = vi.fn(async (url: string, init: RequestInit) => {
    calls.push({ url: new URL(url), init });
    return new Response(body === undefined ? null : JSON.stringify(body), {
      status,
      headers: body === undefined ? {} : { "content-type": "application/json" },
    });
  }) as unknown as typeof globalThis.fetch;
  return { fetch, calls };
}

describe("createClient", () => {
  it("builds urls from the base and drops empty query values", async () => {
    const { fetch, calls } = fakeFetch(200, { items: [], total: 0, skip: 0, take: 20 });
    const api = createClient({ baseUrl: "/slowcooker/api/", fetch });

    await api.jobs({ state: "failed", take: 20, queue: "" });

    expect(calls[0].url.pathname).toBe("/slowcooker/api/jobs");
    expect([...calls[0].url.searchParams]).toEqual([
      ["state", "failed"],
      ["take", "20"],
    ]);
    expect(calls[0].init.method).toBe("GET");
  });

  it("defaults to the path AddAdminUserInterface() uses", async () => {
    const { fetch, calls } = fakeFetch(200, {});
    await createClient({ fetch }).stats();
    expect(calls[0].url.pathname).toBe("/slowcooker/api/stats");
  });

  it("encodes ids and uses the right methods", async () => {
    const { fetch, calls } = fakeFetch(204);
    const api = createClient({ fetch });

    await api.deleteJob("a/b");
    await api.trigger("nightly report");

    expect(calls[0].url.pathname).toBe("/slowcooker/api/jobs/a%2Fb");
    expect(calls[0].init.method).toBe("DELETE");
    expect(calls[1].url.pathname).toBe("/slowcooker/api/recurring/nightly%20report/trigger");
    expect(calls[1].init.method).toBe("POST");
    for (const call of calls) expect(new Headers(call.init.headers).get("x-slowcooker-request")).toBe("1");
  });

  it("passes the name filter through to /jobs", async () => {
    const { fetch, calls } = fakeFetch(200, { items: [], total: 0, skip: 0, take: 50 });
    await createClient({ fetch }).jobs({ name: "generate-daily-report", state: null });
    expect([...calls[0].url.searchParams]).toEqual([["name", "generate-daily-report"]]);
  });

  it("sends the headers you give it, resolving a function each time", async () => {
    const { fetch, calls } = fakeFetch(200, {});
    let token = "one";
    const api = createClient({ fetch, headers: () => ({ Authorization: `Bearer ${token}` }) });

    await api.info();
    token = "two";
    await api.info();

    const sent = (i: number) => new Headers(calls[i].init.headers);
    expect(sent(0).get("authorization")).toBe("Bearer one");
    expect(sent(1).get("authorization")).toBe("Bearer two");
    expect(sent(1).get("accept")).toBe("application/json");
  });

  it("returns the parsed body, or undefined for 204", async () => {
    const info = { version: "1", storage: "InMemoryJobStorage", serverTime: "2026-01-01T00:00:00+00:00" };
    expect(await createClient({ fetch: fakeFetch(200, info).fetch }).info()).toEqual(info);
    expect(await createClient({ fetch: fakeFetch(204).fetch }).deleteJob("x")).toBeUndefined();
  });

  it("turns error responses into SlowcookerApiError with the server's message", async () => {
    const api = createClient({ fetch: fakeFetch(409, { error: "Job 'x' is processing and cannot be requeued until it finishes." }).fetch });

    const error = await api.requeue("x").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(SlowcookerApiError);
    expect(error).toMatchObject({ status: 409, message: "Job 'x' is processing and cannot be requeued until it finishes." });
    expect((error as SlowcookerApiError).url).toContain("/slowcooker/api/jobs/x/requeue");
  });

  it("still fails usefully when the error body is not json", async () => {
    const fetch = vi.fn(async () => new Response("<html>gateway</html>", { status: 502, statusText: "Bad Gateway" })) as unknown as typeof globalThis.fetch;
    const error = await createClient({ fetch }).stats().catch((e: unknown) => e);
    expect(error).toMatchObject({ status: 502, message: "502 Bad Gateway" });
  });
});
