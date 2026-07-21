using Microsoft.UI.Dispatching;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed record ToolUpdateEntry(
    string ToolName,
    string Match,
    int LocalVersion,
    int RemoteVersion,
    string RepoPath,
    string Category,
    string ToolDir);

public static class ToolUpdateService
{
    private static DispatcherQueue? _dispatcherQueue;
    private static readonly SemaphoreSlim _syncSemaphore = new(2);

    public static void Initialize(DispatcherQueue dq)
    {
        _dispatcherQueue = dq;
    }

    public static async Task<List<ToolUpdateEntry>?> CheckForToolUpdatesAsync(CancellationToken ct = default)
    {
        var remoteVersions = await ToolMetadataService.FetchRemoteToolsJsonAsync(ct);
        if (remoteVersions is null) return null;

        var updates = new List<ToolUpdateEntry>();
        var categories = ToolCatalog.GetCategories();

        foreach (var category in categories)
        {
            var tools = ToolCatalog.GetTools(category);
            foreach (var tool in tools)
            {
                if (string.IsNullOrWhiteSpace(tool.DownloadUrl)) continue;

                var toolDir = Path.Combine(ToolCatalog.ToolsRoot, category, tool.Folder ?? tool.Name);
                var localVersion = ToolMetadataService.GetToolVersionByDir(toolDir);

                var remoteMatch = FindRemoteMatch(remoteVersions, tool);
                if (remoteMatch is null) continue;

                if (!localVersion.HasValue || remoteMatch.Version > localVersion.Value)
                {
                    var repoPath = ResolveRepoPath(tool.DownloadUrl);
                    if (repoPath is null) continue;

                    updates.Add(new ToolUpdateEntry(
                        tool.Name,
                        remoteMatch.Match,
                        localVersion ?? 0,
                        remoteMatch.Version,
                        repoPath,
                        category,
                        toolDir));
                }
            }
        }

        return updates;
    }

    private static string? ResolveRepoPath(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;
        if (downloadUrl.StartsWith("gc:", StringComparison.OrdinalIgnoreCase))
            return downloadUrl[3..];
        if (downloadUrl.StartsWith("gh:", StringComparison.OrdinalIgnoreCase))
            return null;
        return null;
    }

    private static RemoteToolVersion? FindRemoteMatch(
        IReadOnlyList<RemoteToolVersion> remoteVersions, ToolItem tool)
    {
        var fileName = Path.GetFileNameWithoutExtension(tool.Path);
        var dirName = tool.Folder ?? tool.Name;
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, tool.Path);

        RemoteToolVersion? bestMatch = null;

        foreach (var rv in remoteVersions)
        {
            if (string.IsNullOrWhiteSpace(rv.Match)) continue;

            if (fileName.Contains(rv.Match, StringComparison.CurrentCultureIgnoreCase) ||
                relativePath.Contains(rv.Match, StringComparison.CurrentCultureIgnoreCase) ||
                dirName.Contains(rv.Match, StringComparison.CurrentCultureIgnoreCase))
            {
                if (bestMatch is null || rv.Match.Length > bestMatch.Match.Length)
                    bestMatch = rv;
            }
        }

        return bestMatch;
    }

    public static void EnqueueToolUpdates(List<ToolUpdateEntry> updates)
    {
        foreach (var update in updates)
        {
            EnqueueSingleToolUpdate(update);
        }
    }

    private static void EnqueueSingleToolUpdate(ToolUpdateEntry update)
    {
        _ = Task.Run(async () =>
        {
            await _syncSemaphore.WaitAsync();
            try
            {
                NotifyToolUpdateStarted(update.ToolName);

                var result = await ToolDownloaderService.SyncToolFromGitCodeDirAsync(
                    update.RepoPath, update.ToolDir, null, null, CancellationToken.None);

                if (result?.Success == true)
                {
                    ToolCatalog.InvalidateTagsCache();
                    ToolMetadataService.InvalidateCache();
                    NotifyToolUpdateComplete(update.ToolName);
                }
                else
                {
                    var ghResult = await ToolDownloaderService.SyncToolFromGitHubDirAsync(
                        update.RepoPath, update.ToolDir, null, null, CancellationToken.None);

                    if (ghResult?.Success == true)
                    {
                        ToolCatalog.InvalidateTagsCache();
                        ToolMetadataService.InvalidateCache();
                        NotifyToolUpdateComplete(update.ToolName);
                    }
                    else
                    {
                        NotifyToolUpdateFailed(update.ToolName,
                            result?.ErrorMessage ?? ghResult?.ErrorMessage ?? "同步失败");
                    }
                }
            }
            catch (Exception ex)
            {
                NotifyToolUpdateFailed(update.ToolName, ex.Message);
            }
            finally
            {
                _syncSemaphore.Release();
            }
        });
    }

    private static void NotifyToolUpdateComplete(string toolName)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowToolUpdateToast(toolName);
        });
    }

    private static void NotifyToolUpdateStarted(string toolName)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowToolUpdateProgressToast(toolName);
        });
    }

    private static void NotifyToolUpdateFailed(string toolName, string error)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowToolUpdateFailedToast(toolName, error);
        });
    }
}
