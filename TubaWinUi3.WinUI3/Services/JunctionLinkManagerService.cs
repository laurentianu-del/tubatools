using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>已知文件夹重定向状态。</summary>
public enum JunctionFolderState
{
    /// <summary>未重定向，位于默认位置。</summary>
    NotRedirected,
    /// <summary>已重定向，且原位置创建了超链接（Junction）。</summary>
    RedirectedWithLink,
    /// <summary>已重定向，但原位置没有超链接（如已被手动移动或 OneDrive 接管）。</summary>
    RedirectedNoLink
}

/// <summary>要管理的用户文件夹（桌面/下载/文档等）。</summary>
public sealed class JunctionFolderItem : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public required Guid FolderId { get; init; }
    /// <summary>相对 %USERPROFILE% 的物理默认路径，如 "Desktop"。</summary>
    public required string DefaultRelativePath { get; init; }
    /// <summary>当前实际位置（SHGetKnownFolderPath 解析结果）。</summary>
    public string CurrentPath { get; set; } = "";
    public JunctionFolderState State { get; set; } = JunctionFolderState.NotRedirected;

    private long _size;
    /// <summary>当前真实位置的占用大小（字节）。</summary>
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

    public string SizeText => AppDataMigrateService.FormatSize(Size);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DefaultRelativePath);
}

/// <summary>迁移/重定向过程进度。</summary>
public sealed class FolderMoveProgress
{
    public string Phase { get; set; } = "";
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentFile { get; set; } = "";
    public int Skipped { get; set; }
}

/// <summary>迁移/重定向/还原操作结果。</summary>
public sealed class FolderMoveResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int Moved { get; set; }
    public int Skipped { get; set; }
    /// <summary>本次复制到目标的文件列表（回滚时逐文件删除，避免误删目标里原有的文件）。</summary>
    public List<string> CopiedFiles { get; set; } = [];
}

/// <summary>自定义文件夹超链接记录（任意目录，如 AppData 子文件夹）。</summary>
public sealed class CustomJunctionItem
{
    /// <summary>源文件夹（原位置，现在是超链接）。</summary>
    public string Source { get; set; } = "";
    /// <summary>目标文件夹（新位置，实际文件所在）。</summary>
    public string Target { get; set; } = "";
}

/// <summary>
/// 超链接管理器核心：把桌面/下载等已知文件夹重定向到其他盘，
/// 在原位置创建 Junction（NTFS 联接点，即"超链接"），支持选迁原文件与一键还原。
/// 安全要点：junction 删除永远用 RemoveDirectory 且先经 ReparsePoint 检测，绝不递归；
/// 迁移先复制+核对文件数，一致后才删除源。
/// </summary>
public static class JunctionLinkManagerService
{
    private sealed record CatalogEntry(
        string Id, string Name, string Glyph, Guid FolderId, string Rel);

    private static readonly CatalogEntry[] Catalog =
    [
        new("desktop", "桌面", "\uE8B7", new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), "Desktop"),
        new("downloads", "下载", "\uE896", new Guid("374DE290-123F-4565-9164-39C4925E467B"), "Downloads"),
        new("documents", "文档", "\uE8A5", new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), "Documents"),
        new("pictures", "图片", "\uEB9F", new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), "Pictures"),
        new("music", "音乐", "\uEC4F", new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), "Music"),
        new("videos", "视频", "\uE8B2", new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), "Videos"),
        new("objects-3d", "3D 对象", "\uE838", new Guid("31C0DD25-9439-4F12-BF41-7FF4EDA38722"), "3D Objects"),
        new("favorites", "收藏夹", "\uE734", new Guid("1777F761-68AD-4D8A-87BD-30B759FA33DD"), "Favorites"),
        new("contacts", "联系人", "\uE77B", new Guid("56784854-C6CB-462B-8169-88E350ACB882"), "Contacts"),
        new("saved-games", "保存的游戏", "\uE7FC", new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"), "Saved Games")
    ];

    // ---- shell32 已知文件夹 API ----
    private const uint KF_FLAG_DEFAULT = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, [MarshalAs(UnmanagedType.LPWStr)] string pszPath);

    private const int SHCNE_UPDATEDIR = 0x00000800;
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_PATHW = 0x0005;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr item1, IntPtr item2);

    // ---- kernel32：删除 junction 只用 RemoveDirectory（不跟随联接，绝不递归） ----
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool RemoveDirectory(string lpPathName);

    // ---- kernel32：清不掉的空壳旧文件夹计划在重启后删除 ----
    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    // ---- 原地创建超链接（目录被占用无法改名/删除时，直接在原目录上设置重解析点） ----
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint FILE_SHARE_DELETE = 0x4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer,
        uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// 把"已存在且已清空"的目录原地变成超链接（mount-point 重解析点）。
    /// 用于原目录被 Explorer/索引器等独占、无法改名/删除的场景；设置前目录必须为空。
    /// </summary>
    public static bool CreateJunctionInPlace(string existingDir, string targetPath)
    {
        try
        {
            var objectName = @"\??\" + targetPath; // 联接的 substitute 名必须是绝对路径
            var subBytes = System.Text.Encoding.Unicode.GetBytes(objectName);
            var printBytes = System.Text.Encoding.Unicode.GetBytes(targetPath);

            // REPARSE_DATA_BUFFER（与 mklink 生成的布局一致，已用 fsutil 对照验证）：
            // 8 字节头 + 8 字节偏移 + substitute(无 null) + 2 字节 null + print(无 null) + 2 字节 null
            var buffer = new byte[20 + subBytes.Length + printBytes.Length];
            WriteUInt32(buffer, 0, IO_REPARSE_TAG_MOUNT_POINT);
            WriteUInt16(buffer, 4, (ushort)(12 + subBytes.Length + printBytes.Length)); // ReparseDataLength = 缓冲区总长 - 8
            WriteUInt16(buffer, 8, 0);                                            // SubstituteNameOffset
            WriteUInt16(buffer, 10, (ushort)subBytes.Length);                     // SubstituteNameLength（不含 null）
            WriteUInt16(buffer, 12, (ushort)(subBytes.Length + 2));               // PrintNameOffset
            WriteUInt16(buffer, 14, (ushort)printBytes.Length);                   // PrintNameLength（不含 null）
            Buffer.BlockCopy(subBytes, 0, buffer, 16, subBytes.Length);
            Buffer.BlockCopy(printBytes, 0, buffer, 18 + subBytes.Length, printBytes.Length);
            // 分隔 null（16+sub）与结尾 null（20+sub+print）保持默认 0 即可

            var h = CreateFile(existingDir, GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (h == InvalidHandleValue) return false;
            try
            {
                return DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, buffer, (uint)buffer.Length,
                    IntPtr.Zero, 0, out _, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(h);
            }
        }
        catch
        {
            return false;
        }
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>枚举全部受管目录并解析各自当前状态。</summary>
    public static IReadOnlyList<JunctionFolderItem> LoadItems()
    {
        var items = new List<JunctionFolderItem>(Catalog.Length);
        foreach (var c in Catalog)
        {
            var item = new JunctionFolderItem
            {
                Id = c.Id,
                Name = c.Name,
                Glyph = c.Glyph,
                FolderId = c.FolderId,
                DefaultRelativePath = c.Rel
            };
            // shell 解析失败（如 3D 对象文件夹被删除）时回退默认路径，后续操作按默认位置处理
            item.CurrentPath = GetKnownFolderPath(c.FolderId) ?? item.DefaultPath;
            item.State = ClassifyState(item.CurrentPath, item.DefaultPath, IsJunction(item.DefaultPath));
            items.Add(item);
        }
        return items;
    }

    /// <summary>解析已知文件夹当前实际路径（按注册表，含重定向）。</summary>
    public static string? GetKnownFolderPath(Guid folderId)
    {
        var hr = SHGetKnownFolderPath(ref folderId, KF_FLAG_DEFAULT, IntPtr.Zero, out var psz);
        if (hr != 0 || psz == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(psz); }
        finally { Marshal.FreeCoTaskMem(psz); }
    }

    /// <summary>写入已知文件夹位置（HKCU，需管理员/普通权限均可，本应用已提权）。</summary>
    private static bool SetKnownFolderPath(Guid folderId, string path)
    {
        try
        {
            return SHSetKnownFolderPath(ref folderId, KF_FLAG_DEFAULT, IntPtr.Zero, path) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void NotifyShellChange(string path1, string path2)
    {
        var p1 = Marshal.StringToHGlobalUni(path1);
        var p2 = Marshal.StringToHGlobalUni(path2);
        try
        {
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, p1, p2);
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(p1);
            Marshal.FreeHGlobal(p2);
        }
    }

    // ---- 状态分类与路径校验（纯逻辑，可单测） ----

    /// <summary>按"当前路径 vs 默认路径 + 原位置是否有 junction"分类状态。</summary>
    public static JunctionFolderState ClassifyState(string? currentPath, string defaultPath, bool junctionAtDefault)
    {
        if (string.IsNullOrWhiteSpace(currentPath)) return JunctionFolderState.NotRedirected;
        if (IsSamePath(currentPath, defaultPath)) return JunctionFolderState.NotRedirected;
        return junctionAtDefault ? JunctionFolderState.RedirectedWithLink : JunctionFolderState.RedirectedNoLink;
    }

    /// <summary>两个路径是否指向同一位置（忽略大小写与末尾分隔符）。</summary>
    public static bool IsSamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\'),
                Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>child 是否位于 parent 之内（含相等）。</summary>
    public static bool IsWithin(string parent, string child)
    {
        try
        {
            var rel = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(child));
            return rel != ".." && !rel.StartsWith("..\\", StringComparison.OrdinalIgnoreCase)
                   && !Path.IsPathRooted(rel);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>是否为盘符根目录（如 D:\）。</summary>
    public static bool IsDriveRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root)
                   && string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>重定向目标的合法性检查，返回错误描述；null 表示通过。</summary>
    public static string? ValidateTarget(JunctionFolderItem item, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return "请选择目标位置。";
        if (!Path.IsPathFullyQualified(targetPath)) return "目标必须是绝对路径（例如 D:\\Desktop）。";
        if (IsDriveRoot(targetPath)) return "不能选择盘符根目录（例如 D:\\），请选择盘内的一个文件夹。";
        if (File.Exists(targetPath)) return "目标路径已存在同名文件。";
        if (IsJunction(targetPath)) return "目标位置不能是超链接（Junction）。";

        var current = string.IsNullOrEmpty(item.CurrentPath) ? item.DefaultPath : item.CurrentPath;
        if (IsSamePath(targetPath, current)) return "目标位置与当前位置相同。";
        if (IsSamePath(targetPath, item.DefaultPath)) return "目标位置不能是原来的默认位置。";
        if (IsWithin(current, targetPath)) return "目标位置不能位于当前位置内部。";

        var parent = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return "目标位置的上级目录不存在。";
        return null;
    }

    /// <summary>为"不迁移"的旧文件夹找可用保留名（原名.old / .old2 / ...）。</summary>
    public static string FindOldFolderName(string dirPath)
    {
        if (!Directory.Exists(dirPath + ".old") && !File.Exists(dirPath + ".old")) return dirPath + ".old";
        for (var i = 2; ; i++)
        {
            var candidate = $"{dirPath}.old{i}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>查找已存在的 .old 保留文件夹，没有则返回 null。</summary>
    public static string? LocateOldFolder(string defaultPath)
    {
        var candidate = defaultPath + ".old";
        if (Directory.Exists(candidate) || File.Exists(candidate)) return candidate;
        for (var i = 2; i < 100; i++)
        {
            candidate = $"{defaultPath}.old{i}";
            if (Directory.Exists(candidate) || File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ---- Junction 操作 ----

    /// <summary>路径是否为联接点/符号链接（ReparsePoint 属性）。</summary>
    public static bool IsJunction(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>在原位置创建 Junction（超链接）。linkPath 处必须不存在任何条目。</summary>
    public static bool CreateJunction(string linkPath, string targetPath, out string error)
    {
        error = "";
        try
        {
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
            {
                error = "原位置已存在同名文件夹/文件。";
                return false;
            }
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(0),
                StandardErrorEncoding = System.Text.Encoding.GetEncoding(0)
            };
            using var p = Process.Start(psi);
            if (p is null) { error = "无法启动 mklink 进程。"; return false; }
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0) return true;
            error = string.IsNullOrWhiteSpace(output) ? "mklink 返回非零退出码。" : output.Trim();
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 删除原位置的 Junction。只删除联接本身（RemoveDirectory 不跟随联接），
    /// 删除任何目录前都必须先确认 ReparsePoint 属性，绝不允许递归删除联接。
    /// </summary>
    public static void RemoveJunction(string junctionPath)
    {
        if (!IsJunction(junctionPath)) return;
        RemoveDirectory(junctionPath);
    }

    // ---- 文件迁移 ----

    /// <summary>
    /// 把 sourceDir 全部内容复制到 destinationPath并核对文件数；**不删除源目录**（源的
    /// 改名/清理由调用方原子地处理）。枚举跳过 junction 子树；目标目录位于源内部时也会被跳过。
    /// 8 路并行复制；单个文件失败记录原因但不中断批次，结束时若有失败则整体失败（由调用方回滚）。
    /// </summary>
    public static async Task<FolderMoveResult> MoveContentsAsync(
        string sourceDir, string destinationPath,
        IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        if (IsSamePath(sourceDir, destinationPath))
            return new FolderMoveResult { Success = false, Message = "源和目标不能是同一个目录。" };

        Directory.CreateDirectory(destinationPath);
        var enumerated = EnumerateFilesSkippingLinks(sourceDir, destinationPath).ToArray();
        // Windows 保留设备名文件（nul/con/com1 等，仅能通过 \\?\ 技巧创建）无法用托管 API 复制，
        // 直接跳过并计数，避免整个迁移被这类垃圾文件卡住
        var deviceSkipped = enumerated.Count(f => IsReservedDeviceName(Path.GetFileName(f)));
        var files = enumerated.Where(f => !IsReservedDeviceName(Path.GetFileName(f))).ToArray();
        var total = files.Length;

        var copied = 0;
        var skipped = 0;
        var processed = 0;
        var copiedFiles = new List<string>(Math.Max(total, 0));
        var failures = new List<string>();
        var createdDirs = new ConcurrentDictionary<string, byte>();
        var sync = new object();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = linkedCts.Token },
            file =>
            {
                string target;
                try
                {
                    // 相对路径手工计算：不走 .NET 路径规范化。
                    // 含 DOS 保留设备名（nul/con/com1 等）的路径会被 GetFullPath 归一化为
                    // "\\.\nul" 并把整个迁移流程炸掉（GetDirectoryName 返回 null）。
                    var srcRoot = sourceDir.TrimEnd('\\');
                    var rel = file.StartsWith(srcRoot + "\\", StringComparison.OrdinalIgnoreCase)
                        ? file.Substring(srcRoot.Length + 1)
                        : file;
                    target = Path.Combine(destinationPath, rel);
                    var parent = Path.GetDirectoryName(target);
                    if (string.IsNullOrEmpty(parent))
                        throw new IOException($"路径计算异常（相对路径 [{rel}]，目标 [{target}]）");
                    if (createdDirs.TryAdd(parent, 0))
                        Directory.CreateDirectory(Extended(parent));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    lock (sync)
                    {
                        processed++;
                        skipped++;
                        failures.Add($"{Path.GetFileName(file)}：{ex.Message}");
                    }
                    return;
                }

                var ok = TryCopyWithRetry(file, target);
                lock (sync)
                {
                    processed++;
                    if (ok)
                    {
                        copied++;
                        copiedFiles.Add(target);
                    }
                    else
                    {
                        skipped++;
                        failures.Add(Path.GetFileName(file));
                    }

                    if (total > 0 && (processed % 25 == 0 || processed == total))
                    {
                        progress?.Report(new FolderMoveProgress
                        {
                            Phase = "正在迁移文件（并行）",
                            Current = processed,
                            Total = total,
                            CurrentFile = Path.GetFileName(file),
                            Skipped = skipped
                        });
                    }
                }
            });

        if (skipped > 0)
        {
            var reason = string.Join("\n", failures.Take(10));
            if (failures.Count > 10) reason += $"\n…… 共 {failures.Count} 个失败文件";
            return new FolderMoveResult
            {
                Success = false,
                Moved = copied,
                Skipped = skipped,
                CopiedFiles = copiedFiles,
                Message = $"有 {skipped} 个文件无法复制，原文件夹已保留：\n{reason}\n请处理这些问题文件后重试（可先从「撤销/还原」排除，或手动删除/重命名异常文件）。"
            };
        }

        // 核对：迁移期间源目录不应新增文件（remaining 与迁移前枚举数一致才算完整）
        var remaining = EnumerateFilesSkippingLinks(sourceDir, destinationPath).Count(f => !IsReservedDeviceName(Path.GetFileName(f)));
        if (remaining != total)
        {
            return new FolderMoveResult
            {
                Success = false,
                Moved = copied,
                CopiedFiles = copiedFiles,
                Message = $"迁移期间检测到源目录有 {remaining - total} 个新增文件，本次迁移不完整，将整体回滚。"
            };
        }

        var msg = $"已迁移 {copied} 个文件到新位置。";
        if (deviceSkipped > 0)
            msg += $"\n已跳过 {deviceSkipped} 个 Windows 保留设备名文件（nul/con 等，无法复制）。";
        return new FolderMoveResult { Success = true, Moved = copied, CopiedFiles = copiedFiles, Message = msg };
    }

    /// <summary>Windows 保留设备名（NUL/CON/PRN/AUX/COM1-9/LPT1-9，忽略大小写与尾随空格/点）。
    /// 这类文件只能通过 \\?\ 前缀创建，无法用托管 API 正常复制。</summary>
    public static bool IsReservedDeviceName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var n = name.TrimEnd(' ', '.').ToUpperInvariant();
        if (n.Length < 3 || n.Length > 4) return false;
        if (n is "NUL" or "CON" or "PRN" or "AUX") return true;
        if (n.StartsWith("COM", StringComparison.Ordinal) && n.Length == 4 && n[3] is >= '1' and <= '9') return true;
        if (n.StartsWith("LPT", StringComparison.Ordinal) && n.Length == 4 && n[3] is >= '1' and <= '9') return true;
        return false;
    }

    /// <summary>枚举文件，跳过 junction/符号链接子树；excludeRoot 内的子树（如目标目录）也被跳过。</summary>
    private static IEnumerable<string> EnumerateFilesSkippingLinks(string root, string? excludeRoot = null)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (excludeRoot != null && IsWithin(excludeRoot, dir)) continue;

            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { dirs = []; }
            foreach (var d in dirs)
            {
                try
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                    stack.Push(d);
                }
                catch { }
            }

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { files = []; }
            foreach (var f in files) yield return f;
        }
    }

    /// <summary>单文件复制：先清目标只读属性，最多重试 3 次。路径加 \\?\ 前缀，
    /// 绕过 DOS 保留设备名（nul 等）识别，确保真实文件被复制而不是被当作设备。</summary>
    private static bool TryCopyWithRetry(string source, string target)
    {
        var src = Extended(source);
        var dst = Extended(target);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (File.Exists(dst))
                {
                    try { File.SetAttributes(dst, FileAttributes.Normal); } catch { }
                }
                File.Copy(src, dst, true);
                // File.Copy 会连带复制源文件的只读属性，迁移语义下目标应收干净
                try { File.SetAttributes(dst, FileAttributes.Normal); } catch { }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 3) return false;
                Thread.Sleep(300);
            }
        }
        return false;
    }

    /// <summary>给路径加扩展长度前缀 \\?\（绕过设备名解释；已带前缀则原样返回）。</summary>
    private static string Extended(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;

    /// <summary>删除目录树（清只读属性、跳过 junction 子树只删联接本身），带重试。</summary>
    private static bool TryDeleteDirectory(string dir)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                DeleteDirectorySafely(dir);
                return true;
            }
            catch
            {
                if (attempt >= 2) return false;
                Thread.Sleep(300);
            }
        }
        return false;
    }

    private static void DeleteDirectorySafely(string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            try
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                {
                    RemoveDirectory(sub); // 只删联接本身，绝不递归
                    continue;
                }
                DeleteDirectorySafely(sub);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (Directory.EnumerateFileSystemEntries(dir).Any())
            throw new IOException($"目录仍有残留条目: {dir}");
        Directory.Delete(dir, false);
    }

    // ---- 重定向 / 还原编排 ----

    /// <summary>
    /// 重定向（原子操作，全成功或全失败）：
    /// 1. 复制并核对文件到目标（不删源）；
    /// 2. 原文件夹整体**改名挪开**（不依赖"删除原文件夹"，被占用时更能成功；
    ///    下载/桌面这类系统文件夹在很多设备上根本删不掉，改名是唯一稳妥做法）；
    /// 3. 原位置创建超链接；
    /// 4. 写注册表已知文件夹并回读验证；
    /// 5. 清理空壳旧文件夹（尽力，失败则安排重启后删除）。
    /// 任意步骤失败：删掉本次复制的文件、把改名的文件夹改回来，整体回滚。
    /// </summary>
    public static async Task<FolderMoveResult> RedirectAsync(
        JunctionFolderItem item, string targetPath, bool migrateFiles,
        IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        var fail = ValidateTarget(item, targetPath);
        if (fail != null) return new FolderMoveResult { Success = false, Message = fail };

        var defaultPath = item.DefaultPath;
        var current = item.CurrentPath;
        FolderMoveResult? move = null;

        try { Directory.CreateDirectory(targetPath); }
        catch (Exception ex)
        {
            return new FolderMoveResult { Success = false, Message = $"无法创建目标目录：{ex.Message}" };
        }
        var targetHadContent = Directory.EnumerateFileSystemEntries(targetPath).Any();

        // 当前真实目录：上次迁移失败留下的空壳 / 正常未重定向目录 / 其他盘上的旧目标
        var sourceDir = Directory.Exists(current) && !IsJunction(current) ? current
            : Directory.Exists(defaultPath) && !IsJunction(defaultPath) ? defaultPath
            : "";
        var hasFiles = sourceDir != "" && Directory.EnumerateFileSystemEntries(sourceDir).Any();

        // 1. 复制 + 核对（失败 → 整体回滚，源目录原样保留）
        if (hasFiles && migrateFiles)
        {
            move = await MoveContentsAsync(sourceDir, targetPath, progress, ct);
            if (!move.Success)
            {
                await RollbackCopyAsync(targetPath, targetHadContent, move.CopiedFiles);
                return new FolderMoveResult
                {
                    Success = false,
                    Message = $"{move.Message}\n已自动回滚：目标位置本次复制的内容已清理，原文件夹与文件未受影响。"
                };
            }
        }

        // 2. 原文件夹改名挪开（重试 3 次处理瞬时占用；这一步决定了旧位置能空出来）
        string? stagedName = null;
        var inPlaceJunction = false;
        if (sourceDir != "")
        {
            ReportPhase(progress, "正在把原文件夹移到暂存位置");
            stagedName = FindOldFolderName(sourceDir);
            var renamed = false;
            for (var attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    Directory.Move(sourceDir, stagedName);
                    renamed = true;
                }
                catch (Exception) when (Directory.Exists(sourceDir))
                {
                    if (attempt < 2) await Task.Delay(300, ct);
                }
            }

            if (!renamed)
            {
                // 慢路径：目录被 Explorer/索引器独占，无法改名 → 清空后在原目录上原地设置重解析点
                ReportPhase(progress, "原文件夹被占用，正在原地创建超链接");
                if (!EmptyDirectorySafely(sourceDir))
                {
                    await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
                    return new FolderMoveResult
                    {
                        Success = false,
                        Message = $"原文件夹被占用且无法清空（部分文件正在使用）。\n已自动回滚：文件已搬回原位置。\n请关闭正在使用它的程序后重试。"
                    };
                }
                if (!CreateJunctionInPlace(sourceDir, targetPath))
                {
                    await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
                    return new FolderMoveResult
                    {
                        Success = false,
                        Message = $"原地创建超链接失败。\n已自动回滚：文件已搬回原位置。"
                    };
                }
                inPlaceJunction = true;
                stagedName = null;
            }
        }

        // 3. 再次重定向场景：先移除上一次残留的旧联接
        if (IsJunction(defaultPath)) RemoveJunction(defaultPath);

        // 4. 原位置创建超链接
        ReportPhase(progress, "正在原位置创建超链接");
        if (!CreateJunction(defaultPath, targetPath, out var jError))
        {
            if (inPlaceJunction)
                await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
            else
                await RollbackRedirectAsync(stagedName, sourceDir, defaultPath, targetPath, targetHadContent, move?.CopiedFiles);
            return new FolderMoveResult
            {
                Success = false,
                Message = $"在原位置创建超链接失败：{jError}\n已自动回滚：原文件夹与文件均已恢复。"
            };
        }

        // 5. 写注册表 + 回读验证（SHGetKnownFolderPath 直读注册表，不受资源管理器缓存影响）
        if (!SetKnownFolderPath(item.FolderId, targetPath))
        {
            RemoveJunction(defaultPath);
            if (inPlaceJunction)
                await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
            else
                await RollbackRedirectAsync(stagedName, sourceDir, defaultPath, targetPath, targetHadContent, move?.CopiedFiles);
            return new FolderMoveResult
            {
                Success = false,
                Message = "写入 Windows 文件夹位置设置失败。\n已自动回滚：原文件夹与文件均已恢复。"
            };
        }
        var verified = GetKnownFolderPath(item.FolderId);
        if (verified is null || !IsSamePath(verified, targetPath))
        {
            RemoveJunction(defaultPath);
            if (inPlaceJunction)
                await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
            else
                await RollbackRedirectAsync(stagedName, sourceDir, defaultPath, targetPath, targetHadContent, move?.CopiedFiles);
            return new FolderMoveResult
            {
                Success = false,
                Message = $"系统设置写入后回读不一致（应指向 {targetPath}，实际为 {verified ?? "(空)"}）。\n已自动回滚：原文件夹与文件均已恢复。"
            };
        }

        // 5.5 新位置补上系统文件夹标记（System 属性 + desktop.ini），
        // 资源管理器才会按本地化名称显示（"下载"而非"Downloads"）。在清理 old 之前做，
        // 以便从 old 里补 desktop.ini。
        ApplyShellFolderStyle(targetPath, stagedName != null && Directory.Exists(stagedName) ? stagedName : null);

        // 6. 清理空壳旧文件夹：尽力删除，删不掉就安排重启后删除（不影响重定向结果）
        if (stagedName != null)
        {
            ReportPhase(progress, "正在清理空壳旧文件夹");
            if (!TryDeleteDirectory(stagedName))
            {
                MoveFileEx(stagedName, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }

        NotifyShellChange(defaultPath, targetPath);

        var msg = $"「{item.Name}」已重定向到 {verified}（已验证）：文件已迁移，原位置已创建超链接，旧路径程序仍可访问。";
        if (stagedName != null && Directory.Exists(stagedName))
            msg += "\n旧空文件夹暂时无法删除，已安排在重启后自动清理。";
        msg += "\n重启资源管理器后，资源管理器将直接显示新位置。";
        return new FolderMoveResult { Success = true, Moved = move?.Moved ?? 0, Message = msg };
    }

    /// <summary>回滚本次复制的内容：目标原本为空则整体删除目标目录；原本有内容则只删本次复制的文件。</summary>
    private static async Task RollbackCopyAsync(string targetPath, bool targetHadContent, List<string>? copiedFiles)
    {
        await Task.CompletedTask;
        try
        {
            if (!targetHadContent)
            {
                TryDeleteDirectory(targetPath);
            }
            else if (copiedFiles is { Count: > 0 })
            {
                foreach (var f in copiedFiles)
                {
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>重定向失败后的整体回滚：移除残留联接 → 改名的文件夹改回原名 → 清理本次复制的内容。</summary>
    private static async Task RollbackRedirectAsync(
        string? stagedName, string sourceDir, string defaultPath, string targetPath,
        bool targetHadContent, List<string>? copiedFiles)
    {
        try { if (IsJunction(defaultPath)) RemoveJunction(defaultPath); } catch { }

        if (stagedName != null && sourceDir != "" && Directory.Exists(stagedName) && !Directory.Exists(sourceDir))
        {
            try { Directory.Move(stagedName, sourceDir); }
            catch { /* 极端情况下改名恢复失败，会在错误信息中提示用户手动处理 */ }
        }

        await RollbackCopyAsync(targetPath, targetHadContent, copiedFiles);
    }

    /// <summary>清空目录的子项但保留目录本身（用于被独占目录的原地建链接路径）。
    /// 有残留（被占用文件）时返回 false。</summary>
    private static bool EmptyDirectorySafely(string dir)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                try
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) { RemoveDirectory(sub); continue; }
                    DeleteDirectorySafely(sub);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    File.SetAttributes(Extended(file), FileAttributes.Normal);
                    File.Delete(Extended(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (Directory.EnumerateFileSystemEntries(dir).Any()) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>原地路径的回滚：移除超链接 → 重建空目录 → 把目标内容全部搬回原位置。
    /// 目标位置的副本保留（不删除，避免误删用户原有文件），由错误信息提示。</summary>
    private static async Task RestoreInPlaceRollbackAsync(string sourceDir, string targetPath,
        IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        try { if (IsJunction(sourceDir)) RemoveJunction(sourceDir); } catch { }
        try { if (!Directory.Exists(sourceDir)) Directory.CreateDirectory(sourceDir); } catch { }
        try { await MoveContentsAsync(targetPath, sourceDir, progress, ct); } catch { }
    }

    /// <summary>
    /// 把目标目录补成"系统文件夹"：设置 System 属性，并从暂存旧文件夹补 desktop.ini。
    /// 资源管理器只对带 System 属性的目录应用 desktop.ini 的本地化名称
    /// （如"下载"），否则回退显示物理文件名（Downloads，英文）。
    /// </summary>
    public static void ApplyShellFolderStyle(string dir, string? fallbackSourceDir)
    {
        try
        {
            var ini = Path.Combine(dir, "desktop.ini");
            try
            {
                if (!File.Exists(ini) && fallbackSourceDir != null)
                {
                    var srcIni = Path.Combine(fallbackSourceDir, "desktop.ini");
                    if (File.Exists(srcIni)) File.Copy(srcIni, ini, false);
                }
            }
            catch { }

            try { File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System); } catch { }
            try { File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.System); } catch { }
        }
        catch { }
    }

    // ---- 自定义文件夹超链接（任意目录，可选择性迁移 AppData 子文件夹等） ----

    private const string CustomJunctionSettingsKey = "CustomJunctions";

    /// <summary>读取持久化的自定义超链接列表（存于 AppSettings JSON）。</summary>
    public static List<CustomJunctionItem> LoadCustomJunctions()
    {
        try
        {
            var json = AppSettings.Get(CustomJunctionSettingsKey);
            if (string.IsNullOrEmpty(json)) return [];
            return JsonSerializer.Deserialize<List<CustomJunctionItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>持久化自定义超链接列表。</summary>
    public static void SaveCustomJunctions(IEnumerable<CustomJunctionItem> items)
    {
        try
        {
            AppSettings.Set(CustomJunctionSettingsKey, JsonSerializer.Serialize(items.ToList()));
        }
        catch { }
    }

    /// <summary>自定义超链接的源文件夹合法性检查（返回错误描述；null 表示通过）。</summary>
    public static string? ValidateCustomSource(string sourceDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir)) return "请选择源文件夹。";
        if (!Directory.Exists(sourceDir)) return "源文件夹不存在。";
        if (IsJunction(sourceDir)) return "源文件夹已经是超链接，请先撤销再迁移。";
        if (IsDriveRoot(sourceDir)) return "不能迁移盘符根目录（例如 C:\\）。";
        if (IsKnownFolderDefaultPath(sourceDir)) return "这是 Windows 个人文件夹，请使用上方列表的「更改位置…」功能。";
        return null;
    }

    /// <summary>自定义超链接的目标位置合法性检查。</summary>
    public static string? ValidateCustomTarget(string sourceDir, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return "请选择目标位置。";
        if (!Path.IsPathFullyQualified(targetPath)) return "目标必须是绝对路径（例如 D:\\AppData）。";
        if (IsDriveRoot(targetPath)) return "不能选择盘符根目录（例如 D:\\），请选择盘内的一个文件夹。";
        if (File.Exists(targetPath)) return "目标路径已存在同名文件。";
        if (IsJunction(targetPath)) return "目标位置不能是超链接。";
        if (IsSamePath(targetPath, sourceDir)) return "目标位置与源文件夹相同。";
        if (IsWithin(sourceDir, targetPath)) return "目标位置不能位于源文件夹内部。";
        if (IsWithin(targetPath, sourceDir)) return "源文件夹不能位于目标位置内部。";
        var parent = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return "目标位置的上级目录不存在。";
        return null;
    }

    /// <summary>path 是否为受管已知文件夹的默认位置（这些目录应走上方重定向功能）。</summary>
    private static bool IsKnownFolderDefaultPath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Catalog.Any(c => IsSamePath(Path.Combine(profile, c.Rel), path));
    }

    /// <summary>
    /// 迁移任意文件夹到新位置并在原位置创建超链接（原子操作）：
    /// 复制并核对 → 原文件夹改名暂存 → 原位置建超链接 → 清理空壳。
    /// 任一步失败整体回滚，原文件夹与文件不受影响。不做注册表修改。
    /// </summary>
    public static async Task<FolderMoveResult> CreateCustomJunctionAsync(
        string sourceDir, string targetPath,
        IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        var fail = ValidateCustomSource(sourceDir) ?? ValidateCustomTarget(sourceDir, targetPath);
        if (fail != null) return new FolderMoveResult { Success = false, Message = fail };

        FolderMoveResult? move = null;
        try { Directory.CreateDirectory(targetPath); }
        catch (Exception ex)
        {
            return new FolderMoveResult { Success = false, Message = $"无法创建目标目录：{ex.Message}" };
        }
        var targetHadContent = Directory.EnumerateFileSystemEntries(targetPath).Any();
        var hasFiles = Directory.EnumerateFileSystemEntries(sourceDir).Any();

        // 1. 复制 + 核对（失败 → 整体回滚）
        if (hasFiles)
        {
            move = await MoveContentsAsync(sourceDir, targetPath, progress, ct);
            if (!move.Success)
            {
                await RollbackCopyAsync(targetPath, targetHadContent, move.CopiedFiles);
                return new FolderMoveResult
                {
                    Success = false,
                    Message = $"{move.Message}\n已自动回滚：目标位置本次复制的内容已清理，原文件夹与文件未受影响。"
                };
            }
        }

        // 2. 原文件夹改名暂存（快路径）
        string? stagedName = null;
        var inPlaceJunction = false;
        {
            ReportPhase(progress, "正在把原文件夹移到暂存位置");
            stagedName = FindOldFolderName(sourceDir);
            var renamed = false;
            for (var attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try { Directory.Move(sourceDir, stagedName); renamed = true; }
                catch (Exception) when (Directory.Exists(sourceDir))
                {
                    if (attempt < 2) await Task.Delay(300, ct);
                }
            }
            if (!renamed)
            {
                // 慢路径：目录被占用无法改名 → 清空后在原目录上原地设置重解析点
                ReportPhase(progress, "源文件夹被占用，正在原地创建超链接");
                if (!EmptyDirectorySafely(sourceDir))
                {
                    await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
                    return new FolderMoveResult
                    {
                        Success = false,
                        Message = $"源文件夹被占用且无法清空（部分文件正在使用）。\n已自动回滚：文件已搬回原位置。\n请退出正在使用它的程序（例如要迁移 AppData 子文件夹时先退出对应软件）后重试。"
                    };
                }
                if (!CreateJunctionInPlace(sourceDir, targetPath))
                {
                    await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
                    return new FolderMoveResult
                    {
                        Success = false,
                        Message = $"原地创建超链接失败。\n已自动回滚：文件已搬回原位置。"
                    };
                }
                inPlaceJunction = true;
                stagedName = null;
            }
        }

        // 2.5 已存在的旧联接先移除（含刚建的原地联接），再统一用 mklink 重建
        if (IsJunction(sourceDir)) RemoveJunction(sourceDir);

        // 3. 原位置创建超链接
        ReportPhase(progress, "正在原位置创建超链接");
        if (!CreateJunction(sourceDir, targetPath, out var jError))
        {
            if (inPlaceJunction)
                await RestoreInPlaceRollbackAsync(sourceDir, targetPath, progress, ct);
            else
                await RollbackRedirectAsync(stagedName, sourceDir, sourceDir, targetPath, targetHadContent, move?.CopiedFiles);
            return new FolderMoveResult
            {
                Success = false,
                Message = $"创建超链接失败：{jError}\n已自动回滚：原文件夹与文件均已恢复。"
            };
        }

        // 4. 清理空壳旧文件夹（尽力；删不掉安排重启后删除）
        if (stagedName != null && Directory.Exists(stagedName))
        {
            ReportPhase(progress, "正在清理空壳旧文件夹");
            if (!TryDeleteDirectory(stagedName))
            {
                MoveFileEx(stagedName, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }

        NotifyShellChange(sourceDir, targetPath);
        var msg = $"已把 {sourceDir} 的全部文件迁移到 {targetPath}，并在原位置创建了超链接：访问旧路径的程序不受影响。";
        if (stagedName != null && Directory.Exists(stagedName))
            msg += "\n旧空文件夹暂时无法删除，已安排在重启后自动清理。";
        return new FolderMoveResult { Success = true, Moved = move?.Moved ?? 0, Message = msg };
    }

    /// <summary>撤销自定义超链接：移除超链接 → 把文件搬回原位置 → 清理空壳目标目录。</summary>
    public static async Task<FolderMoveResult> UndoCustomJunctionAsync(
        CustomJunctionItem item, IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        var sourceDir = item.Source;
        var targetPath = item.Target;

        if (string.IsNullOrWhiteSpace(sourceDir) || !IsJunction(sourceDir))
            return new FolderMoveResult { Success = false, Message = "原位置已不是超链接，无需撤销（文件可能已手动处理）。" };

        // 1. 移除超链接（只删联接，不碰目标）
        ReportPhase(progress, "正在移除超链接");
        RemoveJunction(sourceDir);

        // 2. 把文件搬回原位置；失败则重建超链接保持原状
        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            ReportPhase(progress, "正在把文件搬回原位置");
            var move = await MoveContentsAsync(targetPath, sourceDir, progress, ct);
            if (!move.Success)
            {
                CreateJunction(sourceDir, targetPath, out _);
                return new FolderMoveResult { Success = false, Message = $"{move.Message}\n已恢复超链接，原状态未变。" };
            }
        }

        // 3. 清理空壳目标目录（尽力）
        var leftover = Directory.Exists(targetPath);
        if (leftover) leftover = !TryDeleteDirectory(targetPath);

        NotifyShellChange(sourceDir, targetPath);
        var msg = $"已撤销：位于 {targetPath} 的文件已全部搬回 {sourceDir}，超链接已移除。";
        if (leftover) msg += "\n目标位置残留空目录未能删除，可稍后手动删除。";
        return new FolderMoveResult { Success = true, Message = msg };
    }

    /// <summary>
    /// 还原默认位置：移除原位置 junction → 搬回 .old 保留文件夹 → 把重定向位置文件搬回默认
    /// → 写回默认路径注册表。
    /// </summary>
    public static async Task<FolderMoveResult> RestoreAsync(
        JunctionFolderItem item, IProgress<FolderMoveProgress>? progress, CancellationToken ct)
    {
        var defaultPath = item.DefaultPath;
        var current = item.CurrentPath;
        string? warning = null;

        // 1. 先搬文件（此时超链接仍在，旧路径程序继续可用；搬回失败不动超链接 → 无断链）
        if (Directory.Exists(current) && !IsSamePath(current, defaultPath))
        {
            if (IsJunction(current))
            {
                // 目标位置本身是联接：只删联接，内容留在原地
                RemoveJunction(current);
            }
            else if (Directory.EnumerateFileSystemEntries(current).Any())
            {
                ReportPhase(progress, "正在把文件搬回默认位置");
                var move = await MoveContentsAsync(current, defaultPath, progress, ct);
                if (!move.Success)
                {
                    return new FolderMoveResult
                    {
                        Success = false,
                        Message = $"{move.Message}\n超链接保持原样，未做任何更改。请处理问题文件后重试。"
                    };
                }
            }
        }

        // 2. 文件已全部回到默认位置 → 此时才移除原位置超链接
        if (IsJunction(defaultPath))
        {
            ReportPhase(progress, "正在移除原位置的超链接");
            RemoveJunction(defaultPath);
        }

        // 3. 还原"不迁移"时保留的 .old 旧文件夹（若有）
        var oldDir = LocateOldFolder(defaultPath);
        if (oldDir != null && !Directory.Exists(defaultPath) && !File.Exists(defaultPath))
        {
            ReportPhase(progress, $"正在还原保留的文件夹 {Path.GetFileName(oldDir)}");
            try { Directory.Move(oldDir, defaultPath); }
            catch (Exception ex) { warning = $"保留文件夹 {oldDir} 还原失败：{ex.Message}"; }
        }

        // 4. 尽力清理已搬空的旧位置目录（不影响结果）
        if (Directory.Exists(current) && !IsSamePath(current, defaultPath))
        {
            if (!TryDeleteDirectory(current))
                warning ??= $"旧位置目录 {current} 清理失败（可能被占用），可稍后手动删除。";
        }

        // 5. 默认位置补系统文件夹标记（还原后名称仍显示中文，而非英文物理名）
        ApplyShellFolderStyle(defaultPath, Directory.Exists(current) ? current : null);

        var setOk = SetKnownFolderPath(item.FolderId, defaultPath);
        NotifyShellChange(defaultPath, current);

        if (!setOk) return new FolderMoveResult { Success = false, Message = "无法写回默认位置设置，请重试。" };
        var verified = GetKnownFolderPath(item.FolderId);
        if (verified is null || !IsSamePath(verified, defaultPath))
        {
            return new FolderMoveResult
            {
                Success = false,
                Message = $"写回默认位置设置后回读不一致（应指向 {defaultPath}，实际为 {verified ?? "(空)"}）。"
            };
        }
        return new FolderMoveResult
        {
            Success = true,
            Message = warning is null
                ? $"「{item.Name}」已还原到默认位置。"
                : $"「{item.Name}」已还原到默认位置。注意：{warning}"
        };
    }

    /// <summary>
    /// 重启资源管理器，使已知文件夹位置更改立即生效（资源管理器在启动时缓存路径，注册表改动不会即时切换）。
    /// 会关闭已打开的文件夹窗口。
    /// </summary>
    public static async Task<bool> RestartExplorerAsync()
    {
        try
        {
            var kill = Process.Start(new ProcessStartInfo("taskkill", "/f /im explorer.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            kill?.WaitForExit();
            await Task.Delay(1500); // 等待 shell 退出，再拉起新实例
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReportPhase(IProgress<FolderMoveProgress>? progress, string phase)
    {
        progress?.Report(new FolderMoveProgress { Phase = phase });
    }
}