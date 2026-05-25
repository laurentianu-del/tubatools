# Microsoft Store Listing Content

## Chinese (zh-CN)

### 应用名称
图吧工具箱 WinUI3版

### 简短描述
PC硬件检测与系统维护工具集，集成本地硬件信息查询、蓝屏分析、证书管理、端口监控、垃圾清理、Hosts编辑等实用功能

### 描述
图吧工具箱 WinUI3版是一款面向PC硬件爱好者和系统维护人员的专业工具集应用。采用全新WinUI 3界面框架，提供流畅现代的操作体验。

**特色功能：**

🔧 **硬件信息查询** — 一键获取处理器、主板、内存、显卡、硬盘、声卡、网卡等全面硬件信息，基于WMI实时读取系统数据。

📦 **第三方工具目录** — 自动扫描本地Tools目录中分类整理的硬件检测、显卡工具、磁盘工具等专业工具，支持快速搜索和一键启动。

⚡ **内置实用工具：**
- **功耗监控** — 实时显示CPU/GPU负载和电池放电功率
- **证书屏蔽** — 管理和屏蔽不信任的数字证书
- **端口查看** — 查看TCP/UDP端口占用和进程信息
- **Hosts编辑** — 可视化编辑系统Hosts文件
- **键盘测试** — 挀键检测键盘按键状态
- **垃圾清理** — 扫描清理系统临时文件、浏览器缓存、缩略图缓存等垃圾文件

🔍 **全局搜索** — 快速查找工具箱中的任意工具

⭐ **收藏管理** — 标记常用工具，一键访问

🖥️ **WinUI 3 现代界面** — 原生Windows 11风格，支持暗色模式，毛玻璃效果

本应用为纯离线应用，不收集任何用户数据，所有操作均在本地完成。

### 功能列表
- WMI硬件信息实时查询（CPU、主板、内存、显卡、磁盘、声卡、网卡）
- 第三方硬件工具目录管理与快速启动
- 全局工具搜索
- 工具收藏功能
- CPU/GPU功耗与电池状态实时监控
- 数字证书批量管理（屏蔽/解除屏蔽）
- TCP/UDP端口占用查看与进程管理
- 系统Hosts文件可视化编辑与DNS刷新
- 键盘按键测试
- 系统垃圾文件扫描与清理（临时文件、浏览器缓存、更新缓存等）
- 蓝屏转储分析
- WinUI 3 现代化界面设计
- 完全离线运行，零数据收集

### 关键字
图吧工具箱;硬件检测;系统维护;CPU温度;显卡信息;磁盘检测;端口查看;垃圾清理;Hosts编辑;证书管理

### 版权信息
© 2026 罗澜嘎嘎

---

## English (en-US)

### App Name
Tuba Toolbox WinUI3

### Short Description
PC hardware diagnostics and system maintenance toolkit with built-in info query, blue screen analysis, certificate management, port monitoring, junk cleaning, and more

### Description
Tuba Toolbox WinUI3 Edition is a professional hardware diagnostics and system maintenance toolkit designed for PC enthusiasts and system administrators. Built with the modern WinUI 3 framework for a fluent, contemporary experience.

**Key Features:**

🔧 **Hardware Info Query** — Instantly retrieve comprehensive hardware details including CPU, motherboard, RAM, GPU, disk, audio, and network adapter information via WMI real-time queries.

📦 **Third-Party Tool Catalog** — Automatically scan and organize local hardware diagnostic tools from the Tools directory, with quick search and one-click launch.

⚡ **Built-in Utilities:**
- **Power Monitor** — Real-time CPU/GPU load and battery discharge rate display
- **Certificate Blocker** — Manage and block untrusted digital certificates
- **Port Viewer** — View TCP/UDP port usage and associated process info
- **Hosts Editor** — Visual editing of system Hosts file with DNS flush
- **Keyboard Test** — Key press detection for keyboard diagnostics
- **Junk Cleaner** — Scan and clean temp files, browser caches, thumbnail caches, and more

🔍 **Global Search** — Quickly find any tool in the toolbox

⭐ **Favorites** — Pin frequently used tools for one-click access

🖥️ **WinUI 3 Modern UI** — Native Windows 11 styling with dark mode support and acrylic effects

This is a fully offline application that collects zero user data. All operations are performed locally.

### Product Features
- Real-time WMI hardware info queries (CPU, motherboard, RAM, GPU, disk, audio, network)
- Third-party hardware tool catalog management and quick launch
- Global tool search
- Tool favorites system
- CPU/GPU power and battery status real-time monitoring
- Digital certificate batch management (block/unblock)
- TCP/UDP port usage viewer with process management
- System Hosts file visual editor with DNS flush
- Keyboard key press testing
- System junk file scanning and cleaning (temp files, browser caches, update caches, etc.)
- Blue screen dump analysis
- Modern WinUI 3 interface design
- Fully offline, zero data collection

### Keywords
hardware diagnostics;system maintenance;CPU monitor;GPU info;disk test;port viewer;junk cleaner;hosts editor;certificate manager;PC toolbox

### Copyright
© 2026 罗澜嘎嘎

---

## Privacy Policy URL

Host a page with the following content:

**图吧工具箱 / Tuba Toolbox — Privacy Policy**

图吧工具箱 is a fully offline application. We do not collect, store, or transmit any user personal data. All data is stored only on your local device. No cookies, no tracking, no third-party services.

Contact: xiaohuzi12323@163.com

---

## runFullTrust Justification

This application requires the `runFullTrust` restricted capability for the following reasons:

1. **Process Launch** — The app launches third-party diagnostic executables (.exe, .bat, .cmd) stored in the Tools directory, which requires full-trust process creation.

2. **WMI Hardware Queries** — Hardware information is retrieved via WMI (System.Management) which requires full-trust permissions.

3. **System File Editing** — The built-in Hosts editor modifies `C:\Windows\System32\drivers\etc\hosts`, a protected system file requiring elevated privileges.

4. **Certificate Store Management** — The certificate blocker feature adds/removes entries from the Windows Disallowed certificate store (LocalMachine), requiring administrator-level access.

5. **System Maintenance** — The junk cleaner deletes files from system directories (Windows\Temp, Windows\SoftwareDistribution, etc.) that require elevated privileges.

6. **Port Monitoring** — Port scanning uses P/Invoke to iphlpapi.dll for TCP/UDP table enumeration.

These features are core to the application's purpose as a PC hardware diagnostics and system maintenance toolkit and cannot be implemented without full-trust access.