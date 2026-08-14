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
| `STICKY_ROUTING` | 1 | 会话粘性路由：同一会话（稳定前缀指纹）的多轮请求钉在同一上游，跨轮命中上游前缀缓存；`0` 关闭 |
| `STICKY_TTL` | 1800 | 粘性键有效期（秒） |
| `RESPONSE_CACHE_TTL` | 0 | 非流式**完全重复**请求的响应缓存秒数（0 = 关闭）；命中直接由 KV 返回，省一次上游调用 |

## 缓存优化说明（省钱）

- **会话粘性路由**：多轮 agent 对话若中途换上游（限流/抖动触发 fallback），新上游会当作全新对话，前缀缓存全部失效。粘性路由用「model + 首条消息 + 工具定义」的 SHA-256 指纹把会话钉在上一个成功上游（键 `sticky:<指纹>`，复用 QUOTA KV），跨轮请求字节稳定 → DeepSeek 官方等支持自动前缀缓存的付费上游可跨轮命中（命中价远低于未命中价）。
- **usage 规范化**：把上游的 `prompt_cache_hit_tokens`（DeepSeek 系自定义字段）同步映射为 OpenAI 标准 `prompt_tokens_details.cached_tokens`，否则客户端 OpenAI SDK 解析不到缓存命中数。
- **管理接口可见性**：`/v1/admin/usage` 的每条记录新增 `hit` 字段（当日缓存命中 token），可直接看到省钱效果；应用内 AI 助手 token 气泡也会显示「缓存命中 X (Y%)」。
- 网关侧无法伪造模型级前缀缓存（那是推理服务内部的 KV），以上只是保证它不被破坏、并让同一会话始终命中同一个上游。

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
