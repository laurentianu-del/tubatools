## 目标

修复 WebView2 报错「Microsoft Edge 无法读取和写入其数据目录：D:\APP\TubaWinUi3\TubaWinUi3.exe.WebView2\EBWebView」。

**根因**：应用是非打包 WinUI 3，代码从未指定 UserDataFolder，WebView2 默认在 exe 旁创建 `{exe}.WebView2` 数据目录（微软文档确认）。安装目录不可写（D:\APP 的 ACL/只读属性/同名占位文件等）时即报此错。与 D 盘本身无关，任何不可写安装目录都会触发。

**方案（已确认）**：微软官方推荐的"自定义 UDF 到 %LocalAppData%" 共享环境方案，把 WebView2 数据目录固定到永远可写的 `%LocalAppData%\TubaWinUi3\WebView2`，与安装位置解耦。

## 改动内容

### 1. 新增 `TubaWinUi3.WinUI3/Services/WebView2EnvironmentService.cs`
静态类 + `Lazy<Task<CoreWebView2Environment>>`（防并发重复创建），中文 XML 注释与仓库风格一致：

```csharp
CoreWebView2Environment.CreateAsync(
    browserExecutableFolder: null,
    userDataFolder: Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "WebView2"));
```

### 2. 改 6 处调用点（5 个文件），把共享环境传入 `EnsureCoreWebView2Async(env)`
| 文件 | 行 | 现状 |
|---|---|---|
| `Controls/AgentBrowser/BrowserWindow.xaml.cs` | 59、132 | `await Web.EnsureCoreWebView2Async()` |
| `Pages/BrowserPage.xaml.cs` | 44 | `await WebView.EnsureCoreWebView2Async()` |
| `Pages/ServiceCenterPage.xaml.cs` | 119 | `await WebView.EnsureCoreWebView2Async()` |
| `Pages/PerformanceBenchmarkPage.cs` | 1179、1416 | `await webView/pdfWv.EnsureCoreWebView2Async()` |
| `Services/BuiltinTools/LanFileShareTool.cs` | 239 | `await _webView.EnsureCoreWebView2Async()` |

统一改为 `await xxx.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());`，各文件补 `using TubaWinUi3.Services;`（如缺失）。

### 3. 顺带更新过时注释
`BrowserWindow.xaml.cs:58` 注释「使用默认 WebView2 环境（应用独立用户数据目录…）」改为说明自定义目录。

## 行为说明
- 所有 WebView2 实例共享同一 UDF/同一浏览器进程（微软推荐做法）。
- 旧目录 `TubaWinUi3.exe.WebView2` 不删除、不迁移；新目录首次生成，登录态/Cookie 重置一次（可接受，AI 浏览器本就是独立会话）。
- 现有 5 处 try/catch 错误 UI 不变。

## 验证
1. `dotnet build`（x64 Debug）通过。
2. `dotnet run` 手动验证：AI 浏览器、服务中心品牌页、文件传输、性能基准页，确认 `%LocalAppData%\TubaWinUi3\WebView2` 正常生成且不再弹报错。
3. `dotnet test` 确认现有测试不受影响（无 WebView2 相关单测）。
4. 不提交 git（除非你要求）。