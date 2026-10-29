/**
 * 社区跑分 / 核间延迟数据获取层
 *
 * 数据源与桌面版（TubaWinUi3 / BenchmarkCloudService）保持一致：
 *  - 排行榜：tubatoolsPlugin 仓库的 leaderboard.json（GitCode API → GitCode raw → GitHub raw 依次回退）
 *  - 核间延迟热力图：仓库 reports/latency-images 目录（GitCode contents API → GitHub contents API 依次回退）
 *
 * leaderboard.json 两种格式（桌面版同步支持）：
 *  - 新格式（rebuild-leaderboard workflow 产物）：`reports` 摘要平铺数组，每份报告只出现一次，
 *    各维度排序由客户端本地完成；硬件大字段（OS/主板/内存/硬盘/显示器）拆到
 *    `leaderboard/details/{id}.json`，打开详情时按 `detailsPath` 按需加载。
 *  - 旧格式（兼容）：`leaderboards` 预排好的各维度榜单，每条含全量字段。
 *
 * 请求节流策略（延续桌面版"尽量少请求 API"的传统）：
 *  - 排行榜整份数据只请求一次，SessionStorage + localStorage 双层缓存（默认 6 小时）
 *  - 热力图列表只请求一次，缓存 10 分钟
 *  - 页面内切换排序/筛选全部在本地内存完成，不产生任何额外请求
 *  - 手动刷新按钮主动清除缓存后重新请求
 */

export interface LeaderboardRankEntry {
  id: string
  author: string
  cpuName: string
  gpuName: string
  gamingScore: number
  officeScore: number
  cpuSingleCoreScore: number
  cpuMultiCoreScore: number
  gpuRenderScore: number
  memoryCapacityScore: number
  diskSeqReadScore: number
  diskSeqWriteScore: number
  disk4KReadScore: number
  disk4KWriteScore: number
  browserTotalScore: number
  gamingGrade: string
  officeGrade: string
  submittedAt: string
  /** 新格式：详情文件路径（leaderboard/details/{id}.json），硬件详情按需加载 */
  detailsPath?: string
  /** 硬件详情字段：新格式摘要不含，旧格式/详情文件才有 */
  osName?: string
  motherboardName?: string
  memoryInfo?: string
  diskInfo?: string
  displayInfo?: string
}

export interface LeaderboardData {
  updatedAt: string
  totalReports?: number
  /** 新格式：被榜单收录的报告总数（上限 2000） */
  totalListed?: number
  /** 新格式：摘要平铺数组（rebuild-leaderboard 产物，按综合榜降序） */
  reports?: LeaderboardRankEntry[]
  /** 旧格式（兼容）：预排好的各维度榜单，每条含全量字段 */
  leaderboards?: Record<string, LeaderboardRankEntry[]>
}

export interface LatencyImageInfo {
  name: string
  rawUrl: string
  htmlUrl: string
  sha: string
}

export type SortKey = 'gaming' | 'office' | 'cpu' | 'gpu' | 'disk' | 'browser'

export const SORT_META: { key: SortKey; labelKey: string; field: keyof LeaderboardRankEntry }[] = [
  { key: 'gaming', labelKey: 'rank.sort.gaming', field: 'gamingScore' },
  { key: 'office', labelKey: 'rank.sort.office', field: 'officeScore' },
  { key: 'cpu', labelKey: 'rank.sort.cpu', field: 'cpuMultiCoreScore' },
  { key: 'gpu', labelKey: 'rank.sort.gpu', field: 'gpuRenderScore' },
  { key: 'disk', labelKey: 'rank.sort.disk', field: 'diskSeqReadScore' },
  { key: 'browser', labelKey: 'rank.sort.browser', field: 'browserTotalScore' }
]

/* ---------------- 常量与回退源 ---------------- */

const OWNER = 'luolangaga'
const REPO = 'tubatoolsPlugin'

const GITCODE_API = `https://api.gitcode.com/api/v5/repos/${OWNER}/${REPO}/contents`
const GITHUB_API = `https://api.github.com/repos/${OWNER}/${REPO}/contents`
const GITCODE_RAW_BASE = 'https://raw.gitcode.com'
const GITHUB_RAW = `https://raw.githubusercontent.com/${OWNER}/${REPO}/main`

/** 排行榜整份数据缓存时长 */
const LEADERBOARD_TTL = 6 * 60 * 60 * 1000
/** 热力图列表缓存时长（与桌面版一致：10 分钟） */
const LATENCY_LIST_TTL = 10 * 60 * 1000

/** 懒加载热力图时对 raw.gitcode.com 直连的容错（CDN 偶尔超时） */
const GITHUB_RAW_MIRRORS: string[] = [
  'https://gh-proxy.com/',
  'https://ghproxy.net/',
  'https://gh.llkk.cc/'
]

/* ---------------- 通用工具 ---------------- */

const hasWindow = typeof window !== 'undefined'

function safeGetItem(storage: Storage | null, key: string): string | null {
  try {
    return storage ? storage.getItem(key) : null
  } catch {
    return null
  }
}

function safeSetItem(storage: Storage | null, key: string, value: string): void {
  try {
    storage?.setItem(key, value)
  } catch {
    /* 隐私模式/配额满时静默失败 */
  }
}

function safeRemoveItem(storage: Storage | null, key: string): void {
  try {
    storage?.removeItem(key)
  } catch {
    /* ignore */
  }
}

/** 兼容 GitCode/GitHub 的 contents API：直接解码内联 base64，避免依赖 CORS 不稳定的 raw 域名 */
function decodeContentsBase64(payload: unknown): string {
  if (typeof payload !== 'object' || payload === null) throw new Error('contents 响应格式错误')
  const p = payload as Record<string, unknown>
  const content = p.content
  if (typeof content !== 'string') throw new Error('contents 响应缺少 content 字段')
  const encoding = String(p.encoding ?? 'base64')
  if (encoding === 'base64') {
    const cleaned = content.replace(/\r?\n/g, '')
    const bytes = Uint8Array.from(atob(cleaned), (c) => c.charCodeAt(0))
    return new TextDecoder('utf-8').decode(bytes)
  }
  return content
}

/** 取 GitCode contents API 的内联 base64（优先），或 download_url 兜底 */
async function fetchGitCodeFileJson<T>(path: string): Promise<T> {
  const payload = await fetchJson<Record<string, unknown>>(`${GITCODE_API}/${path}`)
  try {
    return JSON.parse(decodeContentsBase64(payload)) as T
  } catch {
    // 内联 content 缺失/损坏时回退到 download_url（raw.gitcode.com）
    const downloadUrl = String(payload.download_url ?? '')
    if (!downloadUrl) throw new Error('GitCode 未返回可用的文件内容')
    return await fetchJson<T>(downloadUrl)
  }
}

interface CacheBox<T> {
  data: T
  time: number
}

function readCache<T>(key: string, ttl: number): T | null {
  if (!hasWindow) return null
  const raw = safeGetItem(sessionStorage, key) ?? safeGetItem(localStorage, key)
  if (!raw) return null
  try {
    const box = JSON.parse(raw) as CacheBox<T>
    if (Date.now() - box.time < ttl) return box.data
    // 过期：只清 session（跨会话的 localStorage 留作兜底，避免每次访问都重新请求）
    safeRemoveItem(sessionStorage, key)
  } catch {
    /* 损坏数据忽略 */
  }
  return null
}

function writeCache<T>(key: string, data: T): void {
  if (!hasWindow) return
  const box: CacheBox<T> = { data, time: Date.now() }
  const raw = JSON.stringify(box)
  safeSetItem(sessionStorage, key, raw)
  safeSetItem(localStorage, key, raw)
}

/** 记忆最近一次成功的获取，保证页面来回切换时零请求 */
const memoryCache = new Map<string, unknown>()

function clearMemoryCache(): void {
  memoryCache.clear()
}

async function fetchText(url: string, timeoutMs = 30000): Promise<string> {
  const ctrl = new AbortController()
  const timer = setTimeout(() => ctrl.abort(), timeoutMs)
  try {
    const resp = await fetch(url, {
      signal: ctrl.signal,
      headers: { Accept: '*/*' }
    })
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
    return await resp.text()
  } finally {
    clearTimeout(timer)
  }
}

async function fetchJson<T>(url: string, timeoutMs = 30000): Promise<T> {
  const text = await fetchText(url, timeoutMs)
  return JSON.parse(text) as T
}

/* ---------------- 排行榜数据 ---------------- */

const LEADERBOARD_KEY = 'tuba_benchmark_leaderboard_v1'

async function fetchLeaderboardFromGitCode(): Promise<LeaderboardData> {
  return await fetchGitCodeFileJson<LeaderboardData>('leaderboard.json')
}

async function fetchLeaderboardFromGitHub(): Promise<LeaderboardData> {
  // GitHub raw 带 Access-Control-Allow-Origin: *，可直接跨域
  return await fetchJson<LeaderboardData>(`${GITHUB_RAW}/leaderboard.json`)
}

/** 新格式（reports 摘要数组）与旧格式（leaderboards 预排榜单）都算有效 */
function isLeaderboardPayload(data: unknown): data is LeaderboardData {
  if (typeof data !== 'object' || data === null) return false
  const d = data as Record<string, unknown>
  if (Array.isArray(d.reports)) {
    const first = d.reports[0]
    return typeof first !== 'object' || first === null || typeof (first as Record<string, unknown>).id === 'string'
  }
  const boards = d.leaderboards
  return (
    typeof boards === 'object' && boards !== null && Array.isArray((boards as Record<string, unknown>).gaming)
  )
}

async function fetchLeaderboardFresh(): Promise<LeaderboardData> {
  let lastError: unknown
  for (const fetcher of [fetchLeaderboardFromGitCode, fetchLeaderboardFromGitHub]) {
    try {
      const data = await fetcher()
      if (isLeaderboardPayload(data)) return data
      lastError = new Error('数据格式不正确')
    } catch (err) {
      lastError = err
    }
  }
  throw lastError instanceof Error ? lastError : new Error('获取排行榜数据失败')
}

export async function getLeaderboardData(forceRefresh = false): Promise<LeaderboardData> {
  if (!forceRefresh) {
    const mem = memoryCache.get(LEADERBOARD_KEY)
    if (mem) return mem as LeaderboardData
    const cached = readCache<LeaderboardData>(LEADERBOARD_KEY, LEADERBOARD_TTL)
    if (cached) {
      memoryCache.set(LEADERBOARD_KEY, cached)
      return cached
    }
  }

  const data = await fetchLeaderboardFresh()
  memoryCache.set(LEADERBOARD_KEY, data)
  writeCache(LEADERBOARD_KEY, data)
  return data
}

/** 从内存缓存读取（同步），避免页面切换时重复请求 */
export function getLeaderboardCached(): LeaderboardData | null {
  const mem = memoryCache.get(LEADERBOARD_KEY)
  if (mem) return mem as LeaderboardData
  if (!hasWindow) return null
  const cached = readCache<LeaderboardData>(LEADERBOARD_KEY, LEADERBOARD_TTL)
  if (cached) {
    memoryCache.set(LEADERBOARD_KEY, cached)
    return cached
  }
  return null
}

export interface LeaderboardRow {
  rank: number
  entry: LeaderboardRankEntry
}

/** 按分数降序稳定重排（同分保持原顺序，与桌面版 OrderByDescending 语义一致） */
function sortEntriesBy(entries: LeaderboardRankEntry[], field: keyof LeaderboardRankEntry): LeaderboardRankEntry[] {
  return entries
    .map((entry, i) => ({ entry, i, score: Number(entry[field]) || 0 }))
    .sort((a, b) => b.score - a.score || a.i - b.i)
    .map((x) => x.entry)
}

/** 本地排序 + 筛选，不产生任何请求（整份数据一次请求后全量展示） */
export function buildLeaderboard(
  data: LeaderboardData,
  sort: SortKey,
  query = ''
): LeaderboardRow[] {
  let source: LeaderboardRankEntry[]
  if (Array.isArray(data.reports) && data.reports.length > 0) {
    // 新格式：每份报告只出现一次，按当前维度分数本地重排（原数组按综合榜降序）
    const meta = SORT_META.find((m) => m.key === sort)
    source = meta ? sortEntriesBy(data.reports, meta.field) : data.reports
  } else {
    // 旧格式：榜单已由服务器预排好，直接沿用数组顺序
    source = data.leaderboards?.[sort] ?? data.leaderboards?.gaming ?? []
  }
  const q = query.trim().toLowerCase()
  const rows: LeaderboardRow[] = []
  for (const entry of source) {
    if (
      q &&
      !(entry.cpuName || '').toLowerCase().includes(q) &&
      !(entry.gpuName || '').toLowerCase().includes(q) &&
      !(entry.author || '').toLowerCase().includes(q)
    ) {
      continue
    }
    rows.push({ rank: rows.length + 1, entry })
  }
  return rows
}

/* ---------------- 报告详情（新格式：硬件大字段在 leaderboard/details/{id}.json） ---------------- */

/** 摘要条目是否已含硬件详情（旧格式行字段齐全，无需按需加载） */
export function hasHardwareDetails(entry: LeaderboardRankEntry): boolean {
  return !!(entry.osName || entry.motherboardName || entry.memoryInfo || entry.diskInfo || entry.displayInfo)
}

/** 详情文件与摘要同名的字段直接补齐，硬件大字段单独挑选（未知键忽略） */
function mergeDetail(entry: LeaderboardRankEntry, extra: Record<string, unknown>): LeaderboardRankEntry {
  const str = (key: string): string | undefined => {
    const v = extra[key]
    return typeof v === 'string' ? v : undefined
  }
  return {
    ...entry,
    osName: str('osName') ?? entry.osName,
    motherboardName: str('motherboardName') ?? entry.motherboardName,
    memoryInfo: str('memoryInfo') ?? entry.memoryInfo,
    diskInfo: str('diskInfo') ?? entry.diskInfo,
    displayInfo: str('displayInfo') ?? entry.displayInfo
  }
}

/** 已合并的详情内存缓存 + 请求中防重入（null 不缓存，失败可重试） */
const detailCache = new Map<string, LeaderboardRankEntry>()
const detailInFlight = new Map<string, Promise<LeaderboardRankEntry | null>>()

async function fetchDetailInner(entry: LeaderboardRankEntry): Promise<Record<string, unknown>> {
  // detailsPath 是仓库内相对路径（id 已被编码）；缺失时按仓库约定拼 leaderboard/details/{id}.json
  const rel = String(entry.detailsPath ?? '').trim() || `leaderboard/details/${encodeURIComponent(entry.id)}.json`
  let lastError: unknown
  try {
    // 1) GitCode contents API：内联 base64 直接解码（CORS 稳定）
    return await fetchGitCodeFileJson<Record<string, unknown>>(rel)
  } catch (err) {
    lastError = err
  }
  try {
    // 2) GitHub raw（Access-Control-Allow-Origin: *）
    return await fetchJson<Record<string, unknown>>(`${GITHUB_RAW}/${rel}`)
  } catch (err) {
    lastError = err
  }
  throw lastError instanceof Error ? lastError : new Error('获取报告详情失败')
}

/** 同步读取已合并的详情（重复打开同一报告时避免加载条闪烁），未加载过返回 null */
export function getCachedReportDetail(entry: LeaderboardRankEntry): LeaderboardRankEntry | null {
  if (!entry.id) return null
  return detailCache.get(entry.id) ?? null
}

/**
 * 按需加载报告详情（与桌面版 GetReportDetailAsync 行为一致）。
 * 成功返回合并后的条目（含 OS/主板/内存/硬盘/显示器），失败返回 null，调用方显示兜底文案。
 */
export async function fetchReportDetail(entry: LeaderboardRankEntry): Promise<LeaderboardRankEntry | null> {
  if (!entry.id) return null
  if (detailCache.has(entry.id)) return detailCache.get(entry.id) ?? null
  const inflight = detailInFlight.get(entry.id)
  if (inflight) return inflight
  const task = fetchDetailInner(entry)
    .then((detail) => {
      const merged = mergeDetail(entry, detail)
      detailCache.set(entry.id, merged)
      return merged
    })
    .catch(() => null)
  detailInFlight.set(entry.id, task)
  try {
    return await task
  } finally {
    detailInFlight.delete(entry.id)
  }
}

/* ---------------- 核间延迟热力图 ---------------- */

const LATENCY_LIST_KEY = 'tuba_benchmark_latency_list_v1'

function parseLatencyFromContents(items: Array<Record<string, unknown>>): LatencyImageInfo[] {
  const result: LatencyImageInfo[] = []
  for (const item of items) {
    const name = String(item.name ?? '')
    if (!name.toLowerCase().endsWith('.png')) continue
    const rawUrl = String(item.download_url ?? '')
    const htmlUrl = String(item.html_url ?? '')
    const sha = String(item.sha ?? '')
    result.push({
      name,
      rawUrl,
      htmlUrl: htmlUrl || `https://github.com/${OWNER}/${REPO}/blob/main/reports/latency-images/${encodeURIComponent(name)}`,
      sha
    })
  }
  return result.sort((a, b) => a.name.localeCompare(b.name, 'en', { numeric: true }))
}

async function fetchLatencyListFromGitCode(): Promise<LatencyImageInfo[]> {
  const items = await fetchJson<Array<Record<string, unknown>>>(
    `${GITCODE_API}/reports/latency-images`
  )
  return parseLatencyFromContents(items)
}

async function fetchLatencyListFromGitHub(): Promise<LatencyImageInfo[]> {
  const items = await fetchJson<Array<Record<string, unknown>>>(
    `${GITHUB_API}/reports/latency-images`
  )
  return parseLatencyFromContents(items)
}

async function fetchLatencyListFresh(): Promise<LatencyImageInfo[]> {
  let lastError: unknown
  for (const fetcher of [fetchLatencyListFromGitCode, fetchLatencyListFromGitHub]) {
    try {
      const list = await fetcher()
      if (list.length > 0) return list
      lastError = new Error('暂无热力图数据')
    } catch (err) {
      lastError = err
    }
  }
  throw lastError instanceof Error ? lastError : new Error('获取热力图列表失败')
}

export async function getLatencyImages(forceRefresh = false): Promise<LatencyImageInfo[]> {
  if (!forceRefresh) {
    const mem = memoryCache.get(LATENCY_LIST_KEY)
    if (mem) return mem as LatencyImageInfo[]
    const cached = readCache<LatencyImageInfo[]>(LATENCY_LIST_KEY, LATENCY_LIST_TTL)
    if (cached) {
      memoryCache.set(LATENCY_LIST_KEY, cached)
      return cached
    }
  }

  const list = await fetchLatencyListFresh()
  memoryCache.set(LATENCY_LIST_KEY, list)
  writeCache(LATENCY_LIST_KEY, list)
  return list
}

export function getLatencyImagesCached(): LatencyImageInfo[] | null {
  const mem = memoryCache.get(LATENCY_LIST_KEY)
  if (mem) return mem as LatencyImageInfo[]
  if (!hasWindow) return null
  const cached = readCache<LatencyImageInfo[]>(LATENCY_LIST_KEY, LATENCY_LIST_TTL)
  if (cached) {
    memoryCache.set(LATENCY_LIST_KEY, cached)
    return cached
  }
  return null
}

/** 图片最终地址：直接返回 raw URL（<img> 标签加载不经过 fetch，无 CORS 限制） */
export function latencyImageUrl(info: LatencyImageInfo): string {
  if (info.rawUrl) return info.rawUrl
  return `${GITHUB_RAW}/reports/latency-images/${encodeURIComponent(info.name)}`
}

/** 通过 fetch 走镜像代理取图（用于 GitHub 图片在部分网络无法直连的情况） */
export function latencyImageProxyUrls(info: LatencyImageInfo): string[] {
  const raw = latencyImageUrl(info)
  return GITHUB_RAW_MIRRORS.map((proxy) => `${proxy}${raw}`)
}

/* ---------------- 元数据 ---------------- */

/** 从文件名解析出 (CPU 型号, 发布者用户名, 序号)：`{CPU}-{author}-{seq}.png` */
export function parseLatencyName(name: string): { cpu: string; author: string; seq: number } {
  const base = name.toLowerCase().endsWith('.png') ? name.slice(0, -4) : name
  let seq = 0
  let rest = base
  const lastDash = base.lastIndexOf('-')
  if (lastDash > 0) {
    const numPart = base.slice(lastDash + 1)
    if (/^\d+$/.test(numPart)) {
      seq = Number(numPart)
      rest = base.slice(0, lastDash)
    }
  }
  const authorDash = rest.lastIndexOf('-')
  if (authorDash > 0) {
    return { cpu: rest.slice(0, authorDash), author: rest.slice(authorDash + 1), seq }
  }
  return { cpu: rest, author: '', seq }
}

export function invalidateBenchmarkCache(): void {
  clearMemoryCache()
  safeRemoveItem(sessionStorage, LEADERBOARD_KEY)
  safeRemoveItem(localStorage, LEADERBOARD_KEY)
  safeRemoveItem(sessionStorage, LATENCY_LIST_KEY)
  safeRemoveItem(localStorage, LATENCY_LIST_KEY)
}
