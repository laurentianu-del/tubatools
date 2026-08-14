<#
.SYNOPSIS
    生成 MiSans 字体子集（构建时由 GenerateBundledFontSubset MSBuild target 调用）。
.DESCRIPTION
    子集 = 应用硬编码字符（扫描 .cs/.xaml）+ GB2312 一级常用汉字(3755) + ASCII/常用符号。
    首帧渲染只加载小体积子集字体；动态文本（工具名/硬件名/AI 对话）缺字时
    由 App.xaml 的 FontFamily 回退链自动使用全量 MiSans-Medium.otf。
    未安装 Python/fonttools 时静默跳过（回退使用全量字体）。
.PARAMETER ProjectDir
    主项目目录（默认脚本所在目录下的 TubaWinUi3.WinUI3）。
.PARAMETER SourceFont
    源字体路径（默认 <ProjectDir>\Fonts\MiSans-Medium.otf）。
.PARAMETER OutputFont
    输出子集字体路径（默认 <ProjectDir>\Fonts\MiSans-Subset.otf）。
#>
param(
    [string]$ProjectDir = "$PSScriptRoot\TubaWinUi3.WinUI3",
    [string]$SourceFont = "",
    [string]$OutputFont = ""
)

$ErrorActionPreference = "Stop"
if (-not $SourceFont) { $SourceFont = Join-Path $ProjectDir "Fonts\MiSans-Medium.otf" }
if (-not $OutputFont) { $OutputFont = Join-Path $ProjectDir "Fonts\MiSans-Subset.otf" }

if (-not (Test-Path $SourceFont)) {
    Write-Warning "[font-subset] 源字体不存在: $SourceFont，跳过"
    exit 0
}

# 1. 检查 Python + fonttools
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    Write-Warning "[font-subset] 未找到 python，跳过子集化（使用全量字体）"
    exit 0
}
& python -c "import fontTools" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "[font-subset] 未安装 fonttools（pip install fonttools），跳过子集化（使用全量字体）"
    exit 0
}

# 2. 用 Python 生成字符集：应用硬编码字符 + GB2312 一级 3755 常用字 + ASCII/符号
$tempDir = Join-Path $env:TEMP "tubafont_$PID"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$charsFile = Join-Path $tempDir "chars.txt"
$pyScript = Join-Path $tempDir "gen_chars.py"

@'
# -*- coding: utf-8 -*-
import os, sys

proj, out = sys.argv[1], sys.argv[2]
chars = set(chr(c) for c in range(0x20, 0x7F))
chars.update('，。、；：？！…—·《》〈〉【】「」『』（）“”‘’￥×÷°℃％§')

# GB2312 一级汉字区 B0A1-D7F9（3755 常用字，覆盖 99%+ 中文文本）
for row in range(0xB0, 0xD8):
    for col in range(0xA1, 0xFF):
        if row == 0xD7 and col > 0xF9:
            break
        try:
            chars.add(bytes([row, col]).decode('gb2312'))
        except Exception:
            pass

# 应用硬编码字符（.cs/.xaml）
for root, dirs, names in os.walk(proj):
    dirs[:] = [d for d in dirs if d not in ('bin', 'obj', 'publish', 'IconCache', 'node_modules')]
    for n in names:
        if not n.endswith(('.cs', '.xaml')):
            continue
        p = os.path.join(root, n)
        try:
            with open(p, encoding='utf-8') as f:
                text = f.read()
            chars.update(ch for ch in text if ord(ch) > 127)
        except Exception:
            pass

with open(out, 'w', encoding='utf-8') as f:
    f.write(''.join(sorted(chars)))
print('[font-subset] 字符集共 %d 个字符' % len(chars))
'@ | Set-Content -Path $pyScript -Encoding UTF8

& python $pyScript $ProjectDir $charsFile
if ($LASTEXITCODE -ne 0) {
    Write-Warning "[font-subset] 字符集生成失败，跳过子集化"
    exit 0
}

# 3. 调用 pyftsubset 生成子集字体
# --name-IDs=*：保留完整 name 表（含 nameID 16 "MiSans" typographic family），
# 否则 App.xaml 的 FontFamily "#MiSans" 无法匹配子集字体导致回退链失效。
& python -m fontTools.subset $SourceFont `
    --text-file=$charsFile `
    --output-file=$OutputFont `
    --layout-features='*' `
    --glyph-names `
    --symbol-cmap `
    --legacy-cmap `
    --notdef-glyph `
    --notdef-outline `
    --recommended-glyphs `
    --name-IDs=*
if ($LASTEXITCODE -ne 0) {
    Write-Warning "[font-subset] pyftsubset 失败，跳过子集化"
    exit 0
}

$srcSize = (Get-Item $SourceFont).Length / 1MB
$dstSize = (Get-Item $OutputFont).Length / 1MB
Write-Host "[font-subset] 完成: $([math]::Round($srcSize, 2))MB -> $([math]::Round($dstSize, 2))MB ($OutputFont)"

# 清理
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
