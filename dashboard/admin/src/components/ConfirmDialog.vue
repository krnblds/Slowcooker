<script setup lang="ts">
import { nextTick, ref, watch } from "vue";
import { pending, settle } from "../confirm";

const dialog = ref<HTMLDialogElement>();
const confirmButton = ref<HTMLButtonElement>();

watch(
  () => pending.current,
  async (current) => {
    if (current) {
      dialog.value?.showModal();
      await nextTick();
      confirmButton.value?.focus();
    } else {
      dialog.value?.close();
    }
  },
);
</script>

<template>
  <dialog ref="dialog" class="dialog" @cancel.prevent="settle(false)" @click.self="settle(false)">
    <form v-if="pending.current" method="dialog" class="dialog__panel" @submit.prevent="settle(true)">
      <h2 class="dialog__title">{{ pending.current.title }}</h2>
      <p class="dialog__message">{{ pending.current.message }}</p>
      <div class="dialog__actions">
        <button type="button" class="sc-btn" @click="settle(false)">Cancel</button>
        <button ref="confirmButton" type="submit" class="sc-btn" :class="pending.current.danger ? 'sc-btn--danger' : 'sc-btn--primary'">
          {{ pending.current.confirmLabel ?? "Confirm" }}
        </button>
      </div>
    </form>
  </dialog>
</template>
