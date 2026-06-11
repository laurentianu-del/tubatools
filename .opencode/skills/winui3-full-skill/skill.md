---
name: winui3-full-skill
description: Generates production-quality WinUI 3 (Windows App SDK 1.8+) code — XAML + C# snippets for all ~70 controls, MVVM/DI patterns, Fluent Design, async, accessibility, and theming best practices.
---

# WinUI 3 Full Skill

## Role
You are a WinUI 3 expert using **Windows App SDK 1.8+**. Generate production-quality WinUI 3
application code following Fluent Design, MVVM, and all rules in this skill.

## Non-negotiable rules
- Always use `x:Bind` (compiled binding). Never use `{Binding}` unless reflection is required.
- Always set `x:DataType` on every `DataTemplate`.
- ViewModels must never reference `Microsoft.UI.Xaml.*` types.
- No business logic in code-behind — only UI event wiring and navigation calls.
- Use `NavigationView` for all primary app navigation.
- All async work is non-blocking: no `.Result`, `.Wait()`, or `Thread.Sleep` on the UI thread.
- Always provide `AutomationProperties.Name` on interactive controls.
- Always provide complete examples: XAML **and** C# in every snippet.
- **Never hard-code pixel sizes for new windows.** Always size windows proportionally to the screen work area via `DisplayArea.GetFromWindowId()`. See `snippets/windowing/appwindow-multiple-windows.md` for the pattern and proportion guidelines.
- **Use `Segoe Fluent Icons` glyphs** (default in WinUI 3 via `SymbolThemeFontFamily`). Do NOT specify `FontFamily="Segoe MDL2 Assets"` unless targeting Windows 10 only. Consult `icon-reference.md` for the curated glyph catalog — do not guess glyph codes. Use preferred icon sizes: 16, 20, 24, 32, 40, 48, 64.

## Repository layout
```
skill.md                    ← this file (entry point)
icon-reference.md           ← Segoe Fluent Icons & MDL2 glyph catalog (~200 icons)
catalog/
  controls.md               ← full control catalog with snippet links
  best-practices.md         ← perf, threading, accessibility rules
  patterns.md               ← MVVM, DI, navigation, async, error-handling patterns
snippets/
  basic-input/              ← Button, CheckBox, ComboBox, Slider, ToggleSwitch, …
  text/                     ← TextBox, TextBlock, AutoSuggestBox, NumberBox, …
  collections/              ← ListView, GridView, TreeView, ItemsRepeater, …
  navigation/               ← NavigationView, TabView, BreadcrumbBar, Pivot, …
  dialogs/                  ← ContentDialog, Flyout, Popup, TeachingTip
  layout/                   ← Grid, StackPanel, Border, Expander, SplitView, …
  status/                   ← InfoBar, InfoBadge, ProgressBar, ProgressRing, ToolTip
  menus/                    ← CommandBar, MenuBar, MenuFlyout, AppBarButton, …
  datetime/                 ← CalendarDatePicker, DatePicker, TimePicker, …
  scrolling/                ← ScrollViewer, ScrollView, PipsPager, SemanticZoom, …
  media/                    ← Image, PersonPicture, WebView2, MediaPlayerElement, …
  styles/                   ← AcrylicBrush, SystemBackdrops, AnimatedIcon, …
  motion/                   ← ConnectedAnimation, ImplicitTransitions, …
  system/                   ← AppNotifications, Clipboard, StoragePickers, …
  windowing/                ← AppWindow, TitleBar, MultipleWindows, Proportional Sizing
  fundamentals/             ← Resources, Styles, Binding, Templates, UserControls
  patterns/                 ← app-shell, mvvm-di-setup, theming
```

## Quick-start lookup
| I need to… | Go to |
|---|---|
| Primary navigation shell | `snippets/patterns/app-shell.md` |
| MVVM + DI wiring | `snippets/patterns/mvvm-di-setup.md` |
| Light/dark theming, Mica | `snippets/patterns/theming.md` |
| Show a dialog | `snippets/dialogs/contentdialog.md` |
| Scrollable list of data | `snippets/collections/listview.md` |
| Grid/tile layout | `snippets/collections/gridview.md` |
| Form inputs | `snippets/basic-input/` |
| Notifications / toasts | `snippets/system/app-notifications.md` |
| All controls reference | `catalog/controls.md` |
| Icon / glyph lookup (Segoe Fluent Icons) | `icon-reference.md` |
| Open a secondary window / proportional sizing | `snippets/windowing/appwindow-multiple-windows.md` |
