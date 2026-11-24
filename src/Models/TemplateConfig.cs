using System;
using System.Collections.Generic;

namespace LarkSender.Models
{
    /// <summary>
    /// 一条常用语。
    ///
    /// 之前只存字符串正文，导致「卡片标题 / 颜色 / 是否卡片」全丢；
    /// 现在完整保存这些属性，取出来时能还原当时的形态。
    /// </summary>
    public class TemplateConfig
    {
        /// <summary>列表里显示的名称（取正文首行）</summary>
        public string Name { get; set; }

        /// <summary>正文（Markdown）</summary>
        public string Body { get; set; }

        /// <summary>卡片标题；非卡片消息时为空</summary>
        public string Title { get; set; }

        /// <summary>卡片颜色名（绿/红/橙/灰/紫/青/蓝；空=自动）</summary>
        public string Color { get; set; }

        /// <summary>是否为卡片消息</summary>
        public bool IsCard { get; set; }

        /// <summary>是否 @所有人</summary>
        public bool AtAll { get; set; }

        public TemplateConfig()
        {
            Name = "";
            Body = "";
            Title = "";
            Color = "";
            IsCard = false;
            AtAll = false;
        }

        /// <summary>从旧版纯字符串升级（旧模板无卡片信息，按纯文本处理）</summary>
        public static TemplateConfig FromLegacyString(string text)
        {
            var t = new TemplateConfig { Body = text ?? "" };
            t.Name = MakeName(t.Body, t.Title);
            return t;
        }

        /// <summary>生成显示名：卡片有标题用标题，否则取正文首行</summary>
        public static string MakeName(string body, string title)
        {
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();

            var text = body ?? "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                // 去掉 Markdown 标记，让列表更好看
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^#{1,6}\s*", "");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^[-*+]\s+", "");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"^\d+\.\s+", "");
                line = line.Replace("**", "").Replace("~~", "");
                return line.Length > 30 ? line.Substring(0, 30) + "…" : line;
            }
            return "(空)";
        }

        /// <summary>刷新 Name</summary>
        public void RefreshName()
        {
            Name = MakeName(Body, Title);
        }
    }
}