using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LarkSender.Models;

namespace LarkSender.Core
{
    /// <summary>
    /// 配置读写。
    /// 配置格式带 version 字段，低版本会自动迁移到当前版本。
    /// </summary>
    public class ConfigStore
    {
        private readonly string _path;

        public ConfigStore() : this(Paths.ConfigFile) { }

        /// <summary>可指定配置路径，便于测试</summary>
        public ConfigStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("配置路径不能为空", "path");
            _path = path;
        }

        public string Path_ { get { return _path; } }

        /// <summary>
        /// 读取配置。文件不存在时返回空配置（不自动写盘，
        /// 由调用方决定何时保存，避免只读目录下启动报错）。
        /// </summary>
        public AppConfig Load()
        {
            if (!File.Exists(_path)) return new AppConfig();

            string text = File.ReadAllText(_path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return new AppConfig();

            Dictionary<string, object> root;
            try
            {
                root = Json.ParseObject(text);
            }
            catch (JsonException ex)
            {
                throw new ConfigException(
                    "配置文件格式有误，无法解析：\n" + _path + "\n\n" + ex.Message, ex);
            }

            int version = Json.GetInt(root, "version", 1);
            var cfg = ParseConfig(root, version);

            // 低版本自动迁移
            if (version < AppConfig.CurrentVersion)
            {
                cfg.Version = AppConfig.CurrentVersion;
            }
            return cfg;
        }

        /// <summary>保存配置（缩进格式，便于人工查看）</summary>
        public void Save(AppConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            cfg.Version = AppConfig.CurrentVersion;

            var json = Json.SerializeIndented(ToJsonObject(cfg));
            var enc = new UTF8Encoding(false); // 无 BOM
            File.WriteAllText(_path, json, enc);
        }

        // ==================== 解析 ====================

        private static AppConfig ParseConfig(Dictionary<string, object> root, int version)
        {
            var cfg = new AppConfig { Version = version };

            // v1: groups 用 name/webhook/secret/atAll/note
            foreach (var item in Json.GetList(root, "groups"))
            {
                var g = item as Dictionary<string, object>;
                if (g == null) continue;
                var name = Json.GetString(g, "name", "").Trim();
                if (name.Length == 0) continue; // 无名群直接丢弃
                cfg.Groups.Add(new GroupConfig(
                    name,
                    Json.GetString(g, "webhook", ""),
                    Json.GetString(g, "secret", ""),
                    Json.GetBool(g, "atAll", false),
                    Json.GetString(g, "note", "")
                ));
            }

            foreach (var item in Json.GetList(root, "schedules"))
            {
                var s = item as Dictionary<string, object>;
                if (s == null) continue;
                var id = Json.GetString(s, "id", "").Trim();
                if (id.Length == 0) continue;

                var kindStr = Json.GetString(s, "kind", "once");
                ScheduleKind kind;
                if (!TryParseKind(kindStr, out kind)) kind = ScheduleKind.Once;

                cfg.Schedules.Add(new ScheduleConfig
                {
                    Id = id,
                    GroupName = Json.GetString(s, "groupName", ""),
                    Message = Json.GetString(s, "message", ""),
                    AtAll = Json.GetBool(s, "atAll", false),
                    Kind = kind,
                    RunAt = Json.GetString(s, "runAt", ""),
                    CreatedAt = Json.GetString(s, "createdAt", "")
                });
            }

            // v2 新增：常用短语模板
            foreach (var item in Json.GetList(root, "templates"))
            {
                var t = item as string;
                if (!string.IsNullOrWhiteSpace(t)) cfg.Templates.Add(t);
            }

            // v3 新增：@所有人 是否同行
            cfg.AtAllInline = Json.GetBool(root, "atAllInline", false);

            return cfg;
        }

        /// <summary>兼容 v1 的 kind 字符串，以及枚举名</summary>
        private static bool TryParseKind(string s, out ScheduleKind kind)
        {
            kind = ScheduleKind.Once;
            if (string.IsNullOrWhiteSpace(s)) return false;

            switch (s.Trim().ToLowerInvariant())
            {
                case "once": kind = ScheduleKind.Once; return true;
                case "daily": kind = ScheduleKind.Daily; return true;
                case "weekdays": kind = ScheduleKind.Weekdays; return true;
                default: return false;
            }
        }

        // ==================== 序列化 ====================

        private static Dictionary<string, object> ToJsonObject(AppConfig cfg)
        {
            var groups = new List<object>();
            foreach (var g in cfg.Groups)
            {
                groups.Add(new Dictionary<string, object>
                {
                    { "name", g.Name ?? "" },
                    { "webhook", g.Webhook ?? "" },
                    { "secret", g.Secret ?? "" },
                    { "atAll", g.AtAll },
                    { "note", g.Note ?? "" }
                });
            }

            var schedules = new List<object>();
            foreach (var s in cfg.Schedules)
            {
                schedules.Add(new Dictionary<string, object>
                {
                    { "id", s.Id ?? "" },
                    { "groupName", s.GroupName ?? "" },
                    { "message", s.Message ?? "" },
                    { "atAll", s.AtAll },
                    { "kind", KindToString(s.Kind) },
                    { "runAt", s.RunAt ?? "" },
                    { "createdAt", s.CreatedAt ?? "" }
                });
            }

            var templates = new List<object>();
            foreach (var t in cfg.Templates) templates.Add(t ?? "");

            return new Dictionary<string, object>
            {
                { "version", AppConfig.CurrentVersion },
                { "groups", groups },
                { "schedules", schedules },
                { "templates", templates },
                { "atAllInline", cfg.AtAllInline }
            };
        }

        private static string KindToString(ScheduleKind k)
        {
            switch (k)
            {
                case ScheduleKind.Daily: return "daily";
                case ScheduleKind.Weekdays: return "weekdays";
                default: return "once";
            }
        }

        // ==================== 便捷查询 ====================

        /// <summary>按名称找群（忽略大小写）</summary>
        public static GroupConfig FindGroup(AppConfig cfg, string name)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(name)) return null;
            return cfg.Groups.FirstOrDefault(
                g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>按 id 找定时计划</summary>
        public static ScheduleConfig FindSchedule(AppConfig cfg, string id)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(id)) return null;
            return cfg.Schedules.FirstOrDefault(s => s.Id == id);
        }
    }

    /// <summary>配置相关异常</summary>
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
        public ConfigException(string message, Exception inner) : base(message, inner) { }
    }
}