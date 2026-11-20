using System;
using System.IO;

namespace LarkSender.Core
{
    /// <summary>集中管理所有路径，便于测试时覆盖</summary>
    public static class Paths
    {
        /// <summary>程序所在目录；配置跟随 exe，方便绿色版/便携使用</summary>
        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>配置文件：exe 同目录 config.json</summary>
        public static string ConfigFile
        {
            get { return Path.Combine(BaseDir, "config.json"); }
        }

        /// <summary>用户数据目录：%APPDATA%\LarkSender</summary>
        public static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LarkSender");
            }
        }

        /// <summary>运行日志</summary>
        public static string LogFile
        {
            get { return Path.Combine(AppDataDir, "lark-sender.log"); }
        }

        /// <summary>结构化发送历史（JSON Lines）</summary>
        public static string HistoryFile
        {
            get { return Path.Combine(AppDataDir, "history.jsonl"); }
        }

        /// <summary>Windows 计划任务名前缀</summary>
        public const string TaskPrefix = "LarkSender_";

        /// <summary>确保数据目录存在</summary>
        public static void EnsureAppDataDir()
        {
            if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
        }
    }
}