using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

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
