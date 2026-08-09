using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

// ═══════════════════════════════════════════════════════════════
//  Action model — each action knows how to build its FFmpeg fragment
// ═══════════════════════════════════════════════════════════════

internal abstract class VideoAction
{
    public abstract string Name { get; }
    public abstract string Glyph { get; }
    public abstract string Summary { get; }

    public abstract void Edit(ContentDialog dialog);
    public abstract void ApplyEdit(StackPanel panel);

    public virtual void Contribute(PipelineContext ctx) { }
}

internal sealed class TrimAction : VideoAction
{
    public string Start = "00:00:00";
    public string End = "";
    public bool Reencode;

    public override string Name => "裁剪片段";
    public override string Glyph => "\uE9A3";
    public override string Summary => $"{Start} → {(string.IsNullOrEmpty(End) ? "末尾" : End)}{(Reencode ? " (重编码)" : "")}";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "裁剪片段";
        var sp = new StackPanel { Spacing = 12, Width = 460, Children =
        {
            new TextBox { Header = "开始时间", Text = Start, PlaceholderText = "00:00:00", Tag = "start" },
            new TextBox { Header = "结束时间（留空=到末尾）", Text = End, PlaceholderText = "00:00:00", Tag = "end" },
            new CheckBox { Content = "重新编码（精确裁剪，但较慢）", IsChecked = Reencode, Tag = "re" },
            new TextBlock { Text = "时间格式：HH:MM:SS 或 MM:SS 或秒数", FontSize = 11, Opacity = 0.6 }
        }};
        dialog.Content = sp;
    }

    public override void ApplyEdit(StackPanel p)
    {
        Start = Win32.Get<TextBox>(p, "start").Text.Trim();
        End = Win32.Get<TextBox>(p, "end").Text.Trim();
        Reencode = Win32.Get<CheckBox>(p, "re").IsChecked == true;
    }

    public override void Contribute(PipelineContext ctx)
    {
        ctx.SeekBefore = $"-ss {Start}";
        if (!string.IsNullOrEmpty(End)) ctx.SeekAfter = $"-to {End}";
        if (Reencode) { ctx.VCodec = "libx264"; ctx.ACodec = "aac"; }
    }
}

internal sealed class CompressAction : VideoAction
{
    public int Mode; // 0=crf 1=size 2=bitrate
    public int Crf = 28;
    public int TargetMB = 100;
    public int Bitrate = 2000;
    public string BitrateUnit = "Kbps";

    public override string Name => "压缩视频";
    public override string Glyph => "\uE710";
    public override string Summary => Mode switch
    {
        0 => $"质量 CRF {Crf}",
        1 => $"目标 {TargetMB} MB",
        _ => $"码率 {Bitrate}{BitrateUnit}"
    };

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "压缩视频";
        var sp = new StackPanel { Spacing = 12, Width = 460 };
        var rb = new RadioButtons { Header = "压缩模式", SelectedIndex = Mode, Tag = "mode" };
        rb.Items.Add("按质量 (CRF)");
        rb.Items.Add("按目标大小");
        rb.Items.Add("按码率");
        sp.Children.Add(rb);
        sp.Children.Add(new Slider { Header = "CRF (18-40，越大越小)", Minimum = 18, Maximum = 40, Value = Crf, Tag = "crf", StepFrequency = 1 });
        sp.Children.Add(new NumberBox { Header = "目标大小 (MB)", Minimum = 1, Maximum = 102400, Value = TargetMB, Tag = "size" });
        sp.Children.Add(new NumberBox { Header = "目标码率", Minimum = 100, Maximum = 100000, Value = Bitrate, Tag = "br" });
        var uc = new ComboBox { Header = "码率单位", Tag = "unit", SelectedIndex = BitrateUnit == "Mbps" ? 1 : 0 };
        uc.Items.Add("Kbps"); uc.Items.Add("Mbps");
        sp.Children.Add(uc);
        dialog.Content = sp;
    }

    public override void ApplyEdit(StackPanel p)
    {
        Mode = Win32.Get<RadioButtons>(p, "mode").SelectedIndex;
        Crf = (int)Win32.Get<Slider>(p, "crf").Value;
        TargetMB = (int)Win32.Get<NumberBox>(p, "size").Value;
        Bitrate = (int)Win32.Get<NumberBox>(p, "br").Value;
        BitrateUnit = Win32.Get<ComboBox>(p, "unit").SelectedIndex == 1 ? "Mbps" : "Kbps";
    }

    public override void Contribute(PipelineContext ctx)
    {
        ctx.VCodec = "libx264";
        ctx.ACodec = "aac";
        ctx.OverrideEncoding = true;
        switch (Mode)
        {
            case 0: ctx.EncodingArgs = $" -crf {Crf} -preset medium"; break;
            case 1:
                var dur = ctx.SourceDuration.TotalSeconds;
                if (dur > 0)
                {
                    var totalKbps = TargetMB * 8192.0 / dur;
                    var audioKbps = 128.0;
                    var vb = Math.Max(100, (int)((totalKbps - audioKbps) * 0.85));
                    var maxrate = (int)(vb * 1.5);
                    var bufsize = vb * 2;
                    ctx.EncodingArgs = $" -b:v {vb}k -maxrate {maxrate}k -bufsize {bufsize}k -b:a {(int)audioKbps}k -preset medium";
                }
                break;
            case 2:
                ctx.EncodingArgs = $" -b:v {Bitrate}{(BitrateUnit == "Mbps" ? "M" : "K")} -preset medium";
                break;
        }
    }
}

internal sealed class ResizeAction : VideoAction
{
    public int Width = 1920;
    public int Height = 1080;
    public bool KeepAspect = true;

    public override string Name => "调整分辨率";
    public override string Glyph => "\uE739";
    public override string Summary => $"{Width}×{Height}{(KeepAspect ? " (保持比例)" : "")}";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "调整分辨率";
        dialog.Content = new StackPanel { Spacing = 12, Width = 460, Children =
        {
            new NumberBox { Header = "宽度", Minimum = 1, Maximum = 7680, Value = Width, Tag = "w" },
            new NumberBox { Header = "高度", Minimum = 1, Maximum = 4320, Value = Height, Tag = "h" },
            new CheckBox { Content = "保持宽高比", IsChecked = KeepAspect, Tag = "ka" }
        }};
    }

    public override void ApplyEdit(StackPanel p)
    {
        Width = (int)Win32.Get<NumberBox>(p, "w").Value;
        Height = (int)Win32.Get<NumberBox>(p, "h").Value;
        KeepAspect = Win32.Get<CheckBox>(p, "ka").IsChecked == true;
    }

    public override void Contribute(PipelineContext ctx)
    {
        if (KeepAspect && ctx.SourceWidth > 0 && ctx.SourceHeight > 0)
        {
            var w = Width;
            if (w % 2 != 0) w--;
            var h = (int)Math.Round((double)ctx.SourceHeight / ctx.SourceWidth * w);
            if (h % 2 != 0) h--;
            ctx.VideoFilters.Add($"scale={w}:{h}");
        }
        else
        {
            var w = Width;
            var h = Height;
            if (w % 2 != 0) w--;
            if (h % 2 != 0) h--;
            ctx.VideoFilters.Add($"scale={w}:{h}");
        }
        ctx.VCodec = "libx264";
    }
}

internal sealed class RotateAction : VideoAction
{
    public int Mode; // 0=cw 1=ccw 2=180 3=hflip 4=vflip
    static readonly string[] Labels = { "顺时针 90°", "逆时针 90°", "180°", "水平翻转", "垂直翻转" };
    static readonly string[] Filters = { "transpose=1", "transpose=2", "hflip,vflip", "hflip", "vflip" };

    public override string Name => "旋转/翻转";
    public override string Glyph => "\uE7AD";
    public override string Summary => Labels[Mode];

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "旋转/翻转";
        var rb = new RadioButtons { Header = "方式", SelectedIndex = Mode, Tag = "mode" };
        foreach (var l in Labels) rb.Items.Add(l);
        dialog.Content = new StackPanel { Spacing = 12, Width = 460, Children = { rb } };
    }

    public override void ApplyEdit(StackPanel p) => Mode = Win32.Get<RadioButtons>(p, "mode").SelectedIndex;

    public override void Contribute(PipelineContext ctx)
    {
        ctx.VideoFilters.Add(Filters[Mode]);
        ctx.VCodec = "libx264";
    }
}

internal sealed class CropAction : VideoAction
{
    public int Left, Right, Top, Bottom;

    public override string Name => "裁剪画面";
    public override string Glyph => "\uE7A8";
    public override string Summary => $"L{Left} R{Right} T{Top} B{Bottom}";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Content = new StackPanel { Spacing = 12, Width = 460, Children =
        {
            new NumberBox { Header = "左裁剪 (px)", Minimum = 0, Maximum = 3840, Value = Left, Tag = "l" },
            new NumberBox { Header = "右裁剪 (px)", Minimum = 0, Maximum = 3840, Value = Right, Tag = "r" },
            new NumberBox { Header = "上裁剪 (px)", Minimum = 0, Maximum = 2160, Value = Top, Tag = "t" },
            new NumberBox { Header = "下裁剪 (px)", Minimum = 0, Maximum = 2160, Value = Bottom, Tag = "b" }
        }};
        dialog.Title = "裁剪画面";
    }

    public override void ApplyEdit(StackPanel p)
    {
        Left = (int)Win32.Get<NumberBox>(p, "l").Value;
        Right = (int)Win32.Get<NumberBox>(p, "r").Value;
        Top = (int)Win32.Get<NumberBox>(p, "t").Value;
        Bottom = (int)Win32.Get<NumberBox>(p, "b").Value;
    }

    public override void Contribute(PipelineContext ctx)
    {
        if (Left == 0 && Right == 0 && Top == 0 && Bottom == 0) return;
        ctx.VideoFilters.Add($"crop=2*trunc((iw-{Left}-{Right})/2):2*trunc((ih-{Top}-{Bottom})/2):{Left}:{Top}");
        ctx.VCodec = "libx264";
    }
}

internal sealed class SpeedAction : VideoAction
{
    public double Speed = 1.0;
    public bool AdjustAudio = true;

    public override string Name => "变速";
    public override string Glyph => "\uEC4A";
    public override string Summary => $"{Speed:F2}x{(AdjustAudio ? " (含音频)" : "")}";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "调整速度";
        dialog.Content = new StackPanel { Spacing = 12, Width = 460, Children =
        {
            new Slider { Header = "速度倍数", Minimum = 0.25, Maximum = 4.0, Value = Speed, StepFrequency = 0.25, Tag = "speed" },
            new CheckBox { Content = "同时调整音频速度（保持音调）", IsChecked = AdjustAudio, Tag = "aa" }
        }};
    }

    public override void ApplyEdit(StackPanel p)
    {
        Speed = Win32.Get<Slider>(p, "speed").Value;
        AdjustAudio = Win32.Get<CheckBox>(p, "aa").IsChecked == true;
    }

    public override void Contribute(PipelineContext ctx)
    {
        ctx.VideoFilters.Add($"setpts={1.0 / Speed:F4}*PTS");
        if (AdjustAudio)
        {
            var chain = AtempoChain(Speed);
            if (chain.Contains(','))
                ctx.AudioFilters.AddRange(chain.Split(','));
            else
                ctx.AudioFilters.Add($"atempo={chain}");
        }
        else
            ctx.NoAudio = true;
        ctx.VCodec = "libx264";
    }

    static string AtempoChain(double s)
    {
        if (s is >= 0.5 and <= 2.0) return $"{s:F4}";
        var parts = new List<string>();
        while (s > 2.0) { parts.Add("2.0"); s /= 2.0; }
        while (s < 0.5) { parts.Add("0.5"); s /= 0.5; }
        parts.Add($"{s:F4}");
        return string.Join(",", parts);
    }
}

internal sealed class VolumeAction : VideoAction
{
    public int Percent = 150;

    public override string Name => "调整音量";
    public override string Glyph => "\uE767";
    public override string Summary => $"{Percent / 100.0:F1}x";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "调整音量";
        dialog.Content = new StackPanel { Spacing = 12, Width = 460, Children =
        {
            new Slider { Header = "音量 %", Minimum = 0, Maximum = 500, Value = Percent, StepFrequency = 10, Tag = "vol" }
        }};
    }

    public override void ApplyEdit(StackPanel p) => Percent = (int)Win32.Get<Slider>(p, "vol").Value;

    public override void Contribute(PipelineContext ctx)
    {
        ctx.AudioFilters.Add($"volume={Percent / 100.0:F2}");
        ctx.ACodec = "aac";
    }
}

internal sealed class RemoveAudioAction : VideoAction
{
    public override string Name => "移除音频";
    public override string Glyph => "\uE7E8";
    public override string Summary => "静音";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "移除音频";
        dialog.Content = new TextBlock { Text = "此操作将移除视频中的所有音频轨道。", Opacity = 0.8 };
    }

    public override void ApplyEdit(StackPanel p) { }

    public override void Contribute(PipelineContext ctx) => ctx.NoAudio = true;
}

internal sealed class ExtractAudioAction : VideoAction
{
    public string Format = "MP3";
    public string Bitrate = "256";
    public override string Name => "提取音频";
    public override string Glyph => "\uEA69";
    public override string Summary => $"{Format} {Bitrate}kbps";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "提取音频";
        var sp = new StackPanel { Spacing = 12, Width = 460 };
        var fc = new ComboBox { Header = "格式", Tag = "fmt", SelectedIndex = Array.IndexOf(new[] { "MP3", "AAC", "FLAC", "WAV", "OGG", "M4A", "OPUS" }, Format) };
        foreach (var f in new[] { "MP3", "AAC", "FLAC", "WAV", "OGG", "M4A", "OPUS" }) fc.Items.Add(f);
        sp.Children.Add(fc);
        var bc = new ComboBox { Header = "质量 (Kbps)", Tag = "br", SelectedIndex = Array.IndexOf(new[] { "64", "128", "192", "256", "320" }, Bitrate) };
        foreach (var b in new[] { "64", "128", "192", "256", "320" }) bc.Items.Add(b);
        sp.Children.Add(bc);
        dialog.Content = sp;
    }

    public override void ApplyEdit(StackPanel p)
    {
        Format = (string)Win32.Get<ComboBox>(p, "fmt").SelectedItem;
        Bitrate = (string)Win32.Get<ComboBox>(p, "br").SelectedItem;
    }

    public override void Contribute(PipelineContext ctx)
    {
        ctx.AudioOnly = true;
        ctx.ACodec = Format switch
        {
            "MP3" => "libmp3lame", "AAC" => "aac", "FLAC" => "flac",
            "WAV" => "pcm_s16le", "OGG" => "libvorbis", "M4A" => "aac", "OPUS" => "libopus",
            _ => "libmp3lame"
        };
        if (Format is not "FLAC" and not "WAV")
            ctx.ExtraArgs += $" -b:a {Bitrate}k";
    }
}

internal sealed class ReplaceAudioAction : VideoAction
{
    public string AudioPath = "";
    public override string Name => "替换音频";
    public override string Glyph => "\uE8D6";
    public override string Summary => string.IsNullOrEmpty(AudioPath) ? "未选择音频" : Path.GetFileName(AudioPath);

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "替换音频";
        var sp = new StackPanel { Spacing = 12, Width = 460 };
        var tb = new TextBox { Header = "音频文件", Text = AudioPath, IsReadOnly = true, Tag = "path", PlaceholderText = "点击下方选择..." };
        sp.Children.Add(tb);
        var btn = new Button { Content = "选择音频文件" };
        btn.Click += (_, _) =>
        {
            var p = Win32.PickFile(dialog.XamlRoot, "音频文件\0*.mp3;*.aac;*.wav;*.flac;*.ogg;*.m4a;*.wma;*.opus\0所有文件\0*.*\0\0", "选择音频文件");
            if (p is not null) tb.Text = p;
        };
        sp.Children.Add(btn);
        dialog.Content = sp;
    }

    public override void ApplyEdit(StackPanel p) => AudioPath = Win32.Get<TextBox>(p, "path").Text.Trim();

    public override void Contribute(PipelineContext ctx)
    {
        if (string.IsNullOrEmpty(AudioPath)) return;
        ctx.ExtraInputs.Add($"-i \"{AudioPath}\"");
        ctx.ExtraArgs += $" -map 0:v:0 -map {ctx.ExtraInputs.Count}:a:0";
        ctx.ACodec = "aac";
    }
}

internal sealed class WatermarkAction : VideoAction
{
    public int Type; // 0=image 1=text
    public string ImagePath = "";
    public string Text = "";
    public int FontSize = 36;
    public int Position = 3; // 右下
    public int Opacity = 70;

    static readonly string[] PosLabels = { "左上", "上方居中", "右上", "右下", "下方居中", "左下", "居中" };
    static readonly (string x, string y)[] PosCoords = {
        ("10", "10"), ("(w-text_w)/2", "10"), ("w-text_w-10", "10"),
        ("w-text_w-10", "h-text_h-10"), ("(w-text_w)/2", "h-text_h-10"),
        ("10", "h-text_h-10"), ("(w-text_w)/2", "(h-text_h)/2")
    };

    public override string Name => "水印";
    public override string Glyph => "\uE7BA";
    public override string Summary => Type == 0 ? (string.IsNullOrEmpty(ImagePath) ? "图片水印 (未选)" : $"图片: {Path.GetFileName(ImagePath)}") : $"文字: {Text}";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "添加水印";
        var sp = new StackPanel { Spacing = 12, Width = 460 };
        var tr = new RadioButtons { Header = "类型", SelectedIndex = Type, Tag = "type" };
        tr.Items.Add("图片水印"); tr.Items.Add("文字水印");
        sp.Children.Add(tr);

        var ip = new TextBox { Header = "图片路径", Text = ImagePath, IsReadOnly = true, Tag = "img", PlaceholderText = "点击下方选择..." };
        sp.Children.Add(ip);
        var ib = new Button { Content = "选择图片" };
        ib.Click += (_, _) =>
        {
            var p = Win32.PickFile(dialog.XamlRoot, "图片\0*.png;*.jpg;*.jpeg;*.bmp;*.webp\0所有文件\0*.*\0\0", "选择水印图片");
            if (p is not null) ip.Text = p;
        };
        sp.Children.Add(ib);

        sp.Children.Add(new TextBox { Header = "水印文字", Text = Text, Tag = "text" });
        sp.Children.Add(new NumberBox { Header = "字体大小", Minimum = 8, Maximum = 200, Value = FontSize, Tag = "fs" });
        var pc = new ComboBox { Header = "位置", Tag = "pos", SelectedIndex = Position };
        foreach (var l in PosLabels) pc.Items.Add(l);
        sp.Children.Add(pc);
        sp.Children.Add(new Slider { Header = "不透明度 %", Minimum = 10, Maximum = 100, Value = Opacity, StepFrequency = 5, Tag = "op" });
        dialog.Content = sp;
    }

    public override void ApplyEdit(StackPanel p)
    {
        Type = Win32.Get<RadioButtons>(p, "type").SelectedIndex;
        ImagePath = Win32.Get<TextBox>(p, "img").Text.Trim();
        Text = Win32.Get<TextBox>(p, "text").Text.Trim();
        FontSize = (int)Win32.Get<NumberBox>(p, "fs").Value;
        Position = Win32.Get<ComboBox>(p, "pos").SelectedIndex;
        Opacity = (int)Win32.Get<Slider>(p, "op").Value;
    }

    public override void Contribute(PipelineContext ctx)
    {
        if (Type == 0 && !string.IsNullOrEmpty(ImagePath))
        {
            ctx.ExtraInputs.Add($"-i \"{ImagePath}\"");
            var idx = ctx.ExtraInputs.Count;
            ctx.FilterComplex.Add($"[{idx}]format=rgba,colorchannelmixer=aa={Opacity / 100.0:F2}[wm];[0][wm]overlay={PosCoords[Position].x}:{PosCoords[Position].y}");
            ctx.VCodec = "libx264";
        }
        else if (Type == 1 && !string.IsNullOrEmpty(Text))
        {
            var esc = Text.Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
            ctx.VideoFilters.Add($"drawtext=text='{esc}':fontsize={FontSize}:fontcolor=white@0.{Opacity:D2}:x={PosCoords[Position].x}:y={PosCoords[Position].y}");
            ctx.VCodec = "libx264";
        }
    }
}

internal sealed class FilterAction : VideoAction
{
    public int Brightness, Contrast, Saturation;
    public string QuickFilter = "";
    public string QuickFilterName = "";

    static readonly (string name, string filter)[] Presets = {
        ("黑白", "hue=s=0"), ("复古", "curves=vintage"),
        ("暖色调", "eq=saturation=1.3:brightness=0.05"), ("冷色调", "eq=saturation=0.8:brightness=-0.02:contrast=1.1"),
        ("锐化", "unsharp=5:5:1.0"), ("模糊", "boxblur=2:1"),
        ("反色", "negate"), ("镜像", "hflip"), ("降噪", "hqdn3d=4:3:6:4.5")
    };

    public override string Name => "滤镜";
    public override string Glyph => "\uE790";
    public override string Summary =>
        (Brightness != 0 || Contrast != 0 || Saturation != 0 || !string.IsNullOrEmpty(QuickFilter))
        ? $"B{Brightness:+0;-0;0} C{Contrast:+0;-0;0} S{Saturation:+0;-0;0}{(string.IsNullOrEmpty(QuickFilterName) ? "" : " + " + QuickFilterName)}".Trim()
        : "无调整";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "画面滤镜";
        var sp = new StackPanel { Spacing = 12, Width = 460 };
        sp.Children.Add(new Slider { Header = "亮度 (-100~100)", Minimum = -100, Maximum = 100, Value = Brightness, StepFrequency = 5, Tag = "b" });
        sp.Children.Add(new Slider { Header = "对比度 (-100~100)", Minimum = -100, Maximum = 100, Value = Contrast, StepFrequency = 5, Tag = "c" });
        sp.Children.Add(new Slider { Header = "饱和度 (-100~100)", Minimum = -100, Maximum = 100, Value = Saturation, StepFrequency = 5, Tag = "s" });
        var fc = new ComboBox { Header = "快速滤镜", Tag = "qf", PlaceholderText = "无" };
        fc.Items.Add(new ComboBoxItem { Content = "无", Tag = "" });
        foreach (var (n, f) in Presets)
            fc.Items.Add(new ComboBoxItem { Content = n, Tag = f });
        for (int i = 0; i < fc.Items.Count; i++)
            if (fc.Items[i] is ComboBoxItem ci && (string)ci.Tag == QuickFilter) { fc.SelectedIndex = i; break; }
        sp.Children.Add(fc);
        dialog.Content = sp;
    }

    public override void ApplyEdit(StackPanel p)
    {
        Brightness = (int)Win32.Get<Slider>(p, "b").Value;
        Contrast = (int)Win32.Get<Slider>(p, "c").Value;
        Saturation = (int)Win32.Get<Slider>(p, "s").Value;
        var ci = Win32.Get<ComboBox>(p, "qf").SelectedItem as ComboBoxItem;
        QuickFilter = ci?.Tag as string ?? "";
        QuickFilterName = ci?.Content as string ?? "";
        if (QuickFilterName == "无") QuickFilterName = "";
    }

    public override void Contribute(PipelineContext ctx)
    {
        var b = Brightness / 100.0;
        var c = (Contrast + 100.0) / 100.0;
        var s = (Saturation + 100.0) / 100.0;
        if (b != 0 || c != 1.0 || s != 1.0)
            ctx.VideoFilters.Add($"eq=brightness={b:F2}:contrast={c:F2}:saturation={s:F2}");
        if (!string.IsNullOrEmpty(QuickFilter))
            ctx.VideoFilters.Add(QuickFilter);
        ctx.VCodec = "libx264";
    }
}

internal sealed class MakeGifAction : VideoAction
{
    public string Start = "00:00:00";
    public string End = "";
    public int Width = 480;
    public int Fps = 15;
    public bool Optimize = true;

    public override string Name => "GIF 制作";
    public override string Glyph => "\uE8B2";
    public override string Summary => $"{Width}px / {Fps}fps{(Optimize ? " (优化)" : "")}";

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "GIF 制作";
        dialog.Content = new StackPanel { Spacing = 12, Width = 460, Children =
        {
            new TextBox { Header = "开始时间", Text = Start, Tag = "gs", PlaceholderText = "00:00:00" },
            new TextBox { Header = "结束时间", Text = End, Tag = "ge", PlaceholderText = "00:00:10" },
            new NumberBox { Header = "宽度 (px)", Minimum = 100, Maximum = 1920, Value = Width, Tag = "gw" },
            new NumberBox { Header = "帧率 (fps)", Minimum = 5, Maximum = 30, Value = Fps, Tag = "gf" },
            new CheckBox { Content = "优化 GIF（减小文件，生成较慢）", IsChecked = Optimize, Tag = "go" }
        }};
    }

    public override void ApplyEdit(StackPanel p)
    {
        Start = Win32.Get<TextBox>(p, "gs").Text.Trim();
        End = Win32.Get<TextBox>(p, "ge").Text.Trim();
        Width = (int)Win32.Get<NumberBox>(p, "gw").Value;
        Fps = (int)Win32.Get<NumberBox>(p, "gf").Value;
        Optimize = Win32.Get<CheckBox>(p, "go").IsChecked == true;
    }

    public override void Contribute(PipelineContext ctx)
    {
        ctx.IsGif = true;
        if (!string.IsNullOrEmpty(Start)) ctx.SeekBefore = $"-ss {Start}";
        if (!string.IsNullOrEmpty(End)) ctx.SeekAfter = $"-to {End}";
        if (Optimize)
            ctx.VideoFilters.Add($"fps={Fps},scale={Width}:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse");
        else
            ctx.VideoFilters.Add($"fps={Fps},scale={Width}:-1:flags=lanczos");
    }
}

internal sealed class CustomAction : VideoAction
{
    public string Args = "";
    public override string Name => "自定义参数";
    public override string Glyph => "\uE9D9";
    public override string Summary => string.IsNullOrEmpty(Args) ? "(空)" : Args;

    public override void Edit(ContentDialog dialog)
    {
        dialog.Title = "自定义 FFmpeg 参数";
        dialog.Content = new StackPanel { Spacing = 8, Width = 460, Children =
        {
            new TextBlock { Text = "直接输入额外 FFmpeg 参数，将追加到自动生成的参数之后。", FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
            new TextBox { Text = Args, Tag = "args", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, PlaceholderText = "如: -vf \"eq=brightness=0.1\" -r 30" }
        }};
    }

    public override void ApplyEdit(StackPanel p) => Args = Win32.Get<TextBox>(p, "args").Text.Trim();

    public override void Contribute(PipelineContext ctx)
    {
        if (!string.IsNullOrEmpty(Args)) ctx.ExtraArgs += $" {Args}";
    }

    internal bool UseCommandWindow => true;
}

// ═══════════════════════════════════════════════════════════════
//  Pipeline context — accumulates all action contributions
// ═══════════════════════════════════════════════════════════════

internal sealed class PipelineContext
{
    public string SeekBefore = "";
    public string SeekAfter = "";
    public List<string> ExtraInputs = [];
    public List<string> VideoFilters = [];
    public List<string> AudioFilters = [];
    public List<string> FilterComplex = [];
    public string ExtraArgs = "";
    public string? VCodec;
    public string? ACodec;
    public bool NoAudio;
    public bool AudioOnly;
    public bool IsGif;
    public bool OverrideEncoding;
    public string EncodingArgs = "";
    public TimeSpan SourceDuration;
    public int SourceWidth = 1920;
    public int SourceHeight = 1080;

    public string Build(string source, string output, string? vcodecDefault, string? acodecDefault, int crf, string preset)
    {
        var sb = new StringBuilder();

        if (AudioOnly && !IsGif)
        {
            sb.Append($"{SeekBefore} -i \"{source}\"");
            foreach (var ei in ExtraInputs) sb.Append($" {ei}");
            sb.Append(SeekAfter);
            sb.Append($" -vn -c:a {ACodec}");
            sb.Append(ExtraArgs);
            sb.Append($" \"{output}\"");
            return sb.ToString();
        }

        if (IsGif)
        {
            sb.Append($"{SeekBefore} -i \"{source}\"");
            sb.Append(SeekAfter);
            if (VideoFilters.Count > 0)
                sb.Append($" -vf \"{string.Join(",", VideoFilters)}\"");
            sb.Append($" -an \"{output}\"");
            return sb.ToString();
        }

        sb.Append($"{SeekBefore} -i \"{source}\"");
        foreach (var ei in ExtraInputs) sb.Append($" {ei}");
        sb.Append(SeekAfter);

        var useFilterComplex = FilterComplex.Count > 0;
        if (useFilterComplex)
            sb.Append($" -filter_complex \"{string.Join(";", FilterComplex)}\"");
        else if (VideoFilters.Count > 0)
            sb.Append($" -vf \"{string.Join(",", VideoFilters)}\"");

        if (AudioFilters.Count > 0)
            sb.Append($" -af \"{string.Join(",", AudioFilters)}\"");

        var vc = VCodec ?? vcodecDefault ?? "libx264";
        var ac = ACodec ?? acodecDefault ?? "aac";
        if (NoAudio)
        {
            if (vc != "copy") sb.Append($" -c:v {vc}");
            else sb.Append(" -c:v copy");
            sb.Append(" -an");
        }
        else
        {
            if (vc == "copy") sb.Append(" -c:v copy");
            else
            {
                sb.Append($" -c:v {vc}");
                if (OverrideEncoding)
                    sb.Append(EncodingArgs);
                else
                    sb.Append($" -crf {crf} -preset {preset}");
            }
            if (ac == "copy") sb.Append(" -c:a copy");
            else sb.Append($" -c:a {ac}");
        }

        sb.Append(ExtraArgs);
        sb.Append($" \"{output}\"");
        return sb.ToString();
    }
}

// ═══════════════════════════════════════════════════════════════
//  Win32 helpers
// ═══════════════════════════════════════════════════════════════

internal static class Win32
{
    public static T Get<T>(StackPanel p, string tag) where T : FrameworkElement
    {
        foreach (var c in p.Children)
            if (c is T t && t.Tag is string s && s == tag) return t;
        throw new KeyNotFoundException($"Control with tag '{tag}' not found");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENFILENAME
    {
        public int lStructSize; public IntPtr hwndOwner; public IntPtr hInstance;
        public string lpstrFilter; public string lpstrCustomFilter; public int nMaxCustFilter;
        public int nFilterIndex; public string lpstrFile; public int nMaxFile;
        public string lpstrFileTitle; public int nMaxFileTitle; public string lpstrInitialDir;
        public string lpstrTitle; public int Flags; public short nFileOffset;
        public short nFileExtension; public string lpstrDefExt; public IntPtr lCustData;
        public IntPtr lpfnHook; public string lpTemplateName; public IntPtr pvReserved;
        public int dwReserved; public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    const int OFN_FILEMUSTEXIST = 0x1000, OFN_NOCHANGEDIR = 8, OFN_OVERWRITEPROMPT = 2, OFN_ALLOWMULTISELECT = 0x200, OFN_EXPLORER = 0x80000;

    static IntPtr Hwnd() => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);

    public static string? PickOpen(string filter, string title)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = Hwnd(),
            lpstrFilter = filter,
            lpstrFile = new string(new char[1024]),
            nMaxFile = 1024,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR
        };
        return GetOpenFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    public static string? PickFile(XamlRoot anchor, string filter, string title)
        => PickOpen(filter, title);

    public static List<string> PickMultiple(string filter, string title)
    {
        var result = new List<string>();
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = Hwnd(),
            lpstrFilter = filter,
            lpstrFile = new string(new char[8192]),
            nMaxFile = 8192,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_ALLOWMULTISELECT | OFN_EXPLORER
        };
        if (!GetOpenFileName(ref ofn)) return result;
        var parts = ofn.lpstrFile.TrimEnd('\0').Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) result.Add(parts[0]);
        else for (int i = 1; i < parts.Length; i++) result.Add(Path.Combine(parts[0], parts[i]));
        return result;
    }

    public static string? PickSave(string filter, string defExt, string? initialName = null)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = Hwnd(),
            lpstrFilter = filter,
            lpstrFile = (initialName ?? "").PadRight(1024, '\0'),
            nMaxFile = 1024,
            lpstrTitle = "选择输出位置",
            lpstrDefExt = defExt,
            Flags = OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR
        };
        return GetSaveFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }
}

// ═══════════════════════════════════════════════════════════════
//  Window
// ═══════════════════════════════════════════════════════════════

public sealed partial class VideoProcessorPage : Page
{
    static readonly string VideoFilter = "视频文件\0*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.3gp;*.ts;*.mts;*.vob;*.ogv;*.rm;*.rmvb;*.mpg;*.mpeg;*.asf;*.dv\0所有文件\0*.*\0\0";

    record FormatOption(string Name, string Ext, string DefaultVCodec, string DefaultACodec);
    static readonly FormatOption[] Formats = {
        new("MP4", ".mp4", "libx264", "aac"), new("MKV", ".mkv", "libx264", "aac"),
        new("AVI", ".avi", "libx264", "mp3"), new("MOV", ".mov", "libx264", "aac"),
        new("WebM", ".webm", "libvpx-vp9", "libopus"), new("GIF", ".gif", "", ""),
        new("MP3", ".mp3", "", "libmp3lame"), new("AAC", ".aac", "", "aac"),
        new("FLAC", ".flac", "", "flac"), new("WAV", ".wav", "", "pcm_s16le"),
        new("M4A", ".m4a", "", "aac"), new("OGG", ".ogg", "", "libvorbis"),
        new("OPUS", ".opus", "", "libopus"), new("TS", ".ts", "libx264", "aac"),
        new("FLV", ".flv", "libx264", "aac"), new("WMV", ".wmv", "wmv2", "wmav2"),
    };

    readonly ObservableCollection<VideoAction> _actions = [];
    readonly List<string> _appended = [];
    string? _sourceFile;
    VideoFileInfo? _sourceInfo;
    string? _outputFile;
    bool _userEditedOutput;
    FormatOption? _selectedFormat;
    CancellationTokenSource? _cts;
    DownloadItem? _dlItem;

    public VideoProcessorPage()
    {
        InitializeComponent();

        InitFormatGrid();
        _ = CheckFfmpegAsync();
    }

    // ── FFmpeg status ──

    async Task CheckFfmpegAsync()
    {
        if (FfmpegService.IsFfmpegReady)
        {
            FfmpegStatusText.Text = $"FFmpeg: 就绪 ({FfmpegService.GetFfmpegSize()})";
            DeleteFfmpegBtn.Visibility = Visibility.Visible;
            FfmpegDownloadPanel.Visibility = Visibility.Collapsed;
            WorkArea.Visibility = Visibility.Visible;
        }
        else
        {
            FfmpegStatusText.Text = "FFmpeg: 未安装";
            DeleteFfmpegBtn.Visibility = Visibility.Collapsed;
            FfmpegDownloadPanel.Visibility = Visibility.Visible;
            WorkArea.Visibility = Visibility.Collapsed;
        }
    }

    async void DeleteFfmpegBtn_Click(object sender, RoutedEventArgs e)
    {
        var d = new ContentDialog
        {
            Title = "删除 FFmpeg", Content = "确定删除已下载的 FFmpeg？使用时需重新下载。",
            PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme, XamlRoot = Content.XamlRoot
        };
        if (await d.ShowAsync() != ContentDialogResult.Primary) return;
        FfmpegService.DeleteFfmpeg();
        await CheckFfmpegAsync();
        ShowToast("已删除", "FFmpeg 已删除", InfoBarSeverity.Informational);
    }

    void DownloadFfmpegBtn_Click(object sender, RoutedEventArgs e)
    {
        DownloadFfmpegBtn.IsEnabled = false;
        FfmpegDownloadProgress.Visibility = Visibility.Visible;
        FfmpegDownloadProgress.IsIndeterminate = true;
        FfmpegDownloadStatus.Visibility = Visibility.Visible;
        FfmpegDownloadStatus.Text = "正在加入下载队列...";

        _dlItem = FfmpegService.EnsureFfmpegViaQueue();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (_dlItem is null) return;
            var p = _dlItem.Progress;
            var s = _dlItem.State;

            if (s == DownloadItemState.Downloading && p is not null)
            {
                FfmpegDownloadProgress.IsIndeterminate = false;
                FfmpegDownloadProgress.Value = p.Percentage;
                FfmpegDownloadStatus.Text = $"下载中 {DownloadQueueService.FormatSize(p.BytesReceived)}/{(p.TotalBytes > 0 ? DownloadQueueService.FormatSize(p.TotalBytes) : "?")} | {DownloadQueueService.FormatSpeed(p.SpeedMbps)}";
            }
            else if (s is DownloadItemState.Processing or DownloadItemState.Queued or DownloadItemState.Resolving)
            {
                FfmpegDownloadProgress.IsIndeterminate = true;
                FfmpegDownloadStatus.Text = s == DownloadItemState.Processing ? (_dlItem.ProcessingStatus ?? "处理中...") : (s == DownloadItemState.Queued ? "排队中..." : "解析中...");
            }
            else if (s == DownloadItemState.Completed)
            {
                timer.Stop();
                FfmpegDownloadProgress.Value = 100;
                FfmpegDownloadStatus.Text = "FFmpeg 就绪！";
                _ = CheckFfmpegAsync();
                ShowToast("下载完成", "FFmpeg 已就绪", InfoBarSeverity.Success);
            }
            else if (s is DownloadItemState.Failed or DownloadItemState.Cancelled)
            {
                timer.Stop();
                FfmpegDownloadStatus.Text = s == DownloadItemState.Failed ? $"失败: {_dlItem.ErrorMessage}" : "已取消";
                DownloadFfmpegBtn.IsEnabled = true;
            }
        };
        timer.Start();
    }

    // ── Format grid ──

    void InitFormatGrid()
    {
        foreach (var fmt in Formats)
        {
            var item = new GridViewItem { Tag = fmt };
            item.Content = new StackPanel { Width = 64, Spacing = 2, Children =
            {
                new FontIcon { Glyph = fmt.DefaultVCodec == "" && fmt.DefaultACodec != "" ? "\uE7E8" : "\uE8B2", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = fmt.Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
            }};
            FormatGrid.Items.Add(item);
        }
    }

    void FormatGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormatGrid.SelectedItem is GridViewItem item && item.Tag is FormatOption fmt)
        {
            _selectedFormat = fmt;
            PopulateCodecs(fmt);
            var showCodec = fmt.DefaultVCodec != "";
            CodecPanel.Visibility = showCodec ? Visibility.Visible : Visibility.Collapsed;
            QualityPanel.Visibility = showCodec ? Visibility.Visible : Visibility.Collapsed;
            AutoSetOutput();
            UpdateStartButton();
        }
    }

    void PopulateCodecs(FormatOption fmt)
    {
        VideoCodecCombo.Items.Clear();
        AudioCodecCombo.Items.Clear();

        var vc = fmt.Ext switch
        {
            ".mp4" or ".mkv" or ".mov" or ".ts" or ".flv" or ".avi" => new[] { "libx264", "libx265", "libvpx-vp9", "copy" },
            ".webm" => new[] { "libvpx-vp9", "libvpx", "copy" },
            ".wmv" => new[] { "wmv2", "copy" },
            _ => Array.Empty<string>()
        };
        var ac = fmt.Ext switch
        {
            ".mp4" or ".mov" or ".m4a" or ".aac" => new[] { "aac", "copy" },
            ".mkv" or ".ts" => new[] { "aac", "libmp3lame", "libopus", "copy" },
            ".avi" or ".flv" => new[] { "mp3", "aac", "copy" },
            ".mp3" => new[] { "libmp3lame" }, ".flac" => new[] { "flac" },
            ".wav" => new[] { "pcm_s16le" }, ".ogg" => new[] { "libvorbis" },
            ".opus" => new[] { "libopus" }, ".webm" => new[] { "libopus", "libvorbis", "copy" },
            ".wmv" => new[] { "wmav2", "copy" }, _ => Array.Empty<string>()
        };

        foreach (var c in vc) VideoCodecCombo.Items.Add(c);
        foreach (var c in ac) AudioCodecCombo.Items.Add(c);
        VideoCodecCombo.SelectedIndex = 0;
        AudioCodecCombo.SelectedIndex = 0;
    }

    // ── Source file ──

    async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var path = Win32.PickOpen(VideoFilter, "选择视频文件");
        if (string.IsNullOrEmpty(path)) return;

        _sourceFile = path;
        SourceFileBox.Text = path;
        await LoadSourceInfoAsync();
        AutoSetOutput();
        UpdateStartButton();
    }

    void AppendVideo_Click(object sender, RoutedEventArgs e)
    {
        var files = Win32.PickMultiple(VideoFilter, "选择追加视频");
        if (files.Count == 0) return;
        _appended.AddRange(files);
        RenderAppendList();
        UpdateStartButton();
    }

    async Task LoadSourceInfoAsync()
    {
        if (_sourceFile is null) return;
        SourceInfoPanel.Visibility = Visibility.Collapsed;
        SourceInfoHost.Children.Clear();
        _sourceInfo = await FfmpegService.ProbeAsync(_sourceFile);
        if (_sourceInfo is null) return;

        SourceInfoHost.Children.Add(MakeInfo("时长", _sourceInfo.DurationText));
        SourceInfoHost.Children.Add(MakeInfo("分辨率", _sourceInfo.Resolution));
        SourceInfoHost.Children.Add(MakeInfo("视频编码", _sourceInfo.VideoCodec ?? "无"));
        SourceInfoHost.Children.Add(MakeInfo("音频编码", _sourceInfo.AudioCodec ?? "无"));
        SourceInfoHost.Children.Add(MakeInfo("码率", _sourceInfo.BitRateText));
        SourceInfoPanel.Visibility = Visibility.Visible;
    }

    static StackPanel MakeInfo(string label, string value) => new()
    {
        Spacing = 2, Children =
        {
            new TextBlock { Text = label, FontSize = 10, Opacity = 0.6 },
            new TextBlock { Text = value, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
        }
    };

    void RenderAppendList()
    {
        AppendListContainer.Children.Clear();
        AppendListPanel.Visibility = _appended.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _appended.Count; i++)
        {
            var idx = i;
            var name = Path.GetFileName(_appended[i]);
            var row = new Grid { ColumnSpacing = 8, ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }};
            var num = new TextBlock { Text = $"{i + 2}.", FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(num, 0);
            var nm = new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nm, 1);
            var btn = new Button { Content = "✕", FontSize = 10, Padding = new Thickness(6, 1, 6, 1), MinWidth = 0, MinHeight = 0 };
            btn.Click += (_, _) => { _appended.RemoveAt(idx); RenderAppendList(); UpdateStartButton(); };
            Grid.SetColumn(btn, 2);
            row.Children.Add(num); row.Children.Add(nm); row.Children.Add(btn);
            AppendListContainer.Children.Add(row);
        }
    }

    // ── Output ──

    void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var ext = _selectedFormat?.Ext ?? ".mp4";
        var name = _sourceFile is not null ? Path.GetFileNameWithoutExtension(_sourceFile) + "_processed" : "";
        var path = Win32.PickSave($"{ext.TrimStart('.').ToUpper()} 文件\0*{ext}\0所有文件\0*.*\0\0", ext.TrimStart('.'), name);
        if (string.IsNullOrEmpty(path)) return;
        _outputFile = path;
        OutputFileBox.Text = path;
    }

    void OutputFileBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_userEditedOutput && !string.IsNullOrEmpty(OutputFileBox.Text)) _userEditedOutput = true;
        _outputFile = OutputFileBox.Text.Trim();
        UpdateStartButton();
    }

    void AutoSetOutput()
    {
        if (_sourceFile is null) return;
        _userEditedOutput = false;
        var dir = Path.GetDirectoryName(_sourceFile)!;
        var name = Path.GetFileNameWithoutExtension(_sourceFile);
        var ext = _selectedFormat?.Ext ?? Path.GetExtension(_sourceFile);
        _outputFile = Path.Combine(dir, $"{name}_processed{ext}");
        OutputFileBox.Text = _outputFile;
    }

    void UpdateStartButton()
    {
        StartBtn.IsEnabled = !string.IsNullOrEmpty(_sourceFile)
            && _actions.Count > 0
            && !string.IsNullOrEmpty(_outputFile)
            && FfmpegService.IsFfmpegReady;
    }

    // ── Action pipeline ──

    void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        VideoAction action = tag switch
        {
            "Trim" => new TrimAction(),
            "Compress" => new CompressAction(),
            "Resize" => new ResizeAction(),
            "Rotate" => new RotateAction(),
            "Crop" => new CropAction(),
            "Speed" => new SpeedAction(),
            "Volume" => new VolumeAction(),
            "RemoveAudio" => new RemoveAudioAction(),
            "ExtractAudio" => new ExtractAudioAction(),
            "ReplaceAudio" => new ReplaceAudioAction(),
            "Watermark" => new WatermarkAction(),
            "Filter" => new FilterAction(),
            "MakeGif" => new MakeGifAction(),
            "Custom" => new CustomAction(),
            _ => throw new ArgumentOutOfRangeException(tag)
        };
        _actions.Add(action);
        RenderActionList();
        UpdateStartButton();
    }

    async void EditAction(int idx)
    {
        if (idx < 0 || idx >= _actions.Count) return;
        var action = _actions[idx];

        if (action is CustomAction ca)
        {
            await OpenCommandWindow(ca);
            return;
        }

        var dialog = new ContentDialog
        {
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme,
            XamlRoot = Content.XamlRoot
        };
        action.Edit(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (dialog.Content is StackPanel sp) action.ApplyEdit(sp);
            RenderActionList();
        }
    }

    async Task OpenCommandWindow(CustomAction ca)
    {
        var previewCmd = BuildCommand();
        var win = new Window
        {
            Title = "FFmpeg 命令编辑器",
        };
        var root = new Grid { RowSpacing = 12, Padding = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl1 = new TextBlock { Text = "自动生成的完整命令（只读预览）：", FontSize = 13, Opacity = 0.8 };
        Grid.SetRow(lbl1, 0);
        root.Children.Add(lbl1);

        var previewBox = new TextBox
        {
            Text = previewCmd,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 80,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Lime)
        };
        Grid.SetRow(previewBox, 1);
        root.Children.Add(previewBox);

        var lbl2 = new TextBlock { Text = "追加的自定义参数：", FontSize = 13, Opacity = 0.8 };
        Grid.SetRow(lbl2, 2);
        root.Children.Add(lbl2);

        var argsBox = new TextBox
        {
            Text = ca.Args,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 60,
            PlaceholderText = "如: -vf \"eq=brightness=0.1\" -r 30"
        };
        Grid.SetRow(argsBox, 3);
        root.Children.Add(argsBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "确定", Style = Application.Current.Resources["AccentButtonStyle"] as Style };
        var cancelBtn = new Button { Content = "取消" };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        Grid.SetRow(btnRow, 4);
        root.Children.Add(btnRow);

        win.Content = root;
        win.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 500));
        win.AppWindow.Title = "FFmpeg 命令编辑器";

        var tcs = new TaskCompletionSource<bool>();
        okBtn.Click += (_, _) => { tcs.SetResult(true); win.Close(); };
        cancelBtn.Click += (_, _) => { tcs.SetResult(false); win.Close(); };
        win.Closed += (_, _) => tcs.TrySetResult(false);

        win.Activate();
        if (await tcs.Task)
        {
            ca.Args = argsBox.Text.Trim();
            RenderActionList();
        }
    }

    void RenderActionList()
    {
        ActionListContainer.Children.Clear();
        ActionEmptyHint.Visibility = _actions.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        for (int i = 0; i < _actions.Count; i++)
        {
            var idx = i;
            var a = _actions[i];

            var card = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                Background = new SolidColorBrush(ThemeColors.SubtleBg)
            };

            var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            }};

            var num = new TextBlock { Text = $"{i + 1}.", FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(num, 0);
            var icon = new FontIcon { Glyph = a.Glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 1);
            var info = new StackPanel { Spacing = 1, Children =
            {
                new TextBlock { Text = a.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = a.Summary, FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis }
            }};
            Grid.SetColumn(info, 2);

            var editBtn = new Button { Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 }, Padding = new Thickness(8, 4, 8, 4), MinWidth = 0 };
            editBtn.Click += (_, _) => EditAction(idx);
            Grid.SetColumn(editBtn, 3);

            var upBtn = new Button { Content = new FontIcon { Glyph = "\uE74A", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4), MinWidth = 0, IsEnabled = i > 0 };
            upBtn.Click += (_, _) => { (_actions[idx], _actions[idx - 1]) = (_actions[idx - 1], _actions[idx]); RenderActionList(); };
            Grid.SetColumn(upBtn, 4);

            var delBtn = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4), MinWidth = 0 };
            delBtn.Click += (_, _) => { _actions.RemoveAt(idx); RenderActionList(); UpdateStartButton(); };
            Grid.SetColumn(delBtn, 5);

            grid.Children.Add(num); grid.Children.Add(icon); grid.Children.Add(info);
            grid.Children.Add(editBtn); grid.Children.Add(upBtn); grid.Children.Add(delBtn);
            card.Child = grid;
            ActionListContainer.Children.Add(card);
        }
    }

    // ── Build & run ──

    string BuildCommand()
    {
        if (_sourceFile is null || _outputFile is null) return "";

        var ctx = new PipelineContext
        {
            SourceDuration = _sourceInfo?.Duration ?? TimeSpan.Zero,
            SourceWidth = _sourceInfo?.Width ?? 1920,
            SourceHeight = _sourceInfo?.Height ?? 1080
        };

        foreach (var a in _actions) a.Contribute(ctx);

        // Merge mode: appended files
        if (_appended.Count > 0)
        {
            var concatFile = Path.Combine(Path.GetTempPath(), $"ffmpeg_concat_{Guid.NewGuid():N}.txt");
            var all = new List<string> { _sourceFile };
            all.AddRange(_appended);
            File.WriteAllLines(concatFile, all.Select(f => $"file '{f.Replace("\\", "/").Replace("'", "'\\''")}'"));

            // Override: use concat as main input
            var mergeArgs = $"{ctx.SeekBefore} -f concat -safe 0 -i \"{concatFile}\"";
            foreach (var ei in ctx.ExtraInputs) mergeArgs += $" {ei}";
            mergeArgs += ctx.SeekAfter;

            if (ctx.FilterComplex.Count > 0)
                mergeArgs += $" -filter_complex \"{string.Join(";", ctx.FilterComplex)}\"";
            else if (ctx.VideoFilters.Count > 0)
                mergeArgs += $" -vf \"{string.Join(",", ctx.VideoFilters)}\"";
            if (ctx.AudioFilters.Count > 0)
                mergeArgs += $" -af \"{string.Join(",", ctx.AudioFilters)}\"";

            if (ctx.IsGif) mergeArgs += " -an";
            else if (ctx.NoAudio)
            {
                mergeArgs += ctx.VCodec is not null and not "copy" ? $" -c:v {ctx.VCodec}" : " -c copy";
                mergeArgs += " -an";
            }
            else
            {
                var vc = ctx.VCodec ?? _selectedFormat?.DefaultVCodec ?? "libx264";
                var ac = ctx.ACodec ?? _selectedFormat?.DefaultACodec ?? "aac";
                if (vc == "copy") mergeArgs += " -c copy";
                else
                {
                    mergeArgs += $" -c:v {vc}";
                    if (ctx.OverrideEncoding) mergeArgs += ctx.EncodingArgs;
                    else mergeArgs += $" -crf {(int)CrfSlider.Value} -preset {PresetCombo.SelectedItem}";
                    mergeArgs += ac == "copy" ? " -c:a copy" : $" -c:a {ac}";
                }
            }
            mergeArgs += ctx.ExtraArgs;
            mergeArgs += $" \"{_outputFile}\"";
            return mergeArgs;
        }

        var vcodecDefault = _selectedFormat?.DefaultVCodec;
        var acodecDefault = _selectedFormat?.DefaultACodec;
        if (vcodecDefault is not null && vcodecDefault != "copy" && ctx.VCodec is null && !ctx.IsGif && !ctx.AudioOnly)
            ctx.VCodec = VideoCodecCombo.SelectedItem as string ?? vcodecDefault;
        if (acodecDefault is not null && acodecDefault != "copy" && ctx.ACodec is null && !ctx.NoAudio && !ctx.IsGif)
            ctx.ACodec = AudioCodecCombo.SelectedItem as string ?? acodecDefault;

        return ctx.Build(_sourceFile, _outputFile, vcodecDefault, acodecDefault, (int)CrfSlider.Value, PresetCombo.SelectedItem as string ?? "medium");
    }

    string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"源文件: {Path.GetFileName(_sourceFile)}");
        if (_appended.Count > 0) sb.AppendLine($"追加: {_appended.Count} 个视频");
        sb.AppendLine();
        sb.AppendLine("操作流水线:");
        for (int i = 0; i < _actions.Count; i++)
            sb.AppendLine($"  {i + 1}. {_actions[i].Name} — {_actions[i].Summary}");
        sb.AppendLine();
        sb.AppendLine($"输出: {Path.GetFileName(_outputFile)}");
        return sb.ToString();
    }

    async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!FfmpegService.IsFfmpegReady) { ShowToast("FFmpeg 未就绪", "请先下载 FFmpeg", InfoBarSeverity.Error); return; }

        var args = BuildCommand();
        if (string.IsNullOrEmpty(args)) { ShowToast("参数错误", "请检查设置", InfoBarSeverity.Warning); return; }

        var d = new ContentDialog
        {
            Title = "确认操作", Content = BuildSummary(),
            PrimaryButtonText = "开始", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme, XamlRoot = Content.XamlRoot
        };
        if (await d.ShowAsync() != ContentDialogResult.Primary) return;

        StartBtn.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        TaskProgress.IsIndeterminate = true;
        ProgressText.Text = "正在处理...";
        _cts = new CancellationTokenSource();

        try
        {
            var dir = Path.GetDirectoryName(_outputFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var progress = new Progress<(int, string)>(p =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (p.Item1 >= 0) { TaskProgress.IsIndeterminate = false; TaskProgress.Value = p.Item1; }
                    ProgressText.Text = p.Item2;
                }));

            await FfmpegService.RunFfmpegAsync(args, progress, _cts.Token);
            ShowToast("处理完成", Path.GetFileName(_outputFile) ?? "", InfoBarSeverity.Success);
            ProgressText.Text = "处理完成！";
            try { await Windows.System.Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(_outputFile)!); } catch { }
        }
        catch (OperationCanceledException) { ShowToast("已取消", "", InfoBarSeverity.Informational); ProgressText.Text = "已取消"; }
        catch (Exception ex) { ShowToast("处理失败", ex.Message, InfoBarSeverity.Error); ProgressText.Text = $"失败: {ex.Message}"; }
        finally { StartBtn.IsEnabled = true; TaskProgress.IsIndeterminate = false; }
    }

    void CancelBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ── Utils ──

    DispatcherTimer? _toastBarTimer;

    void ShowToast(string title, string msg, InfoBarSeverity sev)
    {
        ToastBar.Title = title; ToastBar.Message = msg; ToastBar.Severity = sev; ToastBar.IsOpen = true;

        _toastBarTimer?.Stop();
        _toastBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastBarTimer.Tick += (s, e) =>
        {
            ToastBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toastBarTimer.Start();
    }

    void CloseButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();
}
