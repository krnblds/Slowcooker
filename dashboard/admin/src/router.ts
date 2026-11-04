import { createRouter, createWebHistory } from "vue-router";
import { basePath } from "./client";
import CronPage from "./pages/CronPage.vue";
import JobsPage from "./pages/JobsPage.vue";
import OverviewPage from "./pages/OverviewPage.vue";
import RecurringPage from "./pages/RecurringPage.vue";

export const router = createRouter({
  history: createWebHistory(basePath),
  routes: [
    { path: "/", name: "overview", component: OverviewPage, meta: { title: "Overview" } },
    { path: "/jobs/:id?", name: "jobs", component: JobsPage, props: true, meta: { title: "Jobs" } },
    { path: "/recurring/:id?", name: "recurring", component: RecurringPage, props: true, meta: { title: "Recurring" } },
    { path: "/cron", name: "cron", component: CronPage, meta: { title: "Cron tester" } },
    { path: "/:pathMatch(.*)*", redirect: "/" },
  ],
});

// Move focus to the page on navigation so screen reader and keyboard users land on the new content.
router.afterEach((to, from) => {
  if (to.name !== from.name) requestAnimationFrame(() => document.getElementById("main")?.focus({ preventScroll: true }));
});
