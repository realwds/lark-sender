using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LarkSender.Models;

namespace LarkSender.Core
{
    /// <summary>
    /// 日志与发送历史。
    ///
    /// 两种存储：
    ///   - lark-sender.log  人读的纯文本，带轮转（超过 1MB 滚动，保留 1 份备份）
    ///   - history.jsonl    机读的 JSON Lines，供界面查历史/重发
    ///
    /// 设计原则：日志失败绝不能影响发送主流程，所有写操作都吞异常，
    /// 但会把最后一次失败原因记在 LastError 里供排查。
    /// </summary>
    public class Logger
    {
        private const long MaxLogBytes = 1024 * 1024;   // 1 MB
        private const int KeepHistoryLines = 500;       // 历史最多保留 500 条

        private readonly string _logPath;
        private readonly string _historyPath;
        private readonly object _lock = new object();

        /// <summary>最近一次写入失败的原因（正常时为 null）</summary>
        public string LastError { get; private set; }

        public Logger() : this(Paths.LogFile, Paths.HistoryFile) { }

        public Logger(string logPath, string historyPath)
        {
            _logPath = logPath;
            _historyPath = historyPath;
        }

        // ==================== 记录 ====================

        /// <summary>记录一次发送（同时写文本日志和结构化历史）</summary>
        public void RecordSend(string groupName, string text, bool atAll, SendResult result, string source)
        {
            RecordSend(groupName, text, atAll, result, source, "text", null, null);
        }

        /// <summary>记录一次发送（带消息类型与卡片信息）</summary>
        public void RecordSend(string groupName, string text, bool atAll, SendResult result, string source,
            string kind, string cardTitle, string cardColor)
        {
            if (result == null) result = SendResult.Fail("未知结果", "", false, 0);

            string first = FirstLine(text, 120);
            if (string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(cardTitle))
                first = "[卡片] " + cardTitle;
            string status = result.Ok ? "OK  " : "FAIL";

            string detail = result.Ok ? "发送成功" : result.Error;
            if (result.Attempts > 1) detail += "（共 " + result.Attempts + " 次）";

            string line = string.Format(
                "{0}\t{1}\t[{2}]\t群={3}\tat_all={4}\t{5}\t{6}",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                status,
                source ?? "-",
                string.IsNullOrEmpty(groupName) ? "-" : groupName,
                atAll,
                first,
                detail);

            AppendLogLine(line);
            AppendHistory(groupName, text, atAll, result, source, kind, cardTitle, cardColor);
        }

        /// <summary>记录一条普通运行日志（非发送）</summary>
        public void Info(string message)
        {
            AppendLogLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\tINFO\t" + message);
        }

        /// <summary>记录错误</summary>
        public void Error(string message, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("\tERROR\t");
            sb.Append(message);
            if (ex != null)
            {
                sb.Append(" | ");
                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(ex.Message);
            }
            AppendLogLine(sb.ToString());
        }

        // ==================== 读取历史 ====================

        /// <summary>读取最近的历史记录（新的在前）</summary>
        public List<HistoryEntry> ReadHistory(int max = 200)
        {
            var list = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(_historyPath)) return list;

                var lines = File.ReadAllLines(_historyPath, Encoding.UTF8);
                // 从末尾往前读，取最新的
                for (int i = lines.Length - 1; i >= 0 && list.Count < max; i--)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = HistoryEntry.FromJson(line);
                    if (entry != null) list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                LastError = "读取历史失败：" + ex.Message;
            }
            return list;
        }

        /// <summary>
        /// 删除指定 id 的历史记录。
        /// 返回实际删除条数；0 表示没找到（可能已被其他实例删掉）。
        /// </summary>
        public int DeleteHistory(IEnumerable<string> ids)
        {
            if (ids == null) return 0;
            var targets = new HashSet<string>();
            foreach (var id in ids)
            {
                if (!string.IsNullOrEmpty(id)) targets.Add(id);
            }
            if (targets.Count == 0) return 0;

            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_historyPath)) return 0;

                    var lines = File.ReadAllLines(_historyPath, Encoding.UTF8);
                    var kept = new List<string>(lines.Length);
                    int removed = 0;

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var entry = HistoryEntry.FromJson(line);
                        // 解析失败的行保留（宁可留着也不静默丢数据）
                        if (entry != null && entry.Id != null && targets.Contains(entry.Id))
                        {
                            removed++;
                            continue;
                        }
                        kept.Add(line);
                    }

                    if (removed > 0)
                    {
                        File.WriteAllLines(_historyPath, kept.ToArray(), new UTF8Encoding(false));
                    }
                    return removed;
                }
                catch (Exception ex)
                {
                    LastError = "删除历史失败：" + ex.Message;
                    return 0;
                }
            }
        }

        /// <summary>清空全部历史</summary>
        public bool ClearHistory()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_historyPath)) File.Delete(_historyPath);
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = "清空历史失败：" + ex.Message;
                    return false;
                }
            }
        }

        // ==================== 内部写盘 ====================

        private void AppendLogLine(string line)
        {
            lock (_lock)
            {
                try
                {
                    Paths.EnsureAppDataDir();
                    RotateIfNeeded();

                    // 显式无 BOM UTF8：首次建文件带 BOM 会影响 grep
                    File.AppendAllText(_logPath, line + "\r\n", new UTF8Encoding(false));
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = "日志写入失败：" + ex.Message;
                }
            }
        }

        /// <summary>日志超过阈值时滚动：xxx.log → xxx.log.1</summary>
        private void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                var fi = new FileInfo(_logPath);
                if (fi.Length < MaxLogBytes) return;

                string backup = _logPath + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(_logPath, backup);
            }
            catch
            {
                // 轮转失败不影响继续写当前文件
            }
        }

        private void AppendHistory(string groupName, string text, bool atAll, SendResult result, string source,
            string kind, string cardTitle, string cardColor)
        {
            lock (_lock)
            {
                try
                {
                    Paths.EnsureAppDataDir();

                    var entry = new HistoryEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Group = groupName ?? "",
                        Text = text ?? "",
                        AtAll = atAll,
                        Ok = result.Ok,
                        Attempts = result.Attempts,
                        Error = result.Error ?? "",
                        Source = source ?? "",
                        Kind = kind ?? "text",
                        CardTitle = cardTitle ?? "",
                        CardColor = cardColor ?? ""
                    };

                    File.AppendAllText(_historyPath, entry.ToJson() + "\n", new UTF8Encoding(false));
                    TrimHistoryIfNeeded();
                }
                catch (Exception ex)
                {
                    LastError = "历史写入失败：" + ex.Message;
                }
            }
        }

        /// <summary>历史超过上限时只保留最近 N 条</summary>
        private void TrimHistoryIfNeeded()
        {
            try
            {
                if (!File.Exists(_historyPath)) return;
                var lines = File.ReadAllLines(_historyPath, Encoding.UTF8);
                if (lines.Length <= KeepHistoryLines) return;

                int start = lines.Length - KeepHistoryLines;
                var kept = new string[KeepHistoryLines];
                Array.Copy(lines, start, kept, 0, KeepHistoryLines);
                File.WriteAllLines(_historyPath, kept, new UTF8Encoding(false));
            }
            catch
            {
                // 裁剪失败不影响新记录
            }
        }

        private static string FirstLine(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int idx = text.IndexOf('\n');
            string first = idx >= 0 ? text.Substring(0, idx) : text;
            first = first.Trim();
            return first.Length <= max ? first : first.Substring(0, max) + "…";
        }
    }

    /// <summary>一条发送历史</summary>
    public class HistoryEntry
    {
        /// <summary>唯一标识，用于删除单条记录。
        /// 旧版本历史没有这个字段，读取时按行内容补算一个稳定值。</summary>
        public string Id { get; set; }
        public string Time { get; set; }
        public string Group { get; set; }
        public string Text { get; set; }
        public bool AtAll { get; set; }
        public bool Ok { get; set; }
        public int Attempts { get; set; }
        public string Error { get; set; }
        public string Source { get; set; }

        /// <summary>发送时的消息类型：text / card</summary>
        public string Kind { get; set; }

        /// <summary>卡片标题（仅 card 类型有效）</summary>
        public string CardTitle { get; set; }

        /// <summary>卡片颜色名（绿/红/橙/灰/紫/青/蓝，空=自动）</summary>
        public string CardColor { get; set; }

        public HistoryEntry()
        {
            Id = "";
            Time = "";
            Group = "";
            Text = "";
            Error = "";
            Source = "";
            Kind = "text";
            CardTitle = "";
            CardColor = "";
        }

        /// <summary>是否卡片消息</summary>
        public bool IsCard
        {
            get { return string.Equals(Kind, "card", StringComparison.OrdinalIgnoreCase); }
        }

        public string ToJson()
        {
            return Json.Serialize(new Dictionary<string, object>
            {
                { "id", Id ?? "" },
                { "time", Time ?? "" },
                { "group", Group ?? "" },
                { "text", Text ?? "" },
                { "atAll", AtAll },
                { "ok", Ok },
                { "attempts", Attempts },
                { "error", Error ?? "" },
                { "source", Source ?? "" },
                { "kind", Kind ?? "text" },
                { "cardTitle", CardTitle ?? "" },
                { "cardColor", CardColor ?? "" }
            });
        }

        public static HistoryEntry FromJson(string line)
        {
            try
            {
                var o = Json.ParseObject(line);
                var entry = new HistoryEntry
                {
                    Id = Json.GetString(o, "id", ""),
                    Time = Json.GetString(o, "time", ""),
                    Group = Json.GetString(o, "group", ""),
                    Text = Json.GetString(o, "text", ""),
                    AtAll = Json.GetBool(o, "atAll", false),
                    Ok = Json.GetBool(o, "ok", false),
                    Attempts = Json.GetInt(o, "attempts", 1),
                    Error = Json.GetString(o, "error", ""),
                    Source = Json.GetString(o, "source", ""),
                    Kind = Json.GetString(o, "kind", "text"),
                    CardTitle = Json.GetString(o, "cardTitle", ""),
                    CardColor = Json.GetString(o, "cardColor", "")
                };

                // 旧版本记录没有 id，用行内容的哈希补一个，保证同一条记录 id 稳定
                if (string.IsNullOrEmpty(entry.Id))
                {
                    entry.Id = LegacyId(line);
                }
                return entry;
            }
            catch
            {
                return null; // 坏行直接跳过，不影响其他记录
            }
        }

        /// <summary>为旧格式记录生成稳定 id（内容 hash）</summary>
        private static string LegacyId(string line)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(line ?? "");
                var hash = md5.ComputeHash(bytes);
                var sb = new StringBuilder("legacy-");
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}