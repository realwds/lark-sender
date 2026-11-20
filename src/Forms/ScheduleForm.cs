using System;
using System.Drawing;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Forms
{
    /// <summary>定时发送对话框：选类型和时间，直接创建系统计划任务</summary>
    public class ScheduleForm : Form
    {
        private readonly string _groupName;
        private readonly string _message;
        private readonly bool _atAll;

        private RadioButton _rbOnce;
        private RadioButton _rbDaily;
        private RadioButton _rbWeekdays;
        private DateTimePicker _dtp;
        private Label _lblHint;

        /// <summary>创建成功的计划；取消或失败时为 null</summary>
        public ScheduleConfig CreatedJob { get; private set; }

        public ScheduleForm(string groupName, string message, bool atAll)
        {
            _groupName = groupName;
            _message = message;
            _atAll = atAll;
            InitUi();
            UpdateHint();
        }

        private void InitUi()
        {
            Text = "定时发送";
            Size = new Size(470, 380);
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
                Padding = new Padding(18)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 目标群
            var lblGroup = new Label
            {
                Text = "发送到：" + _groupName,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 12)
            };
            root.Controls.Add(lblGroup, 0, 0);
            root.SetColumnSpan(lblGroup, 2);

            // 类型
            root.Controls.Add(new Label
            {
                Text = "类型",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 10, 0)
            }, 0, 1);

            var typeRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            _rbOnce = new RadioButton { Text = "一次性", AutoSize = true, Checked = true, Margin = new Padding(0, 4, 14, 0) };
            _rbDaily = new RadioButton { Text = "每天", AutoSize = true, Margin = new Padding(0, 4, 14, 0) };
            _rbWeekdays = new RadioButton { Text = "工作日（周一至周五）", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };

            _rbOnce.CheckedChanged += (s, e) => UpdateHint();
            _rbDaily.CheckedChanged += (s, e) => UpdateHint();
            _rbWeekdays.CheckedChanged += (s, e) => UpdateHint();

            typeRow.Controls.Add(_rbOnce);
            typeRow.Controls.Add(_rbDaily);
            typeRow.Controls.Add(_rbWeekdays);
            root.Controls.Add(typeRow, 1, 1);

            // 时间
            root.Controls.Add(new Label
            {
                Text = "时间",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 10, 10, 0)
            }, 0, 2);

            _dtp = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0),
                Value = NextRoundTime()
            };
            root.Controls.Add(_dtp, 1, 2);

            // 说明
            _lblHint = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 14, 0, 0)
            };
            root.Controls.Add(_lblHint, 0, 4);
            root.SetColumnSpan(_lblHint, 2);

            // 按钮
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 10, 0, 0)
            };

            var btnOk = new Button { Text = "创建任务", Size = new Size(100, 32), Margin = new Padding(8, 0, 0, 0) };
            btnOk.Click += (s, e) => TryCreate();
            btnRow.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "取消", Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            btnRow.Controls.Add(btnCancel);

            root.Controls.Add(btnRow, 0, 5);
            root.SetColumnSpan(btnRow, 2);

            Controls.Add(root);
            CancelButton = btnCancel;
        }

        /// <summary>默认时间：下一个整 5 分钟</summary>
        private static DateTime NextRoundTime()
        {
            var now = DateTime.Now.AddMinutes(5);
            int add = (5 - now.Minute % 5) % 5;
            return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(add);
        }

        private ScheduleKind SelectedKind
        {
            get
            {
                if (_rbDaily.Checked) return ScheduleKind.Daily;
                if (_rbWeekdays.Checked) return ScheduleKind.Weekdays;
                return ScheduleKind.Once;
            }
        }

        private void UpdateHint()
        {
            switch (SelectedKind)
            {
                case ScheduleKind.Daily:
                    _lblHint.Text = "每天在所选时间发送。日期部分忽略，只取时分。\r\n任务长期保留，可在「定时任务」页签删除。";
                    break;
                case ScheduleKind.Weekdays:
                    _lblHint.Text = "每周一至周五在所选时间发送，周末不发送。\r\n日期部分忽略，只取时分。";
                    break;
                default:
                    _lblHint.Text = "在所选日期时间发送一次，发完自动移除。\r\n所选时间必须晚于当前时间。";
                    break;
            }
        }

        private void TryCreate()
        {
            var kind = SelectedKind;
            var when = _dtp.Value;

            // 一次性任务不允许选过去的时间
            if (kind == ScheduleKind.Once && when <= DateTime.Now)
            {
                MessageBox.Show("所选时间已经过去了，请选一个将来的时间。",
                    "时间无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 工作日任务的起始日不能是周末
            if (kind == ScheduleKind.Weekdays &&
                (when.DayOfWeek == DayOfWeek.Saturday || when.DayOfWeek == DayOfWeek.Sunday))
            {
                MessageBox.Show("「工作日」任务的起始日期不能是周末，请改到周一至周五。",
                    "时间无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string jobId = DateTime.Now.ToString("yyyyMMddHHmmss") + "_"
                         + new Random(Guid.NewGuid().GetHashCode()).Next(100, 1000);

            Cursor = Cursors.WaitCursor;
            try
            {
                var mgr = new ScheduleManager();
                mgr.Register(jobId, when, kind);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "创建定时任务失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            CreatedJob = new ScheduleConfig
            {
                Id = jobId,
                GroupName = _groupName,
                Message = _message,
                AtAll = _atAll,
                Kind = kind,
                RunAt = when.ToString("yyyy-MM-dd HH:mm"),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}