import { createRouter, createWebHistory } from "vue-router";
import { basePath } from "./client";
import JobsPage from "./pages/JobsPage.vue";
import OverviewPage from "./pages/OverviewPage.vue";
import RecurringPage from "./pages/RecurringPage.vue";

export const router = createRouter({
  history: createWebHistory(basePath),
  routes: [
    { path: "/", name: "overview", component: OverviewPage },
    { path: "/jobs/:id?", name: "jobs", component: JobsPage, props: true },
    { path: "/recurring/:id?", name: "recurring", component: RecurringPage, props: true },
    { path: "/:pathMatch(.*)*", redirect: "/" },
  ],
});
