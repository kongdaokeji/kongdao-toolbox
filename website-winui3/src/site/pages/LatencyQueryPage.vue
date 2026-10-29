<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner lat-page">
      <header class="site-page-header">
        <h1>{{ t('latency.title') }}</h1>
        <p>{{ t('latency.subtitle') }}</p>
      </header>

      <!-- 控制栏 -->
      <div class="site-card lat-toolbar">
        <WinAutoSuggestBox
          v-model:Text="query"
          :PlaceholderText="t('latency.search-placeholder')"
          QueryIcon="Find"
          class="lat-search-box" />
        <WinButton @Click="refresh">
          <span style="display:flex;align-items:center;gap:6px">
            <span class="icon" aria-hidden="true">&#xE72C;</span>
            <span>{{ t('latency.refresh') }}</span>
          </span>
        </WinButton>
        <span class="lat-status" :class="{ 'lat-status-error': error }">
          {{ statusText }}
        </span>
      </div>

      <!-- 加载 / 错误状态 -->
      <div v-if="loading" class="lat-state">
        <WinProgressRing Width="40" Height="40" IsActive="True" />
        <p class="lat-state-text">{{ t('latency.loading') }}</p>
      </div>
      <div v-else-if="error" class="lat-state">
        <p class="lat-state-error">{{ error }}</p>
        <WinButton Style="AccentButtonStyle" :Content="t('latency.retry')" @Click="refresh" />
      </div>

      <!-- 图片网格 -->
      <template v-else>
        <p v-if="filtered.length === 0" class="lat-empty">{{ t('latency.empty') }}</p>
        <div v-else class="lat-grid">
          <div
            v-for="(img, i) in filtered"
            :key="img.name"
            class="lat-card"
            role="button"
            tabindex="0"
            :style="{ '--card-index': i }"
            @click="openImage(img)"
            @keydown.enter.prevent="openImage(img)"
            @keydown.space.prevent="openImage(img)">
            <span class="lat-card-title">{{ parse(img.name).cpu || img.name }}</span>
            <span class="lat-card-author">{{ parse(img.name).author ? '@' + parse(img.name).author : '' }}</span>
            <span class="lat-card-hint">
              <span class="icon" aria-hidden="true">&#xE91B;</span>{{ t('latency.view') }}
            </span>
          </div>
        </div>
        <p class="lat-count">{{ filtered.length }} / {{ all.length }}</p>
      </template>

      <SiteFooter />
    </div>
  </WinScrollViewer>

  <!-- 查看热力图弹层 -->
  <WinContentDialog
    v-model:IsOpen="dialogOpen"
    :Title="t('latency.dialog-title')"
    :CloseButtonText="t('latency.close')">
    <div v-if="activeImage" class="lat-dialog-content">
      <div class="lat-dialog-meta">
        <span class="lat-dialog-cpu">{{ parse(activeImage.name).cpu || activeImage.name }}</span>
        <span v-if="parse(activeImage.name).author" class="lat-dialog-author">
          @{{ parse(activeImage.name).author }}
        </span>
      </div>
      <div class="lat-lightbox-img-wrap">
        <img
          :src="imgSrc"
          :alt="activeImage.name"
          class="lat-lightbox-img"
          @error="onImageError" />
      </div>
    </div>
  </WinContentDialog>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import WinScrollViewer from '../../components/WinScrollViewer.vue'
import WinAutoSuggestBox from '../../components/WinAutoSuggestBox.vue'
import WinButton from '../../components/WinButton.vue'
import WinContentDialog from '../../components/WinContentDialog.vue'
import WinProgressRing from '../../components/WinProgressRing.vue'
import SiteFooter from '../components/SiteFooter.vue'
import { useI18n } from '../../components/i18n/index'
import {
  getLatencyImages,
  latencyImageProxyUrls,
  latencyImageUrl,
  parseLatencyName,
  type LatencyImageInfo
} from '../services/benchmarkData'

const { t } = useI18n()

const all = ref<LatencyImageInfo[]>([])
const query = ref('')
const loading = ref(true)
const error = ref('')
const activeImage = ref<LatencyImageInfo | null>(null)
const dialogOpen = computed({
  get: () => activeImage.value !== null,
  set: (v) => { if (!v) activeImage.value = null }
})

const parse = (name: string) => parseLatencyName(name)

/* 热力图直连失败时的镜像回退（与桌面版一致，逐级尝试镜像代理） */
const imageFallbackIndex = ref(0)
const imageFallbackUrls = ref<string[]>([])

function openImage(img: LatencyImageInfo) {
  activeImage.value = img
  imageFallbackIndex.value = 0
  imageFallbackUrls.value = latencyImageProxyUrls(img)
}

const imgSrc = computed(() => {
  if (!activeImage.value) return ''
  const maxAttempts = 1 + imageFallbackUrls.value.length
  const idx = imageFallbackIndex.value
  if (idx >= maxAttempts) return '' // 所有源都失败，停止重试
  if (idx > 0 && idx <= imageFallbackUrls.value.length) {
    return imageFallbackUrls.value[idx - 1]
  }
  return latencyImageUrl(activeImage.value)
})

function onImageError() {
  if (!activeImage.value) return
  const maxAttempts = 1 + imageFallbackUrls.value.length
  if (imageFallbackIndex.value < maxAttempts) {
    imageFallbackIndex.value += 1
  }
}

const filtered = computed(() => {
  const q = query.value.toLowerCase()
  if (!q) return all.value
  return all.value.filter((img) => img.name.toLowerCase().includes(q))
})

const statusText = computed(() => {
  if (error.value) return error.value
  if (all.value.length > 0) return `${all.value.length} ${t('latency.total')}`
  return ''
})

async function refresh() {
  loading.value = true
  error.value = ''
  try {
    all.value = await getLatencyImages(true)
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  loading.value = true
  error.value = ''
  try {
    all.value = await getLatencyImages(false)
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.lat-page {
  padding-bottom: 32px;
}

.lat-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  margin-bottom: 16px;
}

.lat-search-box {
  flex: 1 1 220px;
  min-width: 180px;
  max-width: 260px;
}

.lat-status {
  margin-left: auto;
  font-size: 12.5px;
  color: var(--text-tertiary);
  white-space: nowrap;
}

.lat-status-error {
  color: var(--SystemFillColorCriticalBrush, #c42b1c);
}

.lat-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 72px 0;
  color: var(--text-secondary);
}

.lat-state-text {
  margin: 0;
  font-size: 13.5px;
}

.lat-state-error {
  margin: 0;
  font-size: 13.5px;
  color: var(--SystemFillColorCriticalBrush, #c42b1c);
  max-width: 520px;
  text-align: center;
  word-break: break-all;
}

.lat-empty {
  padding: 48px 0;
  text-align: center;
  font-size: 14px;
  color: var(--text-secondary);
}

.lat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 12px;
}

/* 热力图卡片错峰入场（Fluent ControlFastOutSlowIn） */
.lat-card {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 14px;
  text-align: left;
  background: var(--card-bg);
  border: 1px solid var(--card-stroke);
  border-radius: 8px;
  cursor: pointer;
  transition: border-color var(--fast-duration, 0.167s) var(--fast-out-slow-in, ease),
              box-shadow   var(--fast-duration, 0.167s) var(--fast-out-slow-in, ease);
  color: var(--text-primary);
  animation: lat-card-in 0.45s cubic-bezier(0.1, 0.9, 0.2, 1) both;
  animation-delay: calc(min(var(--card-index), 50) * 0.012s);
}

@keyframes lat-card-in {
  from {
    opacity: 0;
    transform: translateY(10px) scale(0.97);
  }
}

.lat-card:hover {
  border-color: var(--accent-base);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12), 0 0 0 1px var(--card-stroke);
}

.lat-card-title {
  font-size: 14px;
  font-weight: 600;
  line-height: 19px;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.lat-card-author {
  font-size: 12px;
  color: var(--text-secondary);
}

.lat-card-hint {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-top: 4px;
  font-size: 11.5px;
  color: var(--text-tertiary);
}

.lat-card-hint .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 12px;
}

.lat-count {
  margin: 16px 0 0;
  text-align: center;
  font-size: 12.5px;
  color: var(--text-tertiary);
}

/* 弹层内图片区域 */
.lat-dialog-content {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.lat-dialog-meta {
  display: flex;
  align-items: baseline;
  gap: 10px;
}

.lat-dialog-cpu {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.lat-dialog-author {
  font-size: 12.5px;
  color: var(--text-secondary);
}

.lat-lightbox-img-wrap {
  overflow: auto;
  max-height: 60vh;
  border-radius: 6px;
  background:
    repeating-conic-gradient(var(--subtle-secondary) 0% 25%, transparent 0% 50%)
    0 0 / 20px 20px;
}

.lat-lightbox-img {
  display: block;
  max-width: 100%;
  height: auto;
  margin: 0 auto;
}

@media (prefers-reduced-motion: reduce) {
  .lat-card { animation: none; }
}
</style>
