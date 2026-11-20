using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using LarkSender.Core;

namespace LarkSender.Forms
{
    /// <summary>
    /// 给窗体设置应用图标（标题栏 + 任务栏）。
    ///
    /// 优先取 exe 内嵌的图标资源；取不到时退回程序目录的 icon.ico。
    /// 都失败就保持系统默认图标，不抛异常——图标缺失不该阻止程序启动。
    /// </summary>
    internal static class AppIcon
    {
        private static Icon _cached;
        private static bool _tried;

        public static void Apply(Form form)
        {
            if (form == null) return;
            var icon = Get();
            if (icon != null) form.Icon = icon;
        }

        private static Icon Get()
        {
            if (_tried) return _cached;
            _tried = true;

            // 1) exe 内嵌图标
            try
            {
                var exe = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) { _cached = ico; return _cached; }
                }
            }
            catch
            {
                // 提取失败，继续尝试下一个来源
            }

            // 2) 程序目录的 icon.ico
            try
            {
                var path = Path.Combine(Paths.BaseDir, "icon.ico");
                if (File.Exists(path))
                {
                    _cached = new Icon(path);
                    return _cached;
                }
            }
            catch
            {
                // 图标格式不支持等情况，忽略
            }

            return null;
        }
    }
}