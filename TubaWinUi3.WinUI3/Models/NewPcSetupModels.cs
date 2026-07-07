using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

public abstract class PcSetupAction
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public string Group { get; init; } = "";
    public bool IsSelected { get; set; }
    public bool IsDangerous { get; init; }
    public bool RequiresAdmin { get; init; }
    public PcSetupActionState State { get; set; } = PcSetupActionState.Pending;
    public string? StatusText { get; set; }

    public abstract Task<PcSetupActionResult> ExecuteAsync(IProgress<string>? progress, CancellationToken ct);
    public abstract string ToPowerShell();
}

public enum PcSetupActionState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}

public sealed class PcSetupActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

public sealed class WingetInstallAction : PcSetupAction
{
    public required string PackageId { get; init; }

    public override async Task<PcSetupActionResult> ExecuteAsync(IProgress<string>? progress, CancellationToken ct)
    {
        State = PcSetupActionState.Running;
        StatusText = "正在安装...";
        try
        {
            var result = await WingetService.InstallAsync(PackageId, new Progress<WingetInstallProgress>(p =>
            {
                StatusText = p.StatusLine;
            }), ct);
            if (result.Success)
            {
                State = PcSetupActionState.Succeeded;
                StatusText = "安装成功";
                return new PcSetupActionResult { Success = true, Message = "安装成功" };
            }
            State = PcSetupActionState.Failed;
            StatusText = result.Message;
            return new PcSetupActionResult { Success = false, Message = result.Message };
        }
        catch (OperationCanceledException)
        {
            State = PcSetupActionState.Skipped;
            StatusText = "已取消";
            return new PcSetupActionResult { Success = false, Message = "已取消" };
        }
        catch (Exception ex)
        {
            State = PcSetupActionState.Failed;
            StatusText = ex.Message;
            return new PcSetupActionResult { Success = false, Message = ex.Message };
        }
    }

    public override string ToPowerShell()
    {
        return $"Write-Host \"  安装 {Name}...\" -ForegroundColor Yellow\nwinget install --id {PackageId} --silent --accept-package-agreements --accept-source-agreements";
    }
}

public sealed class RegistryAction : PcSetupAction
{
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required object Value { get; init; }
    public required Microsoft.Win32.RegistryValueKind ValueKind { get; init; }
    public required string Hive { get; init; }

    public override Task<PcSetupActionResult> ExecuteAsync(IProgress<string>? progress, CancellationToken ct)
    {
        State = PcSetupActionState.Running;
        StatusText = "正在修改注册表...";
        try
        {
            var hive = Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Microsoft.Win32.Registry.CurrentUser
                : Microsoft.Win32.Registry.LocalMachine;
            using var key = hive.OpenSubKey(KeyPath, writable: true);
            if (key is null)
            {
                using var newKey = hive.CreateSubKey(KeyPath);
                newKey.SetValue(ValueName, Value, ValueKind);
            }
            else
            {
                key.SetValue(ValueName, Value, ValueKind);
            }
            State = PcSetupActionState.Succeeded;
            StatusText = "已完成";
            return Task.FromResult(new PcSetupActionResult { Success = true, Message = "注册表修改成功" });
        }
        catch (Exception ex)
        {
            State = PcSetupActionState.Failed;
            StatusText = ex.Message;
            return Task.FromResult(new PcSetupActionResult { Success = false, Message = ex.Message });
        }
    }

    public override string ToPowerShell()
    {
        var psHive = Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? "HKCU:" : "HKLM:";
        var psPath = $"{psHive}\\{KeyPath}";
        var valStr = ValueKind == Microsoft.Win32.RegistryValueKind.DWord
            ? (Value is int i ? i.ToString() : Value.ToString())
            : $"'{Value}'";
        var typeStr = ValueKind == Microsoft.Win32.RegistryValueKind.DWord ? "DWord" : "String";
        return $"Write-Host \"  {Name}...\" -ForegroundColor Yellow\nif (-not (Test-Path '{psPath}')) {{ New-Item -Path '{psPath}' -Force | Out-Null }}\nSet-ItemProperty -Path '{psPath}' -Name '{ValueName}' -Value {valStr} -Type {typeStr}";
    }
}

public sealed class PowerShellAction : PcSetupAction
{
    public required string Script { get; init; }
    public bool UseRunAs { get; init; }

    public override async Task<PcSetupActionResult> ExecuteAsync(IProgress<string>? progress, CancellationToken ct)
    {
        State = PcSetupActionState.Running;
        StatusText = "正在执行...";
        try
        {
            if (UseRunAs)
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"pcsetup_{Guid.NewGuid():N}.ps1");
                await File.WriteAllTextAsync(tempFile, Script, ct);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                var process = System.Diagnostics.Process.Start(psi);
                if (process is null)
                {
                    State = PcSetupActionState.Failed;
                    StatusText = "无法启动进程";
                    return new PcSetupActionResult { Success = false, Message = "无法启动进程" };
                }
                await process.WaitForExitAsync(ct);
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                if (process.ExitCode == 0)
                {
                    State = PcSetupActionState.Succeeded;
                    StatusText = "已完成";
                    return new PcSetupActionResult { Success = true, Message = "执行成功" };
                }
                State = PcSetupActionState.Failed;
                StatusText = $"退出码: {process.ExitCode}";
                return new PcSetupActionResult { Success = false, Message = $"退出码: {process.ExitCode}" };
            }
            else
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{Script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = System.Diagnostics.Process.Start(psi);
                if (process is null)
                {
                    State = PcSetupActionState.Failed;
                    StatusText = "无法启动进程";
                    return new PcSetupActionResult { Success = false, Message = "无法启动进程" };
                }
                await process.WaitForExitAsync(ct);
                if (process.ExitCode == 0)
                {
                    State = PcSetupActionState.Succeeded;
                    StatusText = "已完成";
                    return new PcSetupActionResult { Success = true, Message = "执行成功" };
                }
                var err = await process.StandardError.ReadToEndAsync(ct);
                State = PcSetupActionState.Failed;
                StatusText = err;
                return new PcSetupActionResult { Success = false, Message = err };
            }
        }
        catch (OperationCanceledException)
        {
            State = PcSetupActionState.Skipped;
            StatusText = "已取消";
            return new PcSetupActionResult { Success = false, Message = "已取消" };
        }
        catch (Exception ex)
        {
            State = PcSetupActionState.Failed;
            StatusText = ex.Message;
            return new PcSetupActionResult { Success = false, Message = ex.Message };
        }
    }

    public override string ToPowerShell()
    {
        return $"Write-Host \"  {Name}...\" -ForegroundColor Yellow\n{Script}";
    }
}

public sealed class BurnSample
{
    public DateTime Time { get; init; }
    public float Temp { get; init; }
    public float Power { get; init; }
    public float Clock { get; init; }
    public float Load { get; init; }
}

public sealed class CatalogCategory
{
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public List<CatalogPackage> Packages { get; init; } = [];
    public List<CatalogSubCategory> SubCategories { get; init; } = [];
}

public sealed class CatalogSubCategory
{
    public required string Name { get; init; }
    public List<CatalogPackage> Packages { get; init; } = [];
}

public sealed class CatalogPackage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Desc { get; init; }
    public bool IsSelected { get; set; }
    public WingetInstallState State { get; set; } = WingetInstallState.NotInstalled;
    public string? StatusText { get; set; }
    public int Progress { get; set; }
}

public sealed class VisualPreset
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required Dictionary<string, bool> OptimizeItemStates { get; init; }
}

public sealed class StartupItem
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public bool WantDisable { get; set; }
}

public sealed class ServiceItem
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Recommendation { get; init; }
    public uint CurrentStartType { get; set; }
    public bool WantDisable { get; set; }
}

public sealed class DevLanguageGroup
{
    public required string Language { get; init; }
    public required string Glyph { get; init; }
    public required string Description { get; init; }
    public required List<SetupPackage> Packages { get; init; }
}

public sealed class SetupPackage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Glyph { get; init; }
    public string? Description { get; init; }
    public string? Source { get; init; }
    public string? InstallCommand { get; init; }
    public string? DependsOn { get; init; }
    public string? ManualUrl { get; init; }
    public bool IsSelected { get; set; }
    public WingetInstallState State { get; set; } = WingetInstallState.NotInstalled;
    public string? StatusText { get; set; }
    public int Progress { get; set; }
}

public sealed class SetupStepResult
{
    public int DisabledStartupItems { get; set; }
    public int OptimizedSettings { get; set; }
    public int InstalledSoftware { get; set; }
    public int SecuritySettings { get; set; }
    public List<string> DevConfigResults { get; set; } = [];
}

public enum SetupUserRole
{
    Daily,
    Developer,
    Gaming,
    Design
}

public sealed class OptimizeItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public string Group { get; init; } = "";
    public bool CurrentState { get; set; }
    public bool WantApply { get; set; }
}

public sealed class SecurityItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public bool CurrentState { get; set; }
    public bool WantApply { get; set; }
    public bool RequiresAdmin { get; init; }
    public bool IsDangerous { get; init; }
}
