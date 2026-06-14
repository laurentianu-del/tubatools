using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible.Models
{
    public sealed class ToolItem : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public string Extension { get; set; }

        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set { if (SetField(ref _iconPath, value)) { } }
        }

        private string _iconGlyph;
        public string IconGlyph
        {
            get => _iconGlyph;
            set { if (SetField(ref _iconGlyph, value)) { } }
        }

        public string Description { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string DatabaseSource { get; set; }
        public string DownloadUrl { get; set; }
        public string DownloadFilter { get; set; }
        public string WingetId { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = new List<string>();
        public string TagsText { get { return Tags != null && Tags.Count > 0 ? string.Join("  ", Tags) : ""; } }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetField(ref _isFavorite, value);
        }

        public string Folder { get { return System.IO.Path.GetDirectoryName(RelativePath) ?? Category; } }

        public bool NeedsDownload
        {
            get { return !string.IsNullOrWhiteSpace(DownloadUrl) || !string.IsNullOrWhiteSpace(WingetId); }
        }

        public bool NeedsWingetInstall { get { return !string.IsNullOrWhiteSpace(WingetId); } }

        public bool CanLaunch { get { return true; } }

        public string PrimaryArch { get; set; }
        public IReadOnlyList<ArchVariant> AlternateVersions { get; set; } = new List<ArchVariant>();
        public bool HasAlternateVersions { get { return AlternateVersions != null && AlternateVersions.Count > 0; } }

        public List<ArchOption> ArchOptions { get; set; } = new List<ArchOption>();

        private ArchOption _selectedArch;
        public ArchOption SelectedArch
        {
            get => _selectedArch;
            set
            {
                if (SetField(ref _selectedArch, value))
                {
                    OnPropertyChanged("EffectivePath");
                    OnPropertyChanged("EffectiveWorkingDir");
                }
            }
        }

        public string EffectivePath { get { return SelectedArch != null ? SelectedArch.Path : Path; } }

        public string EffectiveWorkingDir
        {
            get { return System.IO.Path.GetDirectoryName(EffectivePath) ?? ToolCatalog.ToolsRoot; }
        }

        public string LaunchButtonText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DownloadUrl)) return "下载";
                return "打开";
            }
        }

        public void InitArchOptions()
        {
            ArchOptions.Clear();
            var primary = new ArchOption { Name = Name, Path = Path, Arch = PrimaryArch ?? "" };
            ArchOptions.Add(primary);
            if (AlternateVersions != null)
            {
                foreach (var v in AlternateVersions)
                {
                    ArchOptions.Add(new ArchOption { Name = v.Name, Path = v.Path, Arch = v.Arch });
                }
            }
            var isX64 = System.Environment.Is64BitOperatingSystem;
            var preferred = ArchOptions.FirstOrDefault(a =>
                a.Arch.Equals("x64", StringComparison.OrdinalIgnoreCase) && isX64)
                ?? ArchOptions.FirstOrDefault(a =>
                    a.Arch.Equals("x86", StringComparison.OrdinalIgnoreCase) && !isX64)
                ?? primary;
            SelectedArch = preferred;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ToolItem()
        {
            Name = "";
            Category = "";
            Path = "";
            RelativePath = "";
            Extension = "";
        }
    }

    public sealed class ArchVariant
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Arch { get; set; }

        public ArchVariant() { Name = ""; Path = ""; Arch = ""; }
    }

    public sealed class ArchOption
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Arch { get; set; }

        public string DisplayText { get { return string.IsNullOrEmpty(Arch) ? "默认" : Arch; } }

        public override string ToString() { return DisplayText; }

        public ArchOption() { Name = ""; Path = ""; Arch = ""; }
    }
}
