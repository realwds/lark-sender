using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private Label _lblStatus;
        private Button _btnSend;
        private Button _btnSchedule;
        private Button _btnPreview;

        // 定时任务页
        private ListView _lvSched;

        // 历史页
        private ListView _lvHistory;

        private bool _busy;

        public MainForm()
        {
            InitUi();
            LoadConfigToUi();
        }

        // ==================== 界面构建 ====================

        private void InitUi()
        {
            Text = "Lark Sender";
            Size = new Size(760, 660);
            MinimumSize = new Size(680, 580);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AppIcon.Apply(this);

            var tabs = new TabControl { Dock = DockStyle.Fill };
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
                Margin = new Padding(0, 2, 0, 0)
            };
            btnSaveTpl.Click += (s, e) => SaveAsTemplate();
            row2.Controls.Add(btnSaveTpl, 2, 0);

            // ---- 输入框 ----
            _box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6)
            };
            _box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DoSend();
                }
            };

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
            row4.Controls.Add(_chkAll);

            _chkPost = new CheckBox
            {
                Text = "富文本格式",
                AutoSize = true,
                Margin = new Padding(0, 4, 14, 0)
            };
            row4.Controls.Add(_chkPost);

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
            root.Controls.Add(_box, 0, 2);
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
            _lvHistory.Columns.Add("内容", 320);
            _lvHistory.DoubleClick += (s, e) => ReuseHistoryItem();

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var btnReload = new Button { Text = "刷新", Size = new Size(90, 32), Margin = new Padding(0, 0, 8, 0) };
            btnReload.Click += (s, e) => RefreshHistory();
            btnRow.Controls.Add(btnReload);

            var btnReuse = new Button { Text = "填入输入框", Size = new Size(110, 32), Margin = new Padding(0, 0, 12, 0) };
            btnReuse.Click += (s, e) => ReuseHistoryItem();
            btnRow.Controls.Add(btnReuse);

            btnRow.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "双击某条可填回输入框重发",
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
                if (one.Length > 40) one = one.Substring(0, 40) + "…";
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

            using (var dlg = new Form())
            {
                dlg.Text = "发送内容预览";
                dlg.Size = new Size(680, 480);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Font = Font;
                AppIcon.Apply(dlg);

                var tb = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 9f),
                    Text = PrettyJson(payload)
                };
                dlg.Controls.Add(tb);

                var btn = new Button
                {
                    Text = "关闭",
                    Dock = DockStyle.Bottom,
                    Height = 36,
                    DialogResult = DialogResult.OK
                };
                dlg.Controls.Add(btn);
                dlg.AcceptButton = btn;

                dlg.ShowDialog(this);
            }
        }

        private static string PrettyJson(string json)
        {
            try
            {
                var obj = Json.Parse(json);
                return Json.SerializeIndented(obj);
            }
            catch
            {
                return json;
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

        private void ReuseHistoryItem()
        {
            if (_lvHistory.SelectedItems.Count == 0) return;
            var entry = _lvHistory.SelectedItems[0].Tag as HistoryEntry;
            if (entry == null) return;

            _box.Text = entry.Text ?? "";

            // 如果历史里的群还在，顺手切过去
            if (!string.IsNullOrEmpty(entry.Group) && _cboGroup.Items.Contains(entry.Group))
                _cboGroup.SelectedItem = entry.Group;

            _chkAll.Checked = entry.AtAll;
            _box.Focus();
            _box.SelectionStart = _box.TextLength;
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