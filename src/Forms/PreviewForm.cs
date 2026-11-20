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

        private RichTextBox _rich;
        private TextBox _json;
        private SplitContainer _split;

        public PreviewForm(string text, bool atAll, bool isPost, string payload)
        {
            _text = text ?? "";
            _atAll = atAll;
            _isPost = isPost;

            InitUi(payload);
            RenderEffect();
        }

        private void InitUi(string payload)
        {
            Text = "发送预览";
            Size = new Size(1080, 660);
            MinimumSize = new Size(820, 480);
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
                    ? "左侧按飞书富文本样式渲染；右侧是实际发送的 JSON"
                    : "左侧是纯文本效果；右侧是实际发送的 JSON"
            });

            // 底部按钮
            var btnBar = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            var btnCopyJson = new Button { Text = "复制 JSON", Size = new Size(100, 32), Margin = new Padding(0, 0, 8, 0) };
            btnCopyJson.Click += (s, e) => CopyToClipboard(_json.Text, "JSON");
            btnRow.Controls.Add(btnCopyJson);

            var btnCopyText = new Button { Text = "复制原文", Size = new Size(90, 32), Margin = new Padding(0, 0, 8, 0) };
            btnCopyText.Click += (s, e) => CopyToClipboard(_text, "原文");
            btnRow.Controls.Add(btnCopyText);

            var btnClose = new Button { Text = "关闭", Size = new Size(80, 32), DialogResult = DialogResult.OK };
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
                    int target = (int)(_split.Width * 0.45);
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

        /// <summary>在左侧按飞书 post 语义渲染效果</summary>
        private void RenderEffect()
        {
            _rich.Clear();
            _rich.SuspendLayout();

            try
            {
                // @所有人 提示条
                if (_atAll)
                {
                    AppendStyled("所有人", Color.FromArgb(37, 99, 235), new Font(_rich.Font, FontStyle.Bold), false);
                    AppendStyled("  ", Color.Black, _rich.Font, false);
                }

                var lines = _text.Split('\n');
                for (int idx = 0; idx < lines.Length; idx++)
                {
                    var line = lines[idx].TrimEnd('\r');
                    if (idx > 0) AppendStyled("\n", Color.Black, _rich.Font, false);

                    if (!_isPost)
                    {
                        // 纯文本模式：原样显示，不做任何渲染
                        AppendStyled(line, Color.Black, _rich.Font, false);
                        continue;
                    }

                    RenderPostLine(line);
                }
            }
            finally
            {
                _rich.ResumeLayout();
            }
        }

        /// <summary>渲染一行 post 语法的内容</summary>
        private void RenderPostLine(string line)
        {
            if (line.Length == 0) return;

            // `# 标题` → 加粗放大
            if (line.StartsWith("# "))
            {
                var f = new Font(_rich.Font.FontFamily, _rich.Font.Size + 2.5f, FontStyle.Bold);
                AppendStyled(line.Substring(2).Trim(), Color.FromArgb(17, 24, 39), f, false);
                return;
            }

            // `- 列表` → 圆点 + 缩进
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                AppendStyled("  •  ", Color.FromArgb(107, 114, 128), _rich.Font, false);
                RenderInline(line.Substring(2));
                return;
            }

            RenderInline(line);
        }

        /// <summary>渲染行内 `**加粗**`</summary>
        private void RenderInline(string line)
        {
            int i = 0;
            var buf = new StringBuilder();
            bool sawBold = false;

            while (i < line.Length)
            {
                if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '*')
                {
                    int end = line.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        if (buf.Length > 0)
                        {
                            AppendStyled(buf.ToString(), Color.Black, _rich.Font, false);
                            buf.Length = 0;
                        }
                        var boldFont = new Font(_rich.Font, FontStyle.Bold);
                        AppendStyled(line.Substring(i + 2, end - i - 2), Color.Black, boldFont, false);
                        sawBold = true;
                        i = end + 2;
                        continue;
                    }
                }
                buf.Append(line[i]);
                i++;
            }

            if (buf.Length > 0)
            {
                AppendStyled(buf.ToString(), Color.Black, _rich.Font, false);
            }

            // 未闭合的 ** 给个提示，避免用户以为没生效
            if (!sawBold && line.Contains("**"))
            {
                // 什么都没解析出来，但用户写了 **，说明可能没闭合
                // 不做额外处理，原文已经显示出来了
            }
        }

        private void AppendStyled(string text, Color color, Font font, bool underline)
        {
            int start = _rich.TextLength;
            _rich.AppendText(text);
            _rich.Select(start, text.Length);
            _rich.SelectionColor = color;
            _rich.SelectionFont = font;
            _rich.SelectionLength = 0;
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
                return Json.SerializeIndented(obj);
            }
            catch
            {
                return json;
            }
        }
    }
}