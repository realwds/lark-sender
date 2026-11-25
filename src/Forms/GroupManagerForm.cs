using System;
using System.Drawing;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>群管理：增删改群配置</summary>
    public class GroupManagerForm : Form
    {
        private readonly AppConfig _cfg;
        private ListView _lv;

        /// <summary>是否发生过修改（由调用方决定是否落盘）</summary>
        public bool Changed { get; private set; }

        /// <summary>
        /// 数据变化回调（嵌入页签时由主窗口挂接：保存配置 + 刷新发消息页的群下拉）。
        /// 嵌入模式下即时生效，不再依赖对话框关闭时机。
        /// </summary>
        public Action OnDataChanged;

        public GroupManagerForm(AppConfig cfg)
        {
            _cfg = cfg;
            Changed = false;
            InitUi();
            RefreshList();
        }

        private void InitUi()
        {
            Text = "管理群";
            Size = new Size(760, 520);
            MinimumSize = new Size(620, 420);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AppIcon.Apply(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            _lv.Columns.Add("名称", 160);
            _lv.Columns.Add("webhook", 230);
            _lv.Columns.Add("默认@all", 75);
            _lv.Columns.Add("备注", 220);
            _lv.DoubleClick += (s, e) => EditSelected();

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var btnAdd = new Button { Text = "新增", Size = new Size(90, 32), Margin = new Padding(0, 0, 8, 0) };
            btnAdd.Click += (s, e) => AddGroup();
            btnRow.Controls.Add(btnAdd);

            var btnEdit = new Button { Text = "编辑", Size = new Size(90, 32), Margin = new Padding(0, 0, 8, 0) };
            btnEdit.Click += (s, e) => EditSelected();
            btnRow.Controls.Add(btnEdit);

            var btnDel = new Button { Text = "删除", Size = new Size(90, 32), Margin = new Padding(0, 0, 16, 0) };
            btnDel.Click += (s, e) => DeleteSelected();
            btnRow.Controls.Add(btnDel);

            var btnTest = new Button { Text = "测试连通", Size = new Size(100, 32), Margin = new Padding(0, 0, 16, 0) };
            btnTest.Click += (s, e) => TestSelected();
            btnRow.Controls.Add(btnTest);

            btnRow.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "双击可编辑 · webhook 留空则无法发送",
                Margin = new Padding(0, 8, 0, 0)
            });

            root.Controls.Add(_lv, 0, 0);
            root.Controls.Add(btnRow, 0, 1);
            Controls.Add(root);
        }

        private void RefreshList()
        {
            _lv.BeginUpdate();
            try
            {
                _lv.Items.Clear();
                foreach (var g in _cfg.Groups)
                {
                    var it = new ListViewItem(g.Name ?? "");
                    it.SubItems.Add(Notifier.MaskWebhook(g.Webhook));
                    it.SubItems.Add(g.AtAll ? "是" : "否");
                    it.SubItems.Add(g.Note ?? "");
                    if (!g.HasWebhook) it.ForeColor = Color.Firebrick;
                    it.Tag = g.Name;
                    _lv.Items.Add(it);
                }
            }
            finally
            {
                _lv.EndUpdate();
            }
        }

        private GroupConfig SelectedGroup()
        {
            if (_lv.SelectedItems.Count == 0) return null;
            return ConfigStore.FindGroup(_cfg, _lv.SelectedItems[0].Tag as string);
        }

        private void AddGroup()
        {
            using (var dlg = new GroupEditForm(null, _cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                {
                    _cfg.Groups.Add(dlg.Result);
                    Changed = true;
                    NotifyChanged();
                    RefreshList();
                }
            }
        }

        private void EditSelected()
        {
            var g = SelectedGroup();
            if (g == null) return;

            using (var dlg = new GroupEditForm(g, _cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                {
                    string oldName = g.Name;
                    int idx = _cfg.Groups.IndexOf(g);
                    if (idx >= 0) _cfg.Groups[idx] = dlg.Result;

                    // 群改名时同步更新引用它的定时任务，避免任务失效
                    if (oldName != dlg.Result.Name)
                    {
                        foreach (var s in _cfg.Schedules)
                        {
                            if (s.GroupName == oldName) s.GroupName = dlg.Result.Name;
                        }
                    }

                    Changed = true;
                    NotifyChanged();
                    RefreshList();
                }
            }
        }

        private void DeleteSelected()
        {
            var g = SelectedGroup();
            if (g == null) return;

            int used = 0;
            foreach (var s in _cfg.Schedules)
                if (s.GroupName == g.Name) used++;

            string msg = "确定删除群「" + g.Name + "」？";
            if (used > 0)
                msg += "\n\n注意：该群还有 " + used + " 个定时任务，删除后这些任务会发送失败。";

            if (MessageBox.Show(msg, "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _cfg.Groups.Remove(g);
            Changed = true;
            NotifyChanged();
            RefreshList();
        }

        /// <summary>触发数据变更回调（嵌入页签时主窗口即时保存并刷新 UI）</summary>
        private void NotifyChanged()
        {
            var cb = OnDataChanged;
            if (cb != null) { try { cb(); } catch { } }
        }

        /// <summary>发一条测试消息，验证 webhook / 签名是否正确</summary>
        private void TestSelected()
        {
            var g = SelectedGroup();
            if (g == null)
            {
                MessageBox.Show("请先选中一个群。", "未选中",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                var notifier = new Notifier { MaxAttempts = 1 };
                var result = notifier.Send(g,
                    "这是一条来自 Lark Sender 的连通性测试消息，收到请忽略。",
                    false, MessageKind.Text);

                if (result.Ok)
                    MessageBox.Show("群「" + g.Name + "」连通正常，已发送测试消息。",
                        "测试成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show("测试失败：\n\n" + result.Error,
                        "测试失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}