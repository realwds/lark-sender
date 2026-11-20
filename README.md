# lark-sender

[![Build](https://github.com/realwds/lark-sender/actions/workflows/build.yml/badge.svg)](https://github.com/realwds/lark-sender/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)](#)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4.svg)](#)

一个轻量的 Windows 桌面小工具，用于向飞书 / Lark 群组机器人快速发送消息。

双击打开 → 选群 → 输入文字 → 发送。支持多群管理、定时发送、@所有人。

> 单文件 exe，约 130 KB，零第三方依赖，只需系统自带的 .NET Framework 4.x。

## 为什么做这个

飞书群机器人的 webhook 很好用，但日常发条消息要开终端敲 `curl`，或者打开飞书手动 @人，很别扭。
这个工具把「发群消息」变成双击即用的小窗，顺带把定时发送也解决了（走 Windows 计划任务，不常驻内存）。

## 功能

- 🔍 **多群管理** — 下拉选群，每个群独立配置 webhook / 签名密钥
- 📢 **@所有人** — 一键带 `<at user_id="all">`
- ⏰ **定时发送** — 一次性或每天，交给 Windows 计划任务，程序无需常驻
- 🪶 **零依赖** — 单文件 exe，不装 .NET SDK / 不装运行时
- 🚫 **无黑框** — 原生 WinForms，不是脚本套壳
- 📝 **发送日志** — 每次发送记录到 `%APPDATA%\LarkSender\lark-sender.log`
- 🔐 **凭证本地化** — 只读自己的 `config.json`，不碰任何全局凭据文件

## 使用

### 1. 拿到群机器人 webhook

飞书群 → 右上角 `...` → 设置 → 群机器人 → 添加机器人 → **自定义机器人** → 复制 webhook 地址。

形如：

```
https://open.feishu.cn/open-apis/bot/v2/hook/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

### 2. 配置群

点「管理群…」→「新增」→ 填群名和 webhook → 保存。

如果机器人开启了「签名校验」，把密钥填到「签名密钥」字段。
如果机器人开了「自定义关键词」，消息正文必须包含该关键词，否则飞书会返回 `19024`。

### 3. 发送

输入文字 → 点「立即发送」（或 `Ctrl+Enter`）。

定时发送：输入文字 → 点「定时发送…」→ 选时间 → 「定时任务」页签可查看和删除。

## 命令行

```bat
LarkSender.exe /send "内容" /group "群名" /atall
LarkSender.exe /job <JobId>          :: 由计划任务内部调用
```

## 从源码构建

只需 Windows 自带的 `csc.exe`，**不需要 Visual Studio 或 .NET SDK**：

```bat
git clone https://github.com/realwds/lark-sender.git
cd lark-sender
build.cmd
```

产物：`LarkSender.exe` + `icon.ico`（首次构建会自动生成图标）。

清理：`build.cmd clean`

### 首次使用

把 `config.example.json` 复制为 `config.json`，改成你自己的群和 webhook。
或者直接运行 `LarkSender.exe`（会自动生成空白 `config.json`），再点「管理群…」新增。

## 工作原理

```
Windows 计划任务 (LarkSender_<JobId>)
  └─ LarkSender.exe /job <JobId>
       └─ 读取 config.json → 发送消息 → 写日志
            └─ 一次性任务：发完自动删除计划任务和配置记录
```

配置和日志位置：

| 内容 | 路径 |
|---|---|
| 配置 | exe 同目录的 `config.json`（便携，可随 exe 一起搬走） |
| 日志 | `%APPDATA%\LarkSender\lark-sender.log` |

## 已知限制

- **只能 @所有人，无法 @单个成员** —— 飞书群自定义机器人拿不到成员的 `open_id`，这是平台限制。要 @ 具体的人必须改用自建应用机器人。
- **定时任务默认仅在用户登录时执行** —— Windows 计划任务的 `Interactive only` 模式。要做到未登录也跑，需要存密码或用 S4U。
- **定时精度到分钟** —— `schtasks` 本身只支持到分钟。
- **仅 Windows** —— 依赖 WinForms。

## 踩过的坑

这些是在真实环境里踩到的，记录下来给后来人省点时间。

### 1. `schtasks /TR` 指向带参数的 exe 需要双层引号

```bat
REM ❌ 报「无效参数/选项 - '/job'」
schtasks /create /TN x /TR "C:\path\app.exe" /job abc /F

REM ✅ 整个动作包一层，内层 exe 也包一层
schtasks /create /TN x /TR "\"C:\path\app.exe\" /job abc" /F
```

`/TR` 只接受一个参数，并且路径含空格时内层引号是必须的。

### 2. 不要让程序删除正在被执行的 `.cmd`

早期版本用 `.cmd` 包装计划任务，程序发完消息后删掉那个 `.cmd`，导致
`cmd.exe` 报 `The batch file cannot be found` 并返回退出码 1，**计划任务被记为失败**
（`Last Result: 267011`）。

→ 正确做法：计划任务**直接调用 exe**，不经过 `.cmd` 包装。

### 3. `schtasks /SD` 日期格式必须是 `yyyy/MM/dd`

写成 `MM/dd/yyyy` 会报 `ERROR: Invalid Start Date`。

### 4. `ProcessStartInfo.ArgumentList` 在 .NET 4.0 不可用

用 `csc.exe` 编译到 .NET 4.0 profile 时没有这个属性（.NET 4.6+ 才有），
只能手工拼引号。

### 5. WinForms `Anchor` 在控件加入容器前设置会算错偏移

```csharp
// ❌ 控件还没 Add 到父容器就设 Anchor，会按父容器当时的尺寸算偏移，
//    结果控件被推到窗口外，表现为「按钮看不见」
var box = new TextBox { Anchor = AnchorStyles.Bottom, Location = new Point(14, 52) };

// ✅ 用 TableLayoutPanel + Dock 让布局引擎按实际尺寸算
var root = new TableLayoutPanel { Dock = DockStyle.Fill };
```

### 6. ICO 格式：PNG 条目和 .NET `Icon` 类不兼容

`System.Drawing.Icon` 读不出用 PNG 压缩的 128/256 尺寸条目（保存时正常，
读取时抛"请求的范围扩展超过了数组的结尾"）。全部用 DIB 格式最稳。

### 7. `AutoScaleMode.Font` 在部分环境会导致布局错位

改用 `AutoScaleMode.Dpi`。

## 许可证

[Apache License 2.0](LICENSE)