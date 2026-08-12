# OpenAI 兼容代理 Worker（带限速 + 每日限额）

OpenAI 兼容 API 代理，上游按 `opencode-free → opencode → nvidia` 顺序回退，支持流式、reasoning、tool_calls。
在原始脚本基础上新增**分钟级限速**与**每用户每日限额**（按请求数 + token 数）。

## 限流与配额怎么工作

| 层 | 实现 | 说明 |
|---|---|---|
| 分钟级限速 | Cloudflare 原生 `RATE_LIMITER` binding（可选） | 免费计划可用，零 KV 写入；不配置则退化为内存计数兜底（每个 isolate 独立，仅近似） |
| 每日请求数 | KV 计数器 `quota:<日期>:<用户>` | 默认 300 次/天/用户 |
| 每日 token 数 | 同上，同一 KV 键 | 默认 1,000,000；流式响应按字符数/4 估算，若上游流尾返回 `usage` 则优先用它 |
| 用户身份 | `Authorization: Bearer <key>`（SHA-256 哈希）或 `CF-Connecting-IP` | 每个 key / IP 独立配额桶 |

超限返回 `429`，带 `Retry-After` 和 `X-RateLimit-Limit / Remaining / Reset` 头。

## 部署步骤

```bash
cd openai-proxy-worker
npm install

# 1. 创建 KV 命名空间，把输出的 id 填进 wrangler.toml
npx wrangler kv namespace create QUOTA

# 2. （可选）在 wrangler.toml 里打开 [[ratelimits]] 段启用原生限速

# 3. 本地试跑（本地 KV 会自动模拟）
npm run dev

# 4. 部署
npm run deploy
```

## 环境变量（wrangler.toml `[vars]`）

| 变量 | 默认 | 说明 |
|---|---|---|
| `RATE_LIMIT_PER_MINUTE` | 30 | 每用户每分钟请求数，0 = 不限制 |
| `DAILY_REQUESTS` | 300 | 每用户每日请求数，0 = 不限制 |
| `DAILY_TOKENS` | 1000000 | 每用户每日 token 数，0 = 不限制 |
| `ADMIN_KEY` | 空 | 管理接口 key，留空禁用 |
| `OPENCODE_API_KEY` | 内置 key | 可选覆盖 |
| `NVIDIA_API_KEY` | 必填 | nvidia 回退需要 |

## 管理接口

```bash
# 查看今日各用户用量（Bearer 用 ADMIN_KEY）
curl -H "Authorization: Bearer <ADMIN_KEY>" "https://<worker>/v1/admin/usage"
# 指定日期
curl -H "Authorization: Bearer <ADMIN_KEY>" "https://<worker>/v1/admin/usage?date=2026-08-12"
```

## 注意事项

- **KV 免费版写入 1000 次/天**：每成功请求 1 次 KV 写，即免费档最多支撑 ~1000 请求/天。
  量大了建议把计数器换成一个 Durable Object（原子计数、无写入限制，仓库里 `cloudflare-worker/` 已有 DO 先例）。
- KV 是最终一致性：并发请求可能**轻微超限**（读-改-写非原子），免费档可接受；要精确可换 DO。
- 不配置任何 binding 时脚本仍可运行（限速退化为内存、每日限额关闭），方便直接粘到仪表盘测试。
- 原生 `RATE_LIMITER` binding 的 `period` 只能是 10 / 60 / 600 / 3600 秒，**最长 1 小时，做不了每日限额**。
