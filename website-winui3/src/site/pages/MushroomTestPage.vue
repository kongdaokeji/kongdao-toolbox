<template>
  <div class="mush-page">
    <!-- 点击后开始：挂载 iframe 前不创建 WebGL 上下文，不渲染 -->
    <div
      v-if="!started"
      class="mush-gate"
      role="button"
      tabindex="0"
      :aria-label="t('mushroom.start')"
      @click="start"
      @keydown.enter.prevent="start"
      @keydown.space.prevent="start">
      <div class="mush-gate-card">
        <span class="mush-gate-icon" aria-hidden="true">&#xE9F5;</span>
        <h1 class="mush-gate-title">{{ t('mushroom.title') }}</h1>
        <span class="mush-gate-btn">{{ t('mushroom.start') }}</span>
      </div>
    </div>
    <iframe
      v-else
      class="mush-frame"
      :src="shaderUrl"
      :title="t('mushroom.title')"
      allow="fullscreen"
      loading="eager"
      frameborder="0"></iframe>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();

/* 与桌面版毒蘑菇测试完全相同的渲染页面（public/volume-shader/） */
const shaderUrl = `${import.meta.env.BASE_URL}volume-shader/index.html`;

const started = ref(false);
const start = () => {
  started.value = true;
};
</script>

<style scoped>
.mush-page {
  position: relative;
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  background: #000;
}

.mush-frame {
  display: block;
  width: 100%;
  height: 100%;
  border: 0;
  background: #000;
}

/* ---------- 点击开始遮罩（只盖内容区，保留顶栏导航） ---------- */
.mush-gate {
  position: absolute;
  inset: 0;
  z-index: 10;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #000;
  cursor: pointer;
  animation: mush-gate-in 0.25s ease;
}

@keyframes mush-gate-in {
  from {
    opacity: 0;
  }
}

.mush-gate-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 22px;
  padding: 44px 64px;
  background: rgba(32, 32, 32, 0.72);
  -webkit-backdrop-filter: blur(30px) saturate(140%);
  backdrop-filter: blur(30px) saturate(140%);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  box-shadow:
    0 12px 28px rgba(0, 0, 0, 0.55),
    0 0 0 1px rgba(255, 255, 255, 0.04);
}

.mush-gate-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 64px;
  height: 64px;
  border-radius: 50%;
  background: rgba(76, 194, 255, 0.12);
  font-family: 'WinUIOnWebIcons';
  font-size: 34px;
  color: #4cc2ff;
}

.mush-gate-title {
  margin: 0;
  font-size: 22px;
  font-weight: 600;
  letter-spacing: 1px;
  color: #ffffff;
}

.mush-gate-btn {
  padding: 10px 38px;
  border-radius: 8px;
  background: #4cc2ff;
  color: #0b0b0b;
  font-size: 14px;
  font-weight: 600;
  box-shadow: 0 4px 14px rgba(76, 194, 255, 0.35);
  transition: background 0.15s ease, transform 0.1s ease;
}

.mush-gate:hover .mush-gate-btn,
.mush-gate:focus-visible .mush-gate-btn {
  background: #6fd0ff;
}

.mush-gate:active .mush-gate-btn {
  transform: scale(0.97);
}

.mush-gate:focus-visible {
  outline: none;
}
</style>