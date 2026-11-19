<script setup lang="ts">
import { ArrowLeft, ArrowRight, Save, FileQuestion } from 'lucide-vue-next'
import AppLayout from '../layouts/AppLayout.vue'
import AppButton from './AppButton.vue'
import AppSkeleton from './AppSkeleton.vue'
import FormStepper, { type WizardStep } from './FormStepper.vue'

withDefaults(
  defineProps<{
    title: string
    backTo: string
    breadcrumbParent: string
    breadcrumbCurrent: string
    steps: WizardStep[]
    currentStep: number
    canNavigateTo?: (index: number) => boolean
    saveLabel?: string
    saving?: boolean
    disabled?: boolean
    loading?: boolean
    notFound?: boolean
    notFoundTitle?: string
    notFoundDescription?: string
  }>(),
  {
    canNavigateTo: () => true,
    saveLabel: 'Lưu',
    saving: false,
    disabled: false,
    loading: false,
    notFound: false,
    notFoundTitle: 'Không tìm thấy dữ liệu',
    notFoundDescription: 'Mục bạn tìm kiếm không tồn tại hoặc đã bị xoá.'
  }
)

const emit = defineEmits<{
  (e: 'cancel'): void
  (e: 'prev'): void
  (e: 'next'): void
  (e: 'save'): void
  (e: 'selectStep', index: number): void
}>()
</script>

<template>
  <AppLayout>
    <div class="flex-1 w-full flex flex-col min-h-0 gap-3 font-mono">
      <!-- Top Header (Fixed) -->
      <div class="shrink-0 flex items-center justify-between gap-4 flex-nowrap pb-2 border-b border-border">
        <div class="flex items-center gap-2.5 min-w-0">
          <router-link
            :to="backTo"
            aria-label="Quay lại danh sách"
            class="p-1.5 rounded-[2px] border border-border bg-bg-elevated text-fg-muted hover:text-fg-strong hover:bg-bg-hover transition-colors duration-[80ms] focus-visible:outline-2 focus-visible:outline-border-focus shrink-0"
          >
            <ArrowLeft class="w-4 h-4" aria-hidden="true" />
          </router-link>
          <div class="min-w-0">
            <nav aria-label="Breadcrumb" class="text-[11px] text-fg-muted flex items-center gap-1.5 whitespace-nowrap">
              <router-link :to="backTo" class="hover:text-fg-strong hover:underline">
                {{ breadcrumbParent }}
              </router-link>
              <span class="text-fg-dim" aria-hidden="true">/</span>
              <span class="text-fg-strong font-semibold truncate">{{ breadcrumbCurrent }}</span>
            </nav>
            <h1 class="text-[15px] font-bold tracking-tight text-fg-strong flex items-center gap-2 mt-0.5 truncate">
              <slot name="icon" />
              <span class="truncate">{{ title }}</span>
            </h1>
          </div>
        </div>

        <div class="shrink-0">
          <slot name="header-actions" />
        </div>
      </div>

      <!-- Main Wizard Shell (Full Width & Remaining Height) -->
      <div class="flex-1 min-h-0 flex flex-col rounded-[2px] border border-border bg-bg-elevated overflow-hidden">
        <FormStepper
          v-if="!loading && !notFound"
          :steps="steps"
          :current-step="currentStep"
          :can-navigate-to="canNavigateTo"
          @select="emit('selectStep', $event)"
        />

        <!-- Loading Skeleton -->
        <div v-if="loading" class="flex-1 min-h-0 overflow-y-auto p-4" aria-busy="true">
          <AppSkeleton variant="rows" :count="5" />
        </div>

        <!-- Not Found -->
        <div v-else-if="notFound" class="flex-1 min-h-0 overflow-y-auto p-6 flex items-center justify-center">
          <div class="text-center space-y-3">
            <div class="w-10 h-10 rounded-[2px] bg-bg-surface border border-border flex items-center justify-center text-fg-dim mx-auto">
              <FileQuestion class="w-5 h-5" aria-hidden="true" />
            </div>
            <div>
              <h2 class="text-[13px] font-bold text-fg-strong">{{ notFoundTitle }}</h2>
              <p class="prose text-[12px] text-fg-muted mt-1">{{ notFoundDescription }}</p>
            </div>
            <AppButton variant="secondary" @click="emit('cancel')">
              <ArrowLeft class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Quay lại danh sách</span>
            </AppButton>
          </div>
        </div>

        <!-- Step Content & Bottom Action Footer -->
        <template v-else>
          <div class="flex-1 min-h-0 overflow-y-auto p-4 flex flex-col">
            <slot />
          </div>

          <!-- Bottom Action Footer -->
          <div
            class="shrink-0 px-4 py-2 border-t border-border bg-bg-surface flex items-center justify-between gap-3 flex-nowrap"
          >
            <div class="flex items-center gap-2 shrink-0 whitespace-nowrap">
              <AppButton
                type="button"
                variant="ghost"
                :disabled="saving"
                @click="emit('cancel')"
              >
                Huỷ
              </AppButton>
              <AppButton
                v-if="currentStep > 0"
                type="button"
                variant="secondary"
                :disabled="saving"
                @click="emit('prev')"
              >
                <ArrowLeft class="w-3.5 h-3.5" aria-hidden="true" />
                <span>Quay lại</span>
              </AppButton>
            </div>

            <div class="flex items-center gap-2 shrink-0 whitespace-nowrap">
              <slot name="footer-extra" />

              <AppButton
                v-if="currentStep < steps.length - 1"
                type="button"
                variant="primary"
                :disabled="saving || disabled"
                @click="emit('next')"
              >
                <span>Tiếp tục</span>
                <ArrowRight class="w-3.5 h-3.5" aria-hidden="true" />
              </AppButton>

              <AppButton
                v-else
                type="button"
                variant="primary"
                :loading="saving"
                :disabled="saving || disabled"
                @click="emit('save')"
              >
                <Save class="w-3.5 h-3.5" aria-hidden="true" />
                <span>{{ saveLabel }}</span>
              </AppButton>
            </div>
          </div>
        </template>
      </div>
    </div>
  </AppLayout>
</template>
