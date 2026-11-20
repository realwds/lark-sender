using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>主窗口：发消息 + 定时任务 + 历史三个页签</summary>
    public class MainForm : Form
    {
        private readonly ConfigStore _store = new ConfigStore();
        private readonly Logger _logger = new Logger();
        private AppConfig _cfg;

        // 发消息页
        private ComboBox _cboGroup;
        private ComboBox _cboTemplate;
        private Label _lblHook;
        private TextBox _box;
        private CheckBox _chkAll;
        private CheckBox _chkPost;
        private Button _btnSyntaxTitle;
        private Button _btnSyntaxList;
        private Button _btnSyntaxBold;
        private RichTextBox _livePreview;
        private SplitContainer _editorSplit;
        private Label _lblStatus;
        private Button _btnSend;
        private Button _btnSchedule;
        private Button _btnPreview;

        // 定时任务页
        private ListView _lvSched;

        // 历史页
        private ListView _lvHistory;

        private TabControl _tabs;

        private bool _busy;

        public MainForm()
        {
            InitUi();
            LoadConfigToUi();
        }

        /// <summary>
        /// 安全设置分栏位置。
        /// 只在尺寸真的够大时设，且比例控制在合法区间，避免抛异常把程序弄崩。
        /// </summary>
        private static void SafeSetSplitter(SplitContainer sc, double ratio)
        {
            if (sc == null || sc.IsDisposed) return;

            try
            {
                int total = sc.Width;
                if (total <= 0) return;

                int min1 = sc.Panel1MinSize;
                int min2 = sc.Panel2MinSize;
                int usable = total - sc.SplitterWidth;

                // 窗口太窄，两个面板的最小宽度都满足不了 → 不动
                if (usable < min1 + min2) return;

                int target = (int)(total * ratio);
                if (target < min1) target = min1;
                int max = total - min2 - sc.SplitterWidth;
                if (target > max) target = max;

                if (target >= min1 && target <= max && sc.SplitterDistance != target)
                    sc.SplitterDistance = target;
            }
            catch
            {
                // 布局极端情况下忽略，保持默认值
            }
        }

        /// <summary>注册一个在窗口首次显示后执行的动作（用于依赖实际尺寸的布局）</summary>
        private readonly List<Action> _shownHandlers = new List<Action>();

        private void AddShownHandler(Action action)
        {
            if (action == null) return;
            _shownHandlers.Add(action);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            foreach (var h in _shownHandlers)
            {
                try { h(); } catch { }
            }
            _shownHandlers.Clear();
        }

        /// <summary>切换到指定页签（0=发消息 1=定时任务 2=历史记录），供启动参数使用</summary>
        public void SelectTab(int index)
        {
            if (_tabs == null) return;
            if (index < 0 || index >= _tabs.TabPages.Count) return;
            _tabs.SelectedIndex = index;
        }

        // ==================== 界面构建 ====================

        private void InitUi()
        {
            Text = "Lark Sender";
            Size = new Size(940, 680);
            MinimumSize = new Size(760, 580);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AppIcon.Apply(this);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs = tabs;
            var tpSend = new TabPage("发消息") { Padding = new Padding(10) };
            var tpSched = new TabPage("定时任务") { Padding = new Padding(10) };
            var tpHistory = new TabPage("历史记录") { Padding = new Padding(10) };
            tabs.TabPages.AddRange(new[] { tpSend, tpSched, tpHistory });
            Controls.Add(tabs);

            BuildSendTab(tpSend);
            BuildScheduleTab(tpSched);
            BuildHistoryTab(tpHistory);
        }

        private void BuildSendTab(TabPage page)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 选群
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 模板
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 输入框
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 选项
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 按钮

            // ---- 选群行 ----
            var row1 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 6)
            };
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            row1.Controls.Add(new Label
            {
                Text = "发送到",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0)
            }, 0, 0);

            _cboGroup = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 6, 0)
            };
            _cboGroup.SelectedIndexChanged += (s, e) => UpdateHookLabel();
            row1.Controls.Add(_cboGroup, 1, 0);

            var btnManage = new Button
            {
                Text = "管理群…",
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                Margin = new Padding(0, 2, 8, 0)
            };
            btnManage.Click += (s, e) => OpenGroupManager();
            row1.Controls.Add(btnManage, 2, 0);

            _lblHook = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 2, 0, 0)
            };
            row1.Controls.Add(_lblHook, 3, 0);

            // ---- 模板行 ----
            var row2 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 6)
            };
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            row2.Controls.Add(new Label
            {
                Text = "常用语",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0)
            }, 0, 0);

            _cboTemplate = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 6, 0)
            };
            _cboTemplate.SelectedIndexChanged += (s, e) => ApplyTemplate();
            row2.Controls.Add(_cboTemplate, 1, 0);

            var btnSaveTpl = new Button
            {
                Text = "存为常用语",
                AutoSize = true,
                MinimumSize = new Size(100, 26),
                Margin = new Padding(0, 2, 6, 0)
            };
            btnSaveTpl.Click += (s, e) => SaveAsTemplate();
            row2.Controls.Add(btnSaveTpl, 2, 0);

            var btnManageTpl = new Button
            {
                Text = "管理…",
                AutoSize = true,
                MinimumSize = new Size(70, 26),
                Margin = new Padding(0, 2, 0, 0)
            };
            btnManageTpl.Click += (s, e) => OpenTemplateManager();
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row2.ColumnCount = 4;
            row2.Controls.Add(btnManageTpl, 3, 0);

            // ---- 输入框 + 实时预览（左右分栏）----
            // 只看得到 `**加粗**` 符号却看不到效果，是富文本最难用的地方。
            // 这里把输入框和预览并排，边写边看。
            // 注意：不要在这里硬设 SplitterDistance。
            // WinForms 的 SplitContainer 创建时宽度还是 0，设大了会抛
            // “SplitterDistance 必须在 Panel1MinSize 和 Width - Panel2MinSize 之间”，
            // 导致程序启动失败。让它在 Dock 后自己按默认比例分。
            var editorSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                Margin = new Padding(0, 0, 0, 6),
                // 最小值设小一点：窗口初建时宽度可能很小，
                // 两个 MinSize 加起来超过可用宽度就会在后续布局阶段抛异常
                Panel1MinSize = 100,
                Panel2MinSize = 80
            };

            _box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None
            };
            _box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DoSend();
                }
            };
            _box.TextChanged += (s, e) => RenderLivePreview();

            var editHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            editHost.Controls.Add(_box);
            editorSplit.Panel1.Controls.Add(editHost);

            // 实时预览只渲染效果，不渲染 JSON
            _livePreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            var liveHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var liveLabel = new Label
            {
                Text = "预览",
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.DimGray,
                BackColor = Color.FromArgb(246, 248, 250),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            liveHost.Controls.Add(_livePreview);
            liveHost.Controls.Add(liveLabel);
            editorSplit.Panel2.Controls.Add(liveHost);

            _editorSplit = editorSplit;

            // 分栏距离在窗口尺寸稳定后再设，并且每次尺寸变化都重新算一次。
            // SplitContainer 在创建/早期布局阶段宽度很小，此时设 SplitterDistance
            // 会抛 “SplitterDistance 必须在 Panel1MinSize 和 Width - Panel2MinSize 之间”。
            editorSplit.SizeChanged += (s, e) => SafeSetSplitter(editorSplit, 0.55);
            AddShownHandler(() => SafeSetSplitter(editorSplit, 0.55));

            // ---- 选项行 ----
            var row4 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 6)
            };

            _chkAll = new CheckBox
            {
                Text = "艾特所有人",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 14, 0)
            };
            _chkAll.CheckedChanged += (s, e) => RenderLivePreview();
            row4.Controls.Add(_chkAll);

            _chkPost = new CheckBox
            {
                Text = "富文本格式",
                AutoSize = true,
                Margin = new Padding(0, 4, 8, 0)
            };
            _chkPost.CheckedChanged += (s, e) => UpdatePostHint();
            row4.Controls.Add(_chkPost);

            // 富文本语法快捷按钮：勾了富文本才显示
            _btnSyntaxTitle = new Button
            {
                Text = "+ 标题",
                Size = new Size(64, 24),
                Margin = new Padding(0, 3, 4, 0),
                FlatStyle = FlatStyle.System,
                Visible = false
            };
            _btnSyntaxTitle.Click += (s, e) => InsertSyntax("# ");
            row4.Controls.Add(_btnSyntaxTitle);

            _btnSyntaxList = new Button
            {
                Text = "+ 列表",
                Size = new Size(64, 24),
                Margin = new Padding(0, 3, 4, 0),
                FlatStyle = FlatStyle.System,
                Visible = false
            };
            _btnSyntaxList.Click += (s, e) => InsertSyntax("- ");
            row4.Controls.Add(_btnSyntaxList);

            _btnSyntaxBold = new Button
            {
                Text = "B 加粗",
                Size = new Size(64, 24),
                Margin = new Padding(0, 3, 14, 0),
                FlatStyle = FlatStyle.System,
                Visible = false,
                Font = new Font(Font, FontStyle.Bold)
            };
            _btnSyntaxBold.Click += (s, e) => InsertSyntax("**粗体**");
            row4.Controls.Add(_btnSyntaxBold);

            _lblStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Text = "Ctrl+Enter 快速发送",
                Margin = new Padding(0, 6, 0, 0)
            };
            row4.Controls.Add(_lblStatus);

            // ---- 按钮行 ----
            var row5 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            _btnSend = new Button { Text = "立即发送", Size = new Size(110, 34), Margin = new Padding(8, 0, 0, 0) };
            _btnSend.Click += (s, e) => DoSend();
            row5.Controls.Add(_btnSend);

            _btnSchedule = new Button { Text = "定时发送…", Size = new Size(110, 34), Margin = new Padding(8, 0, 0, 0) };
            _btnSchedule.Click += (s, e) => OpenScheduleDialog();
            row5.Controls.Add(_btnSchedule);

            _btnPreview = new Button { Text = "预览", Size = new Size(80, 34), Margin = new Padding(8, 0, 0, 0) };
            _btnPreview.Click += (s, e) => ShowPreview();
            row5.Controls.Add(_btnPreview);

            var btnClose = new Button { Text = "关闭", Size = new Size(80, 34), Margin = new Padding(8, 0, 0, 0) };
            btnClose.Click += (s, e) => Close();
            row5.Controls.Add(btnClose);

            root.Controls.Add(row1, 0, 0);
            root.Controls.Add(row2, 0, 1);
            root.Controls.Add(editorSplit, 0, 2);
            root.Controls.Add(row4, 0, 3);
            root.Controls.Add(row5, 0, 4);
            page.Controls.Add(root);
        }

        private void BuildScheduleTab(TabPage page)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lvSched = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _lvSched.Columns.Add("触发时间", 135);
            _lvSched.Columns.Add("群", 110);
            _lvSched.Columns.Add("类型", 65);
            _lvSched.Columns.Add("内容", 240);
            _lvSched.Columns.Add("下次运行", 160);

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var btnRefresh = new Button { Text = "刷新", Size = new Size(90, 32), Margin = new Padding(0, 0, 8, 0) };
            btnRefresh.Click += (s, e) => RefreshScheduleList();
            btnRow.Controls.Add(btnRefresh);

            var btnDel = new Button { Text = "删除选中", Size = new Size(100, 32), Margin = new Padding(0, 0, 12, 0) };
            btnDel.Click += (s, e) => DeleteSelectedSchedule();
            btnRow.Controls.Add(btnDel);

            btnRow.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "一次性任务发完自动移除",
                Margin = new Padding(0, 8, 0, 0)
            });

            root.Controls.Add(_lvSched, 0, 0);
            root.Controls.Add(btnRow, 0, 1);
            page.Controls.Add(root);
        }

        private void BuildHistoryTab(TabPage page)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lvHistory = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _lvHistory.Columns.Add("时间", 140);
            _lvHistory.Columns.Add("结果", 55);
            _lvHistory.Columns.Add("群", 110);
            _lvHistory.Columns.Add("内容", 300);
            _lvHistory.MultiSelect = true;
            _lvHistory.DoubleClick += (s, e) => CopyHistoryText();

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var btnReload = new Button { Text = "刷新", Size = new Size(80, 32), Margin = new Padding(0, 0, 6, 0) };
            btnReload.Click += (s, e) => RefreshHistory();
            btnRow.Controls.Add(btnReload);

            var btnCopy = new Button { Text = "复制内容", Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0) };
            btnCopy.Click += (s, e) => CopyHistoryText();
            btnRow.Controls.Add(btnCopy);

            var btnDelHist = new Button { Text = "删除选中", Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0) };
            btnDelHist.Click += (s, e) => DeleteSelectedHistory();
            btnRow.Controls.Add(btnDelHist);

            var btnClearHist = new Button { Text = "清空全部", Size = new Size(90, 32), Margin = new Padding(0, 0, 14, 0) };
            btnClearHist.Click += (s, e) => ClearAllHistory();
            btnRow.Controls.Add(btnClearHist);

            btnRow.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "双击可复制内容（可多选）",
                Margin = new Padding(0, 8, 0, 0)
            });

            root.Controls.Add(_lvHistory, 0, 0);
            root.Controls.Add(btnRow, 0, 1);
            page.Controls.Add(root);
        }

        // ==================== 数据加载 ====================

        private void LoadConfigToUi()
        {
            try
            {
                _cfg = _store.Load();
            }
            catch (ConfigException ex)
            {
                MessageBox.Show(ex.Message, "配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _cfg = new AppConfig();
            }

            RefreshGroupCombo();
            RefreshTemplateCombo();
            RefreshScheduleList();
            RefreshHistory();
            RenderLivePreview();
            UpdatePostHint();
        }

        private void RefreshGroupCombo()
        {
            var keep = _cboGroup.SelectedItem as string;
            _cboGroup.Items.Clear();
            foreach (var g in _cfg.Groups) _cboGroup.Items.Add(g.Name);

            if (_cboGroup.Items.Count == 0)
            {
                _lblHook.Text = "还没有配置群，点「管理群…」添加";
                _lblHook.ForeColor = Color.Firebrick;
                return;
            }

            if (keep != null && _cboGroup.Items.Contains(keep)) _cboGroup.SelectedItem = keep;
            else _cboGroup.SelectedIndex = 0;
            UpdateHookLabel();
        }

        private void UpdateHookLabel()
        {
            var name = _cboGroup.SelectedItem as string;
            if (name == null) return;

            var g = ConfigStore.FindGroup(_cfg, name);
            if (g == null) return;

            if (g.HasWebhook)
            {
                _lblHook.Text = "webhook：" + Notifier.MaskWebhook(g.Webhook);
                _lblHook.ForeColor = Color.DimGray;
            }
            else
            {
                _lblHook.Text = "⚠ 该群未配置 webhook";
                _lblHook.ForeColor = Color.Firebrick;
            }
            _chkAll.Checked = g.AtAll;
        }

        private void RefreshTemplateCombo()
        {
            _cboTemplate.Items.Clear();
            _cboTemplate.Items.Add("(选择常用语)");
            foreach (var t in _cfg.Templates)
            {
                var one = (t ?? "").Split('\n')[0].Trim();
                if (one.Length == 0) one = "(空)";
                if (one.Length > 50) one = one.Substring(0, 50) + "…";
                _cboTemplate.Items.Add(one);
            }
            _cboTemplate.SelectedIndex = 0;
        }

        private void RefreshScheduleList()
        {
            _lvSched.BeginUpdate();
            try
            {
                _lvSched.Items.Clear();
                var mgr = new ScheduleManager();

                foreach (var s in _cfg.Schedules.OrderBy(x => x.RunAt))
                {
                    var one = (s.Message ?? "").Split('\n')[0].Trim();
                    if (one.Length > 50) one = one.Substring(0, 50) + "…";

                    var it = new ListViewItem(s.RunAt ?? "");
                    it.SubItems.Add(s.GroupName ?? "");
                    it.SubItems.Add(s.KindDisplay);
                    it.SubItems.Add(one);

                    var next = mgr.NextRunText(s.Id);
                    if (next == null)
                    {
                        it.SubItems.Add("⚠ 系统任务不存在");
                        it.ForeColor = Color.Firebrick;
                    }
                    else
                    {
                        it.SubItems.Add(next);
                    }

                    it.Tag = s.Id;
                    _lvSched.Items.Add(it);
                }
            }
            finally
            {
                _lvSched.EndUpdate();
            }
        }

        private void RefreshHistory()
        {
            _lvHistory.BeginUpdate();
            try
            {
                _lvHistory.Items.Clear();
                var entries = _logger.ReadHistory(200);

                foreach (var h in entries)
                {
                    var one = (h.Text ?? "").Split('\n')[0].Trim();
                    if (one.Length > 60) one = one.Substring(0, 60) + "…";

                    var it = new ListViewItem(h.Time ?? "");
                    it.SubItems.Add(h.Ok ? "成功" : "失败");
                    it.SubItems.Add(h.Group ?? "");
                    it.SubItems.Add(one);
                    if (!h.Ok) it.ForeColor = Color.Firebrick;
                    it.Tag = h;
                    _lvHistory.Items.Add(it);
                }
            }
            finally
            {
                _lvHistory.EndUpdate();
            }
        }

        // ==================== 交互 ====================

        private void ApplyTemplate()
        {
            int idx = _cboTemplate.SelectedIndex;
            if (idx <= 0) return;
            int tplIdx = idx - 1;
            if (tplIdx >= 0 && tplIdx < _cfg.Templates.Count)
            {
                _box.Text = _cfg.Templates[tplIdx];
                _box.SelectionStart = _box.TextLength;
            }
            _cboTemplate.SelectedIndex = 0;
        }

        private void SaveAsTemplate()
        {
            var text = _box.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("输入框是空的。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_cfg.Templates.Contains(text))
            {
                MessageBox.Show("这条常用语已经存在了。", "重复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _cfg.Templates.Add(text);
            try
            {
                _store.Save(_cfg);
                RefreshTemplateCombo();
                SetStatus("已保存为常用语", Color.ForestGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowPreview()
        {
            var text = _box.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("请先输入内容。", "内容为空", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var name = _cboGroup.SelectedItem as string;
            var g = name != null ? ConfigStore.FindGroup(_cfg, name) : null;
            var kind = _chkPost.Checked ? MessageKind.Post : MessageKind.Text;

            string payload;
            try
            {
                var notifier = new Notifier();
                payload = notifier.BuildPayload(text, _chkAll.Checked, g != null ? g.Secret : null, kind);
            }
            catch (Exception ex)
            {
                MessageBox.Show("构造失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dlg = new PreviewForm(text, _chkAll.Checked, _chkPost.Checked, payload))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>勾选/取消富文本时，控制语法按钮的显隐并提示语法</summary>
        private void UpdatePostHint()
        {
            bool on = _chkPost.Checked;
            _btnSyntaxTitle.Visible = on;
            _btnSyntaxList.Visible = on;
            _btnSyntaxBold.Visible = on;

            if (on)
            {
                SetStatus("富文本：# 标题 · - 列表 · **文字** 加粗", Color.FromArgb(37, 99, 235));
            }
            else
            {
                SetStatus("Ctrl+Enter 快速发送", Color.DimGray);
            }

            RenderLivePreview();
        }

        /// <summary>在光标处插入语法片段</summary>
        private void InsertSyntax(string snippet)
        {
            int start = _box.SelectionStart;

            // 对标题/列表：只在行首插入，如果光标不在行首，先换行
            if (snippet == "# " || snippet == "- ")
            {
                bool atLineStart = start == 0 || _box.Text[start - 1] == '\n';
                if (!atLineStart)
                {
                    _box.Text = _box.Text.Insert(start, "\r\n" + snippet);
                    _box.SelectionStart = start + 2 + snippet.Length;
                }
                else
                {
                    _box.Text = _box.Text.Insert(start, snippet);
                    _box.SelectionStart = start + snippet.Length;
                }
            }
            else if (snippet == "**粗体**")
            {
                // 如果有选中文本，包住它；否则插入占位符并选中中间
                if (_box.SelectionLength > 0)
                {
                    var selected = _box.Text.Substring(_box.SelectionStart, _box.SelectionLength);
                    _box.Text = _box.Text.Remove(_box.SelectionStart, _box.SelectionLength)
                                         .Insert(_box.SelectionStart, "**" + selected + "**");
                    _box.SelectionStart = start + 2 + selected.Length + 2;
                }
                else
                {
                    _box.Text = _box.Text.Insert(start, "**粗体**");
                    // 选中「粗体」两个字，方便直接改
                    _box.SelectionStart = start + 2;
                    _box.SelectionLength = 2;
                }
            }
            else
            {
                _box.Text = _box.Text.Insert(start, snippet);
                _box.SelectionStart = start + snippet.Length;
            }

            _box.Focus();
        }

        /// <summary>
        /// 实时渲染右侧预览。
        /// 纯文本模式下直接原文显示；富文本模式下按 post 语义渲染。
        /// </summary>
        private void RenderLivePreview()
        {
            if (_livePreview == null) return;

            var text = _box.Text;
            _livePreview.SuspendLayout();
            try
            {
                _livePreview.Clear();

                if (text.Length == 0)
                {
                    AppendLive("（在左侧输入内容，这里会显示出效果）",
                        Color.FromArgb(156, 163, 175), false, false);
                    return;
                }

                bool isPost = _chkPost.Checked;

                if (_chkAll.Checked)
                {
                    AppendLive("所有人", Color.FromArgb(37, 99, 235), true, false);
                    AppendLive("\n", Color.Black, false, false);
                }

                if (!isPost)
                {
                    // 纯文本：原样显示，只是把 @all 变蓝
                    AppendLive(text, Color.Black, false, false);
                    return;
                }

                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (i > 0) AppendLive("\n", Color.Black, false, false);
                    RenderLiveLine(line);
                }
            }
            finally
            {
                _livePreview.ResumeLayout();
            }
        }

        private void RenderLiveLine(string line)
        {
            if (line.Length == 0) return;

            if (line.StartsWith("# "))
            {
                AppendLive(line.Substring(2).Trim(), Color.FromArgb(17, 24, 39), true, true);
                return;
            }

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                AppendLive("  •  ", Color.FromArgb(107, 114, 128), false, false);
                RenderLiveInline(line.Substring(2));
                return;
            }

            RenderLiveInline(line);
        }

        private void RenderLiveInline(string line)
        {
            int i = 0;
            var buf = new StringBuilder();

            while (i < line.Length)
            {
                if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '*')
                {
                    int end = line.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        if (buf.Length > 0)
                        {
                            AppendLive(buf.ToString(), Color.Black, false, false);
                            buf.Length = 0;
                        }
                        AppendLive(line.Substring(i + 2, end - i - 2), Color.Black, true, false);
                        i = end + 2;
                        continue;
                    }
                }
                buf.Append(line[i]);
                i++;
            }

            if (buf.Length > 0)
                AppendLive(buf.ToString(), Color.Black, false, false);
        }

        private void AppendLive(string text, Color color, bool bold, bool big)
        {
            if (string.IsNullOrEmpty(text)) return;

            int start = _livePreview.TextLength;
            _livePreview.AppendText(text);
            _livePreview.Select(start, text.Length);
            _livePreview.SelectionColor = color;

            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
            float size = _livePreview.Font.Size + (big ? 2.5f : 0f);
            _livePreview.SelectionFont = new Font(_livePreview.Font.FontFamily, size, style);
            _livePreview.SelectionLength = 0;
        }

        private void DoSend()
        {
            if (_busy) return;

            var name = _cboGroup.SelectedItem as string;
            if (name == null)
            {
                MessageBox.Show("请先在「管理群…」里配置至少一个群。",
                    "没有可用的群", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var text = _box.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("内容为空，没发出去", Color.Firebrick);
                _box.Focus();
                return;
            }

            var group = ConfigStore.FindGroup(_cfg, name);
            if (group == null) return;

            var kind = _chkPost.Checked ? MessageKind.Post : MessageKind.Text;
            bool atAll = _chkAll.Checked;

            _busy = true;
            _btnSend.Enabled = false;
            SetStatus("发送中…", Color.DimGray);

            // 网络请求放后台线程，避免界面卡死
            var notifier = new Notifier();
            Task.Run(() =>
            {
                var result = notifier.Send(group, text, atAll, kind);
                _logger.RecordSend(group.Name, text, atAll, result, "manual");
                return result;
            }).ContinueWith(t =>
            {
                try
                {
                    if (t.IsFaulted)
                    {
                        SetStatus("发送失败", Color.Firebrick);
                        MessageBox.Show(t.Exception.GetBaseException().Message,
                            "出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = t.Result;
                    if (result.Ok)
                    {
                        SetStatus("已发送到 " + name, Color.ForestGreen);
                        RefreshHistory();
                        var timer = new System.Windows.Forms.Timer { Interval = 700 };
                        timer.Tick += (s2, e2) => { timer.Stop(); timer.Dispose(); Close(); };
                        timer.Start();
                    }
                    else
                    {
                        SetStatus("发送失败", Color.Firebrick);
                        MessageBox.Show(result.Error, "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    _btnSend.Enabled = true;
                    _busy = false;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private void OpenScheduleDialog()
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

            using (var dlg = new ScheduleForm(name, text, _chkAll.Checked))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.CreatedJob == null) return;

                _cfg.Schedules.Add(dlg.CreatedJob);
                try
                {
                    _store.Save(_cfg);
                    RefreshScheduleList();
                    MessageBox.Show(
                        "已创建定时任务：\n\n群：" + dlg.CreatedJob.GroupName
                        + "\n时间：" + dlg.CreatedJob.RunAt
                        + "\n类型：" + dlg.CreatedJob.KindDisplay,
                        "创建成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存配置失败：" + ex.Message,
                        "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteSelectedSchedule()
        {
            if (_lvSched.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选中一个定时任务。", "未选中",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var id = _lvSched.SelectedItems[0].Tag as string;
            if (MessageBox.Show("确定删除这个定时任务？", "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var mgr = new ScheduleManager();
            mgr.Delete(id);

            _cfg.Schedules.RemoveAll(s => s.Id == id);
            try
            {
                _store.Save(_cfg);
                RefreshScheduleList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存配置失败：" + ex.Message,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>把选中历史的内容复制到剪贴板（支持多选，按行拼接）</summary>
        private void CopyHistoryText()
        {
            if (_lvHistory.SelectedItems.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            // SelectedItems 的颉序与点击顺序有关，按列表位置重新排一下
            var picked = new List<HistoryEntry>();
            foreach (ListViewItem it in _lvHistory.Items)
            {
                if (!it.Selected) continue;
                var e = it.Tag as HistoryEntry;
                if (e != null) picked.Add(e);
            }
            if (picked.Count == 0) return;

            for (int i = 0; i < picked.Count; i++)
            {
                if (i > 0) sb.Append("\r\n");
                sb.Append(picked[i].Text ?? "");
            }

            string content = sb.ToString();
            if (content.Length == 0)
            {
                SetStatus("选中的记录内容为空", Color.DimGray);
                return;
            }

            if (TrySetClipboard(content))
                SetStatus("已复制 " + picked.Count + " 条到剪贴板", Color.ForestGreen);
            else
                SetStatus("复制失败，剪贴板被占用", Color.Firebrick);
        }

        /// <summary>写剪贴板，失败重试几次（剪贴板可能被其他进程临时占用）</summary>
        private static bool TrySetClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(80);
                }
            }
            return false;
        }

        /// <summary>删除选中的历史记录</summary>
        private void DeleteSelectedHistory()
        {
            if (_lvHistory.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选中要删除的记录（可多选）。", "未选中",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ids = new List<string>();
            foreach (ListViewItem it in _lvHistory.Items)
            {
                if (!it.Selected) continue;
                var e = it.Tag as HistoryEntry;
                if (e != null && !string.IsNullOrEmpty(e.Id)) ids.Add(e.Id);
            }
            if (ids.Count == 0) return;

            if (MessageBox.Show("确定删除选中的 " + ids.Count + " 条历史记录？", "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int removed = _logger.DeleteHistory(ids);
            RefreshHistory();

            if (removed > 0)
                SetStatus("已删除 " + removed + " 条历史", Color.ForestGreen);
            else
                SetStatus("删除失败：" + (_logger.LastError ?? "未找到记录"), Color.Firebrick);
        }

        /// <summary>清空全部历史</summary>
        private void ClearAllHistory()
        {
            if (_lvHistory.Items.Count == 0)
            {
                MessageBox.Show("历史记录已经是空的。", "无需清空",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show("确定清空全部 " + _lvHistory.Items.Count + " 条历史记录？\n\n此操作不可恢复。",
                    "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            if (_logger.ClearHistory())
            {
                RefreshHistory();
                SetStatus("历史已清空", Color.ForestGreen);
            }
            else
            {
                SetStatus("清空失败：" + (_logger.LastError ?? "未知错误"), Color.Firebrick);
            }
        }

        /// <summary>打开常用语管理窗口</summary>
        private void OpenTemplateManager()
        {
            using (var dlg = new TemplateManagerForm(_cfg.Templates))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;

                _cfg.Templates = dlg.Result;
                try
                {
                    _store.Save(_cfg);
                    RefreshTemplateCombo();
                    SetStatus("常用语已更新（" + _cfg.Templates.Count + " 条）", Color.ForestGreen);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenGroupManager()
        {
            using (var mgr = new GroupManagerForm(_cfg))
            {
                mgr.ShowDialog(this);
                if (mgr.Changed)
                {
                    try
                    {
                        _store.Save(_cfg);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("保存配置失败：" + ex.Message,
                            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    RefreshGroupCombo();
                }
            }
        }
    }
}