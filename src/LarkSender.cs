// 飞书群消息工具 - C# WinForms 版
// 编译：csc.exe /target:winexe /out:LarkSender.exe LarkSender.cs
//      /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
//
// 用 csc 编译成 /target:winexe 后：
//   - 没有控制台黑框（不需要 ShowWindow 隐藏技巧）
//   - 不受 PS 5.1 编码问题影响
//   - 奇安信不拦（就是个普通桌面程序）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LarkSender
{
    // ==================== 数据模型 ====================
    public class GroupCfg
    {
        public string name { get; set; }
        public string webhook { get; set; }
        public string secret { get; set; }
        public bool atAll { get; set; }
        public string note { get; set; }

        public GroupCfg() { name = ""; webhook = ""; secret = ""; atAll = false; note = ""; }
        public GroupCfg(string n, string w, string s, bool a, string nt)
        { name = n; webhook = w; secret = s; atAll = a; note = nt; }
    }

    public class ScheduleCfg
    {
        public string id { get; set; }
        public string groupName { get; set; }
        public string message { get; set; }
        public bool atAll { get; set; }
        public string kind { get; set; }      // once | daily
        public string runAt { get; set; }     // yyyy-MM-dd HH:mm
        public string createdAt { get; set; }
    }

    public class Config
    {
        public int version { get; set; }
        public List<GroupCfg> groups { get; set; }
        public List<ScheduleCfg> schedules { get; set; }
        public Config() { version = 1; groups = new List<GroupCfg>(); schedules = new List<ScheduleCfg>(); }
    }

    // ==================== 路径与常量 ====================
    public static class Paths
    {
        // 配置跟随 exe 目录，方便绿色版/便携使用
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ConfigFile = Path.Combine(BaseDir, "config.json");

        // 日志放 %APPDATA%\LarkSender\，避免污染程序目录（目录可能只读）
        public static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LarkSender");
        public static readonly string LogFile = Path.Combine(AppDataDir, "lark-sender.log");

        public const string TaskPrefix = "LarkSender_";
    }

    // ==================== 配置读写 ====================
    public static class CfgStore
    {
        public static Config Load()
        {
            try
            {
                // 首次运行：自动生成空白配置，避免用户不知道该建什么文件
                if (!File.Exists(Paths.ConfigFile))
                {
                    var fresh = new Config();
                    try { Save(fresh); } catch { }
                    return fresh;
                }
                var json = File.ReadAllText(Paths.ConfigFile, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return new Config();
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                var cfg = ser.Deserialize<Config>(json);
                if (cfg == null) return new Config();
                if (cfg.groups == null) cfg.groups = new List<GroupCfg>();
                if (cfg.schedules == null) cfg.schedules = new List<ScheduleCfg>();
                return cfg;
            }
            catch (Exception ex)
            {
                throw new Exception("配置文件解析失败：" + Paths.ConfigFile + "\n" + ex.Message);
            }
        }

        public static void Save(Config cfg)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            var json = ser.Serialize(cfg);
            var enc = new UTF8Encoding(false);   // 无 BOM
            File.WriteAllText(Paths.ConfigFile, json, enc);
        }

        public static GroupCfg FindGroup(Config cfg, string name)
        {
            return cfg.groups.FirstOrDefault(g => g.name == name);
        }
    }

    // ==================== 发送 ====================
    public class SendResult
    {
        public bool Ok;
        public string Response;
        public string Error;
    }

    public static class Sender
    {
        public static string ResolveWebhook(GroupCfg g)
        {
            // 只读本应用自己的 config.json，不依赖任何外部凭据文件
            if (g != null && !string.IsNullOrWhiteSpace(g.webhook)) return g.webhook.Trim();
            return null;
        }

        public static string ResolveSecret(GroupCfg g)
        {
            if (g != null && !string.IsNullOrWhiteSpace(g.secret)) return g.secret.Trim();
            return null;
        }

        public static string Mask(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "(未配置)";
            var m = System.Text.RegularExpressions.Regex.Match(url, @"/hook/([A-Za-z0-9\-_]+)");
            if (m.Success)
            {
                var tok = m.Groups[1].Value;
                if (tok.Length <= 8) return "…/" + tok;
                return "…/" + tok.Substring(0, 4) + "****" + tok.Substring(tok.Length - 4);
            }
            return "…" + url.Substring(Math.Max(0, url.Length - 12));
        }

        public static string BuildPayload(string text, bool atAll, string secret)
        {
            var body = text;
            if (atAll) body = "<at user_id=\"all\">所有人</at>\n" + text;

            var sb = new StringBuilder();
            sb.Append("{\"msg_type\":\"text\",\"content\":{\"text\":");
            sb.Append(JsonStr(body));
            if (!string.IsNullOrEmpty(secret))
            {
                // 飞书加签：key = timestamp + "\n" + secret，待签名内容为空串
                var ts = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString();
                var keyBytes = Encoding.UTF8.GetBytes(ts + "\n" + secret);
                using (var h = new HMACSHA256(keyBytes))
                {
                    var sig = Convert.ToBase64String(h.ComputeHash(new byte[0]));
                    sb.Append(",\"timestamp\":\"").Append(ts).Append("\",\"sign\":").Append(JsonStr(sig));
                }
            }
            sb.Append("}}");
            return sb.ToString();
        }

        public static string JsonStr(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        public static SendResult Send(GroupCfg g, string text, bool atAll, string source)
        {
            var res = new SendResult();
            var url = ResolveWebhook(g);
            var gname = g != null ? g.name : "(未指定)";
            if (string.IsNullOrWhiteSpace(url))
            {
                res.Error = "群「" + gname + "」没有配置 webhook。请点「管理群…」填写该群的 webhook 地址。";
                Log(gname, text, atAll, false, res.Error, source);
                return res;
            }

            var secret = ResolveSecret(g);
            var json = BuildPayload(text, atAll, secret);

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(20);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = client.PostAsync(url, content).GetAwaiter().GetResult();
                    var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    res.Response = body;

                    object code = null;
                    try
                    {
                        var ser = new JavaScriptSerializer();
                        var obj = ser.DeserializeObject(body) as Dictionary<string, object>;
                        if (obj != null)
                        {
                            object v;
                            if (obj.TryGetValue("code", out v)) code = v;
                            else if (obj.TryGetValue("StatusCode", out v)) code = v;
                        }
                    }
                    catch { }

                    res.Ok = code != null && (code.ToString() == "0");
                    if (!res.Ok) res.Error = body;
                }
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Error = ex.Message;
            }

            Log(gname, text, atAll, res.Ok, res.Ok ? "发送成功" : res.Error, source);
            return res;
        }

        // 日志写入。故意 swallow 异常：日志失败不应影响发送主流程，
        // 但也绝不能让日志把程序弄崩（目录只读、磁盘满都可能）。
        public static void Log(string groupName, string text, bool atAll, bool ok, string detail, string source)
        {
            try
            {
                var dir = Path.GetDirectoryName(Paths.LogFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var first = (text ?? "").Split('\n')[0].Trim();
                if (first.Length > 120) first = first.Substring(0, 120) + "…";
                var line = string.Format("{0}\t{1}\t[{2}]\t群={3}\tat_all={4}\t{5}\t{6}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ok ? "OK  " : "FAIL",
                    source, string.IsNullOrEmpty(groupName) ? "-" : groupName,
                    atAll, first, detail);
                // 显式无 BOM UTF8，否则首次建文件会带 BOM 影响 grep
                File.AppendAllText(Paths.LogFile, line + "\r\n", new UTF8Encoding(false));
            }
            catch { /* 日志失败静默：不影响发送 */ }
        }
    }

    // ==================== 计划任务 ====================
    public static class TaskMgr
    {
        // 直接把计划任务指向本 exe，不再用 .cmd 包装。
        // 旧做法（.cmd 包装）有两个坑：
        //   1) exe 发送成功后删掉正在被 cmd.exe 执行的 .cmd，cmd 报
        //      "The batch file cannot be found" 并返回非 0，计划任务被标记失败；
        //   2) 多一层进程，排查困难。
        // 已实测 schtasks 可以直接执行 exe + 参数，安全软件不拦（只拦 powershell）。
        public static string Register(string jobId, DateTime runAt, string kind)
        {
            var taskName = Paths.TaskPrefix + jobId;
            var exePath = Path.Combine(Paths.BaseDir, "LarkSender.exe");

            // schtasks /TR 只接受一个参数；命令带空格时必须把整个动作再包一层引号，
            // 且内层 exe 自身也要引号（路径可能含空格）。最终形态：
            //   /TR "\"C:\...\LarkSender.exe\" /job xxx"
            // 已实测：只包外层不加内层，schtasks 会把 /job 当成自己的参数。
            var action = "\\\"" + exePath + "\\\" /job " + jobId;

            var sb = new StringBuilder();
            sb.Append("/create");
            sb.Append(" /TN ").Append(QuoteArg(taskName));
            sb.Append(" /TR ").Append(QuoteArg(action));
            if (kind == "daily")
            {
                sb.Append(" /SC DAILY /ST ").Append(runAt.ToString("HH:mm"));
            }
            else
            {
                // /SD 必须 yyyy/MM/dd
                sb.Append(" /SC ONCE /ST ").Append(runAt.ToString("HH:mm"));
                sb.Append(" /SD ").Append(runAt.ToString("yyyy/MM/dd"));
            }
            sb.Append(" /F");

            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", sb.ToString())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEnd();
                var e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new Exception("创建计划任务失败（退出码 " + p.ExitCode + "）：\n" + o + e);
            }
            return taskName;
        }

        // 给单个参数加双引号（已含引号的不重复加）
        private static string QuoteArg(string s)
        {
            return "\"" + s + "\"";
        }

        public static void Delete(string jobId)
        {
            var taskName = Paths.TaskPrefix + jobId;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/delete /TN \"" + taskName + "\" /F")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi)) { p.WaitForExit(); }
            }
            catch { }
        }

        // 任务下次触发时间文本；查不到返回 null
        public static string NextRunText(string jobId)
        {
            var taskName = Paths.TaskPrefix + jobId;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/query /TN \"" + taskName + "\" /FO LIST")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) return null;
                    foreach (var line in o.Split('\n'))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("Next Run Time:"))
                        {
                            var v = t.Substring("Next Run Time:".Length).Trim();
                            if (v.Length > 0 && v != "N/A") return v;
                            return null;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    // ==================== 主窗体 ====================
    public class MainForm : Form
    {
        private Config _cfg;
        private ComboBox _cboGroup;
        private Label _lblHook;
        private TextBox _box;
        private CheckBox _chkAll;
        private Label _lblStatus;
        private Button _btnSend, _btnSchedule;
        private ListView _lvSched;
        private bool _busy;

        public MainForm()
        {
            InitUi();
            LoadConfig();
        }

        private void InitUi()
        {
            Text = "Lark Sender";
            Size = new Size(700, 620);
            MinimumSize = new Size(620, 540);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            // 固定 DPI 缩放，避开 AutoScaleMode.Font 引起的错位
            AutoScaleMode = AutoScaleMode.Dpi;
            ApplyAppIcon(this);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tpSend = new TabPage("发消息") { Padding = new Padding(10) };
            var tpSched = new TabPage("定时任务") { Padding = new Padding(10) };
            tabs.TabPages.AddRange(new[] { tpSend, tpSched });
            Controls.Add(tabs);

            // ================== 发消息页 ==================
            // 用 TableLayoutPanel + Dock 布局。
            // 不用 Anchor + 绝对坐标：控件还没加入父容器就设 Anchor，
            // 会按父容器当时的（很小的）基准尺寸算偏移，导致控件被拉到窗口外——
            // 表现就是“看不到发送按钮”。
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 选群行
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 输入框
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // @all + 状态
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 按钮行

            // ---- 选群行 ----
            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 8)
            };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var lblGroup = new Label
            {
                Text = "发送到",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0)
            };

            _cboGroup = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 6, 0)
            };
            _cboGroup.SelectedIndexChanged += (s, e) => UpdateHookLabel();

            var btnManage = new Button
            {
                Text = "管理群…",
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                Margin = new Padding(0, 2, 8, 0)
            };
            btnManage.Click += (s, e) => ShowGroupManager();

            _lblHook = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 2, 0, 0)
            };

            topRow.Controls.Add(lblGroup, 0, 0);
            topRow.Controls.Add(_cboGroup, 1, 0);
            topRow.Controls.Add(btnManage, 2, 0);
            topRow.Controls.Add(_lblHook, 3, 0);

            // ---- 输入框（Dock.Fill 自动占满剩余高度）----
            _box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DoSend();
                }
            };

            // ---- @all + 状态行 ----
            var midRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 6)
            };
            midRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            midRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _chkAll = new CheckBox
            {
                Text = "艾特所有人（@all）",
                AutoSize = true,
                Checked = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 2, 14, 0)
            };

            _lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "Ctrl+Enter 快速发送"
            };

            midRow.Controls.Add(_chkAll, 0, 0);
            midRow.Controls.Add(_lblStatus, 1, 0);

            // ---- 按钮行（靠右）----
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            // RightToLeft 流方向：先加的靠右
            _btnSend = new Button { Text = "立即发送", Size = new Size(110, 34), Margin = new Padding(8, 0, 0, 0) };
            _btnSend.Click += (s, e) => DoSend();

            _btnSchedule = new Button { Text = "定时发送…", Size = new Size(110, 34), Margin = new Padding(8, 0, 0, 0) };
            _btnSchedule.Click += (s, e) => ShowScheduleDialog();

            var btnClose = new Button { Text = "关闭", Size = new Size(80, 34), Margin = new Padding(8, 0, 0, 0) };
            btnClose.Click += (s, e) => Close();

            btnRow.Controls.Add(_btnSend);
            btnRow.Controls.Add(_btnSchedule);
            btnRow.Controls.Add(btnClose);

            root.Controls.Add(topRow, 0, 0);
            root.Controls.Add(_box, 0, 1);
            root.Controls.Add(midRow, 0, 2);
            root.Controls.Add(btnRow, 0, 3);
            tpSend.Controls.Add(root);

            // ================== 定时任务页 ==================
            var root2 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root2.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lvSched = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _lvSched.Columns.Add("触发时间", 130);
            _lvSched.Columns.Add("群", 110);
            _lvSched.Columns.Add("类型", 60);
            _lvSched.Columns.Add("内容", 250);
            _lvSched.Columns.Add("系统任务下次运行", 175);

            var schedBtnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var btnRefresh = new Button { Text = "刷新", Size = new Size(90, 32), Margin = new Padding(0, 0, 8, 0) };
            btnRefresh.Click += (s, e) => UpdateScheduleList();

            var btnDel = new Button { Text = "删除选中", Size = new Size(100, 32), Margin = new Padding(0, 0, 12, 0) };
            btnDel.Click += (s, e) => DeleteSelectedSchedule();

            var lblTip = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "一次性任务发完自动移除；每天任务长期保留",
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 0)
            };

            schedBtnRow.Controls.Add(btnRefresh);
            schedBtnRow.Controls.Add(btnDel);
            schedBtnRow.Controls.Add(lblTip);

            root2.Controls.Add(_lvSched, 0, 0);
            root2.Controls.Add(schedBtnRow, 0, 1);
            tpSched.Controls.Add(root2);
        }

        // 把 exe 内置图标应用到窗体（标题栏 + 任务栏）
        internal static void ApplyAppIcon(Form f)
        {
            try
            {
                var ico = LoadAppIcon();
                if (ico != null) { f.Icon = ico; }
            }
            catch { }
        }

        internal static Icon LoadAppIcon()
        {
            try
            {
                // 优先取当前 exe 自身的图标资源
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
            catch { }
            try
            {
                var path = Path.Combine(Paths.BaseDir, "icon.ico");
                if (File.Exists(path)) return new Icon(path);
            }
            catch { }
            return null;
        }

        private void LoadConfig()
        {
            try
            {
                _cfg = CfgStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _cfg = new Config();
            }
            UpdateGroupCombo();
            UpdateScheduleList();
        }

        private void UpdateGroupCombo()
        {
            var keep = _cboGroup.SelectedItem as string;
            _cboGroup.Items.Clear();
            foreach (var g in _cfg.groups) _cboGroup.Items.Add(g.name);

            if (_cboGroup.Items.Count == 0)
            {
                _lblHook.Text = "还没有配置群，点「管理群…」添加";
                _lblHook.ForeColor = Color.Firebrick;
                return;
            }
            if (keep != null && _cboGroup.Items.Contains(keep)) _cboGroup.SelectedItem = keep;
            else _cboGroup.SelectedIndex = 0;
        }

        private void UpdateHookLabel()
        {
            var name = _cboGroup.SelectedItem as string;
            if (name == null) return;
            var g = CfgStore.FindGroup(_cfg, name);
            if (g == null) return;
            var url = Sender.ResolveWebhook(g);
            if (!string.IsNullOrWhiteSpace(url))
            {
                _lblHook.Text = "webhook：" + Sender.Mask(url);
                _lblHook.ForeColor = Color.DimGray;
            }
            else
            {
                _lblHook.Text = "⚠ 该群未配置 webhook";
                _lblHook.ForeColor = Color.Firebrick;
            }
            _chkAll.Checked = g.atAll;
        }

        private void UpdateScheduleList()
        {
            _lvSched.BeginUpdate();
            _lvSched.Items.Clear();
            foreach (var s in _cfg.schedules.OrderBy(x => x.runAt))
            {
                var one = (s.message ?? "").Split('\n')[0].Trim();
                if (one.Length > 40) one = one.Substring(0, 40) + "…";
                var it = new ListViewItem(s.runAt ?? "");
                it.SubItems.Add(s.groupName ?? "");
                it.SubItems.Add(s.kind == "daily" ? "每天" : "一次");
                it.SubItems.Add(one);

                // 查系统任务是否还在，不在就标出来（例如被人在任务计划程序里删了）
                var next = TaskMgr.NextRunText(s.id);
                if (s.kind == "once" && next == null)
                {
                    it.SubItems.Add("⚠ 系统任务不存在");
                    it.ForeColor = Color.Firebrick;
                }
                else
                {
                    it.SubItems.Add(next ?? "-");
                }
                it.Tag = s.id;
                _lvSched.Items.Add(it);
            }
            _lvSched.EndUpdate();
        }

        private void DoSend()
        {
            if (_busy) return;
            var name = _cboGroup.SelectedItem as string;
            if (name == null)
            {
                MessageBox.Show("请先在「管理群…」里配置至少一个群。", "没有可用的群", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var text = _box.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "内容为空，没发出去";
                _box.Focus();
                return;
            }

            var g = CfgStore.FindGroup(_cfg, name);
            _busy = true;
            _btnSend.Enabled = false;
            _lblStatus.ForeColor = Color.DimGray;
            _lblStatus.Text = "发送中…";
            Refresh();

            try
            {
                var res = Sender.Send(g, text, _chkAll.Checked, "manual");
                if (res.Ok)
                {
                    _lblStatus.ForeColor = Color.ForestGreen;
                    _lblStatus.Text = "已发送到 " + name;
                    Refresh();
                    var t = new Timer { Interval = 600 };
                    t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); Close(); };
                    t.Start();
                }
                else
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "发送失败";
                    MessageBox.Show(res.Error ?? res.Response, "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _btnSend.Enabled = true;
                _busy = false;
            }
        }

        private void ShowScheduleDialog()
        {
            var name = _cboGroup.SelectedItem as string;
            if (name == null)
            {
                MessageBox.Show("请先配置群。", "没有可用的群", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var text = _box.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("请先输入要发送的内容。", "内容为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _box.Focus();
                return;
            }

            using (var dlg = new Form())
            {
                dlg.Text = "定时发送";
                dlg.Size = new Size(430, 310);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = Font;
                ApplyAppIcon(dlg);

                var lTarget = new Label { AutoSize = true, Location = new Point(18, 18), Text = "发送到：" + name };

                var rbOnce = new RadioButton { Text = "一次性", AutoSize = true, Checked = true, Location = new Point(18, 50) };
                var rbDaily = new RadioButton { Text = "每天（固定时间）", AutoSize = true, Location = new Point(110, 50) };

                var lWhen = new Label { AutoSize = true, Location = new Point(18, 88), Text = "时间" };
                var dtp = new DateTimePicker
                {
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "yyyy-MM-dd HH:mm",
                    Location = new Point(70, 84),
                    Size = new Size(190, 26),
                    Value = DateTime.Now.AddMinutes(5)
                };

                var lHint = new Label
                {
                    Location = new Point(18, 124),
                    Size = new Size(380, 40),
                    ForeColor = Color.DimGray,
                    Text = "一次性任务的日期和时间都会生效；每天任务只取时间部分。"
                };

                var btnOk = new Button { Text = "确定", Size = new Size(90, 32), Location = new Point(210, 210), DialogResult = DialogResult.OK };
                var btnNo = new Button { Text = "取消", Size = new Size(90, 32), Location = new Point(310, 210), DialogResult = DialogResult.Cancel };

                dlg.Controls.AddRange(new Control[] { lTarget, rbOnce, rbDaily, lWhen, dtp, lHint, btnOk, btnNo });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnNo;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var kind = rbDaily.Checked ? "daily" : "once";
                var when = dtp.Value;
                if (kind == "once" && when < DateTime.Now.AddSeconds(-30))
                {
                    MessageBox.Show("时间已经过去了，请选一个将来的时间。", "时间无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var jobId = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + new Random().Next(100, 999);
                try
                {
                    TaskMgr.Register(jobId, when, kind);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "创建定时任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _cfg.schedules.Add(new ScheduleCfg
                {
                    id = jobId,
                    groupName = name,
                    message = text,
                    atAll = _chkAll.Checked,
                    kind = kind,
                    runAt = when.ToString("yyyy-MM-dd HH:mm"),
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                CfgStore.Save(_cfg);
                UpdateScheduleList();

                MessageBox.Show(
                    "已创建定时任务：\n群：" + name + "\n时间：" + when.ToString("yyyy-MM-dd HH:mm") +
                    "\n类型：" + (kind == "daily" ? "每天" : "一次性"),
                    "定时任务已创建", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void DeleteSelectedSchedule()
        {
            if (_lvSched.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选中一个定时任务。", "未选中", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("确定删除这个定时任务？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var id = _lvSched.SelectedItems[0].Tag as string;
            TaskMgr.Delete(id);
            _cfg.schedules.RemoveAll(s => s.id == id);
            CfgStore.Save(_cfg);
            UpdateScheduleList();
        }

        private void ShowGroupManager()
        {
            using (var mgr = new Form())
            {
                mgr.Text = "管理群";
                mgr.Size = new Size(720, 500);
                mgr.StartPosition = FormStartPosition.CenterParent;
                mgr.Font = Font;
                mgr.ShowInTaskbar = false;
                ApplyAppIcon(mgr);

                var lv = new ListView
                {
                    View = View.Details,
                    FullRowSelect = true,
                    GridLines = true,
                    MultiSelect = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                    Location = new Point(10, 10),
                    Size = new Size(686, 370)
                };
                lv.Columns.Add("名称", 150);
                lv.Columns.Add("webhook", 210);
                lv.Columns.Add("默认@all", 70);
                lv.Columns.Add("备注", 230);

                Action refresh = () =>
                {
                    lv.BeginUpdate();
                    lv.Items.Clear();
                    foreach (var g in _cfg.groups)
                    {
                        var url = Sender.ResolveWebhook(g);
                        var it = new ListViewItem(g.name);
                        it.SubItems.Add(Sender.Mask(url));
                        it.SubItems.Add(g.atAll ? "是" : "否");
                        it.SubItems.Add(g.note ?? "");
                        it.Tag = g.name;
                        lv.Items.Add(it);
                    }
                    lv.EndUpdate();
                };

                var btnAdd = new Button { Text = "新增", Size = new Size(90, 32), Location = new Point(10, 392), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
                var btnEdit = new Button { Text = "编辑", Size = new Size(90, 32), Location = new Point(110, 392), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
                var btnDel = new Button { Text = "删除", Size = new Size(90, 32), Location = new Point(210, 392), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
                var btnDone = new Button { Text = "完成", Size = new Size(90, 32), Location = new Point(606, 392), Anchor = AnchorStyles.Right | AnchorStyles.Bottom, DialogResult = DialogResult.OK };
                var lInfo = new Label { AutoSize = false, Location = new Point(320, 398), Size = new Size(270, 22), ForeColor = Color.DimGray, Text = "群列表保存在 config.json", Anchor = AnchorStyles.Left | AnchorStyles.Bottom };

                btnAdd.Click += (s, e) => EditGroup(null, lv, refresh);
                btnEdit.Click += (s, e) =>
                {
                    if (lv.SelectedItems.Count == 0) return;
                    EditGroup(CfgStore.FindGroup(_cfg, lv.SelectedItems[0].Tag as string), lv, refresh);
                };
                btnDel.Click += (s, e) =>
                {
                    if (lv.SelectedItems.Count == 0) return;
                    var nm = lv.SelectedItems[0].Tag as string;
                    var used = _cfg.schedules.Count(x => x.groupName == nm);
                    var msg = "确定删除群「" + nm + "」？";
                    if (used > 0) msg += "\n\n注意：该群还有 " + used + " 个定时任务，删除后这些任务将发送失败。";
                    if (MessageBox.Show(msg, "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    _cfg.groups.RemoveAll(g => g.name == nm);
                    CfgStore.Save(_cfg);
                    refresh();
                };

                mgr.Controls.AddRange(new Control[] { lv, btnAdd, btnEdit, btnDel, btnDone, lInfo });
                mgr.AcceptButton = btnDone;
                refresh();
                mgr.ShowDialog(this);
            }
            UpdateGroupCombo();
        }

        private void EditGroup(GroupCfg existing, ListView lv, Action refresh)
        {
            using (var ed = new Form())
            {
                ed.Text = existing == null ? "新增群" : "编辑群";
                ed.Size = new Size(580, 370);
                ed.StartPosition = FormStartPosition.CenterParent;
                ed.FormBorderStyle = FormBorderStyle.FixedDialog;
                ed.MaximizeBox = false;
                ed.MinimizeBox = false;
                ed.Font = Font;
                ApplyAppIcon(ed);

                Func<int, string, Label> mkLabel = (y, t) => new Label { Text = t, AutoSize = true, Location = new Point(18, y) };

                var t1 = new TextBox { Location = new Point(94, 16), Size = new Size(450, 26) };
                var t2 = new TextBox { Location = new Point(94, 56), Size = new Size(450, 26) };
                var t3 = new TextBox { Location = new Point(94, 96), Size = new Size(450, 26) };
                var t4 = new TextBox { Location = new Point(94, 136), Size = new Size(450, 26) };
                var chk = new CheckBox { Text = "默认艾特所有人", AutoSize = true, Location = new Point(94, 176) };
                var lTip = new Label
                {
                    Location = new Point(18, 208),
                    Size = new Size(530, 44),
                    ForeColor = Color.DimGray,
                    Text = "填该群的机器人 webhook 地址（https://open.feishu.cn/open-apis/bot/v2/hook/...）。签名密钥仅在该机器人开启「签名校验」时填写，否则留空。"
                };
                var ok = new Button { Text = "保存", Size = new Size(90, 32), Location = new Point(350, 272), DialogResult = DialogResult.OK };
                var no = new Button { Text = "取消", Size = new Size(90, 32), Location = new Point(454, 272), DialogResult = DialogResult.Cancel };

                ed.Controls.AddRange(new Control[] {
                    mkLabel(20,"群名称"), t1, mkLabel(60,"webhook"), t2,
                    mkLabel(100,"签名密钥"), t3, mkLabel(140,"备注"), t4,
                    chk, lTip, ok, no });
                ed.AcceptButton = ok;
                ed.CancelButton = no;

                if (existing != null)
                {
                    t1.Text = existing.name;
                    t2.Text = existing.webhook;
                    t3.Text = existing.secret;
                    t4.Text = existing.note;
                    chk.Checked = existing.atAll;
                }

                while (true)
                {
                    if (ed.ShowDialog(this) != DialogResult.OK) return;

                    var nm = t1.Text.Trim();
                    if (nm.Length == 0)
                    {
                        MessageBox.Show("群名称不能为空。", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    bool dup = _cfg.groups.Any(g => g.name == nm && !(existing != null && existing.name == nm));
                    if (dup)
                    {
                        MessageBox.Show("已存在同名群「" + nm + "」。", "名称重复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    var wh = t2.Text.Trim();
                    if (wh.Length > 0 && !wh.StartsWith("https://open.feishu.cn/open-apis/bot/v2/hook/"))
                    {
                        if (MessageBox.Show(
                            "webhook 看起来不是飞书群机器人地址，仍要保存吗？\n\n期望以 https://open.feishu.cn/open-apis/bot/v2/hook/ 开头",
                            "地址可能有误", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                            continue;
                    }

                    var ng = new GroupCfg(nm, wh, t3.Text.Trim(), chk.Checked, t4.Text.Trim());
                    if (existing != null)
                    {
                        int idx = _cfg.groups.FindIndex(g => g.name == existing.name);
                        if (idx >= 0) _cfg.groups[idx] = ng; else _cfg.groups.Add(ng);
                        if (existing.name != nm)
                            foreach (var s in _cfg.schedules)
                                if (s.groupName == existing.name) s.groupName = nm;
                    }
                    else _cfg.groups.Add(ng);

                    CfgStore.Save(_cfg);
                    refresh();
                    return;
                }
            }
        }
    }

    // ==================== 程序入口 ====================
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // 定时任务模式：LarkSender.exe /job <JobId>
            if (args.Length >= 2 && args[0] == "/job")
            {
                return RunJob(args[1]);
            }

            // 命令行模式：/send "内容" [/group 群名] [/atall]
            if (args.Length >= 2 && args[0] == "/send")
            {
                return RunSend(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return 0;
        }

        private static int RunSend(string[] args)
        {
            try
            {
                var text = args[1];
                string groupName = null;
                bool atAll = false;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "/group" && i + 1 < args.Length) { groupName = args[++i]; }
                    else if (args[i] == "/atall") { atAll = true; }
                }
                var cfg = CfgStore.Load();
                var g = groupName == null
                    ? new GroupCfg("(默认)", "", "", false, "")
                    : CfgStore.FindGroup(cfg, groupName);
                if (g == null) { Console.Error.WriteLine("配置里没有群「" + groupName + "」"); return 2; }

                var res = Sender.Send(g, text, atAll, "cli");
                if (res.Ok) { Console.WriteLine("发送成功 → " + g.name); return 0; }
                Console.Error.WriteLine("发送失败：" + (res.Error ?? res.Response));
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static int RunJob(string jobId)
        {
            try
            {
                var cfg = CfgStore.Load();
                var job = cfg.schedules.FirstOrDefault(s => s.id == jobId);
                if (job == null)
                {
                    Sender.Log("", "(任务 " + jobId + ")", false, false, "配置中已找不到该计划，可能已被删除", "job");
                    return 2;
                }
                var g = CfgStore.FindGroup(cfg, job.groupName);
                if (g == null)
                {
                    Sender.Log(job.groupName, job.message, job.atAll, false, "群「" + job.groupName + "」已不存在", "job");
                    return 3;
                }

                var res = Sender.Send(g, job.message, job.atAll, "job");
                int code = res.Ok ? 0 : 1;

                // 一次性任务发完就清理系统任务和配置记录（不再有 .cmd 文件要删）
                if (job.kind == "once")
                {
                    try
                    {
                        var cfg2 = CfgStore.Load();
                        cfg2.schedules.RemoveAll(s => s.id == jobId);
                        CfgStore.Save(cfg2);
                    }
                    catch { }
                    TaskMgr.Delete(jobId);
                }
                return code;
            }
            catch (Exception ex)
            {
                try { Sender.Log("", "(任务 " + jobId + ")", false, false, "执行异常：" + ex.Message, "job"); } catch { }
                return 1;
            }
        }
    }
}