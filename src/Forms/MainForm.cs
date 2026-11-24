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
        private ComboBox _cboAtAllMode;
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
                RowCount = 6
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 选群
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 模板
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 卡片标题（仅卡片模式）
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 输入框 + 预览
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 选项 + 格式
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

            // ---- 输入框 + 实时预览（左右分栏，双向绑定）----
            // 两边都可编辑：左边写 Markdown，右边改文字会回写到左边。
            var editorSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 10,
                Margin = new Padding(0, 0, 0, 8),
                Panel1MinSize = 100,
                Panel2MinSize = 80,
                // 去掉默认的 3D 凹凸边框，看起来干净
                BorderStyle = BorderStyle.None
            };
            // 分割条美化：先让容器不自绘，再由 Paint 画细线
            editorSplit.Paint += (s, e) => SplitPaint.PaintOver(editorSplit, e);
            SplitPaint.Attach(editorSplit);

            _box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 10f)
            };
            _box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DoSend();
                }
            };
            _box.TextChanged += (s, e) => { if (!_syncingPreview) RenderLivePreview(); };
            var editHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
            editHost.Controls.Add(_box);
            editHost.Paint += (s, e) => SplitPaint.PaintCardBorder(editHost, e);
            editorSplit.Panel1.Controls.Add(editHost);

            // 右侧可编辑预览
            _livePreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            // 双击 / 回车时才开始编辑效果，平时保持渲染态
            _livePreview.LinkClicked += (s, e) => { };
            _livePreview.TextChanged += (s, e) =>
            {
                if (_syncingPreview) return;
                SyncPreviewToSource();
            };

            var liveHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
            liveHost.Paint += (s, e) => SplitPaint.PaintCardBorder(liveHost, e);

            var liveLabel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.FromArgb(249, 250, 251),
                Padding = new Padding(10, 0, 8, 0)
            };
            liveLabel.Controls.Add(new Label
            {
                Text = "预览（可直接修改）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(107, 114, 128),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            });
            liveHost.Controls.Add(_livePreview);
            liveHost.Controls.Add(liveLabel);
            editorSplit.Panel2.Controls.Add(liveHost);

            _editorSplit = editorSplit;

            editorSplit.SizeChanged += (s, e) => SafeSetSplitter(editorSplit, 0.52);
            AddShownHandler(() => SafeSetSplitter(editorSplit, 0.52));

            // ---- 卡片标题行（仅卡片模式可见）----
            var titleRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 6),
                Visible = false
            };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));

            titleRow.Controls.Add(new Label
            {
                Text = "卡片标题",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0)
            }, 0, 0);

            _txtCardTitle = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 6, 0)
            };
            // .NET 4.0 没有 PlaceholderText，用灰色提示文字模拟
            SetPlaceholder(_txtCardTitle, "例如：部署完成 / 周会提醒");
            _txtCardTitle.TextChanged += (s, e) => RenderLivePreview();
            titleRow.Controls.Add(_txtCardTitle, 1, 0);

            titleRow.Controls.Add(new Label
            {
                Text = "颜色",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 0)
            }, 2, 0);

            _cboColor = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 0)
            };
            _cboColor.Items.AddRange(new object[] { "自动", "蓝", "绿", "红", "橙", "灰", "紫", "青" });
            _cboColor.SelectedIndex = 0;
            _cboColor.SelectedIndexChanged += (s, e) => RenderLivePreview();
            titleRow.Controls.Add(_cboColor, 3, 0);

            _titleRow = titleRow;

            // ---- 底部工具栏：只留必要控件，其余收进菜单 ----
            var row4 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 8)
            };
            row4.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var leftTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            // 卡片开关：用 ToolStrip 风格的切换按钮，比 CheckBox 紧凑
            _chkPost = new CheckBox
            {
                Text = "卡片消息",
                AutoSize = true,
                Margin = new Padding(0, 5, 12, 0)
            };
            _chkPost.CheckedChanged += (s, e) => UpdatePostHint();
            leftTools.Controls.Add(_chkPost);

            _chkAll = new CheckBox
            {
                Text = "@所有人",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 5, 6, 0)
            };
            _chkAll.CheckedChanged += (s, e) => { RenderLivePreview(); SaveAtAllInline(); };
            leftTools.Controls.Add(_chkAll);

            // @所有人 同行/换行 下拉（只在勾选 @所有人 时可见）
            _cboAtAllMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 76,
                Margin = new Padding(0, 3, 14, 0),
                Visible = false
            };
            _cboAtAllMode.Items.AddRange(new object[] { "换行", "同行" });
            _cboAtAllMode.SelectedIndexChanged += (s, e) => { RenderLivePreview(); SaveAtAllInline(); };
            leftTools.Controls.Add(_cboAtAllMode);

            // 格式菜单（代替原来的 7 个按钮）
            _btnFormatMenu = new Button
            {
                Text = "格式 ▼",
                Size = new Size(76, 28),
                Margin = new Padding(0, 1, 6, 0),
                FlatStyle = FlatStyle.System,
                Visible = false
            };
            _btnFormatMenu.Click += (s, e) => ShowFormatMenu();
            leftTools.Controls.Add(_btnFormatMenu);

            // 插入菜单
            _btnInsertMenu = new Button
            {
                Text = "插入 ▼",
                Size = new Size(76, 28),
                Margin = new Padding(0, 1, 6, 0),
                FlatStyle = FlatStyle.System,
                Visible = false
            };
            _btnInsertMenu.Click += (s, e) => ShowInsertMenu();
            leftTools.Controls.Add(_btnInsertMenu);

            row4.Controls.Add(leftTools, 0, 0);

            _lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(107, 114, 128),
                Text = "Ctrl+Enter 发送",
                Margin = new Padding(8, 0, 2, 0)
            };
            row4.Controls.Add(_lblStatus, 1, 0);

            // ---- 底部主操作 ----
            var row5 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            _btnSend = new Button
            {
                Text = "发送",
                Size = new Size(120, 36),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.System,
                Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold)
            };
            _btnSend.Click += (s, e) => DoSend();
            row5.Controls.Add(_btnSend);

            _btnMoreMenu = new Button
            {
                Text = "更多 ▼",
                Size = new Size(84, 36),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.System
            };
            _btnMoreMenu.Click += (s, e) => ShowMoreMenu();
            row5.Controls.Add(_btnMoreMenu);

            root.Controls.Add(row1, 0, 0);
            root.Controls.Add(row2, 0, 1);
            root.Controls.Add(titleRow, 0, 2);
            root.Controls.Add(editorSplit, 0, 3);
            root.Controls.Add(row4, 0, 4);
            root.Controls.Add(row5, 0, 5);
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
            split.SplitterWidth = 10;
            split.Paint += (s, e) => SplitPaint.PaintOver(split, e);
            SplitPaint.Attach(split);
            split.SizeChanged += (s, e) => SafeSetSplitter(split, 0.48);
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
            if (_cboAtAllMode != null)
            {
                _cboAtAllMode.SelectedIndex = (_cfg.AtAllInline ? 1 : 0);
            }
            UpdateAtAllModeVisibility();
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
            bool isCard = _chkPost.Checked;
            string cardTitle = _txtCardTitle != null ? RealText(_txtCardTitle).Trim() : "";

            if (string.IsNullOrWhiteSpace(text) && !(isCard && cardTitle.Length > 0))
            {
                MessageBox.Show("请先输入内容。", "内容为空", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var name = _cboGroup.SelectedItem as string;
            var g = name != null ? ConfigStore.FindGroup(_cfg, name) : null;
            var kind = isCard ? MessageKind.Card : MessageKind.Text;
            bool autoColor;
            var cardColor = CurrentColor(out autoColor);

            string payload;
            try
            {
                var notifier = new Notifier();
                payload = notifier.BuildPayload(text, _chkAll.Checked,
                    g != null ? g.Secret : null, kind, cardTitle, cardColor, autoColor, AtAllInline);
            }
            catch (Exception ex)
            {
                MessageBox.Show("构造失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dlg = new PreviewForm(text, _chkAll.Checked, isCard, payload, cardTitle, cardColor, autoColor))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>勾选/取消卡片模式时，显示格式菜单入口</summary>
        private void UpdatePostHint()
        {
            bool on = _chkPost.Checked;

            _titleRow.Visible = on;
            if (_btnFormatMenu != null) _btnFormatMenu.Visible = on;
            if (_btnInsertMenu != null) _btnInsertMenu.Visible = on;

            if (on)
            {
                SetStatus("选中文字后点「格式」改样式", Color.FromArgb(37, 99, 235));
            }
            else
            {
                SetStatus("Ctrl+Enter 发送", Color.FromArgb(107, 114, 128));
            }

            RenderLivePreview();
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

        /// <summary>为 TextBox 模拟占位文字（.NET 4.0 无 PlaceholderText）</summary>
        private static void SetPlaceholder(TextBox tb, string hint)
        {
            if (tb == null) return;

            // 用一个标志位区分：true = 当前显示的是占位文字
            var state = new PlaceholderState { Hint = hint, ShowingHint = true };
            tb.Tag = state;
            tb.ForeColor = Color.FromArgb(156, 163, 175);
            tb.Text = hint;

            tb.GotFocus += (s, e) =>
            {
                if (state.ShowingHint)
                {
                    state.ShowingHint = false;
                    tb.Text = "";
                    tb.ForeColor = Color.Black;
                }
            };
            tb.LostFocus += (s, e) =>
            {
                if (tb.Text.Trim().Length == 0)
                {
                    state.ShowingHint = true;
                    tb.Text = hint;
                    tb.ForeColor = Color.FromArgb(156, 163, 175);
                }
            };
            // 文本被程序改动时（如 SetRealText）同步状态
            tb.TextChanged += (s, e) =>
            {
                if (!state.ShowingHint) return;
                if (tb.Text != hint) state.ShowingHint = false;
            };
        }

        /// <summary>占位符状态（避免用闭包变量导致无法从外部读取）</summary>
        private class PlaceholderState
        {
            public string Hint;
            public bool ShowingHint;
        }

        /// <summary>取 TextBox 真实输入（排除占位符）</summary>
        private static string RealText(TextBox tb)
        {
            if (tb == null) return "";
            var st = tb.Tag as PlaceholderState;
            if (st != null)
            {
                if (st.ShowingHint) return "";
                return tb.Text ?? "";
            }
            return tb.Text ?? "";
        }

        /// <summary>清空 TextBox 并恢复占位符</summary>
        private static void ClearWithPlaceholder(TextBox tb, string hint)
        {
            if (tb == null) return;
            var st = tb.Tag as PlaceholderState;
            if (st != null)
            {
                st.ShowingHint = true;
                tb.Text = hint;
                tb.ForeColor = Color.FromArgb(156, 163, 175);
            }
            else
            {
                tb.Text = "";
            }
        }

        /// <summary>往占位符输入框写入真实内容</summary>
        private static void SetRealText(TextBox tb, string text)
        {
            if (tb == null) return;
            var st = tb.Tag as PlaceholderState;
            if (!string.IsNullOrEmpty(text))
            {
                if (st != null) st.ShowingHint = false;
                tb.Text = text;
                tb.ForeColor = Color.Black;
            }
            else
            {
                ClearWithPlaceholder(tb, st != null ? st.Hint : "");
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

        /// <summary>@所有人 同行/换行 下拉的显隐与取值</summary>
        private bool AtAllInline
        {
            get { return _cboAtAllMode != null && _cboAtAllMode.SelectedIndex == 1; }
        }

        private void UpdateAtAllModeVisibility()
        {
            if (_cboAtAllMode != null)
                _cboAtAllMode.Visible = _chkAll != null && _chkAll.Checked;
        }

        /// <summary>把 @所有人 同行/换行 存进配置（下次打开记住）</summary>
        private void SaveAtAllInline()
        {
            UpdateAtAllModeVisibility();
            if (_cfg == null) return;
            bool now = AtAllInline;
            if (_cfg.AtAllInline == now) return;
            _cfg.AtAllInline = now;
            try { _store.Save(_cfg); } catch { /* 保存失败不影响使用 */ }
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

            menu.Show(_btnInsertMenu, new Point(0, _btnInsertMenu.Height));
        }

        private void ShowMoreMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = Font;

            AddMenuItem(menu, "定时发送…", () => OpenScheduleDialog(), FontStyle.Regular);
            AddMenuItem(menu, "查看发送的 JSON", () => ShowPreview(), FontStyle.Regular);
            menu.Items.Add(new ToolStripSeparator());
            AddMenuItem(menu, "存为常用语", () => SaveAsTemplate(), FontStyle.Regular);
            AddMenuItem(menu, "管理常用语…", () => OpenTemplateManager(), FontStyle.Regular);
            AddMenuItem(menu, "管理群…", () => OpenGroupManager(), FontStyle.Regular);

            menu.Show(_btnMoreMenu, new Point(0, _btnMoreMenu.Height));
        }

        private static void AddMenuItem(ContextMenuStrip menu, string text, Action action, FontStyle style)
        {
            var item = new ToolStripMenuItem(text);
            item.Font = new Font(menu.Font, style);
            item.Click += (s, e) => { try { action(); } catch { } };
            menu.Items.Add(item);
        }

        /// <summary>创建格式按钮（保留旧方法兼容）</summary>
        private Button MakeFormatButton(string text, int width, Font font)
        {
            return new Button
            {
                Text = text,
                Size = new Size(width, 26),
                Margin = new Padding(0, 4, 4, 0),
                FlatStyle = FlatStyle.System,
                Font = font,
                Visible = false
            };
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
        /// 实时渲染右侧预览。
        /// 卡片模式下按飞书卡片的真实样式渲染（彩色标题栏、加粗、链接、分割线等）。
        /// </summary>
        private void RenderLivePreview()
        {
            if (_livePreview == null) return;

            bool auto;
            var color = CurrentColor(out auto);
            string title = _txtCardTitle != null ? RealText(_txtCardTitle) : null;

            // 渲染时不要回写源文本（双向绑定的防循环措施）
            _syncingPreview = true;
            try
            {
                CardRenderer.Render(_livePreview, _box.Text, _chkAll.Checked,
                    _chkPost.Checked, title, color, auto, AtAllInline);
            }
            finally
            {
                _syncingPreview = false;
            }
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
            bool isCard = _chkPost.Checked;
            string cardTitle = _txtCardTitle != null ? RealText(_txtCardTitle).Trim() : "";

            // 卡片模式允许只有标题没正文
            if (string.IsNullOrWhiteSpace(text) && !(isCard && cardTitle.Length > 0))
            {
                SetStatus("内容为空，没发出去", Color.Firebrick);
                _box.Focus();
                return;
            }

            var group = ConfigStore.FindGroup(_cfg, name);
            if (group == null) return;

            var kind = isCard ? MessageKind.Card : MessageKind.Text;
            bool atAll = _chkAll.Checked;
            bool autoColor;
            var cardColor = CurrentColor(out autoColor);

            _busy = true;
            _btnSend.Enabled = false;
            SetStatus("发送中…", Color.DimGray);

            // 网络请求放后台线程，避免界面卡死
            var notifier = new Notifier();
            Task.Run(() =>
            {
                var result = notifier.Send(group, text, atAll, kind, cardTitle, cardColor, autoColor, AtAllInline);
                string colorName = autoColor ? "" : CardBuilder.ColorDisplayName(cardColor);
                _logger.RecordSend(group.Name, text, atAll, result, "manual",
                    isCard ? "card" : "text", cardTitle, colorName);
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
                        ClearWithPlaceholder(_txtCardTitle, "例如：部署完成 / 周会提醒");
                        _box.Focus();
                        RenderLivePreview();
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