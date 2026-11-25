using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LarkSender.Models;

namespace LarkSender.Core
{
    /// <summary>
    /// 飞书群机器人发送器。
    ///
    /// 特性：
    ///   - 支持 text / post 两种消息类型
    ///   - 支持签名校验（HMAC-SHA256）
    ///   - 5xx / 429 / 网络超时自动重试（指数退避）
    ///   - 失败原因分类，便于界面给出可操作提示
    /// </summary>
    public class Notifier
    {
        /// <summary>最大尝试次数（含首次）</summary>
        public int MaxAttempts { get; set; }

        /// <summary>重试基础延迟（毫秒），实际为 base * 2^(n-1)</summary>
        public int RetryBaseDelayMs { get; set; }

        /// <summary>单次请求超时（秒）</summary>
        public int TimeoutSeconds { get; set; }

        /// <summary>可注入的休眠函数，测试时可替换为空操作</summary>
        public Action<int> Sleep { get; set; }

        /// <summary>正文里的 @所有人 词元：发送时转成真正的提及（@位置即插入位置）</summary>
        public const string AtAllToken = "@所有人";

        public Notifier()
        {
            MaxAttempts = 3;
            RetryBaseDelayMs = 600;
            TimeoutSeconds = 20;
            Sleep = ms => { if (ms > 0) Thread.Sleep(ms); };
        }

        // ==================== 发送 ====================

        /// <summary>发送一条消息。永不抛异常，失败信息在返回值里。</summary>
        public SendResult Send(GroupConfig group, string text, bool atAll, MessageKind kind)
        {
            if (group == null)
                return SendResult.Fail("未指定群", "", false, 0);

            if (!group.HasWebhook)
                return SendResult.Fail(
                    "群「" + group.Name + "」没有配置 webhook。",
                    "", false, 0);

            if (string.IsNullOrWhiteSpace(text))
                return SendResult.Fail("消息内容为空。", "", false, 0);

            string payload;
            try
            {
                payload = BuildPayload(text, atAll, group.Secret, kind);
            }
            catch (Exception ex)
            {
                return SendResult.Fail("构造消息失败：" + ex.Message, "", false, 0);
            }

            return PostWithRetry(group.Webhook.Trim(), payload);
        }

        /// <summary>带重试的 HTTP POST</summary>
        private SendResult PostWithRetry(string url, string payload)
        {
            int attempts = 0;
            string lastResponse = "";
            string lastError = "";
            bool lastRetryable = false;

            while (attempts < MaxAttempts)
            {
                attempts++;

                try
                {
                    var outcome = PostOnce(url, payload);
                    lastResponse = outcome.Body;

                    if (outcome.Code == 0)
                        return SendResult.Success(outcome.Body, attempts);

                    // 飞书错误码分类
                    lastError = DescribeFeishuError(outcome.Code, outcome.Body);
                    lastRetryable = IsRetryableCode(outcome.Code);

                    // 业务错误（如关键词不匹配、签名错误）重试无意义
                    if (!lastRetryable)
                        return SendResult.Fail(lastError, outcome.Body, false, attempts);
                }
                catch (HttpRequestException ex)
                {
                    lastError = "网络请求失败：" + Innermost(ex).Message;
                    lastRetryable = true;
                }
                catch (TaskCanceledException)
                {
                    // HttpClient 超时表现为 TaskCanceledException
                    lastError = "请求超时（" + TimeoutSeconds + " 秒）";
                    lastRetryable = true;
                }
                catch (Exception ex)
                {
                    lastError = "发送失败：" + Innermost(ex).Message;
                    lastRetryable = false;
                }

                if (!lastRetryable) break;
                if (attempts >= MaxAttempts) break;

                // 指数退避：600ms, 1200ms, 2400ms...
                int delay = RetryBaseDelayMs * (1 << (attempts - 1));
                Sleep(delay);
            }

            string msg = lastError;
            if (attempts > 1) msg += "（已重试 " + (attempts - 1) + " 次）";
            return SendResult.Fail(msg, lastResponse, lastRetryable, attempts);
        }

        private static Exception Innermost(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private class PostOutcome
        {
            public int Code;
            public string Body;
        }

        private PostOutcome PostOnce(string url, string payload)
        {
            // .NET Framework 4.x 默认可能还是 TLS 1.0，必须显式提升
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { /* 老系统上可能不支持某些枚举，忽略 */ }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
                using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
                using (var resp = client.PostAsync(url, content).GetAwaiter().GetResult())
                {
                    string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    // HTTP 层错误（5xx / 429）当作可重试
                    int status = (int)resp.StatusCode;
                    if (status >= 500 || status == 429)
                    {
                        throw new HttpRequestException(
                            "服务端返回 HTTP " + status + "：" + Truncate(body, 200));
                    }

                    int code = ExtractCode(body);
                    return new PostOutcome { Code = code, Body = body };
                }
            }
        }

        /// <summary>从响应体提取飞书的 code / StatusCode 字段</summary>
        internal static int ExtractCode(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return -1;
            try
            {
                var root = Json.ParseObject(body);
                if (root.ContainsKey("code")) return Json.GetInt(root, "code", -1);
                if (root.ContainsKey("StatusCode")) return Json.GetInt(root, "StatusCode", -1);
                return -1;
            }
            catch (JsonException)
            {
                return -1;
            }
        }

        /// <summary>把飞书错误码翻译成可操作的中文提示</summary>
        internal static string DescribeFeishuError(int code, string body)
        {
            switch (code)
            {
                case 9499:
                    return "请求过于频繁，请降低发送频率。";
                case 19001:
                    return "机器人已被移出群，或 webhook 已失效。请在飞书群重新添加机器人。";
                case 19002:
                    return "签名校验失败。请检查「签名密钥」是否填写正确。";
                case 19003:
                    return "IP 不在白名单内。请在机器人安全设置里加上当前出口 IP。";
                case 19021:
                    return "消息内容不合法。";
                case 19024:
                    return "消息未包含机器人设置的关键词。请在消息里加上该关键词。";
                default:
                    return "飞书返回错误（code=" + code + "）：" + Truncate(body, 300);
            }
        }

        /// <summary>该错误码是否值得重试</summary>
        internal static bool IsRetryableCode(int code)
        {
            // 9499 = 频率限制，值得等一会儿重试
            return code == 9499;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ==================== 消息构造 ====================

        /// <summary>
        /// 根据正文与标题自动判断消息类型：
        /// 填了标题、或正文用了富文本语法 → 卡片；纯文字 → 纯文本。
        /// 界面不再提供「卡片消息」开关。
        /// </summary>
        public static MessageKind PickKind(string text, string cardTitle)
        {
            if (!string.IsNullOrWhiteSpace(cardTitle)) return MessageKind.Card;
            return HasRichSyntax(text) ? MessageKind.Card : MessageKind.Text;
        }

        /// <summary>正文是否用了 Markdown 富文本语法（值得按卡片发送）</summary>
        private static bool HasRichSyntax(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var raw in lines)
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("#")) return true;                                  // 标题
                if (t == "---") return true;                                         // 分割线
                if (t.StartsWith("> ")) return true;                                 // 引用
                if (t.StartsWith("- ") || t.StartsWith("* ")) return true;           // 无序列表
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+\.\s")) return true; // 有序列表
            }
            return text.Contains("**") || text.Contains("~~") ||
                   text.Contains("](") || text.Contains("`");
        }

        /// <summary>构造发送用的 JSON</summary>
        public string BuildPayload(string text, bool atAll, string secret, MessageKind kind)
        {
            return BuildPayload(text, atAll, secret, kind, null, CardBuilder.HeaderColor.Blue, true);
        }

        /// <summary>
        /// 构造发送用的 JSON（卡片模式可显式指定标题与颜色）。
        /// </summary>
        public string BuildPayload(string text, bool atAll, string secret, MessageKind kind,
            string cardTitle, CardBuilder.HeaderColor color, bool autoColor)
        {
            return BuildPayload(text, atAll, secret, kind, cardTitle, color, autoColor, false);
        }

        public string BuildPayload(string text, bool atAll, string secret, MessageKind kind,
            string cardTitle, CardBuilder.HeaderColor color, bool autoColor, bool atAllInline)
        {
            var payload = new Dictionary<string, object>();
            var body = text ?? "";

            // @所有人 作为可插入元素：出现在正文里就换成真正的提及标记
            bool hasToken = body.Contains(AtAllToken);
            if (hasToken)
            {
                body = kind == MessageKind.Card
                    ? body.Replace(AtAllToken, "<at id=all></at>")
                    : body.Replace(AtAllToken, "<at user_id=\"all\">所有人</at>");
                // 提及已在正文里，避免再额外叠加一个
                atAll = false;
            }

            if (kind == MessageKind.Card)
            {
                payload["msg_type"] = "interactive";
                payload["card"] = CardBuilder.BuildCardWithTitle(cardTitle, body, atAll, color, autoColor, atAllInline);
            }
            else
            {
                payload["msg_type"] = "text";
                var content = body;
                if (atAll)
                {
                    content = atAllInline
                        ? "<at user_id=\"all\">所有人</at> " + content
                        : "<at user_id=\"all\">所有人</at>\n" + content;
                }
                payload["content"] = new Dictionary<string, object> { { "text", content } };
            }

            if (!string.IsNullOrEmpty(secret))
            {
                string ts;
                string sign;
                MakeSignature(secret, out ts, out sign);
                payload["timestamp"] = ts;
                payload["sign"] = sign;
            }

            return Json.Serialize(payload);
        }

        /// <summary>发送（卡片模式可指定标题与颜色）</summary>
        public SendResult Send(GroupConfig group, string text, bool atAll, MessageKind kind,
            string cardTitle, CardBuilder.HeaderColor color, bool autoColor)
        {
            return Send(group, text, atAll, kind, cardTitle, color, autoColor, false);
        }

        public SendResult Send(GroupConfig group, string text, bool atAll, MessageKind kind,
            string cardTitle, CardBuilder.HeaderColor color, bool autoColor, bool atAllInline)
        {
            if (group == null)
                return SendResult.Fail("未指定群", "", false, 0);

            if (!group.HasWebhook)
                return SendResult.Fail(
                    "群「" + group.Name + "」没有配置 webhook。",
                    "", false, 0);

            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(cardTitle))
                return SendResult.Fail("消息内容为空。", "", false, 0);

            string payload;
            try
            {
                payload = BuildPayload(text, atAll, group.Secret, kind, cardTitle, color, autoColor, atAllInline);
            }
            catch (Exception ex)
            {
                return SendResult.Fail("构造消息失败：" + ex.Message, "", false, 0);
            }

            return PostWithRetry(group.Webhook.Trim(), payload);
        }

        /// <summary>
        /// 飞书签名：
        ///   key      = timestamp + "\n" + secret
        ///   sign     = base64(HMAC-SHA256(key, ""))
        /// 待签名内容为空字符串，这是飞书的约定。
        /// </summary>
        internal static void MakeSignature(string secret, out string timestamp, out string sign)
        {
            long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            timestamp = ts.ToString();

            var keyBytes = Encoding.UTF8.GetBytes(ts + "\n" + secret);
            using (var hmac = new HMACSHA256(keyBytes))
            {
                var hash = hmac.ComputeHash(new byte[0]);
                sign = Convert.ToBase64String(hash);
            }
        }

        /// <summary>webhook 打码，用于界面展示</summary>
        public static string MaskWebhook(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "(未配置)";
            var m = System.Text.RegularExpressions.Regex.Match(url, @"/hook/([A-Za-z0-9\-_]+)");
            if (m.Success)
            {
                var tok = m.Groups[1].Value;
                if (tok.Length <= 8) return "…/" + tok;
                return "…/" + tok.Substring(0, 4) + "****" + tok.Substring(tok.Length - 4);
            }
            return "…" + url.Substring(Math.Max(0, url.Length - 12));
        }
    }
}