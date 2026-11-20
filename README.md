# lark-sender

[![Build](https://github.com/realwds/lark-sender/actions/workflows/build.yml/badge.svg)](https://github.com/realwds/lark-sender/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)](#)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4.svg)](#)

一个轻量的 Windows 桌面小工具，用于向飞书 / Lark 群组机器人快速发送消息。

双击打开 → 选群 → 输入文字 → 发送。支持多群管理、定时发送、富文本、发送历史。

> 单文件 exe，约 160 KB，零第三方依赖，只需系统自带的 .NET Framework 4.x。

## 为什么做这个

飞书群机器人的 webhook 很好用，但日常发条消息要开终端敲 `curl`，或者打开飞书手动 @人，很别扭。
这个工具把「发群消息」变成双击即用的小窗，顺带把定时发送也解决了（走 Windows 计划任务，不常驻内存）。

## 功能

- 🔍 **多群管理** — 下拉选群，每群独立配置 webhook / 签名密钥，可单独测试连通性
- 📢 **@所有人** — 一键带 `<at user_id="all">`
- 📝 **富文本消息** — 支持 `post` 格式，用 `# 标题`、`**加粗**`、`- 列表` 简单语法
- ⏰ **定时发送** — 一次性 / 每天 / 仅工作日，交给 Windows 计划任务，程序无需常驻
- 🔁 **自动重试** — 网络抖动、频率限制时自动重试，指数退避
- 📋 **发送历史** — 结构化记录，可查看、双击填回输入框重发
- 💬 **常用语模板** — 高频消息存成模板，下拉一键填入
- 👁 **发送预览** — 发送前看清实际发出的 JSON
- 🪶 **零依赖** — 单文件 exe，不装 .NET SDK / 不装运行时
- 🚫 **无控制台** — 原生 WinForms，不是脚本套壳
- 🔐 **凭证本地化** — 只读自己的 `config.json`，不碰任何全局凭据文件

## 使用

### 1. 拿到群机器人 webhook

飞书群 → 右上角 `...` → 设置 → 群机器人 → 添加机器人 → **自定义机器人** → 复制 webhook 地址。

形如：

```
https://open.feishu.cn/open-apis/bot/v2/hook/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

海外版 Lark 是 `https://open.larksuite.com/...`，同样支持。

### 2. 配置群

点「管理群…」→「新增」→ 填群名和 webhook → 保存。

- 机器人开了「签名校验」→ 把密钥填到「签名密钥」
- 机器人开了「自定义关键词」→ 消息正文必须含该词，否则飞书返回 `19024`
- 配好后可以点「测试连通」发一条测试消息验证

### 3. 发送

输入文字 → 点「立即发送」（或 `Ctrl+Enter`）。

- 勾「富文本格式」可用 `# 标题` / `**加粗**` / `- 列表`
- 勾「艾特所有人」消息会带 @all
- 点「预览」可看实际发出的 JSON

### 4. 定时发送

输入文字 → 点「定时发送…」→ 选类型（一次性 / 每天 / 工作日）和时间 → 创建。

「定时任务」页签可查看和删除，会显示系统任务的下次运行时间；任务被外部删除时会标红提示。

## 命令行

```bat
LarkSender.exe /send "内容" /group "群名" /atall
LarkSender.exe /send "内容" /group "群名" /post
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

```bat
build.cmd clean    :: 清理产物
test.cmd           :: 跑单元测试（76 个断言）
build.cmd test     :: 编译 + 测试
```

### 首次使用

把 `config.example.json` 复制为 `config.json` 并改成你自己的群和 webhook。
也可以直接运行 `LarkSender.exe`（会自动生成空白 `config.json`），再点「管理群…」新增。

## 项目结构

```
src/
├── Program.cs              入口：界面 / 命令行发送 / 定时任务执行
├── Models/
│   └── Config.cs           数据模型（群、定时计划、配置、发送结果）
├── Core/
│   ├── Paths.cs            路径常量
│   ├── Json.cs             轻量 JSON 读写（不依赖 NuGet）
│   ├── ConfigStore.cs      配置读写 + 版本迁移
│   ├── Notifier.cs         发送：重试 / 加签 / text & post
│   ├── Logger.cs           日志（轮转）+ 结构化历史
│   └── ScheduleManager.cs  Windows 计划任务管理
├── Forms/
│   ├── MainForm.cs         主窗口（发消息 / 定时任务 / 历史）
│   ├── GroupManagerForm.cs 群列表管理
│   ├── GroupEditForm.cs    新增/编辑群
│   ├── ScheduleForm.cs     定时发送对话框
│   └── AppIcon.cs          窗体图标
└── tests/
    └── Program.cs          单元测试（自带断言，无框架依赖）
```

## 数据位置

| 内容 | 路径 |
|---|---|
| 配置 | exe 同目录的 `config.json`（便携，可随 exe 一起搬走） |
| 日志 | `%APPDATA%\LarkSender\lark-sender.log` |
| 发送历史 | `%APPDATA%\LarkSender\history.jsonl` |

## 工作原理

```
Windows 计划任务 (LarkSender_<JobId>)
  └─ LarkSender.exe /job <JobId>
       └─ 读取 config.json → 发送消息 → 写日志与历史
            └─ 一次性任务：发完自动删除计划任务和配置记录
```

## 已知限制

- **只能 @所有人，无法 @单个成员** —— 飞书群自定义机器人拿不到成员的 `open_id`，这是平台限制。要 @ 具体的人必须改用自建应用机器人。
- **定时任务默认仅在用户登录时执行** —— Windows 计划任务的 `Interactive only` 模式。要做到未登录也跑，需要存密码或用 S4U。
- **定时精度到分钟** —— `schtasks` 本身只支持到分钟。
- **仅 Windows** —— 依赖 WinForms。

## 开发踩坑记录

这些都是在真实环境里踩到的，记录下来给后来人省点时间。

### 1. 群机器人 webhook 的 post 格式多一层 `post`

开放平台 API 和群机器人 webhook 的 post 格式**不一样**：

```jsonc
// ✅ webhook 正确格式
{ "msg_type": "post", "content": { "post": { "zh_cn": { "title": "", "content": [[...]] } } } }

// ❌ 会报 code 10208 "'post' field required"
{ "msg_type": "post", "content": { "zh_cn": { "title": "", "content": [[...]] } } }
```

### 2. `schtasks /TR` 指向带参数的 exe 需要双层引号

```bat
REM ❌ 报「无效参数/选项 - '/job'」
schtasks /create /TN x /TR "C:\path\app.exe" /job abc /F

REM ✅ 整个动作包一层，内层 exe 也包一层
schtasks /create /TN x /TR "\"C:\path\app.exe\" /job abc" /F
```

`/TR` 只接受一个参数，并且路径含空格时内层引号是必须的。

### 3. 不要让程序删除正在被执行的 `.cmd`

早期版本用 `.cmd` 包装计划任务，程序发完消息后删掉那个 `.cmd`，导致
`cmd.exe` 报 `The batch file cannot be found` 并返回退出码 1，**计划任务被记为失败**
（`Last Result: 267011`）。

→ 正确做法：计划任务**直接调用 exe**，不经过 `.cmd` 包装。

### 4. `schtasks /SD` 日期格式必须是 `yyyy/MM/dd`

写成 `MM/dd/yyyy` 会报 `ERROR: Invalid Start Date`。

### 5. `ProcessStartInfo.ArgumentList` 在 .NET 4.0 不可用

用 `csc.exe` 编译到 .NET 4.0 profile 时没有这个属性（.NET 4.6+ 才有），
只能手工拼引号。

### 6. WinForms `Anchor` 在控件加入容器前设置会算错偏移

```csharp
// ❌ 控件还没 Add 到父容器就设 Anchor，会按父容器当时的尺寸算偏移，
//    结果控件被推到窗口外，表现为「按钮看不见」
var box = new TextBox { Anchor = AnchorStyles.Bottom, Location = new Point(14, 52) };

// ✅ 用 TableLayoutPanel + Dock 让布局引擎按实际尺寸算
var root = new TableLayoutPanel { Dock = DockStyle.Fill };
```

### 7. ICO 格式：PNG 条目和 .NET `Icon` 类不兼容

`System.Drawing.Icon` 读不出用 PNG 压缩的 128/256 尺寸条目（保存时正常，
读取时抛「请求的范围扩展超过了数组的结尾」）。全部用 DIB 格式最稳。

### 8. `AutoScaleMode.Font` 在部分环境会导致布局错位

改用 `AutoScaleMode.Dpi`。

### 9. `.cmd` 文件里的中文注释可能引发解析错误

`cmd.exe` 按本地代码页（中文系统为 GBK）解析批处理文件。UTF-8 保存的中文注释
在特定位置会导致后续行被误解析。构建脚本的注释统一用英文最稳妥。

### 10. schtasks 时间精度只到分钟

计划 `10:02:56` 实际 `10:02:00` 触发。定时按分钟选即可。

## 贡献

欢迎提 Issue 和 PR。提交前请确保：

```bat
test.cmd        :: 单元测试全部通过
build.cmd       :: 编译无警告无错误
```

代码风格：4 空格缩进，注释用中文（与现有代码一致）。

## 许可证

[Apache License 2.0](LICENSE)