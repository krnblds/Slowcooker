import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { fileURLToPath } from "node:url";

// The demo dashboard is compiled into src/Slowcooker.AspNetCore/ui and embedded in that assembly, which serves it at
// whatever path AddAdminUserInterface() is configured with (default /slowcooker/). Assets are therefore referenced
// relatively (base "./") and the app finds its API at ./api.
// `npm run dev` serves it at http://localhost:5173/ and proxies /api to the sample running on http://localhost:5000.
export default defineConfig({
  plugins: [vue()],
  base: "./",
  resolve: {
    alias: { "@kit": fileURLToPath(new URL("./src/kit", import.meta.url)) },
  },
  build: {
    outDir: fileURLToPath(new URL("../src/Slowcooker.AspNetCore/ui", import.meta.url)),
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: { "/api": { target: "http://localhost:5000", rewrite: (path) => "/slowcooker" + path } },
  },
});
