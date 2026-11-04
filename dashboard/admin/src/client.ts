import { createClient } from "@slowcooker/vue";

// AddAdminUserInterface() serves this page with <base href="{mount}/"> and the API at {mount}/api, so the dashboard
// works at any mount path, behind any path base and from any deep link. Without a <base> (the Vite dev server) the
// app is at "/" and /api is proxied. Browser cookies travel with the requests, so no auth header is needed here.
const baseHref = document.querySelector("base")?.href ?? new URL("/", document.baseURI).href;

/** The mount path of the dashboard, e.g. "/slowcooker/". Used as the router base. */
export const basePath = new URL(baseHref).pathname;

export const api = createClient({ baseUrl: new URL("api", baseHref).pathname });
