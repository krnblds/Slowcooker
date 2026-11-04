import { createApp } from "vue";
import "@slowcooker/vue/styles.css";
import "./app.css";
import App from "./App.vue";
import { router } from "./router";
import { applyStoredTheme } from "./theme";

applyStoredTheme();
createApp(App).use(router).mount("#app");
