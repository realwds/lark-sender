using System;
using System.Collections.Generic;

namespace LarkSender.Models
{
    /// <summary>消息类型</summary>
    public enum MessageKind
    {
        /// <summary>纯文本，支持 &lt;at user_id="all"&gt; 提及</summary>
        Text = 0,
        /// <summary>
        /// 富文本卡片（interactive + lark_md）。
        /// 为什么不用 post：实测群机器人 webhook 的 post 不支持 style
        /// （加粗/斜体/下划线），只能做纯文本 + 换行；
        /// 而 interactive 卡片的 lark_md 真正支持 Markdown。
        /// </summary>
        Card = 1,
    }

    /// <summary>一个群机器人配置</summary>
    public class GroupConfig
    {
        public string Name { get; set; }
        public string Webhook { get; set; }
        public string Secret { get; set; }
        public bool AtAll { get; set; }
        public string Note { get; set; }

        public GroupConfig()
        {
            Name = "";
            Webhook = "";
            Secret = "";
            Note = "";
        }

        public GroupConfig(string name, string webhook, string secret, bool atAll, string note)
        {
            Name = name ?? "";
            Webhook = webhook ?? "";
            Secret = secret ?? "";
            AtAll = atAll;
            Note = note ?? "";
        }

        /// <summary>webhook 是否已填写</summary>
        public bool HasWebhook
        {
            get { return !string.IsNullOrWhiteSpace(Webhook); }
        }
    }

    /// <summary>定时任务类型</summary>
    public enum ScheduleKind
    {
        /// <summary>一次性，发完自动清理</summary>
        Once = 0,
        /// <summary>每天固定时间</summary>
        Daily = 1,
        /// <summary>仅工作日（周一至周五）</summary>
        Weekdays = 2,
    }

    /// <summary>一条定时计划</summary>
    public class ScheduleConfig
    {
        public string Id { get; set; }
        public string GroupName { get; set; }
        public string Message { get; set; }
        public bool AtAll { get; set; }
        public ScheduleKind Kind { get; set; }
        /// <summary>yyyy-MM-dd HH:mm</summary>
        public string RunAt { get; set; }
        public string CreatedAt { get; set; }

        public ScheduleConfig()
        {
            Id = "";
            GroupName = "";
            Message = "";
            RunAt = "";
            CreatedAt = "";
        }

        public ScheduleKind KindOrDefault
        {
            get { return Kind; }
        }

        public string KindDisplay
        {
            get
            {
                switch (Kind)
                {
                    case ScheduleKind.Daily: return "每天";
                    case ScheduleKind.Weekdays: return "工作日";
                    default: return "一次";
                }
            }
        }
    }

    /// <summary>应用配置</summary>
    public class AppConfig
    {
        /// <summary>配置格式版本，用于迁移</summary>
        public int Version { get; set; }
        public List<GroupConfig> Groups { get; set; }
        public List<ScheduleConfig> Schedules { get; set; }
        /// <summary>常用短语模板</summary>
        public List<string> Templates { get; set; }

        public const int CurrentVersion = 2;

        public AppConfig()
        {
            Version = CurrentVersion;
            Groups = new List<GroupConfig>();
            Schedules = new List<ScheduleConfig>();
            Templates = new List<string>();
        }
    }

    /// <summary>发送结果</summary>
    public class SendResult
    {
        public bool Ok { get; set; }
        public string Response { get; set; }
        /// <summary>失败原因（可直接展示给用户）</summary>
        public string Error { get; set; }
        /// <summary>实际尝试次数</summary>
        public int Attempts { get; set; }
        /// <summary>是否因网络/服务端问题失败（可重试类）</summary>
        public bool Retryable { get; set; }

        public SendResult()
        {
            Response = "";
            Error = "";
            Attempts = 1;
        }

        public static SendResult Success(string response, int attempts)
        {
            return new SendResult { Ok = true, Response = response ?? "", Attempts = attempts };
        }

        public static SendResult Fail(string error, string response, bool retryable, int attempts)
        {
            return new SendResult
            {
                Ok = false,
                Error = error ?? "",
                Response = response ?? "",
                Retryable = retryable,
                Attempts = attempts
            };
        }
    }
}