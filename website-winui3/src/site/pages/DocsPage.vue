<template>
  <div class="docs-page">
    <WinNavigationView
      class="docs-subnav"
      :SelectedItem="selectedDocItem"
      :MenuItems="docNavItems"
      PaneDisplayMode="Left"
      v-model:IsPaneOpen="subPaneOpen"
      :IsSettingsVisible="false"
      :IsPaneToggleButtonVisible="true"
      IsBackButtonVisible="Collapsed"
      :AlwaysShowHeader="false"
      :OpenPaneLength="252"
      @ItemInvoked="onDocNavInvoked">
      <WinScrollViewer class="docs-content-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
        <article class="docs-content">
          <div class="docs-content-header">
            <span class="docs-content-cat">{{ currentCatTitle }}</span>
            <h1>{{ currentDocTitle }}</h1>
          </div>
          <div class="docs-markdown" v-html="renderedHtml"></div>
        </article>
      </WinScrollViewer>
    </WinNavigationView>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { marked } from 'marked';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinNavigationView from '../../components/WinNavigationView.vue';
import { getDocRaw, rawDocs } from '../searchIndex';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const subPaneOpen = ref(localStorage.getItem('docs-pane-open') !== 'false');
watch(subPaneOpen, (open) => {
  localStorage.setItem('docs-pane-open', String(open));
});

const devTitles = {
  index: '开发者文档索引',
  'app-entry': '应用入口与启动流程',
  'main-window': '主窗口架构',
  'tool-catalog': '工具目录服务',
  'hardware-info': '硬件信息服务',
  'lite-monitor': '实时监控服务',
  'builtin-tools': '内置工具系统',
  models: '数据模型',
  'services-api': '服务层 API 参考',
  pages: '页面与导航'
};

const stripFrontmatter = (raw) => {
  const match = /^---\r?\n[\s\S]*?\r?\n---\r?\n?/.exec(raw);
  return match ? raw.slice(match[0].length) : raw;
};

const parseTitle = (raw, cat, file) => {
  const fm = /^---\r?\n([\s\S]*?)\r?\n---/.exec(raw);
  if (fm) {
    const titleMatch = /^title:\s*(.+)$/m.exec(fm[1]);
    if (titleMatch) {
      const title = titleMatch[1].trim();
      const short = title.split('——')[0].trim();
      if (short) return short;
    }
  }
  if (cat === 'dev') return devTitles[file] ?? file;
  return file;
};

const categories = [
  { id: 'guide', title: t('docs.cat-guide'), files: [] },
  { id: 'tools', title: t('docs.cat-tools'), files: [] },
  { id: 'tutorials', title: t('docs.cat-tutorials'), files: [] },
  { id: 'dev', title: t('docs.cat-dev'), files: [] }
];

for (const path in rawDocs) {
  const rel = path.replace('../docs/', '').replace(/\\/g, '/');
  const parts = rel.split('/');
  if (parts.length !== 2 || !parts[1].endsWith('.md')) continue;
  const cat = parts[0];
  const file = parts[1].slice(0, -3);
  const group = categories.find(c => c.id === cat);
  if (!group) continue;
  group.files.push({
    file,
    title: parseTitle(rawDocs[path], cat, file)
  });
}

const guideDefault = 'getting-started';
const categoryDefault = { guide: 'getting-started', tools: 'cpu', tutorials: 'aida64', dev: 'index' };

const currentCat = computed(() => {
  const cat = typeof route.params.cat === 'string' ? route.params.cat : 'guide';
  return categories.some(c => c.id === cat) ? cat : 'guide';
});

const currentFile = computed(() => {
  const cat = currentCat.value;
  const group = categories.find(c => c.id === cat);
  const fallback = categoryDefault[cat] ?? group.files[0]?.file ?? '';
  const file = typeof route.params.file === 'string' ? route.params.file : fallback;
  return group.files.some(f => f.file === file) ? file : fallback;
});

const currentCatTitle = computed(() => categories.find(c => c.id === currentCat.value)?.title ?? '');

const currentDocTitle = computed(() => {
  const group = categories.find(c => c.id === currentCat.value);
  return group?.files.find(f => f.file === currentFile.value)?.title ?? currentFile.value;
});

const rawContent = computed(() => getDocRaw(currentCat.value, currentFile.value));

const isCurrentDoc = (cat, file) => cat === currentCat.value && file === currentFile.value;

/* --- 原生 WinNavigationView 子导航：分类分组可折叠 --- */

const docNavItems = [
  ...categories.map(cat => ({
    Content: cat.title,
    Icon: '\uE8B7',
    Tag: `group-${cat.id}`,
    SelectsOnInvoked: false,
    MenuItems: cat.files.map(f => ({
      Tag: `${cat.id}/${f.file}`,
      cat: cat.id,
      file: f.file,
      Content: f.title,
      Icon: '\uE8A5'
    }))
  }))
];

const selectedDocItem = computed(() => {
  const tag = `${currentCat.value}/${currentFile.value}`;
  for (const group of docNavItems) {
    const item = group.MenuItems.find(entry => entry.Tag === tag);
    if (item) return item;
  }
  return null;
});

const onDocNavInvoked = ({ InvokedItemContainer }) => {
  const item = InvokedItemContainer;
  if (!item || !item.Tag || item.SelectsOnInvoked === false) return;
  goDoc(item.cat, item.file);
};

const goDoc = (cat, file) => {
  if (cat === currentCat.value && file === currentFile.value) return;
  router.push({ name: 'docs', params: { cat, file } });
};

const rewriteLinks = (md, cat) => {
  return md
    .replace(/\]\(\.\/([a-z0-9-]+)\.md\)/g, `](/${cat}/$1)`)
    .replace(/\]\(([a-z0-9-]+)\.md\)/g, `](/${cat}/$1)`)
    .replace(/\]\(\/(guide|dev|tools|tutorials)\/([a-z0-9-]+)\.md\)/g, ']($1/$2)');
};

const renderCallouts = (md) => {
  return md.replace(/::: (\w+)([^\n]*)\n([\s\S]*?)\n:::/g, (_, type, title, body) => {
    const heading = title.trim() ? `<div class="docs-callout-title">${title.trim()}</div>` : '';
    return `<div class="docs-callout docs-callout-${type.toLowerCase()}">${heading}<div class="docs-callout-body">${body.trim()}</div></div>`;
  });
};

const renderer = new marked.Renderer();
renderer.heading = function ({ tokens, depth, text }) {
  const id = text.toLowerCase().replace(/[^\w\u4e00-\u9fa5]+/g, '-').replace(/^-+|-+$/g, '');
  return `<h${depth} id="${id}">${this.parser.parseInline(tokens)}</h${depth}>`;
};

const renderedHtml = computed(() => {
  const raw = rawContent.value;
  if (!raw) return '<p class="docs-empty">文档未找到</p>';
  let md = stripFrontmatter(raw);
  md = rewriteLinks(md, currentCat.value);
  md = renderCallouts(md);
  return marked.parse(md, { gfm: true, renderer });
});

watch(route, () => {
  if (!route.params.file) {
    router.replace({ name: 'docs', params: { cat: currentCat.value, file: currentFile.value } });
  }
}, { immediate: true });

/* 文档页 SEO：标题 = 文档标题——图吧工具箱WinUI3文档 */
watch([currentDocTitle, currentCatTitle], ([title]) => {
  document.title = `${title}——图吧工具箱WinUI3文档`;
  const setMeta = (name, content) => {
    const el = document.head.querySelector(`meta[name="${name}"], meta[property="${name}"]`);
    if (el) el.setAttribute('content', content);
  };
  setMeta('description', `图吧工具箱WinUI3文档：${title}`);
  setMeta('og:title', `${title}——图吧工具箱WinUI3文档`);
  setMeta('og:url', `https://tubawinui3.cn/${currentCat.value}/${currentFile.value}`);
  const canonical = document.head.querySelector('link[rel="canonical"]');
  if (canonical) canonical.setAttribute('href', `https://tubawinui3.cn/${currentCat.value}/${currentFile.value}`);
}, { immediate: true });
</script>

<style scoped>
.docs-page {
  display: flex;
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
}

.docs-subnav {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
}

.docs-subnav :deep(.win-nav-left-panel) {
  border-right: 1px solid var(--stroke-divider);
}

.docs-subnav :deep(.win-nav-item-header) {
  padding-left: 16px;
}

.docs-subnav :deep(.win-nav-item-header .win-text-block) {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-secondary);
}

.docs-content-scroll {
  flex: 1 1 auto;
  min-width: 0;
}

.docs-content {
  max-width: 860px;
  margin: 0 auto;
  padding: 28px 48px 64px 48px;
  box-sizing: border-box;
}

.docs-content-header {
  margin-bottom: 20px;
}

.docs-content-cat {
  display: inline-block;
  margin-bottom: 6px;
  padding: 2px 10px;
  border-radius: 999px;
  background: var(--subtle-secondary);
  color: var(--accent-base);
  font-size: 12px;
  font-weight: 600;
}

.docs-content-header h1 {
  margin: 0;
  font-size: 26px;
  font-weight: 600;
  line-height: 36px;
  color: var(--text-primary);
}

.docs-markdown {
  color: var(--text-primary);
  font-size: 14.5px;
  line-height: 24px;
  word-break: break-word;
}

.docs-markdown :deep(h2) {
  margin: 28px 0 12px 0;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--stroke-divider);
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
}

.docs-markdown :deep(h3) {
  margin: 22px 0 10px 0;
  font-size: 16.5px;
  font-weight: 600;
  color: var(--text-primary);
}

.docs-markdown :deep(p) {
  margin: 10px 0;
}

.docs-markdown :deep(ul),
.docs-markdown :deep(ol) {
  margin: 10px 0;
  padding-left: 24px;
}

.docs-markdown :deep(li) {
  margin: 4px 0;
}

.docs-markdown :deep(a) {
  color: var(--accent-base);
  text-decoration: none;
}

.docs-markdown :deep(a:hover) {
  text-decoration: underline;
}

.docs-markdown :deep(strong) {
  font-weight: 600;
}

.docs-markdown :deep(blockquote) {
  margin: 12px 0;
  padding: 4px 16px;
  border-left: 3px solid var(--accent-base);
  background: var(--subtle-secondary);
  color: var(--text-secondary);
}

.docs-markdown :deep(blockquote p) {
  margin: 6px 0;
}

.docs-markdown :deep(code) {
  padding: 1px 6px;
  border-radius: 4px;
  background: var(--subtle-secondary);
  font-family: Consolas, 'Cascadia Code', monospace;
  font-size: 12.5px;
}

.docs-markdown :deep(pre) {
  margin: 12px 0;
  padding: 14px 16px;
  overflow-x: auto;
  border-radius: 8px;
  background: var(--card-bg-secondary);
  border: 1px solid var(--card-stroke);
}

.docs-markdown :deep(pre code) {
  padding: 0;
  background: transparent;
  font-size: 12.5px;
  line-height: 20px;
  color: var(--text-primary);
}

.docs-markdown :deep(table) {
  width: 100%;
  margin: 12px 0;
  border-collapse: collapse;
  font-size: 13.5px;
}

.docs-markdown :deep(th),
.docs-markdown :deep(td) {
  padding: 8px 12px;
  border: 1px solid var(--stroke-divider);
  text-align: left;
}

.docs-markdown :deep(th) {
  background: var(--subtle-secondary);
  font-weight: 600;
}

.docs-markdown :deep(img) {
  max-width: 100%;
  border-radius: 8px;
  border: 1px solid var(--card-stroke);
}

.docs-markdown :deep(hr) {
  border: 0;
  border-top: 1px solid var(--stroke-divider);
  margin: 20px 0;
}

.docs-callout {
  margin: 14px 0;
  padding: 12px 16px;
  border-radius: 8px;
  border: 1px solid var(--card-stroke);
  font-size: 13.5px;
  line-height: 20px;
}

.docs-callout-title {
  font-weight: 600;
  margin-bottom: 4px;
}

.docs-callout-body p {
  margin: 4px 0;
}

.docs-callout-warning {
  background: var(--SystemFillColorCautionBackgroundBrush, #FFF4CE);
  color: var(--SystemFillColorCautionBrush, #9D5D00);
}

.docs-callout-tip {
  background: var(--SystemFillColorSuccessBackgroundBrush, #DFF6DD);
  color: var(--SystemFillColorSuccessBrush, #0F7B0F);
}

.docs-callout-danger {
  background: var(--SystemFillColorCriticalBackgroundBrush, #FDE7E9);
  color: var(--SystemFillColorCriticalBrush, #C42B1C);
}

.docs-callout-info {
  background: var(--SystemFillColorAttentionBackgroundBrush, rgba(246, 246, 246, 0.5));
  color: var(--SystemFillColorAttentionBrush, #0067C0);
}

.docs-empty {
  color: var(--text-tertiary);
}

@media (max-width: 820px) {
  .docs-content {
    padding: 20px 24px 48px 24px;
  }
}
</style>
