import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { fileURLToPath } from "node:url";

// The demo dashboard is served by the sample ASP.NET app under /dashboard/.
// `npm run dev` proxies API calls to the sample running on http://localhost:5000.
export default defineConfig({
  plugins: [vue()],
  base: "/dashboard/",
  resolve: {
    alias: { "@kit": fileURLToPath(new URL("./src/kit", import.meta.url)) },
  },
  build: {
    outDir: fileURLToPath(new URL("../samples/Slowcooker.Samples.WebApi/wwwroot/dashboard", import.meta.url)),
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: { "/slowcooker": "http://localhost:5000" },
  },
});
