using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LarkSender.Core;
using LarkSender.Forms;
using LarkSender.Models;

namespace LarkSender
{
    /// <summary>
    /// 程序入口。
    ///
    /// 三种运行模式：
    ///   1) 无参数          → 打开主窗口
    ///   2) /send "内容"    → 命令行发送，供脚本调用
    ///   3) /job &lt;JobId&gt;   → 由 Windows 计划任务调用
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length >= 2)
            {
                if (args[0] == "/job")
                    return RunJob(args[1]);
                if (args[0] == "/send")
                    return RunSend(args);
            }

            if (args != null && args.Length == 1 &&
                (args[0] == "/?" || args[0] == "-h" || args[0] == "--help"))
            {
                ShowUsage();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                ShowFatal("启动失败", ex);
                return 1;
            }
        }

        // ==================== 命令行发送 ====================

        private static int RunSend(string[] args)
        {
            try
            {
                string text = args[1];
                string groupName = null;
                bool atAll = false;
                bool post = false;

                for (int i = 2; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "/group":
                            if (i + 1 < args.Length) groupName = args[++i];
                            break;
                        case "/atall":
                            atAll = true;
                            break;
                        case "/post":
                            post = true;
                            break;
                    }
                }

                var store = new ConfigStore();
                var cfg = store.Load();
                var logger = new Logger();
                var notifier = new Notifier();

                GroupConfig group;
                if (string.IsNullOrEmpty(groupName))
                {
                    group = cfg.Groups.FirstOrDefault();
                    if (group == null)
                    {
                        Console.Error.WriteLine("配置里没有任何群，请先打开界面添加。");
                        return 2;
                    }
                }
                else
                {
                    group = ConfigStore.FindGroup(cfg, groupName);
                    if (group == null)
                    {
                        Console.Error.WriteLine("配置里没有群「" + groupName + "」。");
                        Console.Error.WriteLine("已有的群：" + string.Join(", ", cfg.Groups.Select(g => g.Name).ToArray()));
                        return 2;
                    }
                }

                var kind = post ? MessageKind.Post : MessageKind.Text;
                var result = notifier.Send(group, text, atAll, kind);
                logger.RecordSend(group.Name, text, atAll, result, "cli");

                if (result.Ok)
                {
                    Console.WriteLine("发送成功 → " + group.Name);
                    return 0;
                }

                Console.Error.WriteLine("发送失败：" + result.Error);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("执行失败：" + ex.Message);
                return 1;
            }
        }

        // ==================== 定时任务执行 ====================

        private static int RunJob(string jobId)
        {
            var logger = new Logger();
            try
            {
                var store = new ConfigStore();
                var cfg = store.Load();
                var job = ConfigStore.FindSchedule(cfg, jobId);

                if (job == null)
                {
                    logger.Info("计划任务 " + jobId + " 已不存在于配置中（可能已被删除），跳过。");
                    return 2;
                }

                var group = ConfigStore.FindGroup(cfg, job.GroupName);
                if (group == null)
                {
                    var r = SendResult.Fail("群「" + job.GroupName + "」已不存在。", "", false, 0);
                    logger.RecordSend(job.GroupName, job.Message, job.AtAll, r, "job");
                    return 3;
                }

                var notifier = new Notifier();
                var result = notifier.Send(group, job.Message, job.AtAll, MessageKind.Text);
                logger.RecordSend(group.Name, job.Message, job.AtAll, result, "job");

                int exitCode = result.Ok ? 0 : 1;

                // 一次性任务发完就清理（无论成败，避免残留空任务）
                if (job.Kind == ScheduleKind.Once)
                {
                    CleanupOnceJob(jobId, logger);
                }

                return exitCode;
            }
            catch (Exception ex)
            {
                logger.Error("定时任务 " + jobId + " 执行异常", ex);
                return 1;
            }
        }

        /// <summary>清理一次性任务：先移除配置记录，再删系统任务</summary>
        private static void CleanupOnceJob(string jobId, Logger logger)
        {
            try
            {
                var store = new ConfigStore();
                var cfg = store.Load();
                int removed = cfg.Schedules.RemoveAll(s => s.Id == jobId);
                if (removed > 0) store.Save(cfg);
            }
            catch (Exception ex)
            {
                logger.Info("清理一次性任务配置失败：" + ex.Message);
            }

            try
            {
                new ScheduleManager().Delete(jobId);
            }
            catch (Exception ex)
            {
                logger.Info("清理一次性任务系统任务失败：" + ex.Message);
            }
        }

        // ==================== 辅助 ====================

        private static void ShowUsage()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Lark Sender - 飞书/Lark 群机器人桌面客户端");
            sb.AppendLine();
            sb.AppendLine("用法：");
            sb.AppendLine("  LarkSender.exe                        打开界面");
            sb.AppendLine("  LarkSender.exe /send \"内容\"           发送到第一个群");
            sb.AppendLine("  LarkSender.exe /send \"内容\" /group 群名 [/atall] [/post]");
            sb.AppendLine("  LarkSender.exe /job <JobId>           由计划任务内部调用");
            sb.AppendLine();
            sb.AppendLine("参数：");
            sb.AppendLine("  /group <名称>   指定群（默认第一个）");
            sb.AppendLine("  /atall          提及所有人");
            sb.AppendLine("  /post           以富文本(post)格式发送");
            sb.AppendLine();
            sb.AppendLine("配置：exe 同目录 config.json");
            sb.AppendLine("日志：%APPDATA%\\LarkSender\\");
            Console.WriteLine(sb.ToString());
        }

        private static void ShowFatal(string title, Exception ex)
        {
            string msg = ex.Message;
            if (ex is ConfigException)
            {
                msg += "\n\n配置文件位置：" + Paths.ConfigFile;
            }

            try
            {
                MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                Console.Error.WriteLine(title + ": " + msg);
            }
        }
    }
}