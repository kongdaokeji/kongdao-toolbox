<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner">
      <header class="site-page-header">
        <h1>{{ t('why.title') }}</h1>
        <p>{{ t('why.subtitle') }}</p>
      </header>

      <!-- 1. 下载方式对比 -->
      <section class="why-section">
        <div class="why-section-header">
          <span class="why-section-icon" aria-hidden="true">&#xE896;</span>
          <div>
            <h2 class="site-section-title">{{ t('why.dl.title') }}</h2>
            <p class="site-section-subtitle">{{ t('why.dl.subtitle') }}</p>
          </div>
        </div>
        <div class="why-compare-grid">
          <div class="why-card why-card-win">
            <div class="why-card-label">
              <span class="why-badge why-badge-win">WinUI3</span>
              <span class="why-card-verdict">&#xE73D; {{ t('why.winner') }}</span>
            </div>
            <ul class="site-check-list">
              <li v-for="item in dlWinItems" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img src="/comparison/winui3-download.png" alt="图吧工具箱WinUI3下载方式" loading="lazy" />
            </div>
          </div>
          <div class="why-card why-card-legacy">
            <div class="why-card-label">
              <span class="why-badge why-badge-legacy">{{ t('why.legacy') }}</span>
            </div>
            <ul class="site-check-list why-cross-list">
              <li v-for="item in dlLegacyItems" :key="item">
                <span class="why-cross-glyph" aria-hidden="true">&#xE711;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img src="/comparison/legacy-download.png" alt="原版图吧工具箱下载方式" loading="lazy" />
            </div>
          </div>
        </div>
      </section>

      <!-- 2. 软件界面对比 -->
      <section class="why-section">
        <div class="why-section-header">
          <span class="why-section-icon" aria-hidden="true">&#xE7F4;</span>
          <div>
            <h2 class="site-section-title">{{ t('why.ui.title') }}</h2>
            <p class="site-section-subtitle">{{ t('why.ui.subtitle') }}</p>
          </div>
        </div>
        <div class="why-compare-grid">
          <div class="why-card why-card-win">
            <div class="why-card-label">
              <span class="why-badge why-badge-win">WinUI3</span>
              <span class="why-card-verdict">&#xE73D; {{ t('why.winner') }}</span>
            </div>
            <ul class="site-check-list">
              <li v-for="item in uiWinItems" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img :src="screenshotTools" alt="图吧工具箱WinUI3界面" loading="lazy" />
            </div>
          </div>
          <div class="why-card why-card-legacy">
            <div class="why-card-label">
              <span class="why-badge why-badge-legacy">{{ t('why.legacy') }}</span>
            </div>
            <ul class="site-check-list why-cross-list">
              <li v-for="item in uiLegacyItems" :key="item">
                <span class="why-cross-glyph" aria-hidden="true">&#xE711;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img src="/comparison/legacy-ui.png" alt="原版图吧工具箱界面" loading="lazy" />
            </div>
          </div>
        </div>
      </section>

      <!-- 3. UTF-8 支持 -->
      <section class="why-section">
        <div class="why-section-header">
          <span class="why-section-icon" aria-hidden="true">&#xE8D4;</span>
          <div>
            <h2 class="site-section-title">{{ t('why.utf8.title') }}</h2>
            <p class="site-section-subtitle">{{ t('why.utf8.subtitle') }}</p>
          </div>
        </div>
        <div class="why-compare-grid">
          <div class="why-card why-card-win">
            <div class="why-card-label">
              <span class="why-badge why-badge-win">WinUI3</span>
              <span class="why-card-verdict">&#xE73D; {{ t('why.winner') }}</span>
            </div>
            <ul class="site-check-list">
              <li v-for="item in utf8WinItems" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
          </div>
          <div class="why-card why-card-legacy">
            <div class="why-card-label">
              <span class="why-badge why-badge-legacy">{{ t('why.legacy') }}</span>
            </div>
            <ul class="site-check-list why-cross-list">
              <li v-for="item in utf8LegacyItems" :key="item">
                <span class="why-cross-glyph" aria-hidden="true">&#xE711;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img src="/comparison/legacy-utf8.png" alt="原版工具箱UTF-8异常" loading="lazy" />
            </div>
          </div>
        </div>
      </section>

      <!-- 4. 内置工具 -->
      <section class="why-section">
        <div class="why-section-header">
          <span class="why-section-icon" aria-hidden="true">&#xE8F1;</span>
          <div>
            <h2 class="site-section-title">{{ t('why.tools.title') }}</h2>
            <p class="site-section-subtitle">{{ t('why.tools.subtitle') }}</p>
          </div>
        </div>
        <div class="why-compare-grid">
          <div class="why-card why-card-win">
            <div class="why-card-label">
              <span class="why-badge why-badge-win">WinUI3</span>
              <span class="why-card-verdict">&#xE73D; {{ t('why.winner') }}</span>
            </div>
            <ul class="site-check-list">
              <li v-for="item in toolsWinItems" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img src="/comparison/winui3-builtin.png" alt="WinUI3内置工具" loading="lazy" />
            </div>
          </div>
          <div class="why-card why-card-legacy">
            <div class="why-card-label">
              <span class="why-badge why-badge-legacy">{{ t('why.legacy') }}</span>
            </div>
            <ul class="site-check-list why-cross-list">
              <li v-for="item in toolsLegacyItems" :key="item">
                <span class="why-cross-glyph" aria-hidden="true">&#xE711;</span>{{ item }}
              </li>
            </ul>
          </div>
        </div>
      </section>

      <!-- 5. AI 驱动 -->
      <section class="why-section">
        <div class="why-section-header">
          <span class="why-section-icon" aria-hidden="true">&#xEE57;</span>
          <div>
            <h2 class="site-section-title">{{ t('why.ai.title') }}</h2>
            <p class="site-section-subtitle">{{ t('why.ai.subtitle') }}</p>
          </div>
        </div>
        <div class="why-compare-grid">
          <div class="why-card why-card-win">
            <div class="why-card-label">
              <span class="why-badge why-badge-win">WinUI3</span>
              <span class="why-card-verdict">&#xE73D; {{ t('why.winner') }}</span>
            </div>
            <ul class="site-check-list">
              <li v-for="item in aiWinItems" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
            <div class="why-card-img">
              <img src="/comparison/winui3-ai.png" alt="AI智能体" loading="lazy" />
            </div>
          </div>
          <div class="why-card why-card-legacy">
            <div class="why-card-label">
              <span class="why-badge why-badge-legacy">{{ t('why.legacy') }}</span>
            </div>
            <ul class="site-check-list why-cross-list">
              <li v-for="item in aiLegacyItems" :key="item">
                <span class="why-cross-glyph" aria-hidden="true">&#xE711;</span>{{ item }}
              </li>
            </ul>
          </div>
        </div>
      </section>

      <!-- 总结 -->
      <section class="why-summary">
        <div class="site-card why-summary-card">
          <h3>{{ t('why.summary.title') }}</h3>
          <p>{{ t('why.summary.text') }}</p>
          <div class="why-summary-actions">
            <WinButton
              Style="AccentButtonStyle"
              :Content="t('why.summary.download')"
              Height="40"
              Padding="24,0"
              @Click="goDownload" />
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { computed, inject } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import SiteFooter from '../components/SiteFooter.vue';
import screenshotTools from '../../assets/site/screenshot-tools.png';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();
const navigate = inject('navigate', () => {});

const dlWinItems = computed(() => t('why.dl.winItems').split('|'));
const dlLegacyItems = computed(() => t('why.dl.legacyItems').split('|'));
const uiWinItems = computed(() => t('why.ui.winItems').split('|'));
const uiLegacyItems = computed(() => t('why.ui.legacyItems').split('|'));
const utf8WinItems = computed(() => t('why.utf8.winItems').split('|'));
const utf8LegacyItems = computed(() => t('why.utf8.legacyItems').split('|'));
const toolsWinItems = computed(() => t('why.tools.winItems').split('|'));
const toolsLegacyItems = computed(() => t('why.tools.legacyItems').split('|'));
const aiWinItems = computed(() => t('why.ai.winItems').split('|'));
const aiLegacyItems = computed(() => t('why.ai.legacyItems').split('|'));

const goDownload = () => navigate('download');
</script>

<style scoped>
/* ---------- section header with icon ---------- */
.why-section {
  margin-bottom: 40px;
}

.why-section-header {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 20px;
}

.why-section-icon {
  flex: 0 0 auto;
  width: 40px;
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: 'WinUIOnWebIcons';
  font-size: 22px;
  color: var(--accent-base);
  background: var(--subtle-secondary);
  border-radius: 10px;
  margin-top: 2px;
}

.why-section-header .site-section-title {
  margin-bottom: 2px;
}

.why-section-header .site-section-subtitle {
  margin-bottom: 0;
}

/* ---------- compare grid ---------- */
.why-compare-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.why-card {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  border-radius: var(--ControlCornerRadius, 8px);
  border: 1px solid var(--card-stroke);
  background: var(--card-bg);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.03), 0 4px 16px rgba(0, 0, 0, 0.04);
  opacity: 0;
  transform: translateY(20px);
  animation: why-fade-in 0.5s ease forwards;
}

.why-card-win {
  border-color: rgba(15, 123, 15, 0.25);
}

.why-card-legacy {
  border-color: var(--card-stroke);
}

@keyframes why-fade-in {
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.why-card:nth-child(1) { animation-delay: 0.05s; }
.why-card:nth-child(2) { animation-delay: 0.15s; }

/* ---------- card label ---------- */
.why-card-label {
  display: flex;
  align-items: center;
  gap: 10px;
}

.why-badge {
  display: inline-flex;
  align-items: center;
  padding: 3px 12px;
  border-radius: 999px;
  font-size: 13px;
  font-weight: 600;
  line-height: 20px;
}

.why-badge-win {
  background: rgba(15, 123, 15, 0.12);
  color: var(--SystemFillColorSuccessBrush, #0F7B0F);
  border: 1px solid rgba(15, 123, 15, 0.2);
}

.why-badge-legacy {
  background: var(--subtle-secondary);
  color: var(--text-secondary);
  border: 1px solid var(--stroke-divider);
}

.why-card-verdict {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  font-weight: 600;
  color: var(--SystemFillColorSuccessBrush, #0F7B0F);
  font-family: 'WinUIOnWebIcons', 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
}

/* ---------- check / cross lists ---------- */
.why-card :deep(.site-check-list li) {
  font-size: 14px;
  line-height: 22px;
}

.why-cross-list :deep(li) {
  color: var(--text-secondary) !important;
}

.why-cross-glyph {
  flex: 0 0 auto;
  width: 16px;
  height: 22px;
  font-size: 12px;
  line-height: 22px;
  font-family: 'WinUIOnWebIcons';
  color: var(--SystemFillColorCriticalBrush, #C42B1C);
}

/* ---------- card image ---------- */
.why-card-img {
  margin-top: auto;
  padding-top: 4px;
}

.why-card-img img {
  width: 100%;
  border-radius: 8px;
  border: 1px solid var(--stroke-divider);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
}

/* ---------- summary ---------- */
.why-summary {
  margin-top: 16px;
  margin-bottom: 8px;
}

.why-summary-card {
  padding: 36px 32px;
  text-align: center;
}

.why-summary-card h3 {
  margin: 0 0 12px 0;
  font-size: 24px;
  font-weight: 600;
  color: var(--text-primary);
}

.why-summary-card p {
  margin: 0 0 24px 0;
  font-size: 15px;
  line-height: 24px;
  color: var(--text-secondary);
  max-width: 560px;
  margin-left: auto;
  margin-right: auto;
}

.why-summary-actions {
  display: flex;
  justify-content: center;
  gap: 12px;
}

/* ---------- responsive ---------- */
@media (max-width: 900px) {
  .why-compare-grid {
    grid-template-columns: 1fr;
  }

  .why-section-header {
    gap: 12px;
  }
}

@media (max-width: 640px) {
  .why-card {
    padding: 18px 16px;
  }

  .why-summary-card {
    padding: 24px 20px;
  }
}
</style>
