# AI 助手提示词工程优化方案

## 诊断结论（为什么调错工具、步骤多、完成慢）

1. `AgentPrompts.SystemPrompt` 自相矛盾：一边要求"每个建议后询问'需要我帮你执行吗'"，一边又说写操作可直接调用（有确认卡片）→ 模型经常以文字建议收尾而不调工具，多一轮往返。
2. 强制输出"### 分析结果/解决方案/我可以帮你"模板 + "宁可多搜一次"→ 诱导写文本、无限搜索、不收尾。
3. 没有并行调用指令 → 模型一次只调一个工具，一轮一步。
4. 系统提示词被巨型目录稀释：`BuildSystemContext()` 把整个工具箱目录（约 100-120 个工具+简介、39 个内置工具）塞进提示词，整体约 5.5k-6.5k tokens，每轮请求都重发（上限 30 轮）→ 又慢又烧 token，行为规则被稀释导致调错工具。
5. `create_plan` 对任何"复杂任务"都强制先计划后批准 → 多一步往返。

安全机制本身没问题：默认模式弹确认卡片、完全访问模式直接执行——所以提示词只需"直接调用工具"，安全交给模式系统。

## 改动内容

### 1. 重写 `TubaWinUi3.WinUI3/Services/Agent/AgentPrompts.cs` 的 SystemPrompt（核心）
新结构（更短、无矛盾、指令化，全部中文）：

- **身份与目标**：图吧助手，以最少步骤最快完成任务。
- **执行铁律**：
  - 只读工具（get_*/list_*/read_*/web_search/fetch_page/find_files/read_reg/browser_* 等）**直接调用**，禁止用文字建议代替调用。
  - 写工具（run_command/run_powershell/run_cli_tool/write_reg/write_file/edit_file/append_file/delete_file/move_file/copy_file/download_file/launch_tool）**直接调用并填写 reason**，系统按当前模式处理（默认弹确认卡片 / 完全访问直接执行）。**禁止调用前询问"需要我帮你执行吗"**。
  - 任务完成立即输出简洁结论并停止；工具失败 → 读错误信息、修正参数或换方法，同一方式最多重试 1 次。
- **并行调用（减少步骤的关键）**：互不依赖的工具必须在同一条回复里一次性并行调用（例：同时调 get_hardware_info + web_search）；有依赖才按序等待。
- **工具调用准确性**：参数名严格按函数定义（JSON Schema）；写工具必填 reason；launch_tool 名称不确定先 list_tools；run_cli_tool 前必须先 get_cli_tool_usage；浏览器每次操作前重新 browser_get_page。
- **搜索策略**：仅当需要最新/不确定信息时 web_search（硬件评测、驱动、价格、新闻等），中英混合关键词，同一任务最多 3 次搜索，结果足够即停，摘要不足才 fetch_page。
- **计划**：仅 ≥4 步且有依赖的复杂任务、或用户明确要求时才 create_plan；简单任务直接做。
- **输出**：简洁中文结论 + 关键结果；推荐软件时可用 [RECOMMEND_TOOL]/[WEBSITE]/[SETTING] 卡片标记（改为**可选**，不再是强制模板，UI 渲染不受影响）；删除强制"### 分析结果/解决方案/我可以帮你"模板和"宁可多搜一次"。
- 压缩保留：文件操作规范、浏览器自动化、工具箱 CLI 规范。

### 2. 瘦身 `AiAssistantService.BuildSystemContext()`（同文件）
- Tools 目录工具：**只列工具名**（分类分组，去掉每条简介），并注明"详细简介用 list_tools 查询"；约 3k tokens → 约 1k。
- 内置工具（39 个）：保留一行简介（本身很短，且无对应查询工具）。
- 效果：系统提示词整体 5.5k-6.5k tokens → 约 3k，每轮请求都省约 3k tokens，注意力更集中。

### 3. 工具 [Description] 微调（提高调用准确率）
- `launch_tool`：注明 toolName 必须与 list_tools 返回名称一致，不确定先查。
- `edit_file`：注明 oldText 必须与文件原文完全一致（含空白）。
- `run_command` / `run_powershell`：注明多条命令可用 `&&` 合并一次执行。
- `web_search`：注明关键词可中英混合。

### 4. 验证
- `dotnet build`（TubaWinUi3.WinUI3 项目）+ `dotnet test`（全绿；不改动 CliToolboxCatalog，其精确字符串测试不受影响）。
- 你运行 app 实测几个典型问题（新电脑验机 / 电脑卡顿 / 搜索某 CPU 评测 / 清理垃圾）。

## 明确不做
- 不改 `CliToolboxCatalog.BuildIndexContext()`（有精确字符串测试约束，且本身已精简）。
- 不动确认卡片机制、完全访问模式、工具注册表。
- 不清理遗留死代码（AiAssistantService 旧 SystemPrompt + 旧 Agent 循环，已无任何调用点）——本次不碰，可后续单独处理。
