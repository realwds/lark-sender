using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>
    /// 常用语管理：增删改、排序。
    ///
    /// 现在每条常用语保存完整形态（正文 / 卡片标题 / 颜色 / 是否卡片 / 是否@所有人），
    /// 而不只是正文——否则富文本存进去再取出来就丢格式。
    /// </summary>
    public class TemplateManagerForm : Form
    {
        private readonly List<TemplateConfig> _items;

        private ListBox _lb;
        private TextBox _txtBody;
        private TextBox _txtTitle;
        private ComboBox _cboColor;
        private CheckBox _chkCard;
        private CheckBox _chkAtAll;

        /// <summary>保存后的结果；取消时为 null</summary>
        public List<TemplateConfig> Result { get; private set; }

        public TemplateManagerForm(List<TemplateConfig> templates)
        {
            _items = new List<TemplateConfig>();
            if (templates != null)
            {
                // 深拷贝，避免直接改到原配置
                foreach (var t in templates)
                {
                    _items.Add(new TemplateConfig
                    {
                        Name = t.Name,
                        Body = t.Body,
                        Title = t.Title,
                        Color = t.Color,
                        IsCard = t.IsCard,
                        AtAll = t.AtAll
                    });
                }
            }

            InitUi();
            RefreshList();
        }

        private void InitUi()
        {
            Text = "管理常用语";
            Size = new Size(760, 560);
            MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyForm(this);
            AppIcon.Apply(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(14),
                BackColor = Theme.Bg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 左侧列表
            var leftCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 10) };
            _lb = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                Font = Theme.FontBody,
                Margin = new Padding(0)
            };
            var lbHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            lbHost.Controls.Add(_lb);
            leftCard.Controls.Add(lbHost);
            _lb.SelectedIndexChanged += (s, e) => LoadSelected();
            root.Controls.Add(leftCard, 0, 0);

            // 右侧编辑区
            var rightCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(14) };

            var edit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = Theme.Surface
            };
            edit.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            edit.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            edit.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            edit.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            edit.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            edit.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // 卡片开关
            var optRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            _chkCard = new CheckBox { Text = "卡片消息", AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 4, 16, 0) };
            _chkCard.CheckedChanged += (s, e) => UpdateTitleEnabled();
            optRow.Controls.Add(_chkCard);
            _chkAtAll = new CheckBox { Text = "所有人", AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 4, 0, 0) };
            optRow.Controls.Add(_chkAtAll);
            edit.Controls.Add(optRow, 1, 0);

            // 标题
            edit.Controls.Add(MakeLabel("标题"), 0, 1);
            _txtTitle = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 8) };
            edit.Controls.Add(_txtTitle, 1, 1);

            // 颜色
            edit.Controls.Add(MakeLabel("颜色"), 0, 2);
            _cboColor = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 100,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 4, 0, 8),
                Anchor = AnchorStyles.Left
            };
            _cboColor.Items.AddRange(new object[] { "自动", "蓝", "绿", "红", "橙", "灰", "紫", "青" });
            _cboColor.SelectedIndex = 0;
            edit.Controls.Add(_cboColor, 1, 2);

            // 正文
            edit.Controls.Add(MakeLabel("内容"), 0, 3);
            _txtBody = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.FontBody,
                Margin = new Padding(0, 4, 0, 0)
            };
            edit.Controls.Add(_txtBody, 1, 4);

            rightCard.Controls.Add(edit);
            root.Controls.Add(rightCard, 1, 0);

            // 底部按钮
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                BackColor = Theme.Bg
            };

            var btnAdd = MakeBtn("新增", 80);
            btnAdd.Click += (s, e) => AddNew();
            bottom.Controls.Add(btnAdd);

            var btnUpdate = MakeBtn("保存修改", 92);
            btnUpdate.Click += (s, e) => UpdateSelected();
            bottom.Controls.Add(btnUpdate);

            var btnDel = MakeBtn("删除", 80);
            btnDel.Click += (s, e) => DeleteSelected();
            bottom.Controls.Add(btnDel);

            var btnUp = MakeBtn("↑", 40);
            btnUp.Click += (s, e) => MoveSelected(-1);
            bottom.Controls.Add(btnUp);

            var btnDown = MakeBtn("↓", 40);
            btnDown.Click += (s, e) => MoveSelected(1);
            bottom.Controls.Add(btnDown);

            var spacer = new Panel { Width = 20, Height = 1 };
            bottom.Controls.Add(spacer);

            var btnOk = new Button { Text = "保存并关闭", Size = new Size(110, 34), Margin = new Padding(0, 0, 8, 0) };
            Theme.StylePrimary(btnOk);
            btnOk.Click += (s, e) => Commit();
            bottom.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "取消", Size = new Size(80, 34), DialogResult = DialogResult.Cancel };
            Theme.StyleSecondary(btnCancel);
            bottom.Controls.Add(btnCancel);

            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);

            Controls.Add(root);
            CancelButton = btnCancel;
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 7, 10, 0)
            };
        }

        private static Button MakeBtn(string text, int width)
        {
            var b = new Button { Text = text, Size = new Size(width, 32), Margin = new Padding(0, 0, 6, 0) };
            Theme.StyleSecondary(b);
            return b;
        }

        private void UpdateTitleEnabled()
        {
            bool on = _chkCard.Checked;
            _txtTitle.Enabled = on;
            _cboColor.Enabled = on;
            _txtTitle.BackColor = on ? Theme.Surface : Theme.BorderSoft;
            _cboColor.BackColor = on ? Theme.Surface : Theme.BorderSoft;
        }

        private void RefreshList(int selectIndex = -1)
        {
            _lb.BeginUpdate();
            try
            {
                _lb.Items.Clear();
                foreach (var t in _items)
                {
                    var prefix = t.IsCard ? "[卡片] " : "";
                    _lb.Items.Add(prefix + t.Name);
                }
            }
            finally
            {
                _lb.EndUpdate();
            }

            if (selectIndex >= 0 && selectIndex < _lb.Items.Count)
                _lb.SelectedIndex = selectIndex;
        }

        private void LoadSelected()
        {
            int i = _lb.SelectedIndex;
            if (i < 0 || i >= _items.Count)
            {
                _txtBody.Text = "";
                _txtTitle.Text = "";
                _chkCard.Checked = false;
                _chkAtAll.Checked = false;
                _cboColor.SelectedIndex = 0;
                return;
            }

            var t = _items[i];
            _txtBody.Text = t.Body ?? "";
            _txtTitle.Text = t.Title ?? "";
            _chkCard.Checked = t.IsCard;
            _chkAtAll.Checked = t.AtAll;

            int ci = 0;
            if (!string.IsNullOrEmpty(t.Color))
            {
                for (int k = 0; k < _cboColor.Items.Count; k++)
                {
                    if (string.Equals(_cboColor.Items[k].ToString(), t.Color, StringComparison.Ordinal))
                    { ci = k; break; }
                }
            }
            _cboColor.SelectedIndex = ci;
            UpdateTitleEnabled();
        }

        private TemplateConfig ReadEditor()
        {
            var t = new TemplateConfig
            {
                Body = _txtBody.Text,
                Title = _txtTitle.Text.Trim(),
                IsCard = _chkCard.Checked,
                AtAll = _chkAtAll.Checked,
                Color = _chkCard.Checked && _cboColor.SelectedIndex > 0
                        ? _cboColor.SelectedItem.ToString() : ""
            };
            t.RefreshName();
            return t;
        }

        private void AddNew()
        {
            var t = ReadEditor();
            if (t.Body.Trim().Length == 0 && t.Title.Length == 0)
            {
                MessageBox.Show("请先填写内容或标题，再点「新增」。", "内容为空",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtBody.Focus();
                return;
            }

            _items.Add(t);
            RefreshList(_items.Count - 1);
        }

        private void UpdateSelected()
        {
            int i = _lb.SelectedIndex;
            if (i < 0)
            {
                MessageBox.Show("请先在左侧选中一条要修改的常用语。", "未选中",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var t = ReadEditor();
            if (t.Body.Trim().Length == 0 && t.Title.Length == 0)
            {
                MessageBox.Show("内容和标题都是空的。若要删除请点「删除」。", "内容为空",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _items[i] = t;
            RefreshList(i);
        }

        private void DeleteSelected()
        {
            int i = _lb.SelectedIndex;
            if (i < 0)
            {
                MessageBox.Show("请先在左侧选中一条要删除的常用语。", "未选中",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show("确定删除「" + _items[i].Name + "」？", "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _items.RemoveAt(i);
            _txtBody.Text = "";
            _txtTitle.Text = "";

            int next = i < _items.Count ? i : _items.Count - 1;
            RefreshList(next);
            if (next < 0) LoadSelected();
        }

        private void MoveSelected(int delta)
        {
            int i = _lb.SelectedIndex;
            if (i < 0) return;
            int j = i + delta;
            if (j < 0 || j >= _items.Count) return;

            var tmp = _items[i];
            _items[i] = _items[j];
            _items[j] = tmp;
            RefreshList(j);
        }

        private void Commit()
        {
            Result = new List<TemplateConfig>(_items);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}