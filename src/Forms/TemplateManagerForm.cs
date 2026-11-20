using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>
    /// 常用语管理：增删改，支持排序。
    ///
    /// 之前常用语只能加不能删，这里补上完整的管理能力。
    /// 所有修改先在副本上做，点「保存」才写回配置，关闭时未保存则丢弃。
    /// </summary>
    public class TemplateManagerForm : Form
    {
        private readonly List<string> _items;

        private ListBox _lb;
        private TextBox _txt;

        /// <summary>用户保存后的结果；取消时为 null</summary>
        public List<string> Result { get; private set; }

        public TemplateManagerForm(List<string> templates)
        {
            _items = new List<string>(templates ?? new List<string>());
            InitUi();
            RefreshList();
        }

        private void InitUi()
        {
            Text = "管理常用语";
            Size = new Size(680, 520);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AppIcon.Apply(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 左侧：列表
            _lb = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                Margin = new Padding(0, 0, 8, 8)
            };
            _lb.SelectedIndexChanged += (s, e) => LoadSelectedToEditor();
            root.Controls.Add(_lb, 0, 0);

            // 右侧：编辑区
            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            right.Controls.Add(new Label
            {
                Text = "内容（支持多行）",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 4)
            }, 0, 0);

            _txt = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Dock = DockStyle.Fill
            };
            right.Controls.Add(_txt, 0, 1);

            root.Controls.Add(right, 1, 0);

            // 底部按钮区
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var btnAdd = new Button { Text = "新增", Size = new Size(80, 32), Margin = new Padding(0, 0, 6, 0) };
            btnAdd.Click += (s, e) => AddNew();
            bottom.Controls.Add(btnAdd);

            var btnUpdate = new Button { Text = "保存修改", Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0) };
            btnUpdate.Click += (s, e) => UpdateSelected();
            bottom.Controls.Add(btnUpdate);

            var btnDel = new Button { Text = "删除", Size = new Size(80, 32), Margin = new Padding(0, 0, 6, 0) };
            btnDel.Click += (s, e) => DeleteSelected();
            bottom.Controls.Add(btnDel);

            var btnUp = new Button { Text = "↑", Size = new Size(40, 32), Margin = new Padding(0, 0, 6, 0) };
            btnUp.Click += (s, e) => MoveSelected(-1);
            bottom.Controls.Add(btnUp);

            var btnDown = new Button { Text = "↓", Size = new Size(40, 32), Margin = new Padding(0, 0, 20, 0) };
            btnDown.Click += (s, e) => MoveSelected(1);
            bottom.Controls.Add(btnDown);

            var btnOk = new Button { Text = "保存并关闭", Size = new Size(110, 32), Margin = new Padding(0, 0, 6, 0) };
            btnOk.Click += (s, e) => Commit();
            bottom.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "取消", Size = new Size(80, 32), DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(btnCancel);

            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);

            Controls.Add(root);
            CancelButton = btnCancel;
        }

        private void RefreshList(int selectIndex = -1)
        {
            _lb.BeginUpdate();
            try
            {
                _lb.Items.Clear();
                foreach (var t in _items)
                {
                    var one = (t ?? "").Split('\n')[0].Trim();
                    if (one.Length > 30) one = one.Substring(0, 30) + "…";
                    _lb.Items.Add(one.Length > 0 ? one : "(空)");
                }
            }
            finally
            {
                _lb.EndUpdate();
            }

            if (selectIndex >= 0 && selectIndex < _lb.Items.Count)
            {
                _lb.SelectedIndex = selectIndex;
            }
            else if (_lb.Items.Count > 0 && _lb.SelectedIndex < 0)
            {
                // 不自动选中，避免误改
            }
        }

        private void LoadSelectedToEditor()
        {
            int i = _lb.SelectedIndex;
            if (i < 0 || i >= _items.Count)
            {
                _txt.Text = "";
                return;
            }
            _txt.Text = _items[i] ?? "";
        }

        private void AddNew()
        {
            var text = _txt.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("请先在右侧输入内容，再点「新增」。",
                    "内容为空", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txt.Focus();
                return;
            }
            if (_items.Contains(text))
            {
                MessageBox.Show("这条常用语已经存在了。",
                    "重复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _items.Add(text);
            RefreshList(_items.Count - 1);
        }

        private void UpdateSelected()
        {
            int i = _lb.SelectedIndex;
            if (i < 0)
            {
                MessageBox.Show("请先在左侧选中一条要修改的常用语。",
                    "未选中", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var text = _txt.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("内容不能为空。若要删除请点「删除」。",
                    "内容为空", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 修改后不能和其他条目重复
            for (int k = 0; k < _items.Count; k++)
            {
                if (k == i) continue;
                if (_items[k] == text)
                {
                    MessageBox.Show("已存在相同的常用语。",
                        "重复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            _items[i] = text;
            RefreshList(i);
        }

        private void DeleteSelected()
        {
            int i = _lb.SelectedIndex;
            if (i < 0)
            {
                MessageBox.Show("请先在左侧选中一条要删除的常用语。",
                    "未选中", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var preview = (_items[i] ?? "").Split('\n')[0].Trim();
            if (preview.Length > 50) preview = preview.Substring(0, 50) + "…";

            if (MessageBox.Show("确定删除这条常用语？\n\n" + preview, "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _items.RemoveAt(i);
            _txt.Text = "";

            // 删完后选中相邻条目，方便连续删除
            int next = i < _items.Count ? i : _items.Count - 1;
            RefreshList(next);
            if (next < 0) _txt.Text = "";
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
            Result = new List<string>(_items);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}