using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TubaWinUi3.Models;

public sealed record DownloadQueueProgress(
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    double SpeedMbps,
    TimeSpan? EstimatedRemaining);

public enum DownloadItemState
{
    Queued,
    Resolving,
    Downloading,
    Paused,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public sealed record ResolvedDownloadUrl(string Url, string FileName, long Size = 0);

public sealed class DownloadQueueEntry
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Glyph { get; set; }
    public string DestinationPath { get; set; } = "";
    public string? DirectUrl { get; set; }
    public DownloadItemState State { get; set; }
    public string? ResolvedUrl { get; set; }
    public string? ResolvedFileName { get; set; }
    public long ResolvedSize { get; set; }
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public string? PostProcessorKey { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class PostProcessorRegistry
{
    private static readonly Dictionary<string, IDownloadPostProcessor> _processors = [];

    public static void Register(IDownloadPostProcessor processor)
    {
        _processors[processor.DisplayName] = processor;
    }

    public static IDownloadPostProcessor? Find(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return _processors.TryGetValue(key, out var p) ? p : null;
    }

    public static string? GetKey(IDownloadPostProcessor? processor)
    {
        if (processor is null) return null;
        return processor.DisplayName;
    }

    public static void RegisterDefaults()
    {
        Register(new ArchiveExtractProcessor());
        Register(new InstallerLaunchProcessor());
        Register(new MoveToDestinationProcessor());
        Register(new ToolsBundleExtractProcessor());
    }
}

public interface IDownloadPostProcessor
{
    string DisplayName { get; }
    Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct);
}

public sealed class ArchiveExtractProcessor : IDownloadPostProcessor
{
    public string DisplayName => "解压文件";
    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在解压...");
        await Task.Run(() =>
        {
            if (File.Exists(downloadedFilePath))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(downloadedFilePath, destinationPath, true);
                File.Delete(downloadedFilePath);
            }
        }, ct);
    }
}

public sealed class InstallerLaunchProcessor : IDownloadPostProcessor
{
    public string DisplayName => "运行安装程序";
    public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在启动安装程序...");
        var psi = new System.Diagnostics.ProcessStartInfo(downloadedFilePath)
        {
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
        return Task.CompletedTask;
    }
}

public sealed class MoveToDestinationProcessor : IDownloadPostProcessor
{
    public string DisplayName => "移动文件";
    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在移动文件...");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destinationPath);
            var destFile = Path.Combine(destinationPath, Path.GetFileName(downloadedFilePath));
            if (File.Exists(destFile)) File.Delete(destFile);
            File.Move(downloadedFilePath, destFile);
        }, ct);
    }
}

public sealed class DelegatePostProcessor : IDownloadPostProcessor
{
    private readonly Func<string, string, IProgress<string>?, CancellationToken, Task> _action;
    public string DisplayName { get; }

    public DelegatePostProcessor(string displayName,
        Func<string, string, IProgress<string>?, CancellationToken, Task> action)
    {
        DisplayName = displayName;
        _action = action;
    }

    public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
        => _action(downloadedFilePath, destinationPath, statusProgress, ct);
}

public sealed class ChainedPostProcessor : IDownloadPostProcessor
{
    private readonly IDownloadPostProcessor[] _processors;
    public string DisplayName { get; }

    public ChainedPostProcessor(string displayName, params IDownloadPostProcessor[] processors)
    {
        DisplayName = displayName;
        _processors = processors;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        var currentFile = downloadedFilePath;
        for (var i = 0; i < _processors.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            statusProgress?.Report($"{_processors[i].DisplayName} ({i + 1}/{_processors.Length})...");
            await _processors[i].ExecuteAsync(currentFile, destinationPath, statusProgress, ct);
            if (!File.Exists(currentFile) && i < _processors.Length - 1)
                currentFile = Directory.GetFiles(destinationPath).FirstOrDefault() ?? currentFile;
        }
    }
}

public sealed class UpdateInstallProcessor : IDownloadPostProcessor
{
    private readonly bool _isPortableMode;

    public string DisplayName => "准备安装更新";

    public UpdateInstallProcessor(bool isPortableMode)
    {
        _isPortableMode = isPortableMode;
    }

    public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        var isExe = downloadedFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var isZip = downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (isExe)
        {
            statusProgress?.Report("正在启动安装程序...");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = downloadedFilePath,
                UseShellExecute = true
            });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        else if (isZip && _isPortableMode)
        {
            statusProgress?.Report("正在打开文件夹...");
            var folder = Path.GetDirectoryName(downloadedFilePath)!;
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
        else
        {
            statusProgress?.Report("正在打开文件夹...");
            var folder = Path.GetDirectoryName(downloadedFilePath)!;
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }

        return Task.CompletedTask;
    }
}

public sealed class DownloadItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; }
    public string? Description { get; }
    public string? Glyph { get; }
    public string DestinationPath { get; }
    public object? Tag { get; }

    private DownloadItemState _state = DownloadItemState.Queued;
    public DownloadItemState State
    {
        get => _state;
        internal set { if (_state != value) { _state = value; OnPropertyChanged(); } }
    }

    private DownloadQueueProgress? _progress;
    public DownloadQueueProgress? Progress
    {
        get => _progress;
        internal set { _progress = value; OnPropertyChanged(); }
    }

    private string? _processingStatus;
    public string? ProcessingStatus
    {
        get => _processingStatus;
        internal set { if (_processingStatus != value) { _processingStatus = value; OnPropertyChanged(); } }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
    }

    private DateTimeOffset? _completedAt;
    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        internal set { _completedAt = value; OnPropertyChanged(); }
    }

    internal string? DirectUrl { get; }
    internal Func<CancellationToken, Task<ResolvedDownloadUrl>>? UrlResolver { get; }
    internal Func<CancellationToken, Task<List<ResolvedDownloadUrl>>>? MultiFileResolver { get; }
    internal string? AlternateUrl { get; }
    internal IDownloadPostProcessor? PostProcessor { get; }
    internal CancellationTokenSource? Cts { get; set; }

    internal string? ResolvedUrl { get; set; }
    internal string? ResolvedFileName { get; set; }
    internal long ResolvedSize { get; set; }
    internal long ResumePosition { get; set; }
    internal bool SupportsResume { get; set; }

    private DownloadItem(
        string displayName, string? directUrl,
        Func<CancellationToken, Task<ResolvedDownloadUrl>>? urlResolver,
        Func<CancellationToken, Task<List<ResolvedDownloadUrl>>>? multiFileResolver,
        string destinationPath, IDownloadPostProcessor? postProcessor,
        string? description, string? glyph, object? tag,
        string? alternateUrl = null)
    {
        DisplayName = displayName;
        DirectUrl = directUrl;
        UrlResolver = urlResolver;
        MultiFileResolver = multiFileResolver;
        AlternateUrl = alternateUrl;
        DestinationPath = destinationPath;
        PostProcessor = postProcessor;
        Description = description;
        Glyph = glyph;
        Tag = tag;
    }

    public static DownloadItem CreateDirect(
        string displayName, string downloadUrl, string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null, string? glyph = null, object? tag = null)
        => new(displayName, downloadUrl, null, null, destinationPath, postProcessor, description, glyph, tag);

    public static DownloadItem CreateWithResolver(
        string displayName,
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null, string? glyph = null, object? tag = null,
        string? alternateUrl = null)
        => new(displayName, null, urlResolver, null, destinationPath, postProcessor, description, glyph, tag, alternateUrl);

    public static DownloadItem CreateMultiFile(
        string displayName,
        Func<CancellationToken, Task<List<ResolvedDownloadUrl>>> multiFileResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null, string? glyph = null, object? tag = null)
        => new(displayName, null, null, multiFileResolver, destinationPath, postProcessor, description, glyph, tag);

    internal void SetState(DownloadItemState state) => State = state;
    internal void SetProgress(DownloadQueueProgress? progress) => Progress = progress;
    internal void SetProcessingStatus(string? status) => ProcessingStatus = status;
    internal void SetErrorMessage(string? message) => ErrorMessage = message;
    internal void SetCompleted()
    {
        CompletedAt = DateTimeOffset.Now;
        State = DownloadItemState.Completed;
    }

    internal void Reset()
    {
        State = DownloadItemState.Queued;
        Progress = null;
        ProcessingStatus = null;
        ErrorMessage = null;
        CompletedAt = null;
        Cts = new CancellationTokenSource();
        ResolvedUrl = null;
        ResolvedFileName = null;
        ResolvedSize = 0;
        ResumePosition = 0;
        SupportsResume = false;
    }

    internal void PrepareResume()
    {
        Cts = new CancellationTokenSource();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ToolsBundleExtractProcessor : IDownloadPostProcessor
{
    private const int MaxAttempts = 3;      // 首次 + 自动重试 2 次
    private const int RetryDelayMs = 500;
    private const int CleanupAttempts = 3;

    private readonly string? _version;

    public string DisplayName => "解压工具包";

    public ToolsBundleExtractProcessor(string? version = null)
    {
        _version = version;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在解压工具包...");
        await Task.Run(() => ExtractCore(downloadedFilePath, destinationPath, statusProgress), ct);
    }

    private void ExtractCore(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                statusProgress?.Report($"解压遇到文件占用，正在自动重试（第 {attempt}/{MaxAttempts} 次）...");
                Thread.Sleep(RetryDelayMs * attempt);
            }

            var extractDir = Path.Combine(Path.GetTempPath(), $"TubaWinUi3_Extract_{Guid.NewGuid():N}");
            try
            {
                ExtractOnce(downloadedFilePath, destinationPath, extractDir, statusProgress,
                    allowCopyFallback: attempt == MaxAttempts);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDeleteDirectory(extractDir);
                if (attempt >= MaxAttempts)
                {
                    var message = ex.InnerException?.Message ?? ex.Message;
                    throw new IOException($"解压工具包失败（已自动重试 {MaxAttempts - 1} 次）：{message}", ex);
                }
            }
        }

        if (lastError is not null) throw lastError;
    }

    private void ExtractOnce(string downloadedFilePath, string destinationPath, string extractDir,
        IProgress<string>? statusProgress, bool allowCopyFallback)
    {
        if (!File.Exists(downloadedFilePath))
            throw new FileNotFoundException("下载的文件不存在", downloadedFilePath);

        try
        {
            statusProgress?.Report("正在解压文件...");
            System.IO.Compression.ZipFile.ExtractToDirectory(downloadedFilePath, extractDir, true);
        }
        catch
        {
            TryDeleteDirectory(extractDir);
            throw;
        }

        var backupDir = destinationPath + "_bak";

        try
        {
            if (Directory.Exists(destinationPath))
            {
                TryDeleteDirectory(backupDir);
                Directory.Move(destinationPath, backupDir);
            }

            var destParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destParent))
                Directory.CreateDirectory(destParent);

            try
            {
                Directory.Move(extractDir, destinationPath);
            }
            catch
            {
                TryRestoreDirectory(backupDir, destinationPath);
                throw;
            }

            TryDeleteDirectory(backupDir);
            try { File.Delete(downloadedFilePath); } catch { }
            ApplyCompletedState(destinationPath);
        }
        catch
        {
            if (!allowCopyFallback) throw;

            // 兜底：目录原子替换行不通（文件被占用）时，逐文件复制覆盖
            statusProgress?.Report("正在使用文件拷贝模式完成安装...");
            TryRestoreDirectory(backupDir, destinationPath);
            try
            {
                CopyDirectoryContents(extractDir, destinationPath);
                TryDeleteDirectory(extractDir);
                try { File.Delete(downloadedFilePath); } catch { }
                ApplyCompletedState(destinationPath);
            }
            catch (Exception fallbackEx)
            {
                throw new IOException($"解压工具包失败：{fallbackEx.Message}", fallbackEx);
            }
        }
    }

    private void ApplyCompletedState(string destinationPath)
    {
        if (!string.IsNullOrEmpty(_version))
        {
            Services.AppSettings.Set("ToolsBundleVersion", _version);
        }

        Services.ToolCatalog.RefreshToolsRoot();

        // 强制刷新侧边栏 / 标签页的工具分类（MSIX 内核安装完成后立即生效）
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RefreshToolCategories);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        for (var i = 0; i < CleanupAttempts && Directory.Exists(dir); i++)
        {
            try
            {
                Directory.Delete(dir, true);
                return;
            }
            catch
            {
                if (i < CleanupAttempts - 1) Thread.Sleep(300);
            }
        }
    }

    private static void TryRestoreDirectory(string backupDir, string destinationPath)
    {
        if (Directory.Exists(destinationPath) && Directory.Exists(backupDir))
        {
            try { Directory.Delete(destinationPath, true); } catch { }
        }
        if (Directory.Exists(backupDir) && !Directory.Exists(destinationPath))
        {
            try { Directory.Move(backupDir, destinationPath); } catch { }
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destinationPath, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, target, true);
        }
    }
}

public sealed class CommunityToolInstallProcessor : IDownloadPostProcessor
{
    private readonly string _toolId;
    private readonly string _category;
    private readonly bool _isArchive;

    public string DisplayName => "安装社区工具";

    public CommunityToolInstallProcessor(string toolId, string category, bool isArchive)
    {
        _toolId = toolId;
        _category = category;
        _isArchive = isArchive;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var toolsRoot = Services.ToolCatalog.ToolsRoot;
            var categoryDir = Path.Combine(toolsRoot, _category);
            Directory.CreateDirectory(categoryDir);
            var toolDir = Path.Combine(categoryDir, _toolId);

            if (Directory.Exists(toolDir))
            {
                try { Directory.Delete(toolDir, true); } catch { }
            }
            Directory.CreateDirectory(toolDir);

            if (_isArchive)
            {
                statusProgress?.Report("正在解压...");
                System.IO.Compression.ZipFile.ExtractToDirectory(downloadedFilePath, toolDir, true);
                try { File.Delete(downloadedFilePath); } catch { }
            }
            else
            {
                var destPath = Path.Combine(toolDir, Path.GetFileName(downloadedFilePath));
                File.Move(downloadedFilePath, destPath, true);
            }

            Services.ToolCatalog.InvalidateTagsCache();
        }, ct);
    }
}
