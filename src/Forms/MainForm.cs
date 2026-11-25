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
using System.Runtime.InteropServices;

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
        private TextBox _txtCardTitle;
        private ComboBox _cboColor;
        private TableLayoutPanel _titleRow;
        private Button _btnFormatMenu;
        private Button _btnInsertMenu;
        private Button _btnMoreMenu;
        private RichTextBox _livePreview;
        private SplitContainer _editorSplit;
        /// <summary>同步标志：防止左右互相同步时死循环</summary>
        private bool _syncingPreview;
        private Label _lblStatus;
        private Button _btnSend;

        // 定时任务页
        private ListView _lvSched;

        // 历史页
        private ListView _lvHistory;
        private RichTextBox _historyPreview;
        private SplitContainer _historySplit;

        private TabControl _tabs;

        private bool _busy;
        private ToolTip _tips;
        private GroupManagerForm _groupMgr;
        private TemplateManagerForm _tplMgr;
        private EditScrollHook _boxScrollHook;
        /// <summary>启动时从配置读到的分栏比例（BuildSendTab 早于 LoadConfigToUi 执行）</summary>
        private double _cfgSplitRatio = -1;

        // ==================== Win32 辅助（占位符 / 滚动同步） ====================

        private const int WM_VSCROLL = 0x114;
        private const int WM_MOUSEWHEEL = 0x20A;
        private const int EM_GETFIRSTVISIBLELINE = 0x15;
        private const int EM_SETCUEBANNER = 0x1501;
        private const int EM_GETSCROLLPOS = 0x4DD;
        private const int EM_SETSCROLLPOS = 0x4DE;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

        /// <summary>
        /// 系统级占位符（EM_SETCUEBANNER）。比手搓的“显示/清空 hint 文本”可靠：
        /// 占位文字永远不算真实内容，不会串进预览或被发送。
        /// </summary>
        private static void SetCueBanner(TextBox tb, string hint)
        {
            if (tb == null) return;
            SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, hint ?? "");
        }

        /// <summary>让预览滚动位置跟随左侧编辑框（按可见比例映射）</summary>
        private void SyncPreviewScrollFromBox()
        {
            if (_livePreview == null || _box == null || _livePreview.IsDisposed) return;

            try
            {
                int firstLine = (int)SendMessage(_box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                int totalLines = _box.GetLineFromCharIndex(_box.TextLength) + 1;
                int lineH = Math.Max(1, _box.Font.Height);
                int visibleLines = Math.Max(1, _box.ClientSize.Height / lineH);
                int maxLine = Math.Max(0, totalLines - visibleLines);

                var last = _livePreview.GetPositionFromCharIndex(Math.Max(0, _livePreview.TextLength - 1));
                int contentH = (last.IsEmpty ? 0 : last.Y) + _livePreview.Font.Height;
                int maxScroll = Math.Max(0, contentH - _livePreview.ClientSize.Height);

                int y = maxLine == 0 ? 0 : (int)((double)firstLine / maxLine * maxScroll);
                var pt = new Point(0, y);
                SendMessage(_livePreview.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref pt);
            }
            catch
            {
                // 句柄未就绪等极端情况忽略
            }
        }

        /// <summary>监听编辑框滚动（TextBox 没有 VScroll 事件），滚则带预览一起走</summary>
        private class EditScrollHook : NativeWindow
        {
            private readonly Action _onScroll;

            public EditScrollHook(Control c, Action onScroll)
            {
                _onScroll = onScroll;
                AssignHandle(c.Handle);
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
                {
                    try { _onScroll(); } catch { }
                }
            }
        }

        public MainForm()
        {
            // 先加载配置：构建 UI 时要用到分栏比例和窗口位置
            _cfg = LoadConfigSafe();
            if (_cfg.EditorSplitPercent >= 10 && _cfg.EditorSplitPercent <= 90)
                _cfgSplitRatio = _cfg.EditorSplitPercent / 100.0;

            InitUi();
            RestoreWindowState();
            LoadConfigToUi();
        }

        /// <summary>读取配置，失败时提示并回退到空配置（不中断启动）</summary>
        private AppConfig LoadConfigSafe()
        {
            try
            {
                return _store.Load();
            }
            catch (ConfigException ex)
            {
                MessageBox.Show(ex.Message, "配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new AppConfig();
            }
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

            // 打开即进入输入状态，少一次点击
            if (_tabs != null && _tabs.SelectedIndex == 0 && _box != null) _box.Focus();
        }

        /// <summary>Ctrl+Enter 在发消息页任何位置都能发送，不必先点回内容框</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                if (_tabs != null && _tabs.SelectedIndex == 0 && !_busy)
                {
                    DoSend();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
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
            Size = new Size(980, 700);
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.ApplyForm(this);
            AppIcon.Apply(this);
            KeyPreview = true;

            _tips = new ToolTip();

            var tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs = tabs;
            // 前三个页签顺序固定（CLI /tab 参数按索引跳转），管理类页签排后面
            var tpSend = new TabPage("发消息") { Padding = new Padding(10) };
            var tpSched = new TabPage("定时任务") { Padding = new Padding(10) };
            var tpHistory = new TabPage("历史记录") { Padding = new Padding(10) };
            var tpGroups = new TabPage("群管理") { Padding = new Padding(4) };
            var tpTemplates = new TabPage("常用语") { Padding = new Padding(4) };
            tabs.TabPages.AddRange(new[] { tpSend, tpSched, tpHistory, tpGroups, tpTemplates });
            Controls.Add(tabs);

            BuildSendTab(tpSend);
            BuildScheduleTab(tpSched);
            BuildHistoryTab(tpHistory);
            BuildGroupTab(tpGroups);
            BuildTemplateTab(tpTemplates);
        }

        /// <summary>把一个 Form 作为子控件嵌入页签（去掉边框，占满页签）</summary>
        private static void EmbedInTab(TabPage page, Form form)
        {
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Fill;
            form.StartPosition = FormStartPosition.Manual;
            page.Controls.Add(form);
            form.Show();
        }

        /// <summary>群管理页签：直接操作配置对象，变更即时落盘并刷新发消息页</summary>
        private void BuildGroupTab(TabPage page)
        {
            _groupMgr = new GroupManagerForm(_cfg);
            _groupMgr.OnDataChanged = () =>
            {
                try { _store.Save(_cfg); } catch (Exception ex)
                {
                    MessageBox.Show("保存配置失败：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshGroupCombo();
                SetStatus("群配置已更新", Color.ForestGreen);
            };
            EmbedInTab(page, _groupMgr);
        }

        /// <summary>常用语页签：增删改排序即时落盘并刷新下拉</summary>
        private void BuildTemplateTab(TabPage page)
        {
            _tplMgr = new TemplateManagerForm(_cfg.Templates);
            _tplMgr.OnDataChanged = items =>
            {
                _cfg.Templates = items;
                try { _store.Save(_cfg); } catch (Exception ex)
                {
                    MessageBox.Show("保存配置失败：" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshTemplateCombo();
                SetStatus("常用语已更新（" + items.Count + " 条）", Color.ForestGreen);
            };
            EmbedInTab(page, _tplMgr);
        }

        private void BuildSendTab(TabPage page)
        {
            page.Padding = new Padding(10);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 目标群
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 卡片标题
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 编辑区
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 工具条
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 主操作

            // ==================== 目标群 ====================
            var rowGroup = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 8)
            };
            rowGroup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
            rowGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            rowGroup.Controls.Add(new Label
            {
                Text = "发送到",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 6, 10, 0)
            }, 0, 0);

            _cboGroup = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 10, 0)
            };
            _cboGroup.SelectedIndexChanged += (s, e) => UpdateHookLabel();
            rowGroup.Controls.Add(_cboGroup, 1, 0);

            var groupTail = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            _lblHook = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Text = "",
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 6, 8, 0)
            };
            _lblHook.Click += (s, e) => CopyCurrentWebhook();
            groupTail.Controls.Add(_lblHook);
            _tips.SetToolTip(_lblHook, "点击复制完整 webhook");

            var btnManageGroup = new Button
            {
                Text = "管理群…",
                Size = new Size(78, 25),
                Margin = new Padding(0, 1, 0, 0)
            };
            btnManageGroup.Click += (s, e) => OpenGroupManager();
            groupTail.Controls.Add(btnManageGroup);

            rowGroup.Controls.Add(groupTail, 2, 0);

            // ==================== 卡片标题（填了标题或用了格式即按卡片发送，无需开关） ====================
            var rowTitle = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 8)
            };
            rowTitle.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowTitle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rowTitle.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowTitle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            rowTitle.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            rowTitle.Controls.Add(new Label
            {
                Text = "标题",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 6, 10, 0)
            }, 0, 0);

            _txtCardTitle = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 2, 10, 0)
            };
            SetCueBanner(_txtCardTitle, "选填，例如：部署完成");
            _txtCardTitle.TextChanged += (s, e) => RenderLivePreview();
            // 标题填完回车直接跳到正文
            _txtCardTitle.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Control)
                {
                    e.SuppressKeyPress = true;
                    _box.Focus();
                }
            };
            rowTitle.Controls.Add(_txtCardTitle, 1, 0);

            rowTitle.Controls.Add(new Label
            {
                Text = "颜色",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 6, 8, 0)
            }, 2, 0);

            _cboColor = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 10, 0)
            };
            _cboColor.Items.AddRange(new object[] { "自动", "蓝", "绿", "红", "橙", "灰", "紫", "青" });
            _cboColor.SelectedIndex = 0;
            _cboColor.SelectedIndexChanged += (s, e) => RenderLivePreview();
            rowTitle.Controls.Add(_cboColor, 3, 0);

            _titleRow = rowTitle;

            // ==================== 编辑区（左写右看，与其他页一致的分组框样式） ====================
            var editorSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                Margin = new Padding(0, 0, 0, 8),
                Panel1MinSize = 100,
                Panel2MinSize = 100
            };

            var leftBox = new GroupBox
            {
                Text = "内容（Markdown）",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            _box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle
            };
            // Ctrl+Enter 由窗体级 ProcessCmdKey 统一处理（焦点在任何控件上都生效）
            _box.TextChanged += (s, e) => { if (!_syncingPreview) RenderLivePreview(); };
            _boxScrollHook = new EditScrollHook(_box, SyncPreviewScrollFromBox);
            leftBox.Controls.Add(_box);
            editorSplit.Panel1.Controls.Add(leftBox);

            var rightBox = new GroupBox
            {
                Text = "预览（可直接编辑）",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            _livePreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            _livePreview.TextChanged += (s, e) =>
            {
                if (_syncingPreview) return;
                SyncPreviewToSource();
            };
            rightBox.Controls.Add(_livePreview);
            editorSplit.Panel2.Controls.Add(rightBox);

            _editorSplit = editorSplit;
            // 只在首次显示时应用记忆比例；窗口 resize 不再强制拉回 1:1，
            // 否则用户拖过的分栏位置会被覆盖
            double splitRatio = _cfgSplitRatio >= 0.10 && _cfgSplitRatio <= 0.90 ? _cfgSplitRatio : 0.5;
            AddShownHandler(() => SafeSetSplitter(editorSplit, splitRatio));

            // ==================== 工具条 ====================
            var rowTools = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 8)
            };
            rowTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rowTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var toolsLeft = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            // 常用语下拉放在最前（最常用）
            _cboTemplate = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170,
                Margin = new Padding(0, 2, 6, 0)
            };
            _cboTemplate.SelectedIndexChanged += (s, e) => ApplyTemplate();
            toolsLeft.Controls.Add(_cboTemplate);

            var btnSaveTpl = new Button
            {
                Text = "存为常用语",
                Size = new Size(90, 25),
                Margin = new Padding(0, 1, 12, 0)
            };
            btnSaveTpl.Click += (s, e) => SaveAsTemplate();
            toolsLeft.Controls.Add(btnSaveTpl);

            _btnFormatMenu = MakeToolButton("格式", 68);
            _btnFormatMenu.Click += (s, e) => ShowFormatMenu();
            toolsLeft.Controls.Add(_btnFormatMenu);

            _btnInsertMenu = MakeToolButton("插入", 68);
            _btnInsertMenu.Click += (s, e) => ShowInsertMenu();
            toolsLeft.Controls.Add(_btnInsertMenu);

            _btnMoreMenu = MakeToolButton("更多", 68);
            _btnMoreMenu.Click += (s, e) => ShowMoreMenu();
            toolsLeft.Controls.Add(_btnMoreMenu);

            rowTools.Controls.Add(toolsLeft, 0, 0);

            _lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.DimGray,
                Text = "",
                Margin = new Padding(8, 0, 0, 0)
            };
            rowTools.Controls.Add(_lblStatus, 1, 0);

            // ==================== 主操作 ====================
            var rowAction = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            rowAction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rowAction.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var hint = new Label
            {
                Text = "Ctrl + Enter 快速发送",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 8, 0, 0)
            };
            rowAction.Controls.Add(hint, 0, 0);

            _btnSend = new Button
            {
                Text = "发送",
                Size = new Size(110, 32)
            };
            _btnSend.Click += (s, e) => DoSend();
            rowAction.Controls.Add(_btnSend, 1, 0);

            root.Controls.Add(rowGroup, 0, 0);
            root.Controls.Add(rowTitle, 0, 1);
            root.Controls.Add(editorSplit, 0, 2);
            root.Controls.Add(rowTools, 0, 3);
            root.Controls.Add(rowAction, 0, 4);
            page.Controls.Add(root);
        }

        /// <summary>创建工具条小按钮（与其他页的系统按钮同一风格）</summary>
        private Button MakeToolButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Size = new Size(width, 25),
                Margin = new Padding(0, 1, 6, 0)
            };
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

            // 左：历史列表；右：选中记录的渲染预览
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                Margin = new Padding(0, 0, 0, 8),
                Panel1MinSize = 100,
                Panel2MinSize = 100
            };

            _lvHistory = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None
            };
            _lvHistory.Columns.Add("时间", 130);
            _lvHistory.Columns.Add("结果", 50);
            _lvHistory.Columns.Add("群", 100);
            _lvHistory.Columns.Add("内容", 200);
            _lvHistory.SelectedIndexChanged += (s, e) => ShowSelectedHistoryPreview();

            var lvHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2) };
            lvHost.Controls.Add(_lvHistory);
            split.Panel1.Controls.Add(lvHost);

            // 右侧预览区
            _historyPreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var previewLabel = new Label
            {
                Text = "预览",
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.DimGray,
                BackColor = Color.FromArgb(246, 248, 250),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            previewHost.Controls.Add(_historyPreview);
            previewHost.Controls.Add(previewLabel);
            split.Panel2.Controls.Add(previewHost);

            _historySplit = split;
            split.BorderStyle = BorderStyle.None;
            split.SplitterWidth = 6;
            // 只在首次显示时定位，resize 不重置（用户拖过的位置要保留）
            AddShownHandler(() => SafeSetSplitter(split, 0.48));

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

            var btnPreview = new Button { Text = "大图预览", Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0) };
            btnPreview.Click += (s, e) => OpenHistoryPreviewWindow();
            btnRow.Controls.Add(btnPreview);

            var btnCopy = new Button { Text = "复制原文", Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0) };
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
                Text = "选中记录可在右侧预览效果",
                Margin = new Padding(0, 8, 0, 0)
            });

            root.Controls.Add(split, 0, 0);
            root.Controls.Add(btnRow, 0, 1);
            page.Controls.Add(root);
        }

        // ==================== 数据加载 ====================

        private void LoadConfigToUi()
        {
            RefreshGroupCombo();
            RefreshTemplateCombo();
            RefreshScheduleList();
            RefreshHistory();
            RestoreDraft();
            RenderLivePreview();
        }

        /// <summary>恢复上次关闭时的窗口位置/尺寸（不在任何屏幕上时忽略）</summary>
        private void RestoreWindowState()
        {
            if (_cfg.WindowWidth < MinimumSize.Width || _cfg.WindowHeight < MinimumSize.Height) return;
            if (_cfg.WindowLeft < -10000 || _cfg.WindowTop < -10000) return;

            var topLeft = new Rectangle(_cfg.WindowLeft, _cfg.WindowTop, 200, 200);
            bool onScreen = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(topLeft));
            if (!onScreen) return;

            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(_cfg.WindowLeft, _cfg.WindowTop, _cfg.WindowWidth, _cfg.WindowHeight);
        }

        /// <summary>恢复上次未发送的草稿，避免误关窗口丢内容（卡片与否由内容自动判断）</summary>
        private void RestoreDraft()
        {
            var d = _cfg.Draft;
            if (d == null || d.IsEmpty) return;

            _box.Text = d.Body ?? "";
            _txtCardTitle.Text = d.Title ?? "";
            SelectColorByName(d.Color);
            SetStatus("已恢复上次未发送的草稿", Theme.TextSecondary);
        }

        /// <summary>按颜色名选中下拉项（找不到则回到「自动」）</summary>
        private void SelectColorByName(string colorName)
        {
            if (_cboColor == null || string.IsNullOrEmpty(colorName)) return;
            for (int k = 0; k < _cboColor.Items.Count; k++)
            {
                if (string.Equals(_cboColor.Items[k].ToString(), colorName, StringComparison.Ordinal))
                {
                    _cboColor.SelectedIndex = k;
                    return;
                }
            }
            _cboColor.SelectedIndex = 0;
        }

        /// <summary>关闭时记忆窗口状态、分栏比例和未发送草稿</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_cfg == null) return;

            try
            {
                if (WindowState == FormWindowState.Normal)
                {
                    _cfg.WindowLeft = Left;
                    _cfg.WindowTop = Top;
                    _cfg.WindowWidth = Width;
                    _cfg.WindowHeight = Height;
                }

                if (_editorSplit != null && _editorSplit.Width > 20)
                {
                    _cfg.EditorSplitPercent = (int)Math.Round(
                        100.0 * _editorSplit.SplitterDistance / _editorSplit.Width);
                }

                _cfg.Draft = MakeDraft();
                _store.Save(_cfg);
            }
            catch
            {
                // 记忆失败不影响关闭
            }
        }

        /// <summary>当前编辑区快照（空内容返回 null，不写草稿）</summary>
        private DraftConfig MakeDraft()
        {
            string body = _box != null ? _box.Text : "";
            string title = _txtCardTitle != null ? _txtCardTitle.Text.Trim() : "";
            if (body.Trim().Length == 0 && title.Length == 0) return null;

            return new DraftConfig
            {
                Body = body,
                Title = title,
                Color = _cboColor != null && _cboColor.SelectedIndex > 0
                    ? _cboColor.SelectedItem.ToString() : ""
            };
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
        }

        private void RefreshTemplateCombo()
        {
            _cboTemplate.Items.Clear();
            _cboTemplate.Items.Add("选择常用语…");
            foreach (var t in _cfg.Templates)
            {
                var prefix = t.IsCard ? "[卡片] " : "";
                _cboTemplate.Items.Add(prefix + t.Name);
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

        /// <summary>把选中历史记录渲染到右侧预览区</summary>
        private void ShowSelectedHistoryPreview()
        {
            if (_historyPreview == null) return;

            if (_lvHistory.SelectedItems.Count == 0)
            {
                _historyPreview.Clear();
                CardRenderer.AppendHint(_historyPreview, "在左侧选中一条记录，这里会显示发送时的效果");
                return;
            }

            if (_lvHistory.SelectedItems.Count > 1)
            {
                CardRenderer.AppendHint(_historyPreview,
                    "已选中 " + _lvHistory.SelectedItems.Count + " 条，右侧只预览第一条");
            }

            var entry = _lvHistory.SelectedItems[0].Tag as HistoryEntry;
            if (entry == null) return;

            var color = EntryColor(entry);
            CardRenderer.Render(_historyPreview, entry.Text, entry.AtAll, entry.IsCard,
                entry.CardTitle, color, string.IsNullOrEmpty(entry.CardColor));
        }

        /// <summary>从历史记录里还原卡片颜色</summary>
        private static CardBuilder.HeaderColor EntryColor(HistoryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.CardColor))
                return CardBuilder.HeaderColor.Blue;
            return CardBuilder.ParseColorNamePublic(entry.CardColor);
        }

        /// <summary>用大窗口预览选中历史（左边效果、右边 JSON 的完整版）</summary>
        private void OpenHistoryPreviewWindow()
        {
            if (_lvHistory.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选中一条历史记录。", "未选中",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var entry = _lvHistory.SelectedItems[0].Tag as HistoryEntry;
            if (entry == null) return;

            string payload = "";
            try
            {
                var notifier = new Notifier();
                bool auto = string.IsNullOrEmpty(entry.CardColor);
                payload = notifier.BuildPayload(
                    entry.Text, entry.AtAll, null,
                    entry.IsCard ? MessageKind.Card : MessageKind.Text,
                    entry.CardTitle, EntryColor(entry), auto);
            }
            catch (Exception ex)
            {
                MessageBox.Show("构造预览失败：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dlg = new PreviewForm(entry.Text, entry.AtAll, entry.IsCard, payload,
                entry.CardTitle, EntryColor(entry), string.IsNullOrEmpty(entry.CardColor)))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>
        /// 生成历史列表的摘要文字：去掉 Markdown 符号，卡片则优先用标题。
        /// 用户看到的是“部署完成 v2.3”而不是“# 部署完成 v2.3”。
        /// </summary>
        private static string MakeSummary(HistoryEntry h)
        {
            if (h == null) return "";

            // 卡片：有标题就用标题
            if (h.IsCard && !string.IsNullOrWhiteSpace(h.CardTitle))
            {
                var t = "[卡片] " + h.CardTitle.Trim();
                if (t.Length > 60) t = t.Substring(0, 60) + "…";
                return t;
            }

            var text = h.Text ?? "";
            // 取第一行非空内容
            string line = "";
            foreach (var raw in text.Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                line = t;
                break;
            }

            // 去掉常见 Markdown 标记
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^#{1,6}\s*", "");   // 标题
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^[-*+]\s+", "");     // 列表
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^>\s*", "");         // 引用
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^\d+\.\s+", "");     // 有序列表

            if (line.Length > 60) line = line.Substring(0, 60) + "…";
            return h.IsCard ? "[卡片] " + line : line;
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
                    // 列表里显示可读摘要，去掉 Markdown 符号
                    var one = MakeSummary(h);

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
                var tpl = _cfg.Templates[tplIdx];

                // 还原当时保存的形态：正文 + 卡片标题 + 颜色（是否卡片由内容自动判断）
                _box.Text = tpl.Body ?? "";
                _box.SelectionStart = _box.TextLength;

                _txtCardTitle.Text = tpl.Title ?? "";

                SelectColorByName(tpl.Color);

                RenderLivePreview();
                SetStatus("已填入常用语：" + tpl.Name, Theme.Primary);
            }
            _cboTemplate.SelectedIndex = 0;
        }

        private void SaveAsTemplate()
        {
            var body = _box.Text.Trim();
            string title = _txtCardTitle.Text.Trim();

            // 有标题没正文也算有内容（卡片形态）
            if (body.Length == 0 && title.Length == 0)
            {
                MessageBox.Show("内容和标题都是空的，没东西可存。", "无法保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var tpl = new TemplateConfig
            {
                Body = body,
                Title = title,
                IsCard = Notifier.PickKind(body, title) == MessageKind.Card,
                AtAll = (body + " " + title).Contains(Notifier.AtAllToken),
                Color = _cboColor != null && _cboColor.SelectedIndex > 0
                        ? _cboColor.SelectedItem.ToString() : ""
            };
            tpl.RefreshName();

            // 重复检查：正文+标题 完全一致才算重复
            foreach (var t in _cfg.Templates)
            {
                if (t.Body == tpl.Body && t.Title == tpl.Title && t.IsCard == tpl.IsCard)
                {
                    MessageBox.Show("这条常用语已经存在了。", "重复",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            _cfg.Templates.Add(tpl);
            try
            {
                _store.Save(_cfg);
                RefreshTemplateCombo();
                SetStatus("已保存常用语：" + tpl.Name, Theme.Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowPreview()
        {
            var text = _box.Text;
            string cardTitle = _txtCardTitle != null ? _txtCardTitle.Text.Trim() : "";
            var kind = Notifier.PickKind(text, cardTitle);

            if (string.IsNullOrWhiteSpace(text) && !(kind == MessageKind.Card && cardTitle.Length > 0))
            {
                MessageBox.Show("请先输入内容。", "内容为空", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var name = _cboGroup.SelectedItem as string;
            var g = name != null ? ConfigStore.FindGroup(_cfg, name) : null;
            bool autoColor;
            var cardColor = CurrentColor(out autoColor);

            string payload;
            try
            {
                var notifier = new Notifier();
                payload = notifier.BuildPayload(text, false,
                    g != null ? g.Secret : null, kind, cardTitle, cardColor, autoColor, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("构造失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dlg = new PreviewForm(text, false, kind == MessageKind.Card, payload, cardTitle, cardColor, autoColor))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>取当前选的颜色（自动 = 根据标题猜）</summary>
        private CardBuilder.HeaderColor CurrentColor(out bool auto)
        {
            int idx = _cboColor != null ? _cboColor.SelectedIndex : 0;
            auto = idx <= 0;
            if (auto) return CardBuilder.HeaderColor.Blue;

            switch (idx)
            {
                case 1: return CardBuilder.HeaderColor.Blue;
                case 2: return CardBuilder.HeaderColor.Green;
                case 3: return CardBuilder.HeaderColor.Red;
                case 4: return CardBuilder.HeaderColor.Orange;
                case 5: return CardBuilder.HeaderColor.Grey;
                case 6: return CardBuilder.HeaderColor.Purple;
                case 7: return CardBuilder.HeaderColor.Turquoise;
                default: return CardBuilder.HeaderColor.Blue;
            }
        }


        /// <summary>
        /// 右侧预览被编辑后，把内容回写到左侧源文本。
        ///
        /// 预览是富文本渲染后的样子，反向转回 Markdown 只能尽力而为：
        /// 保留结构（粗体/斜体/列表），拿不到的属性（如链接地址）忽略。
        /// 这也是为什么左侧是主编辑区，右侧适合微调措辞。
        /// </summary>
        private void SyncPreviewToSource()
        {
            if (_syncingPreview) return;

            _syncingPreview = true;
            try
            {
                var md = RichTextToMarkdown();
                md = StripPreviewTitle(md);
                if (md != null && md != _box.Text)
                {
                    int sel = _box.SelectionStart;
                    _box.Text = md;
                    if (sel <= _box.TextLength) _box.SelectionStart = sel;
                }
            }
            finally
            {
                _syncingPreview = false;
            }
        }

        /// <summary>
        /// 卡片模式下预览开头是彩色标题栏（渲染时加了两个空格的前后留白），
        /// 它来自标题输入框而不是正文。右侧回写时必须把它剥掉，
        /// 否则标题会被带进正文，再次渲染后「内容里也有标题」。
        /// </summary>
        private string StripPreviewTitle(string md)
        {
            if (md == null) return null;

            string title = _txtCardTitle != null ? _txtCardTitle.Text.Trim() : "";
            if (title.Length == 0) return md;

            // 标题栏在预览里渲染为 "  标题  "（留白 + 加粗字体）。
            // RichTextToMarkdown 会把加粗转成 **…**，所以回写的开头是
            // "**  标题  **"（也可能没有留白或没有星号），四种形态都试。
            string[] heads =
            {
                "**  " + title + "  **",
                "  " + title + "  ",
                "**" + title + "**",
                title
            };
            foreach (string h in heads)
            {
                if (md.StartsWith(h, StringComparison.Ordinal))
                {
                    return md.Substring(h.Length).TrimStart('\r', '\n');
                }
            }

            return md; // 开头不是标题栏，不动
        }

        /// <summary>把 RichTextBox 的富文本反向转成 Markdown</summary>
        private string RichTextToMarkdown()
        {
            if (_livePreview == null) return null;

            var sb = new StringBuilder();
            var plain = _livePreview.Text;
            int pos = 0;

            // 逐字符读样式，合并连续相同样式的片段
            while (pos < plain.Length)
            {
                _livePreview.Select(pos, 1);
                var font = _livePreview.SelectionFont;
                var color = _livePreview.SelectionColor;
                bool bold = font != null && font.Bold;
                bool italic = font != null && font.Italic;
                bool strike = font != null && font.Strikeout;
                bool isCode = font != null && font.Name != null &&
                              font.Name.Equals("Consolas", StringComparison.OrdinalIgnoreCase);

                int start = pos;
                while (pos < plain.Length)
                {
                    _livePreview.Select(pos, 1);
                    var f2 = _livePreview.SelectionFont;
                    var c2 = _livePreview.SelectionColor;
                    bool same =
                        (f2 != null && f2.Bold) == bold &&
                        (f2 != null && f2.Italic) == italic &&
                        (f2 != null && f2.Strikeout) == strike &&
                        (f2 != null && f2.Name != null && f2.Name.Equals("Consolas", StringComparison.OrdinalIgnoreCase)) == isCode &&
                        c2.Equals(color);
                    if (!same) break;
                    pos++;
                }

                var chunk = plain.Substring(start, pos - start);
                sb.Append(DecorateChunk(chunk, bold, italic, strike, isCode));
            }

            return sb.ToString();
        }

        /// <summary>按样式给文本包上 Markdown 标记（跳过空白）</summary>
        private static string DecorateChunk(string text, bool bold, bool italic, bool strike, bool isCode)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // 纯空白或换行不包标记，否则会出现 "**  **" 这种垃圾
            if (text.Trim().Length == 0) return text;

            string s = text;
            if (isCode) return "`" + s + "`";
            if (strike) s = "~~" + s + "~~";
            if (bold && italic) s = "***" + s + "***";
            else if (bold) s = "**" + s + "**";
            else if (italic) s = "*" + s + "*";
            return s;
        }


        // ==================== 菜单 ====================

        private void ShowFormatMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = Font;

            AddMenuItem(menu, "加粗", () => WrapSelection("**", "**", "粗体文字"), FontStyle.Bold);
            AddMenuItem(menu, "斜体", () => WrapSelection("*", "*", "斜体文字"), FontStyle.Italic);
            AddMenuItem(menu, "删除线", () => WrapSelection("~~", "~~", "删除文字"), FontStyle.Strikeout);
            menu.Items.Add(new ToolStripSeparator());
            AddMenuItem(menu, "无序列表", () => InsertLinePrefix("- "), FontStyle.Regular);
            AddMenuItem(menu, "有序列表", () => InsertLinePrefix("1. "), FontStyle.Regular);

            menu.Show(_btnFormatMenu, new Point(0, _btnFormatMenu.Height));
        }

        private void ShowInsertMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = Font;

            AddMenuItem(menu, "链接", () => InsertLink(), FontStyle.Regular);
            AddMenuItem(menu, "分割线", () => InsertLinePrefix("---"), FontStyle.Regular);
            menu.Items.Add(new ToolStripSeparator());
            AddMenuItem(menu, "@所有人", () => InsertAtAll(), FontStyle.Bold);

            menu.Show(_btnInsertMenu, new Point(0, _btnInsertMenu.Height));
        }

        private void ShowMoreMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = Font;

            AddMenuItem(menu, "定时发送…", () => OpenScheduleDialog(), FontStyle.Regular);
            AddMenuItem(menu, "查看发送的 JSON", () => ShowPreview(), FontStyle.Regular);
            menu.Items.Add(new ToolStripSeparator());
            AddMenuItem(menu, "管理常用语…", () => OpenTemplateManager(), FontStyle.Regular);
            AddMenuItem(menu, "管理群…", () => OpenGroupManager(), FontStyle.Regular);
            menu.Items.Add(new ToolStripSeparator());
            AddMenuItem(menu, "关闭窗口", () => Close(), FontStyle.Regular);

            menu.Show(_btnMoreMenu, new Point(0, _btnMoreMenu.Height));
        }

        private static void AddMenuItem(ContextMenuStrip menu, string text, Action action, FontStyle style)
        {
            var item = new ToolStripMenuItem(text);
            item.Font = new Font(menu.Font, style);
            item.Click += (s, e) => { try { action(); } catch { } };
            menu.Items.Add(item);
        }

        /// <summary>
        /// 用标记包裹选中文字（没选区就插入占位符并选中它）。
        /// 这是非技术用户的主要交互方式：选中一段字 → 点「加粗」。
        /// </summary>
        private void WrapSelection(string prefix, string suffix, string placeholder)
        {
            int start = _box.SelectionStart;
            int len = _box.SelectionLength;

            if (len > 0)
            {
                var selected = _box.Text.Substring(start, len);
                _box.Text = _box.Text.Remove(start, len).Insert(start, prefix + selected + suffix);
                _box.SelectionStart = start + prefix.Length + len + suffix.Length;
                _box.SelectionLength = 0;
            }
            else
            {
                _box.Text = _box.Text.Insert(start, prefix + placeholder + suffix);
                // 选中占位符，直接打字就能替换
                _box.SelectionStart = start + prefix.Length;
                _box.SelectionLength = placeholder.Length;
            }

            _box.Focus();
            RenderLivePreview();
        }

        /// <summary>在当前行首插入前缀（列表、编号、分割线）</summary>
        private void InsertLinePrefix(string prefix)
        {
            int caret = _box.SelectionStart;
            int lineStart = _box.Text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            if (caret == 0) lineStart = 0;

            // 分割线单独一行
            if (prefix == "---")
            {
                var before = _box.Text.Substring(0, lineStart);
                var after = _box.Text.Substring(lineStart);
                var needNl = before.Length > 0 && !before.EndsWith("\n") ? "\r\n" : "";
                _box.Text = before + needNl + "---\r\n" + after;
                _box.SelectionStart = before.Length + needNl.Length + 5;
            }
            else
            {
                _box.Text = _box.Text.Insert(lineStart, prefix);
                _box.SelectionStart = caret + prefix.Length;
            }

            _box.Focus();
            RenderLivePreview();
        }

        /// <summary>插入链接：有选中文字就用它做文字，否则插入占位</summary>
        private void InsertLink()
        {
            int start = _box.SelectionStart;
            int len = _box.SelectionLength;
            string label = len > 0 ? _box.Text.Substring(start, len) : "链接文字";
            string snippet = "[" + label + "](https://)";

            if (len > 0) _box.Text = _box.Text.Remove(start, len).Insert(start, snippet);
            else _box.Text = _box.Text.Insert(start, snippet);

            // 光标定位到 https:// 后面，方便直接粘地址
            int urlStart = start + 1 + label.Length + 2;
            _box.SelectionStart = urlStart;
            _box.SelectionLength = 0;

            _box.Focus();
            RenderLivePreview();
        }

        /// <summary>
        /// 在光标处插入 @所有人 元素：发送时转成真正的提及，
        /// 提及出现在正文里的位置就是用户插入的位置。
        /// </summary>
        private void InsertAtAll()
        {
            int start = _box.SelectionStart;
            const string token = Notifier.AtAllToken + " ";
            _box.Text = _box.Text.Insert(start, token);
            _box.SelectionStart = start + token.Length;
            _box.SelectionLength = 0;

            _box.Focus();
            RenderLivePreview();
        }

        /// <summary>
        /// 实时渲染右侧预览。
        /// 卡片模式下按飞书卡片的真实样式渲染（彩色标题栏、加粗、链接、分割线等）。
        /// </summary>
        private void RenderLivePreview()
        {
            if (_livePreview == null) return;

            bool auto;
            var color = CurrentColor(out auto);
            string title = _txtCardTitle != null ? _txtCardTitle.Text : null;
            var kind = Notifier.PickKind(_box.Text, title);

            // 渲染时不要回写源文本（双向绑定的防循环措施）
            _syncingPreview = true;
            try
            {
                CardRenderer.Render(_livePreview, _box.Text, false,
                    kind == MessageKind.Card, title, color, auto, false);
            }
            finally
            {
                _syncingPreview = false;
            }

            // 重渲染会清空控件导致滚动归零：渲染完按左侧位置带回来，长文编辑不再跳顶
            SyncPreviewScrollFromBox();
        }

        /// <summary>把当前群的完整 webhook 复制到剪贴板（webhook 掩码可点击）</summary>
        private void CopyCurrentWebhook()
        {
            var name = _cboGroup.SelectedItem as string;
            var g = name != null ? ConfigStore.FindGroup(_cfg, name) : null;
            if (g == null || !g.HasWebhook)
            {
                SetStatus("该群没有配置 webhook", Theme.Warning);
                return;
            }

            if (TrySetClipboard(g.Webhook))
                SetStatus("webhook 已复制到剪贴板", Theme.Success);
            else
                SetStatus("复制失败，剪贴板被占用", Theme.Danger);
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
            string cardTitle = _txtCardTitle != null ? _txtCardTitle.Text.Trim() : "";
            var kind = Notifier.PickKind(text, cardTitle);

            // 有标题没正文也允许发送（标题-only 卡片）
            if (string.IsNullOrWhiteSpace(text) && !(kind == MessageKind.Card && cardTitle.Length > 0))
            {
                SetStatus("内容为空，没发出去", Color.Firebrick);
                _box.Focus();
                return;
            }

            var group = ConfigStore.FindGroup(_cfg, name);
            if (group == null) return;

            // @所有人 是正文里的元素（发送时由 Notifier 转成提及），不再单独开关
            bool atAll = false;
            bool autoColor;
            var cardColor = CurrentColor(out autoColor);

            _busy = true;
            _btnSend.Enabled = false;
            _btnSend.Text = "发送中…";
            SetStatus("发送中…", Color.DimGray);

            // 网络请求放后台线程，避免界面卡死
            var notifier = new Notifier();
            Task.Run(() =>
            {
                var result = notifier.Send(group, text, atAll, kind, cardTitle, cardColor, autoColor, false);
                string colorName = autoColor ? "" : CardBuilder.ColorDisplayName(cardColor);
                _logger.RecordSend(group.Name, text, atAll, result, "manual",
                    kind == MessageKind.Card ? "card" : "text", cardTitle, colorName);
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

                        // 发送成功后留在窗口：清空输入框并聚焦，方便接着发下一条。
                        // 不自动关窗——用户经常要连发多条。
                        _box.Clear();
                        _txtCardTitle.Clear();
                        _box.Focus();
                        RenderLivePreview();

                        // 草稿随发送清掉，下次启动不再恢复已发出的内容
                        _cfg.Draft = null;
                        try { _store.Save(_cfg); } catch { }
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
                    _btnSend.Text = "发送";
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

            using (var dlg = new ScheduleForm(name, text, false))
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

        /// <summary>跳到群管理页签</summary>
        private void OpenGroupManager()
        {
            if (_tabs != null && _tabs.TabPages.Count > 3) _tabs.SelectedIndex = 3;
        }

        /// <summary>跳到常用语页签</summary>
        private void OpenTemplateManager()
        {
            if (_tabs != null && _tabs.TabPages.Count > 4) _tabs.SelectedIndex = 4;
        }
    }
}
