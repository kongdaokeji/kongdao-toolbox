const fs = require('fs');

const src = 'C:\\Users\\luolan\\Downloads\\leaderboard (1).json';
const out = 'C:\\Users\\luolan\\Desktop\\tubawinui3\\benchmark-worker\\seed.sql';

const d = JSON.parse(fs.readFileSync(src, 'utf8'));

// 兼容三种结构：
// - 新单列：reports 每份报告只存一遍（各维度排序由客户端本地算）
// - 中间版：reports（摘要）+ boards（各榜 id 有序列表）
// - 旧结构：leaderboards（各榜全量条目，同一份报告在每个榜重复一遍）
const collected = [];
if (Array.isArray(d.reports)) {
  for (const e of d.reports) collected.push(e);
} else if (d.leaderboards) {
  for (const dim of Object.keys(d.leaderboards)) {
    for (const e of d.leaderboards[dim]) {
      collected.push(e);
    }
  }
}
// 去重：同一 id 只保留一份（新结构 boards 去重后精确等于 reports；旧结构去掉跨榜重复）
const seen = new Set();
const deduped = [];
for (const e of collected) {
  if (seen.has(e.id)) continue;
  seen.add(e.id);
  deduped.push(e);
}
const rows = deduped;

function sqlStr(s) {
  if (s === null || s === undefined) return "''";
  // 单引号转义
  return "'" + String(s).replace(/'/g, "''") + "'";
}

function toMs(v) {
  if (typeof v === 'number') return v;
  if (typeof v === 'string') {
    const t = Date.parse(v);
    if (!isNaN(t)) return t;
    const n = parseInt(v, 10);
    if (!isNaN(n)) return n;
  }
  return Date.now();
}

// device_hash：用 author+id 派生稳定 hash（FNV-1a 32），便于"我的报告"按设备过滤
function hashStr(s) {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = (h * 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, '0');
}

const lines = [];
lines.push('-- 自动生成的种子数据：' + rows.length + ' 份历史报告');
lines.push('-- 由 seed-from-leaderboard.js 从 leaderboard (1).json 生成');
lines.push('');
// D1 不支持 BEGIN/COMMIT 事务语句，直接逐条 INSERT

for (const e of rows) {
  const id = e.id;
  const author = e.author || '匿名用户';
  const deviceHash = 'seed-' + hashStr(author + id);
  const submittedAt = toMs(e.submittedAt);
  const cols = '(id, author, device_hash, submitted_at, cpu_name, gpu_name, os_name, motherboard_name, memory_info, disk_info, display_info, gaming_score, gaming_grade, office_score, office_grade, cpu_single_core_score, cpu_multi_core_score, gpu_render_score, memory_capacity_score, disk_seq_read_score, disk_seq_write_score, disk_4k_read_score, disk_4k_write_score, browser_total_score)';
  const vals = [
    sqlStr(id),
    sqlStr(author),
    sqlStr(deviceHash),
    String(submittedAt),
    sqlStr(e.cpuName),
    sqlStr(e.gpuName),
    sqlStr(e.osName),
    sqlStr(e.motherboardName),
    sqlStr(e.memoryInfo),
    sqlStr(e.diskInfo),
    sqlStr(e.displayInfo),
    String(e.gamingScore || 0),
    sqlStr(e.gamingGrade),
    String(e.officeScore || 0),
    sqlStr(e.officeGrade),
    String(e.cpuSingleCoreScore || 0),
    String(e.cpuMultiCoreScore || 0),
    String(e.gpuRenderScore || 0),
    String(e.memoryCapacityScore || 0),
    String(e.diskSeqReadScore || 0),
    String(e.diskSeqWriteScore || 0),
    String(e.disk4KReadScore || 0),
    String(e.disk4KWriteScore || 0),
    String(e.browserTotalScore || 0),
  ].join(', ');
  lines.push('INSERT INTO benchmark_reports ' + cols + ' VALUES (' + vals + ');');
}

fs.writeFileSync(out, lines.join('\n'), 'utf8');
console.log('wrote ' + out);
console.log('rows=' + rows.length);
console.log('bytes=' + fs.statSync(out).size);