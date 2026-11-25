using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>
    /// 消息预览窗：左边看「发出去长什么样」，右边看实际 JSON。
    ///
    /// 之前只显示 JSON，用户看不懂 `{"tag":"text","style":["bold"]}` 意味着什么。
    /// 现在用 RichTextBox 按飞书 post 的样式渲染：标题加粗放大、列表加点、加粗文字加粗。
    /// </summary>
    public class PreviewForm : Form
    {
        private readonly string _text;
        private readonly bool _atAll;
        private readonly bool _isPost;
        private readonly string _cardTitle;
        private readonly CardBuilder.HeaderColor _cardColor;
        private readonly bool _autoColor;

        private RichTextBox _rich;
        private TextBox _json;
        private SplitContainer _split;

        public PreviewForm(string text, bool atAll, bool isPost, string payload)
            : this(text, atAll, isPost, payload, null, CardBuilder.HeaderColor.Blue, true)
        {
        }

        public PreviewForm(string text, bool atAll, bool isPost, string payload,
            string cardTitle, CardBuilder.HeaderColor color, bool autoColor)
        {
            _text = text ?? "";
            _atAll = atAll;
            _isPost = isPost;
            _cardTitle = cardTitle;
            _cardColor = color;
            _autoColor = autoColor;

            InitUi(payload);
            RenderEffect();
        }

        private void InitUi(string payload)
        {
            Text = "发送预览";
            Size = new Size(900, 560);
            MinimumSize = new Size(700, 420);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AppIcon.Apply(this);

            // 顶部说明
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(12, 8, 12, 0),
                BackColor = Color.FromArgb(246, 248, 250)
            };
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = _isPost
                    ? "左侧按飞书卡片样式渲染；右侧是实际发送的 JSON"
                    : "左侧是纯文本效果；右侧是实际发送的 JSON"
            });

            // 底部按钮：统一高度，靠右垂直居中
            var btnBar = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 11, 12, 11) };
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0)
            };

            var btnCopyJson = new Button { Text = "复制 JSON", Size = new Size(100, 30), Margin = new Padding(0, 0, 8, 0) };
            btnCopyJson.Click += (s, e) => CopyToClipboard(_json.Text, "JSON");
            btnRow.Controls.Add(btnCopyJson);

            var btnCopyText = new Button { Text = "复制原文", Size = new Size(90, 30), Margin = new Padding(0, 0, 8, 0) };
            btnCopyText.Click += (s, e) => CopyToClipboard(_text, "原文");
            btnRow.Controls.Add(btnCopyText);

            var btnClose = new Button { Text = "关闭", Size = new Size(80, 30), Margin = new Padding(0), DialogResult = DialogResult.OK };
            btnRow.Controls.Add(btnClose);
            btnBar.Controls.Add(btnRow);

            // 左右分栏
            // 同 MainForm：不在创建时硬设 SplitterDistance，避免宽度未定时抛异常。
            // 改为在 Shown 事件里按实际宽度分配。
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                Panel1MinSize = 100,
                Panel2MinSize = 100
            };
            Shown += (s, e) =>
            {
                try
                {
                    int target = (int)(_split.Width * 0.42);
                    int max = _split.Width - _split.Panel2MinSize - _split.SplitterWidth;
                    if (target < _split.Panel1MinSize) target = _split.Panel1MinSize;
                    if (max > 0 && target > max) target = max;
                    if (max > 0 && target >= _split.Panel1MinSize) _split.SplitterDistance = target;
                }
                catch { /* 极端尺寸下分不了就算了，用默认值 */ }
            };

            // 左：效果渲染
            var leftBox = new GroupBox
            {
                Text = "效果预览",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            _rich = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f),
                DetectUrls = false
            };
            leftBox.Controls.Add(_rich);

            // 右：JSON
            var rightBox = new GroupBox
            {
                Text = "实际发送的 JSON",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            _json = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9f),
                Text = PrettyJson(payload)
            };
            rightBox.Controls.Add(_json);

            _split.Panel1.Controls.Add(leftBox);
            _split.Panel2.Controls.Add(rightBox);

            Controls.Add(_split);
            Controls.Add(btnBar);
            Controls.Add(header);

            AcceptButton = btnClose;
        }

        /// <summary>在左侧按飞书卡片/纯文本的实际样式渲染</summary>
        private void RenderEffect()
        {
            CardRenderer.Render(_rich, _text, _atAll, _isPost, _cardTitle, _cardColor, _autoColor);
        }

        private void CopyToClipboard(string content, string what)
        {
            if (string.IsNullOrEmpty(content))
            {
                MessageBox.Show("内容为空。", "无法复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(content);
                    MessageBox.Show("已复制" + what + "到剪贴板。", "复制成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(80);
                }
            }
            MessageBox.Show("复制失败，剪贴板被占用。", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string PrettyJson(string json)
        {
            try
            {
                var obj = Json.Parse(json);
                // TextBox（Win32 EDIT）只把 CRLF 当换行，单独的 \n 会被连在一行里
                return Json.SerializeIndented(obj).Replace("\n", "\r\n");
            }
            catch
            {
                return json;
            }
        }
    }
}