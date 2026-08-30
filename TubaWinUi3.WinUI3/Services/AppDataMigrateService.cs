using System.ComponentModel;

namespace TubaWinUi3.Services;

/// <summary>AppData 中按应用统计的迁移候选（Roaming/Local/LocalLow 的顶层文件夹）。</summary>
public sealed class AppDataAppItem : INotifyPropertyChanged
{
    /// <summary>源文件夹完整路径。</summary>
    public required string Source { get; init; }
    /// <summary>应用文件夹名。</summary>
    public required string Name { get; init; }
    /// <summary>所在区域：Roaming / Local / LocalLow。</summary>
    public required string Area { get; init; }
    /// <summary>占用大小（字节；-1 = 尚未统计，0 = 已迁移）。</summary>
    private long _size = -1;

    public long Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                PropertyChanged?.Invoke(this, new(nameof(Size)));
                PropertyChanged?.Invoke(this, new(nameof(SizeText)));
            }
        }
    }

    /// <summary>迁移后的目标位置（用于撤销）。</summary>
    public string Target { get; set; } = "";

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    }

    private bool _migrated;
    public bool Migrated
    {
        get => _migrated;
        set
        {
            if (_migrated != value)
            {
                _migrated = value;
                PropertyChanged?.Invoke(this, new(nameof(Migrated)));
                PropertyChanged?.Invoke(this, new(nameof(CanSelect)));
                PropertyChanged?.Invoke(this, new(nameof(SizeText)));
            }
        }
    }

    public bool CanSelect => !_migrated;

    public string SizeText => _migrated ? "—" : Size < 0 ? "正在扫描…" : AppDataMigrateService.FormatSize(Size);
    public string StatusText => _migrated ? "已迁移" : "未迁移";

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// AppData 按应用迁移：扫描 Roaming/Local/LocalLow 顶层文件夹（按应用维度），
/// 统计占用大小；勾选后一次性迁移到目标盘，每个应用在原位置创建超链接
/// （复用 JunctionLinkManagerService 的原子迁移逻辑），记录保存在自定义超链接列表。
/// </summary>
public static class AppDataMigrateService
{
    /// <summary>系统组件/商店应用目录：不建议迁移，扫描时排除。</summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Packages",   // 商店应用（UWP）
        "Microsoft"   // 系统组件（Edge/Windows/OneDrive 等）
    };

    /// <summary>AppData\Roaming 路径。</summary>
    public static string RoamingPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>AppData\Local 路径。</summary>
    public static string LocalPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>AppData\LocalLow 路径。</summary>
    public static string LocalLowPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");

    /// <summary>是否被排除（Microsoft / Packages）。</summary>
    public static bool IsExcluded(string name) => Excluded.Contains(name);

    /// <summary>
    /// 快速枚举全部应用目录（不统计大小，立即返回）：名称/区域立即可得，
    /// Size=-1 表示"正在扫描…"（未统计）；已迁移的项 Size=0 且带 Target/Migrated。
    /// 列表按 Roaming/Local/LocalLow 顺序输出。
    /// </summary>
    public static List<AppDataAppItem> EnumerateItems(CancellationToken ct)
    {
        var dirs = new List<(string Dir, string Area)>();
        foreach (var (area, root) in (new[] { ("Roaming", RoamingPath), ("Local", LocalPath), ("LocalLow", LocalLowPath) }))
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue; // 某个根目录无法枚举（权限/损坏），跳过该区域，不影响其他
            }
            foreach (var dir in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                string name;
                try { name = Path.GetFileName(dir); }
                catch { continue; }
                if (name.Length == 0 || Excluded.Contains(name)) continue;
                dirs.Add((dir, area));
            }
        }

        var items = new List<AppDataAppItem>(dirs.Count);
        var persisted = JunctionLinkManagerService.LoadCustomJunctions();
        var targetBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in persisted) targetBySource[p.Source] = p.Target;

        foreach (var (dir, area) in dirs)
        {
            ct.ThrowIfCancellationRequested();
            var isJunction = JunctionLinkManagerService.IsJunction(dir);
            var item = new AppDataAppItem
            {
                Source = dir,
                Name = Path.GetFileName(dir),
                Area = area,
                Size = isJunction ? 0 : -1 // -1 = 正在扫描…
            };
            if (isJunction && targetBySource.TryGetValue(dir, out var target))
            {
                item.Target = target;
                item.Migrated = true;
            }
            items.Add(item);
        }
        return items;
    }

    /// <summary>
    /// 并行逐个统计大小：每个应用算完立即调用 onSized（可能在工作线程上调用，请自行封送到 UI 线程）。
    /// 被取消时部分项保持 -1（正在扫描…）。
    /// </summary>
    public static void ComputeSizesInParallel(
        IReadOnlyList<AppDataAppItem> items, Action<AppDataAppItem>? onSized,
        IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        var pending = items.Where(i => !i.Migrated).ToArray();
        if (pending.Length == 0) return;
        var done = 0;
        Parallel.ForEach(pending, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            it =>
            {
                ct.ThrowIfCancellationRequested();
                long size;
                try { size = ComputeDirSize(it.Source, ct); }
                catch { size = -1; }
                it.Size = size;
                onSized?.Invoke(it);
                var d = Interlocked.Increment(ref done);
                progress?.Report(new FolderMoveProgress
                {
                    Phase = "正在统计 AppData 占用",
                    Current = d,
                    Total = pending.Length,
                    CurrentFile = it.Name
                });
            });
    }

    /// <summary>统计目录占用字节数（跳过 junction 子树，单目录异常不影响整体）。</summary>
    public static long ComputeDirSize(string dir, CancellationToken ct)
    {
        long total = 0;
        try
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var d = stack.Pop();
                foreach (var sub in Directory.EnumerateDirectories(d))
                {
                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                        stack.Push(sub);
                    }
                    catch { }
                }
                foreach (var f in Directory.EnumerateFiles(d))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>为应用计算默认目标路径：{基础目标}\{区域}\{应用名}，已存在则追加序号。</summary>
    public static string ResolveTarget(string baseTarget, AppDataAppItem item)
    {
        var candidate = Path.Combine(baseTarget, item.Area, item.Name);
        if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        for (var i = 2; ; i++)
        {
            var next = $"{candidate} ({i})";
            if (!Directory.Exists(next) && !File.Exists(next)) return next;
        }
    }

    /// <summary>
    /// 批量迁移选中的应用：逐个走原子迁移（复制核对 → 改名暂存 → 建超链接），
    /// 单个失败不中断其他应用；成功后写入持久化自定义超链接列表。
    /// </summary>
    public static async Task<FolderMoveResult> MigrateSelectedAsync(
        IReadOnlyList<AppDataAppItem> items, string baseTarget,
        IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        var ok = 0;
        var failures = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            progress?.Report(new FolderMoveProgress
            {
                Phase = $"正在迁移应用 {item.Name}（{i + 1}/{items.Count}）",
                Current = i + 1,
                Total = items.Count,
                CurrentFile = item.Area
            });

            var target = ResolveTarget(baseTarget, item);
            var result = await JunctionLinkManagerService.CreateCustomJunctionAsync(item.Source, target, progress, ct);
            if (result.Success)
            {
                item.Migrated = true;
                item.Target = target;
                var customs = JunctionLinkManagerService.LoadCustomJunctions();
                customs.RemoveAll(x => string.Equals(x.Source, item.Source, StringComparison.OrdinalIgnoreCase));
                customs.Add(new CustomJunctionItem { Source = item.Source, Target = target });
                JunctionLinkManagerService.SaveCustomJunctions(customs);
                ok++;
            }
            else
            {
                failures.Add($"{item.Name}：{result.Message.Split('\n')[0]}");
            }
        }

        if (failures.Count == 0)
            return new FolderMoveResult { Success = true, Message = $"已成功迁移 {ok} 个应用，并在原位置创建了超链接。" };

        return new FolderMoveResult
        {
            Success = false,
            Message = $"成功迁移 {ok} 个，失败 {failures.Count} 个：\n" + string.Join("\n", failures)
        };
    }

    /// <summary>人类可读的大小（B/KB/MB/GB/TB）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        var i = -1;
        string[] units = ["KB", "MB", "GB", "TB"];
        do
        {
            v /= 1024;
            i++;
        } while (v >= 1024 && i < units.Length - 1);
        return $"{v:0.#} {units[i]}";
    }
}