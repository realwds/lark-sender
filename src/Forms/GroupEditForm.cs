using System;
using System.Drawing;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>新增/编辑单个群</summary>
    public class GroupEditForm : Form
    {
        private readonly GroupConfig _existing;
        private readonly AppConfig _cfg;

        private TextBox _txtName;
        private TextBox _txtWebhook;
        private TextBox _txtSecret;
        private TextBox _txtNote;
        private CheckBox _chkAtAll;

        /// <summary>用户点「保存」后的结果；取消时为 null</summary>
        public GroupConfig Result { get; private set; }

        public GroupEditForm(GroupConfig existing, AppConfig cfg)
        {
            _existing = existing;
            _cfg = cfg;
            InitUi();
            LoadValues();
        }

        private void InitUi()
        {
            Text = _existing == null ? "新增群" : "编辑群";
            Size = new Size(620, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AppIcon.Apply(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(16)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++)
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            root.Controls.Add(MakeLabel("群名称"), 0, 0);
            _txtName = MakeText();
            root.Controls.Add(_txtName, 1, 0);

            root.Controls.Add(MakeLabel("webhook"), 0, 1);
            _txtWebhook = MakeText();
            root.Controls.Add(_txtWebhook, 1, 1);

            root.Controls.Add(MakeLabel("签名密钥"), 0, 2);
            _txtSecret = MakeText();
            root.Controls.Add(_txtSecret, 1, 2);

            root.Controls.Add(MakeLabel("备注"), 0, 3);
            _txtNote = MakeText();
            root.Controls.Add(_txtNote, 1, 3);

            _chkAtAll = new CheckBox
            {
                Text = "默认艾特所有人",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            root.Controls.Add(_chkAtAll, 1, 4);

            var tip = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Text = "webhook 形如 https://open.feishu.cn/open-apis/bot/v2/hook/xxxx\r\n"
                     + "签名密钥仅在该机器人开启「签名校验」时才需要填写。\r\n"
                     + "若机器人开启了「自定义关键词」，消息正文必须包含该关键词。",
                Margin = new Padding(0, 10, 0, 0)
            };
            root.Controls.Add(tip, 0, 5);
            root.SetColumnSpan(tip, 2);

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 48,
                Padding = new Padding(0, 8, 16, 0)
            };

            var btnOk = new Button { Text = "保存", Size = new Size(90, 32), DialogResult = DialogResult.None };
            btnOk.Click += (s, e) => TrySave();
            btnRow.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "取消", Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            btnRow.Controls.Add(btnCancel);

            Controls.Add(root);
            Controls.Add(btnRow);
            CancelButton = btnCancel;
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 10, 0)
            };
        }

        private static TextBox MakeText()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 0, 0)
            };
        }

        private void LoadValues()
        {
            if (_existing == null) return;
            _txtName.Text = _existing.Name;
            _txtWebhook.Text = _existing.Webhook;
            _txtSecret.Text = _existing.Secret;
            _txtNote.Text = _existing.Note;
            _chkAtAll.Checked = _existing.AtAll;
        }

        private void TrySave()
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("群名称不能为空。", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtName.Focus();
                return;
            }

            // 名称唯一性（编辑时排除自己）
            foreach (var g in _cfg.Groups)
            {
                if (_existing != null && ReferenceEquals(g, _existing)) continue;
                if (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("已存在同名群「" + name + "」。", "名称重复",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _txtName.Focus();
                    return;
                }
            }

            string webhook = _txtWebhook.Text.Trim();
            if (webhook.Length > 0 &&
                !webhook.StartsWith("https://open.feishu.cn/open-apis/bot/v2/hook/", StringComparison.OrdinalIgnoreCase) &&
                !webhook.StartsWith("https://open.larksuite.com/open-apis/bot/v2/hook/", StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageBox.Show(
                    "webhook 看起来不是飞书/Lark 群机器人地址，仍要保存吗？\n\n"
                    + "期望以 https://open.feishu.cn/open-apis/bot/v2/hook/ 开头\n"
                    + "海外版 Lark 以 https://open.larksuite.com/ 开头",
                    "地址可能有误", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            if (webhook.Length == 0)
            {
                var r = MessageBox.Show(
                    "webhook 为空，这个群将无法发送消息。仍要保存吗？",
                    "webhook 为空", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            Result = new GroupConfig(name, webhook, _txtSecret.Text.Trim(),
                _chkAtAll.Checked, _txtNote.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}