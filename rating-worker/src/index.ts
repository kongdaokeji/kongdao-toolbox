export interface Env {
  DB: D1Database;
}

// ---------------------------------------------------------------------------
// 工具函数
// ---------------------------------------------------------------------------

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};

function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json", ...CORS_HEADERS },
  });
}

function error(message: string, status = 400): Response {
  return json({ error: message }, status);
}

// 生成唯一 ID：时间戳 + 随机后缀
function genId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
}

// 对字符串做轻量哈希（FNV-1a 32），用于 device_hash 校验，返回 hex
function hashStr(s: string): string {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = (h * 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, "0");
}

function normalize(s: unknown): string {
  if (typeof s !== "string") return "";
  return s.trim();
}

// 兼容 snake_case / camelCase 两种字段名（客户端可能发送任意一种）
function pick(body: Record<string, unknown>, ...keys: string[]): unknown {
  for (const k of keys) {
    if (body[k] !== undefined && body[k] !== null) return body[k];
  }
  return undefined;
}

function clampInt(v: unknown, min: number, max: number): number {
  const n = typeof v === "number" ? v : parseInt(String(v), 10);
  if (!Number.isFinite(n)) return min;
  return Math.max(min, Math.min(max, Math.round(n)));
}

// 取查询参数，大小写不敏感
function param(url: URL, key: string, fallback = ""): string {
  return url.searchParams.get(key) ?? fallback;
}

// ---------------------------------------------------------------------------
// 评分校验常量
// ---------------------------------------------------------------------------
const LAPTOP_DIMENSIONS = [
  "overallScore",
  "buildQualityScore",
  "screenScore",
  "noiseScore",
  "performanceScore",
] as const;

const ALLOWED_COMPONENT_TYPES = new Set([
  "cpu",
  "gpu",
  "memory",
  "motherboard",
  "disk",
  "cooler",
  "psu",
  "case",
  "monitor",
  "other",
]);

const LAPTOP_SORT_DIMENSIONS = new Set([
  "overall",
  "buildQuality",
  "screen",
  "noise",
  "performance",
  "count",
  "latest",
]);

const DESKTOP_SORT_DIMENSIONS = new Set([
  "overall",
  "count",
  "latest",
]);

// 文字评价最大长度
const MAX_REVIEW_LENGTH = 500;
// 作者最大长度
const MAX_AUTHOR_LENGTH = 40;

// ---------------------------------------------------------------------------
// 笔记本评分
// ---------------------------------------------------------------------------

interface LaptopSubmission {
  deviceModel: string;
  cpu: string;
  gpu: string;
  overallScore: number;
  buildQualityScore: number;
  screenScore: number;
  noiseScore: number;
  performanceScore: number;
  reviewText?: string;
  author?: string;
  deviceHash?: string;
}

interface LaptopRow {
  id: string;
  device_model: string;
  cpu: string;
  gpu: string;
  overall_score: number;
  build_quality_score: number;
  screen_score: number;
  noise_score: number;
  performance_score: number;
  review_text: string | null;
  author: string;
  device_hash: string;
  created_at: number;
}

const LAPTOP_SORT_MAP: Record<string, string> = {
  overall: "avg_overall",
  buildQuality: "avg_build_quality",
  screen: "avg_screen",
  noise: "avg_noise",
  performance: "avg_performance",
  count: "rating_count",
  latest: "latest_at",
};

async function submitLaptop(db: D1Database, body: Record<string, unknown>): Promise<Response> {
  const deviceModel = normalize(pick(body, "deviceModel", "device_model"));
  const cpu = normalize(pick(body, "cpu"));
  const gpu = normalize(pick(body, "gpu"));
  if (!deviceModel || !cpu || !gpu)
    return error("缺少必要字段：deviceModel / cpu / gpu");

  const scores: Record<string, number> = {
    overallScore: clampInt(pick(body, "overallScore", "overall_score"), 0, 10),
    buildQualityScore: clampInt(pick(body, "buildQualityScore", "build_quality_score"), 0, 10),
    screenScore: clampInt(pick(body, "screenScore", "screen_score"), 0, 10),
    noiseScore: clampInt(pick(body, "noiseScore", "noise_score"), 0, 10),
    performanceScore: clampInt(pick(body, "performanceScore", "performance_score"), 0, 10),
  };
  for (const [k, v] of Object.entries(scores)) {
    if (v <= 0) return error(`评分 ${k} 必须为 1-10 的整数`);
  }

  const reviewText = normalize(pick(body, "reviewText", "review_text")).slice(0, MAX_REVIEW_LENGTH);
  const author = (normalize(pick(body, "author")) || "匿名用户").slice(0, MAX_AUTHOR_LENGTH);
  const deviceHash = normalize(pick(body, "deviceHash", "device_hash")) || hashStr(deviceModel + cpu + gpu);
  const now = Date.now();

  // 去重校验：同一 device_hash 对同一机型只能提交一次
  const existing = await db
    .prepare("SELECT id FROM laptop_ratings WHERE device_hash = ?1 AND device_model = ?2 AND cpu = ?3 AND gpu = ?4 LIMIT 1")
    .bind(deviceHash, deviceModel, cpu, gpu)
    .first<{ id: string }>();
  if (existing)
    return error("你已经对该机型提交过评分，同一设备对同一机型只能提交一次", 409);

  const id = genId();
  await db
    .prepare(
      `INSERT INTO laptop_ratings
       (id, device_model, cpu, gpu, overall_score, build_quality_score, screen_score, noise_score, performance_score, review_text, author, device_hash, created_at)
       VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13)`
    )
    .bind(
      id, deviceModel, cpu, gpu,
      scores.overallScore, scores.buildQualityScore, scores.screenScore, scores.noiseScore, scores.performanceScore,
      reviewText || null, author, deviceHash, now
    )
    .run();

  return json({ ok: true, id, createdAt: now });
}

async function laptopLeaderboard(db: D1Database, url: URL): Promise<Response> {
  const sortBy = param(url, "sortBy", "overall");
  if (!LAPTOP_SORT_MAP[sortBy]) return error(`不支持的排序维度：${sortBy}`);

  const limit = clampInt(param(url, "limit", "50"), 1, 200);
  const page = clampInt(param(url, "page", "1"), 1, 1000);
  const offset = (page - 1) * limit;

  const orderCol = LAPTOP_SORT_MAP[sortBy];
  const rows = await db
    .prepare(
      `SELECT
         device_model AS device_model,
         cpu,
         gpu,
         ROUND(AVG(overall_score), 1)        AS avg_overall,
         ROUND(AVG(build_quality_score), 1)  AS avg_build_quality,
         ROUND(AVG(screen_score), 1)         AS avg_screen,
         ROUND(AVG(noise_score), 1)           AS avg_noise,
         ROUND(AVG(performance_score), 1)     AS avg_performance,
         COUNT(*)                             AS rating_count,
         MAX(created_at)                       AS latest_at
       FROM laptop_ratings
       GROUP BY device_model, cpu, gpu
       ORDER BY ${orderCol} DESC, avg_overall DESC
       LIMIT ?1 OFFSET ?2`
    )
    .bind(limit, offset)
    .all();

  const total = await db
    .prepare("SELECT COUNT(*) AS c FROM (SELECT 1 FROM laptop_ratings GROUP BY device_model, cpu, gpu)")
    .first<{ c: number }>();

  return json({
    sortBy,
    page,
    limit,
    total: total?.c ?? 0,
    entries: rows.results ?? [],
  });
}

async function laptopReviews(db: D1Database, url: URL): Promise<Response> {
  const deviceModel = normalize(param(url, "deviceModel"));
  const cpu = normalize(param(url, "cpu"));
  const gpu = normalize(param(url, "gpu"));
  if (!deviceModel || !cpu || !gpu) return error("缺少 deviceModel / cpu / gpu");

  const limit = clampInt(param(url, "limit", "20"), 1, 100);
  const rows = await db
    .prepare(
      `SELECT id, overall_score, build_quality_score, screen_score, noise_score, performance_score,
              review_text, author, created_at
       FROM laptop_ratings
       WHERE device_model = ?1 AND cpu = ?2 AND gpu = ?3
       ORDER BY created_at DESC
       LIMIT ?4`
    )
    .bind(deviceModel, cpu, gpu, limit)
    .all();

  return json({ reviews: rows.results ?? [] });
}

// ---------------------------------------------------------------------------
// 台式机部件评分
// ---------------------------------------------------------------------------

interface DesktopSubmission {
  componentType: string;
  componentModel: string;
  overallScore: number;
  reviewText?: string;
  author?: string;
  deviceHash?: string;
}

const DESKTOP_SORT_MAP: Record<string, string> = {
  overall: "avg_overall",
  count: "rating_count",
  latest: "latest_at",
};

async function submitDesktop(db: D1Database, body: Record<string, unknown>): Promise<Response> {
  const componentType = normalize(pick(body, "componentType", "component_type")).toLowerCase();
  if (!ALLOWED_COMPONENT_TYPES.has(componentType))
    return error("不支持的部件类型：" + componentType);
  const componentModel = normalize(pick(body, "componentModel", "component_model"));
  if (!componentModel) return error("缺少必要字段：componentModel");

  const overallScore = clampInt(pick(body, "overallScore", "overall_score"), 0, 10);
  if (overallScore <= 0) return error("评分 overallScore 必须为 1-10 的整数");

  const reviewText = normalize(pick(body, "reviewText", "review_text")).slice(0, MAX_REVIEW_LENGTH);
  const author = (normalize(pick(body, "author")) || "匿名用户").slice(0, MAX_AUTHOR_LENGTH);
  const deviceHash = normalize(pick(body, "deviceHash", "device_hash")) || hashStr(componentType + componentModel);
  const now = Date.now();

  // 去重校验：同一 device_hash 对同一部件型号只能提交一次
  const existing = await db
    .prepare("SELECT id FROM desktop_ratings WHERE device_hash = ?1 AND component_type = ?2 AND component_model = ?3 LIMIT 1")
    .bind(deviceHash, componentType, componentModel)
    .first<{ id: string }>();
  if (existing)
    return error("你已经对该部件提交过评分，同一设备对同一部件只能提交一次", 409);

  const id = genId();
  await db
    .prepare(
      `INSERT INTO desktop_ratings
       (id, component_type, component_model, overall_score, review_text, author, device_hash, created_at)
       VALUES (?1,?2,?3,?4,?5,?6,?7,?8)`
    )
    .bind(id, componentType, componentModel, overallScore, reviewText || null, author, deviceHash, now)
    .run();

  return json({ ok: true, id, createdAt: now });
}

async function desktopLeaderboard(db: D1Database, url: URL): Promise<Response> {
  const componentType = normalize(param(url, "componentType")).toLowerCase();
  if (!ALLOWED_COMPONENT_TYPES.has(componentType))
    return error("缺少或无效的 componentType");

  const sortBy = param(url, "sortBy", "overall");
  if (!DESKTOP_SORT_MAP[sortBy]) return error(`不支持的排序维度：${sortBy}`);

  const limit = clampInt(param(url, "limit", "50"), 1, 200);
  const page = clampInt(param(url, "page", "1"), 1, 1000);
  const offset = (page - 1) * limit;

  const orderCol = DESKTOP_SORT_MAP[sortBy];
  const rows = await db
    .prepare(
      `SELECT
         component_type AS component_type,
         component_model,
         ROUND(AVG(overall_score), 1) AS avg_overall,
         COUNT(*)                      AS rating_count,
         MAX(created_at)               AS latest_at
       FROM desktop_ratings
       WHERE component_type = ?1
       GROUP BY component_model
       ORDER BY ${orderCol} DESC, avg_overall DESC
       LIMIT ?2 OFFSET ?3`
    )
    .bind(componentType, limit, offset)
    .all();

  const total = await db
    .prepare("SELECT COUNT(*) AS c FROM (SELECT 1 FROM desktop_ratings WHERE component_type = ?1 GROUP BY component_model)")
    .bind(componentType)
    .first<{ c: number }>();

  return json({
    componentType,
    sortBy,
    page,
    limit,
    total: total?.c ?? 0,
    entries: rows.results ?? [],
  });
}

async function desktopReviews(db: D1Database, url: URL): Promise<Response> {
  const componentType = normalize(param(url, "componentType")).toLowerCase();
  const componentModel = normalize(param(url, "componentModel"));
  if (!componentType || !componentModel) return error("缺少 componentType / componentModel");

  const limit = clampInt(param(url, "limit", "20"), 1, 100);
  const rows = await db
    .prepare(
      `SELECT id, overall_score, review_text, author, created_at
       FROM desktop_ratings
       WHERE component_type = ?1 AND component_model = ?2
       ORDER BY created_at DESC
       LIMIT ?3`
    )
    .bind(componentType, componentModel, limit)
    .all();

  return json({ reviews: rows.results ?? [] });
}

// ---------------------------------------------------------------------------
// 总体统计
// ---------------------------------------------------------------------------

async function stats(db: D1Database): Promise<Response> {
  const laptopTotal = await db.prepare("SELECT COUNT(*) AS c FROM laptop_ratings").first<{ c: number }>();
  const laptopModels = await db
    .prepare("SELECT COUNT(*) AS c FROM (SELECT 1 FROM laptop_ratings GROUP BY device_model, cpu, gpu)")
    .first<{ c: number }>();
  const desktopTotal = await db.prepare("SELECT COUNT(*) AS c FROM desktop_ratings").first<{ c: number }>();
  const desktopModels = await db
    .prepare("SELECT component_type, COUNT(*) AS c, COUNT(DISTINCT component_model) AS models FROM desktop_ratings GROUP BY component_type")
    .all();

  return json({
    laptop: {
      ratings: laptopTotal?.c ?? 0,
      machines: laptopModels?.c ?? 0,
    },
    desktop: {
      ratings: desktopTotal?.c ?? 0,
      byType: (desktopModels.results ?? []).reduce<Record<string, { ratings: number; models: number }>>(
        (acc, r: any) => {
          acc[r.component_type] = { ratings: r.c, models: r.models };
          return acc;
        },
        {}
      ),
    },
  });
}

// ---------------------------------------------------------------------------
// 路由
// ---------------------------------------------------------------------------

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    const url = new URL(request.url);
    const path = url.pathname;
    const method = request.method;
    const db = env.DB;

    try {
      if (path === "/api/health" && method === "GET")
        return json({ ok: true, service: "tuba-rating-api", time: Date.now() });

      if (path === "/api/stats" && method === "GET")
        return await stats(db);

      // 笔记本
      if (path === "/api/ratings/laptop" && method === "POST") {
        const body = (await request.json()) as Record<string, unknown>;
        return await submitLaptop(db, body);
      }
      if (path === "/api/ratings/laptop/leaderboard" && method === "GET")
        return await laptopLeaderboard(db, url);
      if (path === "/api/ratings/laptop/reviews" && method === "GET")
        return await laptopReviews(db, url);

      // 台式机
      if (path === "/api/ratings/desktop" && method === "POST") {
        const body = (await request.json()) as Record<string, unknown>;
        return await submitDesktop(db, body);
      }
      if (path === "/api/ratings/desktop/leaderboard" && method === "GET")
        return await desktopLeaderboard(db, url);
      if (path === "/api/ratings/desktop/reviews" && method === "GET")
        return await desktopReviews(db, url);

      return error("Not found", 404);
    } catch (err) {
      return error("服务器内部错误：" + (err instanceof Error ? err.message : String(err)), 500);
    }
  },
};