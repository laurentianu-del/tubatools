import { test } from "node:test";
import assert from "node:assert/strict";
import { conversationFingerprintInput, requestCacheKey, orderProviders } from "../src/index.js";

// 缓存优化纯函数单测：会话指纹稳定性 / 响应缓存键 / 粘性排序
// （不依赖 wrangler / KV，本地 node --test 即可运行）

const route = { provider: "opencode-free", model: "deepseek-v4-flash-free" };

function agentBody(round, history) {
	return {
		model: "auto",
		stream: true,
		temperature: 0.4,
		messages: [
			{ role: "system", content: "图吧助手系统提示词" },
			...history,
			{ role: "user", content: `第 ${round} 轮提问` },
		],
		tools: [{ type: "function", function: { name: "run_command", description: "运行命令" } }],
	};
}

test("会话指纹：多轮 agent 请求指纹一致（前缀缓存可跨轮命中）", () => {
	const round1 = conversationFingerprintInput(route, agentBody(1, []));
	const round2 = conversationFingerprintInput(route, agentBody(2, [
		{ role: "assistant", content: "", tool_calls: [{ id: "c1", type: "function", function: { name: "run_command", arguments: "{}" } }] },
		{ role: "tool", tool_call_id: "c1", content: "ok" },
	]));
	const round3 = conversationFingerprintInput(route, agentBody(3, [
		{ role: "assistant", content: "", tool_calls: [{ id: "c1", type: "function", function: { name: "run_command", arguments: "{}" } }] },
		{ role: "tool", tool_call_id: "c1", content: "ok" },
		{ role: "assistant", content: "完成" },
	]));
	assert.equal(round1, round2);
	assert.equal(round2, round3);
});

test("会话指纹：不同系统提示词（不同会话）指纹不同", () => {
	const a = conversationFingerprintInput(route, { messages: [{ role: "system", content: "提示词A" }, { role: "user", content: "hi" }] });
	const b = conversationFingerprintInput(route, { messages: [{ role: "system", content: "提示词B" }, { role: "user", content: "hi" }] });
	assert.notEqual(a, b);
});

test("会话指纹：确定性（同输入两次输出相同）", () => {
	const body = agentBody(1, []);
	assert.equal(conversationFingerprintInput(route, body), conversationFingerprintInput(route, body));
});

test("响应缓存键：完全相同请求同键、任一差异不同键", () => {
	const body1 = { model: "auto", messages: [{ role: "user", content: "你好" }], temperature: 0.3 };
	const body2 = { model: "auto", messages: [{ role: "user", content: "你好" }], temperature: 0.3 };
	const body3 = { model: "auto", messages: [{ role: "user", content: "你好呀" }], temperature: 0.3 };
	assert.equal(requestCacheKey(route, body1), requestCacheKey(route, body2));
	assert.notEqual(requestCacheKey(route, body1), requestCacheKey(route, body3));
});

test("粘性排序：粘性上游排最前，其余保持原序；无效粘性不改变原链", () => {
	assert.deepEqual(orderProviders("opencode", ["opencode-free", "opencode", "nvidia"]), ["opencode", "opencode-free", "nvidia"]);
	assert.deepEqual(orderProviders("nvidia", ["opencode-free", "opencode", "nvidia"]), ["nvidia", "opencode-free", "opencode"]);
	assert.deepEqual(orderProviders(null, ["opencode-free", "opencode", "nvidia"]), ["opencode-free", "opencode", "nvidia"]);
	assert.deepEqual(orderProviders("unknown", ["opencode-free", "opencode", "nvidia"]), ["opencode-free", "opencode", "nvidia"]);
});
