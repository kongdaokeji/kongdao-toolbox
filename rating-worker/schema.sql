-- 图吧工具箱 · 硬件评分系统 D1 数据库 schema
-- 使用：wrangler d1 execute tuba-ratings --file=./schema.sql  (本地)
--       wrangler d1 execute tuba-ratings --remote --file=./schema.sql  (线上)

-- ========== 笔记本评分表 ==========
CREATE TABLE IF NOT EXISTS laptop_ratings (
  id TEXT PRIMARY KEY,
  device_model TEXT NOT NULL,
  cpu TEXT NOT NULL,
  gpu TEXT NOT NULL,
  overall_score INTEGER NOT NULL,
  build_quality_score INTEGER NOT NULL,
  screen_score INTEGER NOT NULL,
  noise_score INTEGER NOT NULL,
  performance_score INTEGER NOT NULL,
  review_text TEXT,
  author TEXT NOT NULL DEFAULT '匿名用户',
  device_hash TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_laptop_model ON laptop_ratings (device_model);
CREATE INDEX IF NOT EXISTS idx_laptop_overall ON laptop_ratings (overall_score DESC);
CREATE INDEX IF NOT EXISTS idx_laptop_created ON laptop_ratings (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_laptop_dedup ON laptop_ratings (device_hash, device_model, cpu, gpu);

-- ========== 台式机部件评分表 ==========
CREATE TABLE IF NOT EXISTS desktop_ratings (
  id TEXT PRIMARY KEY,
  component_type TEXT NOT NULL,      -- cpu / gpu / memory / motherboard / disk / cooler / psu / case / monitor
  component_model TEXT NOT NULL,
  overall_score INTEGER NOT NULL,
  review_text TEXT,
  author TEXT NOT NULL DEFAULT '匿名用户',
  device_hash TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_desktop_type ON desktop_ratings (component_type);
CREATE INDEX IF NOT EXISTS idx_desktop_model ON desktop_ratings (component_type, component_model);
CREATE INDEX IF NOT EXISTS idx_desktop_overall ON desktop_ratings (component_type, overall_score DESC);
CREATE INDEX IF NOT EXISTS idx_desktop_created ON desktop_ratings (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_desktop_dedup ON desktop_ratings (device_hash, component_type, component_model);