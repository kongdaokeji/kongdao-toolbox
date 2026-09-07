# 图吧工具箱 WinUI3 官方网站

本目录是图吧工具箱（TubaWinUi3）的新版官方网站，基于 [WinUIonWeb](https://github.com/Furry-Xiyi/WinUIonWeb) 构建——一个把 WinUI 控件带到 Web 的 Vue 3 项目。整站界面全部使用 Web 版 WinUI 控件（标题栏、NavigationView、按钮、ComboBox、RadioButtons、InfoBar、进度环等），外观与 Windows 11 的 WinUI 3 应用一致，支持浅色 / 深色 / 跟随系统主题。

## 开发

```bash
npm install
npm run dev       # http://localhost:63179
```

## 构建

```bash
npm run build     # 类型检查 + Vite 构建，输出到 dist/
npm run preview   # 本地预览构建产物
```

> 路由采用 HTML5 history 模式，文档 URL 延续原官网格式（`/guide/x`、`/tools/x`、`/tutorials/x`、`/dev/x`）。
> 部署时需配置服务器 SPA fallback（所有路径回退到 `index.html`）。

## 目录结构

```
src/
├── site/                  # 本站应用（替换了上游的 gallery 演示）
│   ├── App.vue            # 应用壳：WinTitleBar + WinNavigationView + 路由
│   ├── router.ts          # 页面路由（hash 模式）
│   ├── Strings/           # 站点文案（zh-CN / en-US）
│   ├── components/        # 站点级组件（如 SiteFooter）
│   └── pages/
│       ├── HomePage.vue   # 首页：Hero + 特性 + 工具分类网格 + 支持 + 页脚
│       ├── DownloadPage.vue  # 下载页：架构检测 + 版本拉取 + 下载卡片
│       ├── DocsPage.vue   # 文档页：原 VitePress 文档渲染（路由 /guide /tools /tutorials /dev）
│       ├── ThanksPage.vue # 下载感谢页（/download/thanks）
│       └── AboutPage.vue  # 关于页：主题切换 + 导航栏位置 + 社区链接 + 许可声明
├── docs/                  # 文档源（自原 VitePress 站点迁移：guide/tools/tutorials/dev）
├── public/tutorials/      # 教程图片（路径与原站一致）
├── components/            # WinUIonWeb 的 Web 版 WinUI 控件库（勿改）
├── assets/                # 图标字体、站点截图
└── styles/                # WinUI 主题与动画样式表
```

## 页面内容

- **首页** — 项目简介、四大特性板块（开源免费 / 硬件检测 / WinUI 3 原生 / 内置工具）、82 款外部工具 + 20 款内置工具分类网格、支持与致谢。
- **下载页** — 自动检测访问者 CPU 架构并推荐对应版本，从 GitCode / GitHub 拉取最新 Release 版本号，提供便携版、安装包、网盘与 Microsoft Store 下载入口，附系统要求与隐私声明。
- **文档页** — 原 VitePress 文档站（guide / tools / tutorials / dev 共 45 篇）已整体迁移，支持分类侧边导航、代码块、表格、警告框、教程图片与文档间互链。
- **关于页** — 站点主题切换（跟随系统 / 浅色 / 深色）、导航栏位置（自动 / 左侧 / 顶部）、社区链接、GPL-3.0 许可声明。

## 声明

- 本站设计灵感来源于 [DevToys 官网](https://devtoys.app)（MIT 协议），并进行了本地化修改与适配。
- 界面控件基于 [WinUIonWeb](https://github.com/Furry-Xiyi/WinUIonWeb)（GPL-3.0），是对 Microsoft WinUI 的独立 Web 实现，与 Microsoft 无任何关联。
