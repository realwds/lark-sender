using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LarkSender.Models;

namespace LarkSender.Core
{
    /// <summary>
    /// Windows 计划任务管理。
    ///
    /// 关键约束（都是实测踩出来的）：
    ///   1) schtasks 不能直接执行 powershell 命令（安全软件会拦），但可以直接执行 exe
    ///   2) /TR 只接受一个参数，命令带空格时必须双层引号
    ///   3) /SD 日期格式必须是 yyyy/MM/dd
    ///   4) 不要让程序删除正在被执行的 .cmd（会导致 cmd 返回非 0，任务被判失败）
    ///      → 所以这里直接用 exe，不用 .cmd 包装
    /// </summary>
    public class ScheduleManager
    {
        private readonly string _exePath;

        public ScheduleManager() : this(Path.Combine(Paths.BaseDir, "LarkSender.exe")) { }

        public ScheduleManager(string exePath)
        {
            _exePath = exePath;
        }

        // ==================== 注册 ====================

        /// <summary>
        /// 创建 Windows 计划任务。
        /// </summary>
        /// <param name="jobId">任务 ID，会拼成 LarkSender_&lt;id&gt;</param>
        /// <param name="runAt">首次触发时间</param>
        /// <param name="kind">类型：一次性 / 每天 / 工作日</param>
        public void Register(string jobId, DateTime runAt, ScheduleKind kind)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                throw new ArgumentException("jobId 不能为空", "jobId");
            if (runAt <= DateTime.Now.AddMinutes(-1))
                throw new ArgumentException("触发时间必须是将来的时间", "runAt");

            string taskName = Paths.TaskPrefix + jobId;

            // 内层给 exe 加引号（路径可能含空格），外层给整个 action 加引号
            string action = "\"" + _exePath + "\" /job " + jobId;

            var sb = new StringBuilder();
            sb.Append("/create");
            sb.Append(" /TN ").Append(Quote(taskName));
            sb.Append(" /TR ").Append(Quote(action));

            switch (kind)
            {
                case ScheduleKind.Weekdays:
                    // WEEKLY + MON-FRI
                    sb.Append(" /SC WEEKLY");
                    sb.Append(" /D MON,TUE,WED,THU,FRI");
                    sb.Append(" /ST ").Append(runAt.ToString("HH:mm"));
                    break;

                case ScheduleKind.Daily:
                    sb.Append(" /SC DAILY");
                    sb.Append(" /ST ").Append(runAt.ToString("HH:mm"));
                    break;

                default: // Once
                    sb.Append(" /SC ONCE");
                    sb.Append(" /ST ").Append(runAt.ToString("HH:mm"));
                    // /SD 必须是 yyyy/MM/dd
                    sb.Append(" /SD ").Append(runAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
                    break;
            }
            sb.Append(" /F");

            RunSchtasks(sb.ToString(), true);
        }

        // ==================== 删除 ====================

        /// <summary>删除计划任务；任务不存在视为成功</summary>
        public void Delete(string jobId)
        {
            string taskName = Paths.TaskPrefix + jobId;
            try
            {
                RunSchtasks("/delete /TN " + Quote(taskName) + " /F", false);
            }
            catch
            {
                // 任务本来就不存在，忽略
            }
        }

        // ==================== 查询 ====================

        /// <summary>任务是否存在于系统中</summary>
        public bool Exists(string jobId)
        {
            string taskName = Paths.TaskPrefix + jobId;
            try
            {
                RunSchtasks("/query /TN " + Quote(taskName) + " /FO LIST", false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>下次触发时间文本；任务不存在返回 null</summary>
        public string NextRunText(string jobId)
        {
            string taskName = Paths.TaskPrefix + jobId;
            string output;
            try
            {
                output = RunSchtasks("/query /TN " + Quote(taskName) + " /FO LIST", false);
            }
            catch
            {
                return null;
            }

            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Next Run Time:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = t.Substring("Next Run Time:".Length).Trim();
                    if (v.Length == 0 || v == "N/A") return null;
                    return v;
                }
            }
            return null;
        }

        // ==================== 底层执行 ====================

        /// <summary>
        /// 执行 schtasks 命令。throwOnError=true 时非零退出码抛异常。
        /// </summary>
        private string RunSchtasks(string arguments, bool throwOnError)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // schtasks 输出是本地代码页（中文系统为 GBK），不能按 UTF8 读
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            using (var p = Process.Start(psi))
            {
                // 先读 stderr 再读 stdout 可能死锁，用异步读避开
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit(15000);

                string stdout = stdoutTask.Result ?? "";
                string stderr = stderrTask.Result ?? "";

                if (throwOnError && p.ExitCode != 0)
                {
                    string msg = (stderr + "\n" + stdout).Trim();
                    throw new ScheduleException(
                        "创建计划任务失败（退出码 " + p.ExitCode + "）。\n"
                        + "可能原因：没有权限、任务名已存在且被锁定、或触发时间不合法。\n\n"
                        + Truncate(msg, 500));
                }
                return stdout;
            }
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ==================== 校验 ====================

        /// <summary>校验触发时间是否合法，返回错误信息（合法时返回 null）</summary>
        public static string ValidateRunAt(DateTime runAt, ScheduleKind kind)
        {
            if (kind == ScheduleKind.Once && runAt <= DateTime.Now)
                return "一次性任务的触发时间必须是将来的时间。";

            if (kind == ScheduleKind.Weekdays)
            {
                var dow = runAt.DayOfWeek;
                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                    return "「工作日」任务的起始日期不能是周末，请改到周一至周五。";
            }
            return null;
        }
    }

    /// <summary>计划任务相关异常</summary>
    public class ScheduleException : Exception
    {
        public ScheduleException(string message) : base(message) { }
    }
}