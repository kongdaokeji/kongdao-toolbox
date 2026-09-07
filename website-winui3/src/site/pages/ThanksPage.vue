<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="thanks-page">
      <!-- 头部：深蓝 + 1010 动态滚动背景 -->
      <header class="thanks-hero">
        <div class="thanks-hero-inner">
          <span class="thanks-hero-badge">
            <span class="icon" aria-hidden="true">&#xE73D;</span>
            {{ t('thanks.subtitle') }}
          </span>
          <h1 class="thanks-hero-title">{{ t('thanks.heading') }}</h1>
          <p class="thanks-hero-desc">{{ t('thanks.desc') }}</p>
          <WinInfoBar
            IsOpen
            :IsClosable="false"
            Severity="Success"
            :Title="t('thanks.download-started')"
            :Message="t('thanks.download-started-desc')"
            MaxWidth="560"
            HorizontalAlignment="Left" />
        </div>
      </header>

      <!-- 下载信息卡片 -->
      <div class="thanks-body">
        <div class="thanks-card-stack">
          <WinSettingsCard
            :Header="t('download.version')"
            :Description="t('thanks.meta-version')"
            HeaderIcon="&#xE895;"
            :Height="68">
            <template #default>
              <WinTextBlock class="thanks-value" :Text="version || '—'" />
            </template>
          </WinSettingsCard>
          <WinSettingsCard
            :Header="t('download.sys-arch')"
            :Description="t('thanks.meta-arch')"
            HeaderIcon="&#xE950;"
            :Height="68">
            <template #default>
              <WinTextBlock class="thanks-value" :Text="arch || '—'" />
            </template>
          </WinSettingsCard>
          <WinSettingsCard
            :Header="t('thanks.type')"
            :Description="t('thanks.meta-type')"
            HeaderIcon="&#xE8B7;"
            :Height="68">
            <template #default>
              <WinTextBlock class="thanks-value" :Text="typeLabel" />
            </template>
          </WinSettingsCard>
          <WinSettingsCard
            :Header="t('thanks.source')"
            :Description="t('thanks.meta-source')"
            HeaderIcon="&#xE9B0;"
            :Height="68">
            <template #default>
              <WinTextBlock class="thanks-value" :Text="sourceLabel" />
            </template>
          </WinSettingsCard>

          <WinInfoBar
            IsOpen
            :IsClosable="false"
            Severity="Informational"
            :Title="t('thanks.note-title')"
            :Message="t('thanks.note')" />

          <div class="thanks-actions">
            <WinButton
              v-if="downloadUrl"
              Style="AccentButtonStyle"
              :Content="t('thanks.again')"
              Height="38"
              Padding="20,0"
              @Click="openUrl" />
            <WinButton
              :Content="t('thanks.back-home')"
              Height="38"
              Padding="16,0"
              @Click="goHome" />
          </div>
        </div>
      </div>
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import WinTextBlock from '../../components/WinTextBlock.vue';
import WinSettingsCard from '../../components/WinSettingsCard.vue';
import WinInfoBar from '../../components/WinInfoBar.vue';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const version = computed(() => String(route.query.version ?? ''));
const arch = computed(() => String(route.query.arch ?? ''));
const type = computed(() => String(route.query.type ?? ''));
const downloadUrl = computed(() => String(route.query.url ?? ''));

const typeLabel = computed(() => {
  if (type.value === 'setup') return t('download.setup');
  return t('download.portable');
});

const sourceLabel = computed(() => {
  const url = downloadUrl.value;
  if (!url) return '—';
  try {
    const host = new URL(url).host;
    if (host.includes('gitcode')) return 'GitCode';
    if (host.includes('github')) return 'GitHub';
    if (host.includes('quark')) return t('download.quark');
    if (host.includes('baidu')) return t('download.baidu');
    return host;
  } catch {
    return '—';
  }
});

const openUrl = () => {
  if (downloadUrl.value) window.open(downloadUrl.value, '_blank', 'noopener');
};

const goHome = () => router.push({ name: 'home' });
</script>

<style scoped>
.thanks-page {
  width: 100%;
  min-width: 0;
}

/* ---------- 头部：深蓝 + 1010 动态滚动背景（与首页 Hero 一致） ---------- */

@keyframes thanks-bg-scrolling {
  0% { background-position: 0px 196px; }
  100% { background-position: 0px 0px; }
}

.thanks-hero {
  position: relative;
  overflow: hidden;
  background-color: rgba(0, 90, 158, 0.95);
}

.thanks-hero::before {
  content: '';
  position: absolute;
  width: 2000%;
  height: 2000%;
  top: -1000%;
  left: -1000%;
  z-index: 0;
  background: url('../../assets/site/hero-tile.png') repeat 0 0;
  background-color: rgba(0, 90, 158, 0.95);
  overflow: hidden;
  transform: rotateX(15deg) rotateZ(-15deg) skewX(15deg);
  transform-style: preserve-3d;
  animation: thanks-bg-scrolling 20s infinite linear;
  pointer-events: none;
}

.thanks-hero-inner {
  position: relative;
  z-index: 1;
  max-width: 760px;
  margin: 0 auto;
  padding: 56px 36px 44px 36px;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 14px;
}

.thanks-hero-badge {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 4px 14px;
  border: 1px solid rgba(255, 255, 255, 0.35);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.12);
  color: #ffffff;
  font-size: 13px;
  font-weight: 600;
  line-height: 24px;
}

.thanks-hero-badge .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 14px;
}

.thanks-hero-title {
  margin: 0;
  font-size: 40px;
  font-weight: 700;
  line-height: 52px;
  letter-spacing: -0.5px;
  background: linear-gradient(to right bottom, rgb(255, 255, 255) 30%, rgba(255, 255, 255, 0.30)) text;
  -webkit-box-decoration-break: clone;
  -webkit-text-fill-color: transparent;
  text-wrap: balance;
}

.thanks-hero-desc {
  margin: 0 0 4px 0;
  font-size: 15px;
  line-height: 22px;
  color: rgba(255, 255, 255, 0.82);
  max-width: 560px;
}

/* ---------- 主体：WinUI 控件卡片 ---------- */

.thanks-body {
  max-width: 760px;
  margin: 0 auto;
  padding: 28px 36px 64px 36px;
  box-sizing: border-box;
}

.thanks-card-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.thanks-value {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.thanks-actions {
  display: flex;
  gap: 12px;
  margin-top: 8px;
  flex-wrap: wrap;
  justify-content: center;
}

@media (max-width: 640px) {
  .thanks-hero-inner,
  .thanks-body {
    padding-left: 20px;
    padding-right: 20px;
  }
}
</style>
