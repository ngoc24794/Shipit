<script setup lang="ts">
import { onMounted } from 'vue'
import { useTheme } from './composables/useTheme'

const { initTheme } = useTheme()

onMounted(() => {
  initTheme()
})
</script>

<template>
  <router-view v-slot="{ Component, route }">
    <Transition name="d1-view" mode="out-in">
      <component :is="Component" :key="route.path" />
    </Transition>
  </router-view>
</template>

<style scoped>
.d1-view-enter-active,
.d1-view-leave-active {
  transition: opacity var(--dur-fast) var(--ease-snap), transform var(--dur-fast) var(--ease-snap);
}

.d1-view-enter-from {
  opacity: 0;
  transform: translateY(3px);
}

.d1-view-leave-to {
  opacity: 0;
}
</style>
