import json, subprocess, os

script_dir = os.path.dirname(os.path.abspath(__file__))
template_path = os.path.join(script_dir, "changelog-prompt.txt")

with open(template_path, "r", encoding="utf-8") as f:
    prompt = f.read()

prompt = prompt.replace("{{COMMIT_LOG}}", os.environ.get("COMMIT_LOG", ""))
prompt = prompt.replace("{{CODE_DIFF}}", os.environ.get("CODE_DIFF", ""))
prompt = prompt.replace("{{CODE_DIFF_DETAIL}}", os.environ.get("CODE_DIFF_DETAIL", ""))

api_key = os.environ["DEEPSEEK_API_KEY"]

payload = json.dumps({
    "model": "deepseek-v4-pro",
    "messages": [
        {"role": "system", "content": "你是一位资深的技术文档工程师，精通 .NET/WinUI 开发，擅长阅读代码 diff 并将其转化为用户友好的更新说明。你输出的更新日志将直接用于 GitHub Release 页面，面向普通用户。输出纯 Markdown 格式，不要用代码块包裹整体内容。"},
        {"role": "user", "content": prompt}
    ],
    "temperature": 0.3,
    "max_tokens": 4000
})

result = subprocess.run(
    ["curl", "-s", "https://api.deepseek.com/chat/completions",
     "-H", "Content-Type: application/json",
     "-H", f"Authorization: Bearer {api_key}",
     "-d", payload],
    capture_output=True, text=True
)

try:
    data = json.loads(result.stdout)
    content = data["choices"][0]["message"]["content"]
except Exception as e:
    content = f"AI 生成更新日志失败: {e}\n请查看 git 历史获取详细变更信息。"

with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
    f.write("result<<EOF\n")
    f.write(content + "\n")
    f.write("EOF\n")
