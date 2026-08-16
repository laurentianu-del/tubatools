## 修复 ARM64 CI 构建失败（exit code 216）

### 根因
`GenerateBundledToolCache` MSBuild target（`TubaWinUi3.WinUI3/TubaWinUi3.csproj:146-161`）在 publish 前无条件运行刚构建的 exe（`--build-tool-cache`）生成 `Metadata/tool_cache.json`。CI 矩阵在 x64 的 `windows-latest` runner 上构建 `win-arm64`，ARM64 exe 无法在 x64 Windows 上执行 → 退出码 216（`ERROR_EXE_MACHINE_TYPE_MISMATCH`）→ MSB3073 构建失败。`GenerateBundledIconCache`（PowerShell）不受影响，已成功生成 129 个图标。

另外：缓存内容依赖宿主 OS 架构（`ToolCatalog.PreferredArchPriority` 基于 `RuntimeInformation.OSArchitecture`，ToolCatalog.cs:692），因此只有当"宿主架构 == 目标包架构"时生成的缓存才是该包的正确归档选择。

### 改动 1（核心）：csproj 架构守卫
文件：`TubaWinUi3.WinUI3/TubaWinUi3.csproj`

在 `GenerateBundledToolCache` target 前新增 PropertyGroup 计算宿主/目标兼容性：
```xml
<PropertyGroup>
  <_HostOSArchitecture>$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)</_HostOSArchitecture>
  <_CanGenerateToolCache Condition="'$(RuntimeIdentifier)' == 'win-x64'  and '$(_HostOSArchitecture)' == 'X64'">true</_CanGenerateToolCache>
  <_CanGenerateToolCache Condition="'$(RuntimeIdentifier)' == 'win-arm64' and '$(_HostOSArchitecture)' == 'Arm64'">true</_CanGenerateToolCache>
  <_CanGenerateToolCache Condition="'$(RuntimeIdentifier)' == 'win-x86'  and ('$(_HostOSArchitecture)' == 'X64' or '$(_HostOSArchitecture)' == 'X86')">true</_CanGenerateToolCache>
</PropertyGroup>
```
然后：
- `<Exec>`（148 行）加 `Condition="'$(_CanGenerateToolCache)' == 'true'"`；
- 动态登记 ItemGroup（155-160 行）加同样 Condition（守卫跳过时不登记不存在的文件，与精简版行为一致——评估期 glob 已排除 tool_cache.json，csproj:50）；
- target 内加一条 `Condition="'$(_CanGenerateToolCache)' != 'true'"` 的 `<Message Importance="High">` 说明跳过原因；
- 更新 target 注释说明守卫逻辑。

跳过时包内无 `tool_cache.json` → 运行时 `TryLoadBundledCache` 失败 → 回退首次启动全量扫描（`GetAllToolsAsync` ③，与 lite 版一致），且 `RefreshCacheInBackground` 会在后台按真实 OS 架构自愈归档选择。此守卫一处修复：GitHub CI、GitCode 镜像 workflow、本地 `build-setup.ps1` / `build-store.ps1` / `build-msix-store.ps1` 的 arm64 publish。

### 改动 2：GitHub CI arm64 任务切换原生 ARM64 runner
文件：`.github/workflows/build-release.yml`（build 任务，64-74 行）

矩阵加 `os` 字段并按架构选择 runner：
```yaml
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        include:
          - arch: x64
            rid: win-x64
            os: windows-latest
          - arch: x86
            rid: win-x86
            os: windows-latest
          - arch: arm64
            rid: win-arm64
            os: windows-11-arm   # public preview，原生 arm64
```
这样 arm64 任务在 ARM64 机器上构建并运行 exe，`OSArchitecture=Arm64` → 生成**正确**的 arm64 随包缓存。风险与兜底：`windows-11-arm` 为 public preview，若该 runner 上某些步骤（fonttools/Inno-Setup-Action/setup-dotnet）异常，改动 1 的守卫保证任何宿主上构建都不会再报 216，必要时 arm64 任务可回退 windows-latest（仅丢失随包缓存）。GitCode 镜像 workflow（`.gitcode/workflows/build-release.yml`）不改（无 arm64 runner），靠守卫降级。

### 改动 3（顺带清理）：消除日志中的 CS0414 警告
文件：`TubaWinUi3.WinUI3/Pages/RogueCleanerPage.xaml.cs:49` — 删除赋值但从未使用的字段 `_cmSubModule`。

### 验证
1. `dotnet build`（本机 x64）— 不回归，缓存 target 照常执行。
2. `dotnet publish -c Release -r win-x64 -p:Platform=x64 -o out` — 仍生成 tool_cache.json（回归检查）。
3. `dotnet publish -c Release -r win-arm64 -p:Platform=ARM64 -o out_arm64`（本机 x64）— 修复前报 216，修复后跳过并打印提示，publish 成功。
4. `dotnet test` 确认无回归。
5. 触发 CI（workflow_dispatch）观察 arm64 job 在 windows-11-arm 上成功且生成缓存。