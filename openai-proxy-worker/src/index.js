const OPENCODE_FREE_BASE = "https://opencode.ai/zen/v1";
const OPENCODE_BASE = "https://opencode.ai/zen/go/v1";
const NVIDIA_BASE = "https://integrate.api.nvidia.com/v1";

const OPENCODE_DEFAULT_API_KEY = "sk-8wuyUyKWKilCANNxSYjrrdYgYQaCUr0BNKVfjrk2g9p5MNOYMorQnMeNnwk3sfp2";
const OPENCODE_FREE_MODEL = "deepseek-v4-flash-free";
const OPENCODE_MODEL = "deepseek-v4-flash";
const NVIDIA_FALLBACK_MODEL = "deepseek-ai/deepseek-v4-flash";

const MODEL_ROUTES = {
	"auto": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"gpt-4o": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"gpt-4o-mini": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"gpt-4": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"gpt-4-turbo": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"gpt-3.5-turbo": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"claude-3-opus": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"claude-3-sonnet": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"claude-3-haiku": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"deepseek-chat": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"deepseek-reasoner": { provider: "opencode-free", model: "deepseek-v4-flash-free", enableThinking: true },
	"deepseek-v4-flash": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"deepseek-v4-flash-free": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"deepseek-v4-flash-thinking": { provider: "opencode-free", model: "deepseek-v4-flash-free", enableThinking: true },
	"glm-4-flash": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"glm-4.7-flash": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"glm-4.7-flash-thinking": { provider: "opencode-free", model: "deepseek-v4-flash-free", enableThinking: true },
	"gemini-pro": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"qwen-coder": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"llama-4-scout": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"llama-3.3-70b": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"mistral-small": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"qwq-32b": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"qwen3-30b": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"nemotron-120b": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"qwen2.5-coder-32b": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
	"llama-3.1-8b-fast": { provider: "opencode-free", model: "deepseek-v4-flash-free" },
};

const FALLBACK_CHAIN = ["opencode-free", "opencode", "nvidia"];

// ==================== 限流与每日配额 ====================
// 层级：
//   1) 分钟级限速 —— 优先用 Cloudflare 原生 RATE_LIMITER binding（免费计划可用），
//      没有 binding 时退化为内存计数（每个 isolate 独立，只做近似兜底）。
//   2) 每日配额 —— KV 计数器（键带日期前缀），按"用户"分别计数。
//      用户身份：请求带 Bearer key 时按 key 的 SHA-256 哈希区分；否则按 CF-Connecting-IP。
//      KV 是最终一致性，并发下可能轻微超限（免费版可接受；要精确计数可换 Durable Object）。
const KV_QUOTA_TTL = 2 * 24 * 3600; // 配额键保留 48h，按日期前缀隔离，日切后自然失效

function numOr(value, fallback) {
	const n = Number(value);
	return Number.isFinite(n) && n >= 0 ? n : fallback;
}

// 默认限额，均可用环境变量覆盖；0 = 不限制
function defaultLimits(env) {
	return {
		perMinute: numOr(env.RATE_LIMIT_PER_MINUTE, 30), // 每分钟请求数
		dailyRequests: numOr(env.DAILY_REQUESTS, 300), // 每日请求数
		dailyTokens: numOr(env.DAILY_TOKENS, 1000000), // 每日 token 数（流式按内容估算）
	};
}

async function sha256Hex(text) {
	const data = new TextEncoder().encode(text);
	const digest = await crypto.subtle.digest("SHA-256", data);
	return Array.from(new Uint8Array(digest))
		.map((b) => b.toString(16).padStart(2, "0"))
		.join("");
}

// 身份识别：优先 Bearer key（哈希后做 ID，避免明文 key 出现在配额键/日志里），否则按 IP
async function identifyUser(request) {
	const auth = request.headers.get("authorization") || "";
	const token = auth.replace(/^Bearer\s+/i, "").trim();
	if (token) {
		return `key:${await sha256Hex(token)}`;
	}
	return `ip:${request.headers.get("cf-connecting-ip") || "unknown"}`;
}

// 内存限速兜底（无 RATE_LIMITER binding 时使用）
const memRateCounts = new Map(); // `${uid}:${minute}` -> count

function checkMemRateLimit(uid) {
	const minute = Math.floor(Date.now() / 60000);
	const memKey = `${uid}:${minute}`;
	const next = (memRateCounts.get(memKey) || 0) + 1;
	memRateCounts.set(memKey, next);
	// 顺手清理 2 分钟前的旧条目，防止 Map 无限增长
	if (memRateCounts.size > 10000) {
		for (const k of memRateCounts.keys()) {
			if (!k.endsWith(`:${minute}`) && !k.endsWith(`:${minute - 1}`)) {
				memRateCounts.delete(k);
			}
		}
	}
	return next;
}

function utcDateString() {
	return new Date().toISOString().slice(0, 10); // YYYY-MM-DD（UTC）
}

function rateLimitHeaders(info) {
	const headers = {};
	if (info.retryAfter != null) headers["Retry-After"] = String(info.retryAfter);
	if (info.limit != null) headers["X-RateLimit-Limit"] = String(info.limit);
	if (info.remaining != null) headers["X-RateLimit-Remaining"] = String(info.remaining);
	if (info.reset != null) headers["X-RateLimit-Reset"] = String(info.reset);
	return headers;
}

// 请求前的限额检查。返回 { ok, quotaKey, ... }；不通过时返回 429 所需信息
async function checkQuota(env, uid, limits) {
	// 1) 分钟级限速
	const minuteReset = Math.floor((Math.floor(Date.now() / 60000) + 1) * 60);
	if (limits.perMinute > 0) {
		if (env.RATE_LIMITER) {
			const res = await env.RATE_LIMITER.limit({ key: uid });
			if (!res.success) {
				return { ok: false, reason: "rate", retryAfter: 60, reset: minuteReset };
			}
		} else if (checkMemRateLimit(uid) > limits.perMinute) {
			return { ok: false, reason: "rate", retryAfter: 60, reset: minuteReset };
		}
	}

	// 2) 每日配额（KV；未配置 QUOTA binding 时跳过，脚本仍可运行）
	const date = utcDateString();
	const quotaKey = `quota:${date}:${uid}`;
	const dailyReset = Math.floor(new Date().setUTCHours(24, 0, 0, 0) / 1000);

	if (!env.QUOTA) {
		return { ok: true, quotaKey: null, dailyReset, limit: null, remaining: null };
	}

	const raw = await env.QUOTA.get(quotaKey);
	const cur = raw ? JSON.parse(raw) : { req: 0, tok: 0 };

	const reqLimited = limits.dailyRequests > 0 && cur.req >= limits.dailyRequests;
	const tokLimited = limits.dailyTokens > 0 && cur.tok >= limits.dailyTokens;
	if (reqLimited || tokLimited) {
		return {
			ok: false,
			reason: "daily",
			retryAfter: Math.max(1, dailyReset - Math.floor(Date.now() / 1000)),
			reset: dailyReset,
			limit: limits.dailyRequests,
			remaining: Math.max(0, limits.dailyRequests - cur.req),
		};
	}

	return {
		ok: true,
		quotaKey,
		dailyReset,
		limit: limits.dailyRequests,
		remaining: limits.dailyRequests > 0 ? limits.dailyRequests - cur.req : null,
	};
}

// 请求成功（或流式结束）后记账：请求数 +1、token 累加
async function recordUsage(ctx, tokens) {
	if (!ctx || !ctx.quotaKey || !ctx.env.QUOTA) return;
	try {
		const raw = await ctx.env.QUOTA.get(ctx.quotaKey);
		const cur = raw ? JSON.parse(raw) : { req: 0, tok: 0 };
		await ctx.env.QUOTA.put(
			ctx.quotaKey,
			JSON.stringify({ req: cur.req + 1, tok: cur.tok + (tokens || 0) }),
			{ expirationTtl: KV_QUOTA_TTL }
		);
	} catch {
		// 记账失败不阻断业务
	}
}

function quotaErrorResponse(info) {
	const message =
		info.reason === "rate"
			? `Rate limit exceeded, retry after ${info.retryAfter}s`
			: `Daily quota exceeded, retry after ${info.retryAfter}s`;
	return jsonResponse(
		{ error: { message, type: "rate_limit_error", code: 429 } },
		429,
		rateLimitHeaders(info)
	);
}

// ==================== 基础工具 ====================

function resolveRoute(requestedModel) {
	return MODEL_ROUTES[requestedModel] || MODEL_ROUTES["auto"];
}

function buildMessages(messages) {
	return messages.map((msg) => {
		const m = { role: msg.role };

		if (msg.content !== null && msg.content !== undefined) {
			if (typeof msg.content === "string") {
				m.content = msg.content;
			} else {
				const textParts = [];
				const imageParts = [];
				for (const part of msg.content) {
					if (part.type === "text" && part.text) {
						textParts.push(part.text);
					} else if (part.type === "image_url" && part.image_url) {
						imageParts.push({ type: "image_url", image_url: part.image_url });
					}
				}
				if (imageParts.length > 0) {
					m.content = [
						...textParts.map((t) => ({ type: "text", text: t })),
						...imageParts,
					];
				} else {
					m.content = textParts.join("\n");
				}
			}
		} else {
			m.content = null;
		}

		if (msg.tool_calls) m.tool_calls = msg.tool_calls;
		if (msg.tool_call_id) m.tool_call_id = msg.tool_call_id;
		if (msg.name) m.name = msg.name;

		return m;
	});
}

function jsonResponse(data, status = 200, extraHeaders = {}) {
	return new Response(JSON.stringify(data), {
		status,
		headers: {
			"Content-Type": "application/json",
			"Access-Control-Allow-Origin": "*",
			"Access-Control-Allow-Methods": "GET, POST, OPTIONS",
			"Access-Control-Allow-Headers": "*",
			...extraHeaders,
		},
	});
}

function errorResponse(message, status = 400, type = "invalid_request_error") {
	return jsonResponse({ error: { message, type, code: status } }, status);
}

function handleOptions() {
	return new Response(null, {
		status: 204,
		headers: {
			"Access-Control-Allow-Origin": "*",
			"Access-Control-Allow-Methods": "GET, POST, OPTIONS",
			"Access-Control-Allow-Headers": "*",
			"Access-Control-Max-Age": "86400",
		},
	});
}

function generateId() {
	return `chatcmpl-${crypto.randomUUID().replace(/-/g, "").slice(0, 24)}`;
}

// ==================== 上游调用 ====================

async function callZen(env, base, model, body, displayModel, ctx) {
	const apiKey = env.OPENCODE_API_KEY || OPENCODE_DEFAULT_API_KEY;

	const messages = buildMessages(body.messages);
	const requestBody = {
		model,
		messages,
		stream: body.stream || false,
		...(body.temperature !== undefined ? { temperature: body.temperature } : {}),
		...(body.max_tokens !== undefined ? { max_tokens: body.max_tokens } : {}),
		...(body.top_p !== undefined ? { top_p: body.top_p } : {}),
		...(body.frequency_penalty !== undefined ? { frequency_penalty: body.frequency_penalty } : {}),
		...(body.presence_penalty !== undefined ? { presence_penalty: body.presence_penalty } : {}),
		...(body.stop ? { stop: body.stop } : {}),
		...(body.tools ? { tools: body.tools } : {}),
		...(body.tool_choice ? { tool_choice: body.tool_choice } : {}),
		...(body.response_format ? { response_format: body.response_format } : {}),
		...(body.reasoning_effort !== undefined ? { reasoning_effort: body.reasoning_effort } : {}),
	};

	const upstream = await fetch(`${base}/chat/completions`, {
		method: "POST",
		headers: {
			"Content-Type": "application/json",
			"Authorization": `Bearer ${apiKey}`,
		},
		body: JSON.stringify(requestBody),
	});

	if (!upstream.ok) {
		const errText = await upstream.text();
		throw new Error(`OpenCode ${upstream.status}: ${errText}`);
	}

	if (body.stream) {
		return handleUpstreamStream(upstream, displayModel, ctx);
	}

	const result = await upstream.json();
	return jsonResponse(rewriteNonStreamResponse(result, displayModel), 200, ctx.rateHeaders);
}

async function callNvidia(env, route, body, displayModel, ctx) {
	const apiKey = env.NVIDIA_API_KEY;
	if (!apiKey) throw new Error("NVIDIA_API_KEY not configured");

	const messages = buildMessages(body.messages);
	const requestBody = {
		model: route.model,
		messages,
		stream: body.stream || false,
		...(body.temperature !== undefined ? { temperature: body.temperature } : {}),
		...(body.max_tokens !== undefined ? { max_tokens: body.max_tokens } : {}),
		...(body.top_p !== undefined ? { top_p: body.top_p } : {}),
		...(body.frequency_penalty !== undefined ? { frequency_penalty: body.frequency_penalty } : {}),
		...(body.presence_penalty !== undefined ? { presence_penalty: body.presence_penalty } : {}),
		...(body.stop ? { stop: body.stop } : {}),
		...(body.tools ? { tools: body.tools } : {}),
		...(body.tool_choice ? { tool_choice: body.tool_choice } : {}),
		...(body.response_format ? { response_format: body.response_format } : {}),
		...(route.enableThinking ? { chat_template_kwargs: { thinking: true, reasoning_effort: "high" } } : {}),
	};

	const upstream = await fetch(`${NVIDIA_BASE}/chat/completions`, {
		method: "POST",
		headers: {
			"Content-Type": "application/json",
			"Authorization": `Bearer ${apiKey}`,
		},
		body: JSON.stringify(requestBody),
	});

	if (!upstream.ok) {
		const errText = await upstream.text();
		throw new Error(`NVIDIA ${upstream.status}: ${errText}`);
	}

	if (body.stream) {
		return handleUpstreamStream(upstream, displayModel, ctx);
	}

	const result = await upstream.json();
	return jsonResponse(rewriteNonStreamResponse(result, displayModel), 200, ctx.rateHeaders);
}

function rewriteNonStreamResponse(result, displayModel) {
	const choice = result.choices?.[0];
	if (!choice) {
		return {
			id: generateId(),
			object: "chat.completion",
			created: Math.floor(Date.now() / 1000),
			model: displayModel,
			choices: [{ index: 0, message: { role: "assistant", content: "" }, finish_reason: "stop" }],
			usage: { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 },
		};
	}

	const msg = {
		role: "assistant",
		content: choice.message?.content ?? "",
	};

	if (choice.message?.reasoning_content) {
		msg.reasoning_content = choice.message.reasoning_content;
	}

	if (choice.message?.tool_calls) {
		msg.tool_calls = choice.message.tool_calls;
	}

	return {
		id: result.id || generateId(),
		object: "chat.completion",
		created: result.created || Math.floor(Date.now() / 1000),
		model: displayModel,
		choices: [{
			index: 0,
			message: msg,
			finish_reason: choice.finish_reason || "stop",
		}],
		usage: result.usage || { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 },
	};
}

function handleUpstreamStream(upstream, displayModel, ctx) {
	const id = generateId();
	const created = Math.floor(Date.now() / 1000);
	const encoder = new TextEncoder();

	const readable = new ReadableStream({
		async start(controller) {
			const sendSSE = (data) => {
				controller.enqueue(encoder.encode(`data: ${JSON.stringify(data)}\n\n`));
			};

			sendSSE({
				id, object: "chat.completion.chunk", created, model: displayModel,
				choices: [{ index: 0, delta: { role: "assistant", content: "" }, finish_reason: null }],
			});

			let accChars = 0; // 累计输出字符数，用于流式时估算 token
			let finalUsage = null; // 上游若在流尾返回 usage，则优先用它记账

			try {
				const reader = upstream.body.getReader();
				const decoder = new TextDecoder();
				let buffer = "";

				while (true) {
					const { done, value } = await reader.read();
					if (done) break;
					buffer += decoder.decode(value, { stream: true });

					const lines = buffer.split("\n");
					buffer = lines.pop() || "";

					for (const line of lines) {
						const trimmed = line.trim();
						if (!trimmed || !trimmed.startsWith("data:")) continue;
						const dataStr = trimmed.slice(5).trim();
						if (dataStr === "[DONE]") continue;

						try {
							const parsed = JSON.parse(dataStr);
							if (parsed.usage) finalUsage = parsed.usage;
							const choice = parsed.choices?.[0];
							if (!choice) continue;

							const upstreamDelta = choice.delta;
							if (!upstreamDelta) continue;

							const delta = {};

							if (upstreamDelta.content) {
								delta.content = upstreamDelta.content;
								accChars += upstreamDelta.content.length;
							}
							if (upstreamDelta.reasoning_content) delta.reasoning_content = upstreamDelta.reasoning_content;
							if (upstreamDelta.reasoning) delta.reasoning_content = upstreamDelta.reasoning;
							if (upstreamDelta.tool_calls) delta.tool_calls = upstreamDelta.tool_calls;
							if (upstreamDelta.function_call) delta.function_call = upstreamDelta.function_call;

							if (Object.keys(delta).length > 0) {
								sendSSE({
									id, object: "chat.completion.chunk", created, model: displayModel,
									choices: [{ index: 0, delta, finish_reason: choice.finish_reason || null }],
								});
							} else if (choice.finish_reason) {
								sendSSE({
									id, object: "chat.completion.chunk", created, model: displayModel,
									choices: [{ index: 0, delta: {}, finish_reason: choice.finish_reason }],
								});
							}
						} catch {
							// skip malformed
						}
					}
				}
			} catch {
				// stream error
			} finally {
				// 流结束（含客户端中断）后记账：有 usage 用 usage，否则按字符数估算
				const u = finalUsage;
				const tokens = u
					? (u.prompt_tokens || 0) + (u.completion_tokens || 0)
					: Math.ceil(accChars / 4);
				await recordUsage(ctx, tokens);
			}

			sendSSE({
				id, object: "chat.completion.chunk", created, model: displayModel,
				choices: [{ index: 0, delta: {}, finish_reason: "stop" }],
				...(finalUsage ? { usage: finalUsage } : {}),
			});
			controller.enqueue(encoder.encode("data: [DONE]\n\n"));
			controller.close();
		},
	});

	return new Response(readable, {
		headers: {
			"Content-Type": "text/event-stream",
			"Cache-Control": "no-cache",
			Connection: "keep-alive",
			"Access-Control-Allow-Origin": "*",
			...ctx.rateHeaders,
		},
	});
}

async function callProvider(env, provider, route, body, displayModel, ctx) {
	switch (provider) {
		case "opencode-free":
			return callZen(env, OPENCODE_FREE_BASE, OPENCODE_FREE_MODEL, body, displayModel, ctx);
		case "opencode":
			return callZen(env, OPENCODE_BASE, route.model, body, displayModel, ctx);
		case "nvidia":
			return callNvidia(env, route, body, displayModel, ctx);
	}
}

function getFallbackRoute(provider) {
	switch (provider) {
		case "nvidia": return { provider: "nvidia", model: NVIDIA_FALLBACK_MODEL };
		case "opencode": return { provider: "opencode", model: OPENCODE_MODEL };
		default: return { provider: "opencode-free", model: OPENCODE_FREE_MODEL };
	}
}

async function handleChatCompletions(request, env) {
	let body;
	try {
		body = await request.json();
	} catch {
		return errorResponse("Invalid JSON body");
	}

	if (!body.messages || body.messages.length === 0) {
		return errorResponse("messages is required and must not be empty");
	}

	// ---- 限流与每日配额检查（只对 chat/completions 生效） ----
	const uid = await identifyUser(request);
	const limits = defaultLimits(env);
	const quota = await checkQuota(env, uid, limits);
	if (!quota.ok) {
		return quotaErrorResponse(quota);
	}

	const ctx = {
		env,
		quotaKey: quota.quotaKey,
		rateHeaders: rateLimitHeaders({
			limit: quota.limit,
			remaining: quota.remaining,
			reset: quota.dailyReset,
		}),
	};

	const displayModel = body.model || "auto";
	const route = resolveRoute(displayModel);

	const providers = [route.provider, ...FALLBACK_CHAIN.filter((p) => p !== route.provider)];

	for (const provider of providers) {
		const currentRoute = provider === route.provider ? route : getFallbackRoute(provider);
		try {
			const resp = await callProvider(env, provider, currentRoute, body, displayModel, ctx);

			// 非流式：拿到响应后按 usage 记账（克隆响应，不影响返回给客户端）
			if (!body.stream && ctx.quotaKey) {
				resp.clone()
					.json()
					.then((data) => {
						const u = data?.usage;
						const tokens = u ? (u.prompt_tokens || 0) + (u.completion_tokens || 0) : 0;
						return recordUsage(ctx, tokens);
					})
					.catch(() => {});
			}

			return resp;
		} catch (e) {
			console.log(`Provider ${provider} failed: ${e.message}, trying next...`);
			continue;
		}
	}

	return errorResponse("All providers failed", 503, "server_error");
}

function handleModels() {
	const models = Object.keys(MODEL_ROUTES).map((id) => {
		const route = MODEL_ROUTES[id];
		return {
			id,
			object: "model",
			created: 1700000000,
			owned_by: route.provider,
		};
	});

	return jsonResponse({
		object: "list",
		data: models,
	});
}

// 查看每日各用户用量（需要 ADMIN_KEY）
async function handleAdminUsage(request, env, url) {
	if (!env.QUOTA) {
		return errorResponse("QUOTA KV binding not configured", 503, "server_error");
	}
	const auth = request.headers.get("authorization") || "";
	const token = auth.replace(/^Bearer\s+/i, "").trim();
	if (!env.ADMIN_KEY || token !== env.ADMIN_KEY) {
		return errorResponse("Unauthorized", 401, "authentication_error");
	}

	const date = url.searchParams.get("date") || utcDateString();
	const prefix = `quota:${date}:`;
	const { keys } = await env.QUOTA.list({ prefix });

	const usage = [];
	for (const k of keys) {
		const raw = await env.QUOTA.get(k.name);
		usage.push({ user: k.name.slice(prefix.length), ...(raw ? JSON.parse(raw) : {}) });
	}
	usage.sort((a, b) => (b.req || 0) - (a.req || 0));

	return jsonResponse({ date, usage });
}

export default {
	async fetch(request, env) {
		const url = new URL(request.url);

		if (request.method === "OPTIONS") {
			return handleOptions();
		}

		const path = url.pathname;

		if (request.method === "POST") {
			if (path === "/v1/chat/completions") {
				return handleChatCompletions(request, env);
			}
		}

		if (request.method === "GET") {
			if (path === "/v1/models" || path === "/v1/models/") {
				return handleModels();
			}
			if (path === "/v1/admin/usage") {
				return handleAdminUsage(request, env, url);
			}
		}

		if (path === "/" || path === "") {
			return jsonResponse({
				service: "openai-proxy",
				description: "OpenAI-compatible API proxy with fallback",
				providers: {
					"opencode-free": { base: OPENCODE_FREE_BASE, models: ["deepseek-v4-flash-free"], features: ["free-tier", "thinking/reasoning", "tool_calls"] },
					opencode: { base: OPENCODE_BASE, models: ["deepseek-v4-flash"], features: ["thinking/reasoning", "tool_calls"] },
					nvidia: { base: NVIDIA_BASE, models: ["deepseek-ai/deepseek-v4-flash"], features: ["thinking/reasoning", "tool_calls"] },
				},
				fallback_order: ["opencode-free", "opencode", "nvidia"],
				model_routing: Object.fromEntries(
					Object.entries(MODEL_ROUTES).map(([k, v]) => [k, `${v.provider}/${v.model}${v.enableThinking ? " (thinking)" : ""}`]),
				),
				rate_limiting: {
					per_minute: "RATE_LIMIT_PER_MINUTE (default 30, 0=off)",
					daily_requests: "DAILY_REQUESTS (default 300, 0=off)",
					daily_tokens: "DAILY_TOKENS (default 1000000, 0=off)",
					identity: "Bearer API key (SHA-256) or client IP",
					binding: "RATE_LIMITER (optional, native) / QUOTA KV (required for daily quota)",
				},
				endpoints: {
					"POST /v1/chat/completions": "Chat completions (streaming + reasoning + tool_calls, rate-limited)",
					"GET /v1/models": "List available models",
					"GET /v1/admin/usage": "Daily usage per user (requires ADMIN_KEY)",
				},
				env_required: ["NVIDIA_API_KEY"],
				env_optional: ["OPENCODE_API_KEY (defaults to the built-in key)", "RATE_LIMIT_PER_MINUTE", "DAILY_REQUESTS", "DAILY_TOKENS", "ADMIN_KEY"],
				bindings_required: ["QUOTA (KV namespace, needed for daily quota)"],
				bindings_optional: ["RATE_LIMITER (native rate limit binding; falls back to in-memory)"],
				note: "Fallback chain: opencode-free (zen/v1) → opencode (zen/go/v1) → nvidia. Rate limit: native binding or in-memory per minute; daily quota: KV counters per user.",
			});
		}

		return errorResponse("Not found", 404, "not_found");
	},
};
