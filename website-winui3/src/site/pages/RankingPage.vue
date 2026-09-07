<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner rank-page">
      <header class="site-page-header">
        <h1>{{ t('rank.title') }}</h1>
        <p>{{ t('rank.subtitle') }}</p>
      </header>

      <WinInfoBar
        :IsOpen="true"
        Severity="Informational"
        :IsClosable="false"
        :Title="t('rank.info-title')"
        :Message="t('rank.info-message')" />

      <!-- 控制栏 -->
      <div class="site-card rank-toolbar">
        <WinComboBox
          v-model:SelectedValue="sort"
          :ItemsSource="sortOptions"
          DisplayMemberPath="label"
          SelectedValuePath="value"
          MinWidth="120" />
        <WinAutoSuggestBox
          v-model:Text="query"
          :PlaceholderText="t('rank.search-placeholder')"
          QueryIcon="Find"
          class="rank-search-box" />
        <WinButton @Click="refresh">
          <span style="display:flex;align-items:center;gap:6px">
            <span class="icon" aria-hidden="true">&#xE72C;</span>
            <span>{{ t('rank.refresh') }}</span>
          </span>
        </WinButton>
        <span class="rank-status" :class="{ 'rank-status-error': error }">
          {{ statusText }}
        </span>
      </div>

      <!-- 加载 / 错误 -->
      <div v-if="loading" class="rank-state">
        <WinProgressRing Width="40" Height="40" IsActive="True" />
        <p class="rank-state-text">{{ t('rank.loading') }}</p>
      </div>
      <div v-else-if="error" class="rank-state">
        <p class="rank-state-error">{{ error }}</p>
        <WinButton Style="AccentButtonStyle" :Content="t('rank.retry')" @Click="refresh" />
      </div>

      <template v-else>
        <p v-if="rows.length === 0" class="rank-empty">{{ t('rank.empty') }}</p>

        <!-- 排行榜列表（全量渲染，逐行错峰入场） -->
        <div v-else class="site-card rank-list">
          <div
            v-for="(row, i) in rows"
            :key="row.entry.id"
            class="rank-row"
            role="button"
            tabindex="0"
            :style="{ '--row-index': i }"
            @click="openDetail(row.entry)"
            @keydown.enter.prevent="openDetail(row.entry)"
            @keydown.space.prevent="openDetail(row.entry)">
            <div class="rank-medal" :class="medalClass(row.rank)">{{ row.rank }}</div>
            <div class="rank-main">
              <div class="rank-author">@{{ row.entry.author }}</div>
              <div class="rank-hw">
                <span>{{ row.entry.cpuName }}</span>
                <span v-if="row.entry.gpuName" class="rank-sep">·</span>
                <span>{{ row.entry.gpuName }}</span>
              </div>
              <!-- 单项分数子行（与桌面版一致：CPU多核 / GPU / 内存 / 硬盘） -->
              <div class="rank-scores-bar">
                <span class="rank-score-chip">
                  <span class="rank-score-chip-label">CPU</span>
                  <span class="rank-score-chip-value">{{ row.entry.cpuMultiCoreScore }}</span>
                </span>
                <span class="rank-score-chip">
                  <span class="rank-score-chip-label">GPU</span>
                  <span class="rank-score-chip-value">{{ row.entry.gpuRenderScore }}</span>
                </span>
                <span class="rank-score-chip">
                  <span class="rank-score-chip-label">内存</span>
                  <span class="rank-score-chip-value">{{ row.entry.memoryCapacityScore }}</span>
                </span>
                <span class="rank-score-chip">
                  <span class="rank-score-chip-label">硬盘</span>
                  <span class="rank-score-chip-value">{{ row.entry.diskSeqReadScore }}</span>
                </span>
                <span class="rank-score-chip">
                  <span class="rank-score-chip-label">浏览器</span>
                  <span class="rank-score-chip-value">{{ row.entry.browserTotalScore }}</span>
                </span>
              </div>
            </div>
            <div class="rank-score">
              <div class="rank-score-stack">
                <div class="rank-score-num">{{ row.entry.gamingScore }}</div>
                <div class="rank-score-label">{{ t('rank.game') }}</div>
                <div class="rank-grade" :class="'rank-grade-' + (row.entry.gamingGrade || 'd').toLowerCase()">
                  {{ row.entry.gamingGrade || '—' }}
                </div>
              </div>
              <div class="rank-score-stack">
                <div class="rank-score-num">{{ row.entry.officeScore }}</div>
                <div class="rank-score-label">{{ t('rank.office') }}</div>
                <div class="rank-grade" :class="'rank-grade-' + (row.entry.officeGrade || 'd').toLowerCase()">
                  {{ row.entry.officeGrade || '—' }}
                </div>
              </div>
            </div>
          </div>
        </div>
      </template>

      <SiteFooter />
    </div>
  </WinScrollViewer>

  <!-- 报告详情弹层 -->
  <WinContentDialog
    v-model:IsOpen="dialogOpen"
    :Title="'@' + (active?.author ?? '') + ' ' + t('rank.report-title')"
    :CloseButtonText="t('rank.close')">
    <div v-if="active" class="rank-detail">
      <div class="rank-detail-grid">
        <span class="rank-detail-label">{{ t('rank.detail.cpu') }}</span>
        <span class="rank-detail-value">{{ active.cpuName || '—' }}</span>
        <span class="rank-detail-label">{{ t('rank.detail.gpu') }}</span>
        <span class="rank-detail-value">{{ active.gpuName || '—' }}</span>
        <span class="rank-detail-label">{{ t('rank.detail.motherboard') }}</span>
        <span class="rank-detail-value">{{ active.motherboardName || '—' }}</span>
        <span class="rank-detail-label">{{ t('rank.detail.memory') }}</span>
        <span class="rank-detail-value">{{ active.memoryInfo || '—' }}</span>
        <span class="rank-detail-label">{{ t('rank.detail.disk') }}</span>
        <span class="rank-detail-value">{{ active.diskInfo || '—' }}</span>
        <span class="rank-detail-label">{{ t('rank.detail.os') }}</span>
        <span class="rank-detail-value">{{ active.osName || '—' }}</span>
      </div>
      <div class="rank-detail-scores">
        <div class="rank-detail-score">
          <div class="rank-score-num">{{ active.gamingScore }}</div>
          <div class="rank-score-label">{{ t('rank.game') }} ({{ active.gamingGrade || '—' }})</div>
        </div>
        <div class="rank-detail-score">
          <div class="rank-score-num">{{ active.officeScore }}</div>
          <div class="rank-score-label">{{ t('rank.office') }} ({{ active.officeGrade || '—' }})</div>
        </div>
      </div>
      <table class="rank-detail-table">
        <tbody>
          <tr>
            <td>{{ t('rank.detail.cpu-single') }}</td>
            <td>{{ active.cpuSingleCoreScore }}</td>
            <td>{{ t('rank.detail.cpu-multi') }}</td>
            <td>{{ active.cpuMultiCoreScore }}</td>
          </tr>
          <tr>
            <td>{{ t('rank.detail.gpu-render') }}</td>
            <td>{{ active.gpuRenderScore }}</td>
            <td>{{ t('rank.detail.memory-cap') }}</td>
            <td>{{ active.memoryCapacityScore }}</td>
          </tr>
          <tr>
            <td>{{ t('rank.detail.disk-read') }}</td>
            <td>{{ active.diskSeqReadScore }}</td>
            <td>{{ t('rank.detail.disk-write') }}</td>
            <td>{{ active.diskSeqWriteScore }}</td>
          </tr>
          <tr>
            <td>{{ t('rank.detail.disk-4k-read') }}</td>
            <td>{{ active.disk4KReadScore }}</td>
            <td>{{ t('rank.detail.disk-4k-write') }}</td>
            <td>{{ active.disk4KWriteScore }}</td>
          </tr>
          <tr>
            <td>{{ t('rank.detail.browser') }}</td>
            <td colspan="3">{{ active.browserTotalScore }}</td>
          </tr>
        </tbody>
      </table>
      <p class="rank-detail-time">{{ t('rank.detail.submitted') }} {{ formatTime(active.submittedAt) }}</p>
    </div>
  </WinContentDialog>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, watch } from 'vue'
import WinScrollViewer from '../../components/WinScrollViewer.vue'
import WinComboBox from '../../components/WinComboBox.vue'
import WinAutoSuggestBox from '../../components/WinAutoSuggestBox.vue'
import WinButton from '../../components/WinButton.vue'
import WinContentDialog from '../../components/WinContentDialog.vue'
import WinProgressRing from '../../components/WinProgressRing.vue'
import WinInfoBar from '../../components/WinInfoBar.vue'
import SiteFooter from '../components/SiteFooter.vue'
import { useI18n } from '../../components/i18n/index'
import {
  SORT_META,
  buildLeaderboard,
  getLeaderboardData,
  invalidateBenchmarkCache,
  type LeaderboardData,
  type LeaderboardRankEntry,
  type SortKey
} from '../services/benchmarkData'

const { t } = useI18n()

const sortOptions = computed(() =>
  SORT_META.map(m => ({ value: m.key, label: t(m.labelKey) }))
)

const sort = ref<SortKey>('gaming')
const query = ref('')
const rows = ref<{ rank: number; entry: LeaderboardRankEntry }[]>([])
const loading = ref(true)
const error = ref('')
const active = ref<LeaderboardRankEntry | null>(null)
const dialogOpen = computed({
  get: () => active.value !== null,
  set: (v) => { if (!v) active.value = null }
})

/* 记录最近一次拿到的完整数据，切换排序/筛选时本地重算，不产生额外请求 */
const memoryLeaderboard = ref<LeaderboardData | null>(null)

const statusText = computed(() => {
  if (error.value) return error.value
  if (rows.value.length > 0) return `${rows.value.length} ${t('rank.entries')}`
  return ''
})

function medalClass(rank: number): string {
  if (rank === 1) return 'rank-medal-1'
  if (rank === 2) return 'rank-medal-2'
  if (rank === 3) return 'rank-medal-3'
  return ''
}

function formatTime(value: string | undefined): string {
  if (!value) return '—'
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return value
  return d.toLocaleString()
}

function openDetail(entry: LeaderboardRankEntry) {
  active.value = entry
}

async function refresh() {
  loading.value = true
  error.value = ''
  try {
    invalidateBenchmarkCache()
    const data = await getLeaderboardData(true)
    memoryLeaderboard.value = data
    rows.value = buildLeaderboard(data, sort.value, query.value)
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

watch([sort, query], () => {
  if (!memoryLeaderboard.value) return
  rows.value = buildLeaderboard(memoryLeaderboard.value, sort.value, query.value)
})

onMounted(async () => {
  loading.value = true
  error.value = ''
  try {
    const data = await getLeaderboardData(false)
    memoryLeaderboard.value = data
    rows.value = buildLeaderboard(data, sort.value, query.value)
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.rank-page {
  padding-bottom: 32px;
}

.rank-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  margin-bottom: 16px;
}

.rank-search-box {
  flex: 1 1 220px;
  min-width: 180px;
  max-width: 260px;
}

.rank-status {
  margin-left: auto;
  font-size: 12.5px;
  color: var(--text-tertiary);
  white-space: nowrap;
}

.rank-status-error {
  color: var(--SystemFillColorCriticalBrush, #c42b1c);
}

.rank-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 72px 0;
  color: var(--text-secondary);
}

.rank-state-text {
  margin: 0;
  font-size: 13.5px;
}

.rank-state-error {
  margin: 0;
  font-size: 13.5px;
  color: var(--SystemFillColorCriticalBrush, #c42b1c);
  max-width: 520px;
  text-align: center;
  word-break: break-all;
}

.rank-empty {
  padding: 48px 0;
  text-align: center;
  font-size: 14px;
  color: var(--text-secondary);
}

.rank-list {
  overflow: hidden;
}

/* 排行条目错峰入场（Fluent ControlFastOutSlowIn） */
.rank-row {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 16px;
  cursor: pointer;
  border-bottom: 1px solid var(--stroke-divider);
  transition: background var(--fast-duration, 0.167s) var(--fast-out-slow-in, ease);
  animation: rank-row-in 0.45s cubic-bezier(0.1, 0.9, 0.2, 1) both;
  animation-delay: calc(min(var(--row-index), 50) * 0.015s);
}

@keyframes rank-row-in {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
}

.rank-row:last-child {
  border-bottom: 0;
}

.rank-row:hover {
  background: var(--subtle-secondary);
}

.rank-medal {
  flex: 0 0 auto;
  width: 40px;
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 15px;
  font-weight: 700;
  color: var(--text-secondary);
  background: var(--subtle-secondary);
  border-radius: 8px;
}

.rank-medal-1 {
  color: #8a5a00;
  background: linear-gradient(135deg, #ffe08a, #f5c84c);
  box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.06);
}

.rank-medal-2 {
  color: #3f4449;
  background: linear-gradient(135deg, #e6e9ec, #c6ccd2);
}

.rank-medal-3 {
  color: #6b3d16;
  background: linear-gradient(135deg, #f2c8a0, #d9a06b);
}

.rank-main {
  flex: 1 1 auto;
  min-width: 0;
}

.rank-author {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--text-primary);
}

.rank-hw {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  font-size: 12px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
}

.rank-sep {
  color: var(--text-tertiary);
}

.rank-sub {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 4px;
}

.rank-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 1px 8px;
  font-size: 11px;
  color: var(--text-tertiary);
  background: var(--subtle-secondary);
  border-radius: 999px;
  max-width: 320px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rank-chip .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 11px;
}

/* 单项分数子行 */
.rank-scores-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 4px;
}

.rank-score-chip {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  padding: 1px 7px;
  font-size: 10.5px;
  border-radius: 4px;
  background: var(--subtle-secondary);
}

.rank-score-chip-label {
  color: var(--text-tertiary);
  font-weight: 500;
}

.rank-score-chip-value {
  color: var(--text-secondary);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

/* 右侧总分区域（游戏 + 办公双列） */
.rank-score {
  flex: 0 0 auto;
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: flex-end;
}

.rank-score-stack {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0;
}

.rank-score-num {
  font-size: 20px;
  font-weight: 700;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
}

.rank-score-label {
  font-size: 11px;
  color: var(--text-tertiary);
}

.rank-grade {
  flex: 0 0 auto;
  min-width: 30px;
  padding: 1px 6px;
  font-size: 11.5px;
  font-weight: 700;
  text-align: center;
  border-radius: 4px;
}

.rank-grade-s {
  color: var(--SystemFillColorSuccessBrush, #0d7a3e);
  background: var(--SystemFillColorSuccessBackgroundBrush, rgba(13, 122, 62, 0.14));
}

.rank-grade-a {
  color: var(--SystemFillColorAttentionBrush, #0b5fa0);
  background: var(--SystemFillColorAttentionBackgroundBrush, rgba(11, 95, 160, 0.14));
}

.rank-grade-b {
  color: var(--SystemFillColorCautionBrush, #9a6b00);
  background: var(--SystemFillColorCautionBackgroundBrush, rgba(154, 107, 0, 0.14));
}

.rank-grade-c,
.rank-grade-d {
  color: var(--SystemFillColorCriticalBrush, #a13c2b);
  background: var(--SystemFillColorCriticalBackgroundBrush, rgba(161, 60, 43, 0.12));
}

/* 详情弹层内容 */
.rank-detail {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.rank-detail-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 6px 16px;
  font-size: 13px;
}

.rank-detail-label {
  color: var(--text-secondary);
  white-space: nowrap;
}

.rank-detail-value {
  color: var(--text-primary);
  word-break: break-all;
}

.rank-detail-scores {
  display: flex;
  gap: 24px;
}

.rank-detail-score {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.rank-detail-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.rank-detail-table td {
  padding: 7px 10px;
  border-bottom: 1px solid var(--stroke-divider);
  color: var(--text-primary);
}

.rank-detail-table td:nth-child(odd) {
  color: var(--text-secondary);
  width: 30%;
  background: var(--subtle-secondary);
}

.rank-detail-time {
  margin: 0;
  font-size: 12px;
  color: var(--text-tertiary);
}

@media (max-width: 640px) {
  .rank-row {
    flex-wrap: wrap;
  }
  .rank-score {
    min-width: 0;
    width: 100%;
    justify-content: flex-start;
  }
}

@media (prefers-reduced-motion: reduce) {
  .rank-row { animation: none; }
}
</style>
