using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    internal static class PathHelper
    {
        public static string GetRelativePath(string relativeTo, string path)
        {
            if (string.IsNullOrWhiteSpace(relativeTo)) return path;
            if (string.IsNullOrWhiteSpace(path)) return path;

            var fromUri = new Uri(relativeTo.TrimEnd('\\') + "\\");
            var toUri = new Uri(path.TrimEnd('\\') + "\\");

            if (fromUri.Scheme != toUri.Scheme || fromUri.Host != toUri.Host)
                return path;

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relative = Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', '\\');
            if (relative.EndsWith("\\")) relative = relative.Substring(0, relative.Length - 1);
            return relative;
        }
    }

    public static class ToolCatalog
    {
        private static readonly string[] LaunchableExtensions = new[]
        {
            ".exe", ".bat", ".cmd", ".lnk", ".msc", ".ps1", ".vbs"
        };

        public static string AppDirectory
        {
            get
            {
                try
                {
                    var path = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            return dir;
                    }
                }
                catch { }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        public static string ToolsRoot { get { return FindToolsRoot(); } }

        public static IReadOnlyList<string> GetCategories()
        {
            if (!Directory.Exists(ToolsRoot))
                return new List<string>();

            var dirs = Directory.GetDirectories(ToolsRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            var orderJson = AppSettings.Get("CategoryOrder");
            List<string> ordered = null;
            if (!string.IsNullOrWhiteSpace(orderJson))
            {
                try
                {
                    ordered = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(orderJson);
                }
                catch { }
            }

            if (ordered != null && ordered.Count > 0)
            {
                var orderedSet = new HashSet<string>(ordered, StringComparer.CurrentCultureIgnoreCase);
                var result = ordered.Where(name => dirs.Contains(name)).ToList();
                foreach (var d in dirs.OrderBy(d2 => d2, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!orderedSet.Contains(d))
                        result.Add(d);
                }
                return result;
            }

            return dirs.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static IReadOnlyList<ToolItem> GetTools(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || !Directory.Exists(ToolsRoot))
                return new List<ToolItem>();

            var categoryRoot = Path.Combine(ToolsRoot, category);
            if (!Directory.Exists(categoryRoot))
                return new List<ToolItem>();

            var toolDirs = Directory.GetDirectories(categoryRoot).ToList();
            var merged = MergeArchDirectories(toolDirs);

            var items = new List<ToolItem>();
            foreach (var pair in merged.Select(toolDir => new { toolDir, launchable = FindPrimaryLaunchable(toolDir) }))
            {
                if (pair.launchable == null && !ToolMetadataService.HasDownloadUrl(category, pair.toolDir))
                    continue;
                var path = pair.launchable ?? CreatePlaceholderPath(pair.toolDir);
                items.Add(CreateToolItemWithVariants(category, categoryRoot, path, pair.toolDir));
            }

            var toolOrderJson = AppSettings.Get("ToolOrder_" + category);
            List<string> toolOrder = null;
            if (!string.IsNullOrWhiteSpace(toolOrderJson))
            {
                try
                {
                    toolOrder = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(toolOrderJson);
                }
                catch { }
            }

            if (toolOrder != null && toolOrder.Count > 0)
            {
                var orderedSet = new HashSet<string>(toolOrder, StringComparer.CurrentCultureIgnoreCase);
                var result = new List<ToolItem>();
                foreach (var name in toolOrder)
                {
                    var match = items.FirstOrDefault(it => it.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
                    if (match != null) result.Add(match);
                }
                foreach (var item in items.OrderBy(it => it.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!orderedSet.Contains(item.Name))
                        result.Add(item);
                }
                return result;
            }

            return items
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static List<string> MergeArchDirectories(List<string> toolDirs)
        {
            var dirNames = toolDirs.Select(d => Path.GetFileName(d)).ToList();
            var consumed = new HashSet<int>();
            var result = new List<string>();

            for (var i = 0; i < toolDirs.Count; i++)
            {
                if (consumed.Contains(i)) continue;

                var strippedI = StripArchSuffix(dirNames[i]);
                result.Add(toolDirs[i]);

                for (var j = i + 1; j < toolDirs.Count; j++)
                {
                    if (consumed.Contains(j)) continue;
                    var strippedJ = StripArchSuffix(dirNames[j]);
                    if (strippedI.Equals(strippedJ, StringComparison.OrdinalIgnoreCase))
                        consumed.Add(j);
                }
            }

            return result;
        }

        public static IReadOnlyList<ToolItem> GetAllToolsLazy(int skip, int take)
        {
            if (!Directory.Exists(ToolsRoot))
                return new List<ToolItem>();

            return GetCategories()
                .SelectMany(GetTools)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public static int GetAllToolsCount()
        {
            if (!Directory.Exists(ToolsRoot)) return 0;
            return GetCategories().Sum(c => GetTools(c).Count);
        }

        private static IReadOnlyList<string> _cachedTags;
        private static IReadOnlyList<ToolItem> _cachedAllTools;

        private static IReadOnlyList<ToolItem> GetAllToolsCached()
        {
            if (_cachedAllTools != null) return _cachedAllTools;
            if (!Directory.Exists(ToolsRoot))
            {
                _cachedAllTools = new List<ToolItem>();
                return _cachedAllTools;
            }
            _cachedAllTools = GetCategories().SelectMany(GetTools).ToList();
            return _cachedAllTools;
        }

        public static IReadOnlyList<string> GetAllTags()
        {
            if (_cachedTags != null) return _cachedTags;

            var allTools = GetAllToolsCached();
            _cachedTags = allTools
                .SelectMany(t => t.Tags ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => g.Key)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return _cachedTags;
        }

        public static void InvalidateTagsCache()
        {
            _cachedTags = null;
            _cachedAllTools = null;
        }

        public static IReadOnlyList<ToolItem> Search(string query, string tag = null)
        {
            if (!Directory.Exists(ToolsRoot))
                return new List<ToolItem>();

            var normalizedQuery = (query ?? "").Trim();
            if (normalizedQuery.Length == 0 && string.IsNullOrEmpty(tag))
                return new List<ToolItem>();

            var allTools = GetAllToolsCached();
            var result = new List<ToolItem>();

            foreach (var item in allTools)
            {
                var matchesQuery = normalizedQuery.Length == 0 ||
                    item.Name.IndexOf(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    item.RelativePath.IndexOf(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (item.Tags != null && item.Tags.Any(t => t.IndexOf(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0));

                var matchesTag = string.IsNullOrEmpty(tag) ||
                    (item.Tags != null && item.Tags.Any(t => t.Equals(tag, StringComparison.CurrentCultureIgnoreCase)));

                if (matchesQuery && matchesTag)
                    result.Add(item);
            }

            return result;
        }

        private static ToolItem CreateToolItemWithVariants(string category, string categoryRoot, string path, string toolDir)
        {
            var extension = Path.GetExtension(path);
            var name = GetDisplayName(path);
            var relativePath = PathHelper.GetRelativePath(categoryRoot, path);
            var metadata = ToolMetadataService.GetMetadata(category, path);
            var isPlaceholder = !File.Exists(path) && (!string.IsNullOrWhiteSpace(metadata.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata.WingetId) || !string.IsNullOrWhiteSpace(metadata.RemoteUrl));

            var primaryArch = DetectArch(Path.GetFileNameWithoutExtension(path));
            var archDisplay = FormatArchDisplay(primaryArch);

            var alternates = FindAllArchVariants(toolDir, path);

            var categoryRootDir = Path.Combine(ToolsRoot, category);
            if (Directory.Exists(categoryRootDir))
            {
                var dirName = Path.GetFileName(toolDir);
                var strippedDir = StripArchSuffix(dirName);
                foreach (var otherDir in Directory.GetDirectories(categoryRootDir))
                {
                    var otherName = Path.GetFileName(otherDir);
                    if (otherName.Equals(dirName, StringComparison.OrdinalIgnoreCase)) continue;
                    var strippedOther = StripArchSuffix(otherName);
                    if (!strippedOther.Equals(strippedDir, StringComparison.OrdinalIgnoreCase)) continue;

                    var otherLaunchable = FindPrimaryLaunchable(otherDir);
                    if (otherLaunchable == null) continue;

                    var otherFileName = Path.GetFileNameWithoutExtension(otherLaunchable);
                    var otherArch = DetectArch(otherFileName);
                    if (otherArch == null) continue;

                    alternates.Add(new ArchVariant
                    {
                        Name = CleanupName(StripArchSuffix(otherFileName)),
                        Path = otherLaunchable,
                        Arch = FormatArchDisplay(otherArch)
                    });
                }
            }

            var jsonVariants = ToolMetadataService.GetArchVariants(path, toolDir);
            foreach (var jv in jsonVariants)
            {
                string variantPath = null;

                if (!string.IsNullOrWhiteSpace(jv.File))
                {
                    var candidate = Path.Combine(toolDir, jv.File);
                    if (File.Exists(candidate))
                        variantPath = candidate;
                }

                if (variantPath == null && !string.IsNullOrWhiteSpace(jv.Dir))
                {
                    var altDir = Path.Combine(categoryRootDir, jv.Dir);
                    if (Directory.Exists(altDir))
                    {
                        var altLaunchable = FindPrimaryLaunchable(altDir);
                        if (altLaunchable != null)
                            variantPath = altLaunchable;
                    }
                }

                if (variantPath == null) continue;
                if (variantPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
                if (alternates.Any(a => a.Path.Equals(variantPath, StringComparison.OrdinalIgnoreCase))) continue;

                var vName = Path.GetFileNameWithoutExtension(variantPath);
                alternates.Add(new ArchVariant
                {
                    Name = CleanupName(StripArchSuffix(vName)),
                    Path = variantPath,
                    Arch = jv.Arch ?? FormatArchDisplay(DetectArch(vName)) ?? "x86"
                });
            }

            var cleanName = CleanupName(StripArchSuffix(name));
            if (string.IsNullOrWhiteSpace(cleanName))
                cleanName = CleanupName(name);

            var item = new ToolItem
            {
                Name = cleanName,
                Category = category,
                Path = path,
                RelativePath = relativePath,
                Extension = isPlaceholder ? "待下载" : extension.TrimStart('.').ToUpperInvariant(),
                IconPath = null,
                IconGlyph = isPlaceholder ? null : ToolIconService.GetIconGlyph(path),
                Description = metadata.Description,
                Publisher = metadata.Publisher,
                Version = metadata.Version,
                DatabaseSource = metadata.DatabaseSource,
                DownloadUrl = metadata.DownloadUrl,
                RemoteUrl = metadata.RemoteUrl,
                DownloadFilter = metadata.DownloadFilter,
                WingetId = metadata.WingetId,
                Tags = metadata.Tags ?? new List<string>(),
                IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path),
                PrimaryArch = archDisplay.Length > 0 ? archDisplay : null,
                AlternateVersions = alternates
            };
            item.InitArchOptions();
            return item;
        }

        private static bool IsLaunchable(string path)
        {
            var extension = Path.GetExtension(path);
            for (int i = 0; i < LaunchableExtensions.Length; i++)
            {
                if (extension.Equals(LaunchableExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static readonly string[] ArchSuffixes = new[]
        {
            "64", "32", "x64", "x86", "_x64", "_x86", "_64", "_32",
            "w64", "w32", "_Win64", "_Win32", "ARM64", "_ARM64"
        };

        private static readonly string[] ArchX64Patterns = new[]
        {
            "x64", "_x64", "w64", "_Win64"
        };

        private static readonly string[] ArchArm64Patterns = new[]
        {
            "ARM64", "_ARM64", "arm64", "_arm64"
        };

        private static readonly string[] Arch32Patterns = new[]
        {
            "x86", "_x86", "32", "_32", "w32", "_Win32"
        };

        private static bool IsX64OS { get { return Environment.Is64BitOperatingSystem; } }

        private static string DetectArch(string name)
        {
            foreach (var p in ArchArm64Patterns)
            {
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return "ARM64";
            }
            foreach (var p in ArchX64Patterns)
            {
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return "x64";
            }
            foreach (var p in Arch32Patterns)
            {
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return "x86";
            }
            return null;
        }

        private static string FormatArchDisplay(string arch)
        {
            if (arch == "ARM64") return "ARM64";
            if (arch == "x64" || arch == "Win64") return "x64";
            if (arch == "x86" || arch == "Win32") return "x86";
            return arch ?? "";
        }

        private static List<ArchVariant> FindAllArchVariants(string toolDir, string primaryPath)
        {
            var variants = new List<ArchVariant>();
            var dirName = Path.GetFileName(toolDir);
            var primaryExt = primaryPath != null ? Path.GetExtension(primaryPath) : null;

            var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
                .Where(IsLaunchable)
                .ToList();

            foreach (var filePath in allLaunchables)
            {
                if (filePath.Equals(primaryPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (primaryExt != null && !Path.GetExtension(filePath).Equals(primaryExt, StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var arch = DetectArch(fileName);
                if (arch == null) continue;

                var stripped = StripArchSuffix(fileName);
                var dirStripped = StripArchSuffix(dirName);
                if (!stripped.Equals(dirStripped, StringComparison.OrdinalIgnoreCase) &&
                    !stripped.Equals(dirName, StringComparison.OrdinalIgnoreCase))
                    continue;

                variants.Add(new ArchVariant
                {
                    Name = CleanupName(StripArchSuffix(fileName)),
                    Path = filePath,
                    Arch = FormatArchDisplay(arch)
                });
            }

            return variants;
        }

        private static string FindPrimaryLaunchable(string toolDir)
        {
            var dirName = Path.GetFileName(toolDir);

            var launchTarget = ToolMetadataService.GetLaunchTarget(toolDir);
            if (!string.IsNullOrWhiteSpace(launchTarget))
            {
                var targetPath = Path.Combine(toolDir, launchTarget);
                if (File.Exists(targetPath) && IsLaunchable(targetPath))
                    return targetPath;

                var deepTarget = Directory.EnumerateFiles(toolDir, launchTarget, SearchOption.AllDirectories)
                    .FirstOrDefault(f => IsLaunchable(f));
                if (deepTarget != null)
                    return deepTarget;
            }

            var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
                .Where(IsLaunchable)
                .ToList();

            if (allLaunchables.Count == 0) return null;
            if (allLaunchables.Count == 1) return allLaunchables[0];

            var directLaunchables = Directory.EnumerateFiles(toolDir)
                .Where(IsLaunchable)
                .ToList();

            var match = directLaunchables.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            var archCandidates = directLaunchables
                .Where(f => StripArchSuffix(Path.GetFileNameWithoutExtension(f))
                    .Equals(StripArchSuffix(dirName), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (archCandidates.Count > 0) return PickPreferredArch(archCandidates);

            match = allLaunchables.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            archCandidates = allLaunchables
                .Where(f => StripArchSuffix(Path.GetFileNameWithoutExtension(f))
                    .Equals(StripArchSuffix(dirName), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (archCandidates.Count > 0) return PickPreferredArch(archCandidates);
            if (directLaunchables.Count > 0) return directLaunchables[0];

            return allLaunchables[0];
        }

        private static string PickPreferredArch(List<string> candidates)
        {
            if (IsX64OS)
            {
                var x64 = candidates.FirstOrDefault(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    foreach (var p in ArchX64Patterns)
                    {
                        if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                });
                if (x64 != null) return x64;
            }
            else
            {
                var x86 = candidates.FirstOrDefault(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    foreach (var p in Arch32Patterns)
                    {
                        if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                });
                if (x86 != null) return x86;
            }

            return candidates[0];
        }

        private static string StripArchSuffix(string name)
        {
            foreach (var suffix in ArchSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - suffix.Length);
            }
            return name;
        }

        private static string CleanupName(string name)
        {
            return name
                .Replace("_x64", " x64")
                .Replace("_x86", " x86")
                .Replace("_ARM64", " ARM64")
                .Replace("_arm64", " ARM64")
                .Replace("_", " ");
        }

        private static string GetDisplayName(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.Equals("start", StringComparison.OrdinalIgnoreCase))
                return fileName;

            var parentName = Directory.GetParent(path) != null ? Directory.GetParent(path).Name : null;
            return string.IsNullOrWhiteSpace(parentName) ? fileName : parentName;
        }

        private static string CreatePlaceholderPath(string toolDir)
        {
            var dirName = Path.GetFileName(toolDir);
            return Path.Combine(toolDir, dirName + ".exe");
        }

        private static string FindToolsRoot()
        {
            var outputTools = Path.Combine(AppDirectory, "Tools");
            if (Directory.Exists(outputTools))
                return outputTools;

            var directory = new DirectoryInfo(AppDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Tools");
                if (Directory.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }

            return outputTools;
        }
    }
}
