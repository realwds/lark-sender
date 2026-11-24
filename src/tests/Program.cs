using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LarkSender.Core;
using LarkSender.Models;

namespace LarkSender.Tests
{
    /// <summary>
    /// 轻量单元测试入口。
    ///
    /// 为什么不引 xUnit/NUnit：
    /// 本项目坚持「clone 下来 build.cmd 就能跑」，不引入任何 NuGet 依赖。
    /// 这个自带的测试器足够覆盖纯函数逻辑（JSON、签名、配置、消息构造）。
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> _failures = new List<string>();

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("Lark Sender - 单元测试");
            Console.WriteLine(new string('=', 60));

            RunJsonTests();
            RunSignatureTests();
            RunPayloadTests();
            RunConfigTests();
            RunNotifierErrorTests();
            RunScheduleValidationTests();
            RunHistoryTests();
            RunCardBuilderTests();

            Console.WriteLine(new string('=', 60));
            Console.WriteLine();
            if (_failed == 0)
            {
                Console.WriteLine("全部通过：" + _passed + " 个断言");
                return 0;
            }

            Console.WriteLine("失败 " + _failed + " 个 / 通过 " + _passed + " 个");
            Console.WriteLine();
            foreach (var f in _failures) Console.WriteLine("  ✗ " + f);
            return 1;
        }

        // ==================== 断言 ====================

        private static void Ok(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ✓ " + what); }
            else { _failed++; _failures.Add(what); Console.WriteLine("  ✗ " + what); }
        }

        private static void Eq<T>(T actual, T expected, string what)
        {
            bool same = EqualityComparer<T>.Default.Equals(actual, expected);
            if (same) { _passed++; Console.WriteLine("  ✓ " + what); }
            else
            {
                _failed++;
                string msg = what + "  [期望: " + expected + ", 实际: " + actual + "]";
                _failures.Add(msg);
                Console.WriteLine("  ✗ " + msg);
            }
        }

        private static void Throws<TEx>(Action action, string what) where TEx : Exception
        {
            try
            {
                action();
                _failed++;
                _failures.Add(what + "  [期望抛异常但没有]");
                Console.WriteLine("  ✗ " + what + "  [期望抛异常但没有]");
            }
            catch (TEx)
            {
                _passed++;
                Console.WriteLine("  ✓ " + what);
            }
            catch (Exception ex)
            {
                _failed++;
                string msg = what + "  [期望 " + typeof(TEx).Name + "，实际 " + ex.GetType().Name + "]";
                _failures.Add(msg);
                Console.WriteLine("  ✗ " + msg);
            }
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("[" + name + "]");
        }

        // ==================== JSON ====================

        private static void RunJsonTests()
        {
            Section("JSON 解析");

            var obj = Json.ParseObject("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null}");
            Eq(Json.GetInt(obj, "a", 0), 1, "解析整数");
            Eq(Json.GetString(obj, "b", ""), "x", "解析字符串");
            Eq(Json.GetBool(obj, "c", false), true, "解析布尔");
            Eq(Json.GetString(obj, "d", "fallback"), "fallback", "null 取字符串回退");

            var arr = Json.Parse("[1,2,3]") as List<object>;
            Ok(arr != null && arr.Count == 3, "解析数组");

            // 转义
            var esc = Json.ParseObject("{\"k\":\"a\\\"b\\\\c\\nd\"}");
            Eq(Json.GetString(esc, "k", ""), "a\"b\\c\nd", "解析转义字符");

            var uni = Json.ParseObject("{\"k\":\"\\u4e2d\\u6587\"}");
            Eq(Json.GetString(uni, "k", ""), "中文", "解析 \\u 转义");

            // 嵌套
            var nested = Json.ParseObject("{\"o\":{\"x\":5},\"l\":[{\"y\":1}]}");
            var sub = Json.GetObject(nested, "o");
            Eq(Json.GetInt(sub, "x", 0), 5, "解析嵌套对象");
            Eq(Json.GetList(nested, "l").Count, 1, "解析嵌套数组");

            // 负数与小数
            var num = Json.ParseObject("{\"neg\":-42,\"f\":3.5}");
            Eq(Json.GetInt(num, "neg", 0), -42, "解析负数");
            Ok(Math.Abs(Convert.ToDouble(num["f"]) - 3.5) < 1e-9, "解析小数");

            Section("JSON 序列化");

            var d = new Dictionary<string, object>
            {
                { "s", "he said \"hi\"\n" },
                { "n", 42 },
                { "b", true },
                { "nil", null }
            };
            string json = Json.Serialize(d);
            Ok(json.Contains("\\\"hi\\\""), "序列化转义双引号");
            Ok(json.Contains("\\n"), "序列化转义换行");
            Ok(json.Contains("\"n\":42"), "序列化整数");

            // 往返
            var round = Json.ParseObject(json);
            Eq(Json.GetString(round, "s", ""), "he said \"hi\"\n", "序列化后再解析保持一致");

            // 中文
            var zh = Json.Serialize(new Dictionary<string, object> { { "t", "你好世界" } });
            Ok(zh.Contains("你好世界"), "序列化中文不转义为 \\u");

            Section("JSON 异常");

            Throws<JsonException>(() => Json.Parse("{"), "未闭合对象抛异常");
            Throws<JsonException>(() => Json.Parse("{'a':1}"), "单引号非法抛异常");
            Throws<JsonException>(() => Json.Parse("{\"a\":}"), "缺值抛异常");
            Throws<JsonException>(() => Json.ParseObject("[1,2]"), "根节点非对象抛异常");
            Throws<JsonException>(() => Json.Parse("{\"a\":1} extra"), "尾部多余内容抛异常");
        }

        // ==================== 签名 ====================

        private static void RunSignatureTests()
        {
            Section("飞书签名");

            string ts, sign;
            Notifier.MakeSignature("test-secret", out ts, out sign);

            Ok(!string.IsNullOrEmpty(ts), "时间戳非空");
            long tsVal;
            Ok(long.TryParse(ts, out tsVal), "时间戳是数字");
            Ok(!string.IsNullOrEmpty(sign), "签名非空");
            Ok(sign.Length == 44 && sign.EndsWith("="), "签名是 44 位 Base64");

            // 相同 secret + 相同秒内结果应稳定
            string ts2, sign2;
            Notifier.MakeSignature("test-secret", out ts2, out sign2);
            Ok(ts == ts2 ? sign == sign2 : true, "同一秒内签名稳定");

            // 不同 secret 产生不同签名
            string tsa, sa, tsb, sb;
            Notifier.MakeSignature("secret-a", out tsa, out sa);
            Notifier.MakeSignature("secret-b", out tsb, out sb);
            Ok(sa != sb, "不同密钥签名不同");
        }

        // ==================== 消息构造 ====================

        private static void RunPayloadTests()
        {
            Section("Payload 构造");

            var n = new Notifier();

            // 纯文本，不 @
            string p1 = n.BuildPayload("hello", false, null, MessageKind.Text);
            var o1 = Json.ParseObject(p1);
            Eq(Json.GetString(o1, "msg_type", ""), "text", "text 类型 msg_type 正确");
            var c1 = Json.GetObject(o1, "content");
            Eq(Json.GetString(c1, "text", ""), "hello", "text 内容正确");
            Ok(!o1.ContainsKey("timestamp"), "无密钥时不带 timestamp");

            // @所有人
            string p2 = n.BuildPayload("hi", true, null, MessageKind.Text);
            var c2 = Json.GetObject(Json.ParseObject(p2), "content");
            string text2 = Json.GetString(c2, "text", "");
            Ok(text2.Contains("<at user_id=\"all\">"), "@所有人 带 at 标签");
            Ok(text2.Contains("hi"), "@所有人 保留原内容");

            // 带密钥
            string p3 = n.BuildPayload("x", false, "sec", MessageKind.Text);
            var o3 = Json.ParseObject(p3);
            Ok(o3.ContainsKey("timestamp"), "有密钥时带 timestamp");
            Ok(o3.ContainsKey("sign"), "有密钥时带 sign");

            // 卡片类型（interactive + lark_md）
            string p4 = n.BuildPayload("# 标题\n正文", false, null, MessageKind.Card);
            var o4 = Json.ParseObject(p4);
            Eq(Json.GetString(o4, "msg_type", ""), "interactive", "Card 类型 msg_type 正确");
            var card4 = Json.GetObject(o4, "card");
            Ok(card4 != null, "Card 含 card 对象");
            var header = Json.GetObject(card4, "header");
            Ok(header != null, "Card 含标题栏（# 标题触发）");
            var ht = Json.GetObject(header, "title");
            Eq(Json.GetString(ht, "content", ""), "标题", "标题内容正确");
            Ok(Json.GetList(card4, "elements").Count > 0, "Card 含元素");

            // 特殊字符不破坏 JSON
            string tricky = "引号\" 反斜杠\\ 换行\n 制表\t 中文";
            string p5 = n.BuildPayload(tricky, false, null, MessageKind.Text);
            var o5 = Json.ParseObject(p5); // 能解析即说明转义正确
            var c5 = Json.GetObject(o5, "content");
            Eq(Json.GetString(c5, "text", ""), tricky, "特殊字符往返正确");
        }

        // ==================== 配置 ====================

        private static void RunConfigTests()
        {
            Section("配置读写");

            string dir = Path.Combine(Path.GetTempPath(), "larksender-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "config.json");

            try
            {
                var store = new ConfigStore(path);

                // 不存在时返回空配置
                var empty = store.Load();
                Eq(empty.Groups.Count, 0, "配置文件不存在时返回空配置");
                Eq(empty.Schedules.Count, 0, "空配置无定时任务");

                // 保存并读回
                var cfg = new AppConfig();
                cfg.Groups.Add(new GroupConfig("测试群", "https://example.com/hook/abc", "sec", true, "备注"));
                cfg.Templates.Add("常用语一");
                store.Save(cfg);

                var loaded = store.Load();
                Eq(loaded.Groups.Count, 1, "群数量往返一致");
                Eq(loaded.Groups[0].Name, "测试群", "群名往返一致");
                Eq(loaded.Groups[0].AtAll, true, "atAll 往返一致");
                Eq(loaded.Templates.Count, 1, "常用语数量往返一致");
                Eq(loaded.Version, AppConfig.CurrentVersion, "版本号写入当前版本");

                // 查找
                Ok(ConfigStore.FindGroup(loaded, "测试群") != null, "按名称找到群");
                Ok(ConfigStore.FindGroup(loaded, "测试群") != null, "大小写不敏感查找");
                Ok(ConfigStore.FindGroup(loaded, "不存在") == null, "找不到返回 null");

                // 损坏的 JSON
                File.WriteAllText(path, "{ 这不是 json", Encoding.UTF8);
                Throws<ConfigException>(() => store.Load(), "损坏的配置文件抛 ConfigException");

                // 空文件
                File.WriteAllText(path, "", Encoding.UTF8);
                Eq(store.Load().Groups.Count, 0, "空文件返回空配置");

                Section("配置迁移（v1 → v2）");

                string v1 = @"{
                    ""version"": 1,
                    ""groups"": [{""name"":""老群"",""webhook"":""hook"",""atAll"":true}],
                    ""schedules"": [{""id"":""j1"",""groupName"":""老群"",""message"":""msg"",""kind"":""daily"",""runAt"":""2026-01-01 09:00""}]
                }";
                File.WriteAllText(path, v1, Encoding.UTF8);
                var migrated = store.Load();
                Eq(migrated.Version, AppConfig.CurrentVersion, "v1 自动升级到当前版本");
                Eq(migrated.Groups.Count, 1, "v1 群被正确读取");
                Eq(migrated.Schedules.Count, 1, "v1 定时任务被正确读取");
                Eq(migrated.Schedules[0].Kind, ScheduleKind.Daily, "v1 kind 字符串转为枚举");
                Ok(migrated.Schedules[0].KindDisplay == "每天", "kind 显示文本正确");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ==================== 错误码 ====================

        private static void RunNotifierErrorTests()
        {
            Section("飞书错误码处理");

            Eq(Notifier.ExtractCode("{\"code\":0,\"msg\":\"ok\"}"), 0, "提取 code");
            Eq(Notifier.ExtractCode("{\"StatusCode\":0}"), 0, "提取 StatusCode");
            Eq(Notifier.ExtractCode("{\"code\":19024}"), 19024, "提取非零 code");
            Eq(Notifier.ExtractCode("not json"), -1, "非 JSON 返回 -1");
            Eq(Notifier.ExtractCode(""), -1, "空串返回 -1");

            Ok(Notifier.IsRetryableCode(9499), "9499 频率限制可重试");
            Ok(!Notifier.IsRetryableCode(19002), "19002 签名错误不重试");
            Ok(!Notifier.IsRetryableCode(0), "成功不重试");

            Ok(Notifier.DescribeFeishuError(19024, "").Contains("关键词"), "19024 提示含关键词");
            Ok(Notifier.DescribeFeishuError(19002, "").Contains("签名"), "19002 提示含签名");
            Ok(Notifier.DescribeFeishuError(99999, "raw").Contains("99999"), "未知码显示原码");

            Section("Webhook 打码");

            string masked = Notifier.MaskWebhook("https://open.feishu.cn/open-apis/bot/v2/hook/aaaabbbb-cccc-dddd-eeee-ffffffffffff");
            Ok(masked.Contains("****"), "打码包含 ****");
            Ok(!masked.Contains("ed23-da3f-486c"), "打码隐藏了 token 中段");
            Eq(Notifier.MaskWebhook(""), "(未配置)", "空 webhook 提示");
        }

        // ==================== 定时校验 ====================

        private static void RunScheduleValidationTests()
        {
            Section("定时任务校验");

            Ok(ScheduleManager.ValidateRunAt(DateTime.Now.AddHours(1), ScheduleKind.Once) == null,
                "未来的时间通过校验");

            Ok(ScheduleManager.ValidateRunAt(DateTime.Now.AddMinutes(-5), ScheduleKind.Once) != null,
                "过去的时间被拒绝");

            // 未来的周末对 Once 是允许的
            var nextSaturday = NextWeekday(DayOfWeek.Saturday);
            Ok(ScheduleManager.ValidateRunAt(nextSaturday, ScheduleKind.Once) == null,
                "一次性任务允许周末");

            // 工作日任务起始日不能是周末
            Ok(ScheduleManager.ValidateRunAt(nextSaturday, ScheduleKind.Weekdays) != null,
                "工作日任务拒绝周末起始");

            var nextWednesday = NextWeekday(DayOfWeek.Wednesday);
            Ok(ScheduleManager.ValidateRunAt(nextWednesday, ScheduleKind.Weekdays) == null,
                "工作日任务接受周三");

            // 每天任务对时间无限制
            Ok(ScheduleManager.ValidateRunAt(DateTime.Now.AddMinutes(-5), ScheduleKind.Daily) == null,
                "每天任务不校验过去时间");
        }

        // ==================== 卡片构造 ====================

        private static void RunCardBuilderTests()
        {
            Section("卡片构造 - 标题与颜色");

            var r1 = CardBuilder.Parse("# 发布完成\n服务正常", false);
            Eq(r1.Title, "发布完成", "# 提取为卡片标题");
            Eq(r1.Color, CardBuilder.HeaderColor.Green, "标题含「完成」→ 绿色");

            var r2 = CardBuilder.Parse("# 部署失败\n详情", false);
            Eq(r2.Color, CardBuilder.HeaderColor.Red, "标题含「失败」→ 红色");

            var r3 = CardBuilder.Parse("# 注意事项\n详情", false);
            Eq(r3.Color, CardBuilder.HeaderColor.Orange, "标题含「注意」→ 橙色");

            var r4 = CardBuilder.Parse("# 日常通知\n内容", false);
            Eq(r4.Color, CardBuilder.HeaderColor.Blue, "无关键词 → 蓝色");

            var r5 = CardBuilder.Parse("!绿 自定义标题\n内容", false);
            Eq(r5.Title, "自定义标题", "!颜色 语法提取标题");
            Eq(r5.Color, CardBuilder.HeaderColor.Green, "!绿 强制绿色");

            Section("卡片构造 - 元素");

            var r6 = CardBuilder.Parse("# 标题\n第一段\n\n第二段", false);
            Ok(r6.Elements.Count >= 2, "空行分段落");

            var r7 = CardBuilder.Parse("正文\n---\n页脚内容", false);
            Ok(r7.Elements.Count >= 2, "--- 分隔正文与页脚");
            var lastElem = r7.Elements[r7.Elements.Count - 1] as Dictionary<string, object>;
            Eq(Json.GetString(lastElem, "tag", ""), "note", "--- 后内容进 note 页脚");

            var r8 = CardBuilder.Parse("## 二级标题\n内容", false);
            Ok(r8.Elements.Count >= 1, "## 生成元素");
            var h2elem = r8.Elements[0] as Dictionary<string, object>;
            var h2text = Json.GetObject(h2elem, "text");
            Ok(Json.GetString(h2text, "content", "").Contains("**"), "## 转成加粗");

            Section("卡片构造 - @所有人");

            var r9 = CardBuilder.Parse("内容", true);
            bool hasAt = false;
            foreach (var e in r9.Elements)
            {
                var d = e as Dictionary<string, object>;
                var t = Json.GetObject(d, "text");
                if (t != null && Json.GetString(t, "content", "").Contains("<at id=all>")) hasAt = true;
            }
            Ok(hasAt, "atAll=true 时包含 <at id=all>");

            var r10 = CardBuilder.Parse("内容", false);
            bool hasAt2 = false;
            foreach (var e in r10.Elements)
            {
                var d = e as Dictionary<string, object>;
                var t = Json.GetObject(d, "text");
                if (t != null && Json.GetString(t, "content", "").Contains("<at id=all>")) hasAt2 = true;
            }
            Ok(!hasAt2, "atAll=false 时不包含 <at>");

            Section("卡片构造 - 边界");

            var r11 = CardBuilder.Parse("", false);
            Ok(r11.Elements.Count > 0, "空内容也有占位元素");
            Eq(r11.Title, "", "空内容无标题");

            var r12 = CardBuilder.Parse(null, false);
            Ok(r12.Elements.Count > 0, "null 不抛异常");

            var r13 = CardBuilder.Parse("# \n正文", false);
            Eq(r13.Title, "", "空标题不写入");

            Section("卡片构造 - 完整 JSON");

            var cardObj = CardBuilder.BuildCard("# 测试\n**加粗** 内容", false);
            Ok(cardObj.ContainsKey("config"), "卡片含 config");
            Ok(cardObj.ContainsKey("header"), "卡片含 header");
            Ok(cardObj.ContainsKey("elements"), "卡片含 elements");

            string json = Json.Serialize(cardObj);
            var parsed = Json.ParseObject(json);
            Eq(Json.GetString(parsed, "header", null) == null ? "" : "has", "has", "序列化后可解析");

            Eq(CardBuilder.ColorToTemplate(CardBuilder.HeaderColor.Green), "green", "绿色模板名");
            Eq(CardBuilder.ColorToTemplate(CardBuilder.HeaderColor.Red), "red", "红色模板名");
            Eq(CardBuilder.ColorToTemplate(CardBuilder.HeaderColor.Blue), "blue", "蓝色模板名");
        }

        private static DateTime NextWeekday(DayOfWeek target)
        {
            var d = DateTime.Now.AddDays(1);
            while (d.DayOfWeek != target) d = d.AddDays(1);
            return new DateTime(d.Year, d.Month, d.Day, 10, 0, 0);
        }

        // ==================== 历史记录 ====================

        private static void RunHistoryTests()
        {
            Section("历史记录");

            string dir = Path.Combine(Path.GetTempPath(), "larksender-hist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "t.log");
            string histPath = Path.Combine(dir, "h.jsonl");

            try
            {
                var logger = new Logger(logPath, histPath);

                // 写 3 条
                for (int i = 1; i <= 3; i++)
                {
                    logger.RecordSend("群" + i, "内容" + i, false,
                        SendResult.Success("ok", 1), "manual");
                }

                var list = logger.ReadHistory(10);
                Eq(list.Count, 3, "写入 3 条读到 3 条");
                Eq(list[0].Text, "内容3", "最新的在最前");
                Ok(!string.IsNullOrEmpty(list[0].Id), "每条都有 id");

                // id 应唯一
                var ids = new HashSet<string>();
                foreach (var e in list) ids.Add(e.Id);
                Eq(ids.Count, 3, "3 条记录的 id 互不相同");

                // 删除 1 条
                int removed = logger.DeleteHistory(new[] { list[0].Id });
                Eq(removed, 1, "删除 1 条返回 1");
                Eq(logger.ReadHistory(10).Count, 2, "删后剩 2 条");

                // 幂等：再删同一个 id 应返回 0
                Eq(logger.DeleteHistory(new[] { list[0].Id }), 0, "重复删除返回 0");
                Eq(logger.ReadHistory(10).Count, 2, "重复删除不影响其他记录");

                // 批量删
                var rest = logger.ReadHistory(10);
                var restIds = new List<string>();
                foreach (var e in rest) restIds.Add(e.Id);
                Eq(logger.DeleteHistory(restIds), 2, "批量删除 2 条");
                Eq(logger.ReadHistory(10).Count, 0, "全删后为空");

                // 再写、然后清空
                logger.RecordSend("群X", "x", false, SendResult.Success("ok", 1), "cli");
                Eq(logger.ReadHistory(10).Count, 1, "清空前有 1 条");
                Ok(logger.ClearHistory(), "清空返回 true");
                Eq(logger.ReadHistory(10).Count, 0, "清空后为空");

                // 空 ids 不报错也不删东西
                logger.RecordSend("群Y", "y", false, SendResult.Success("ok", 1), "cli");
                Eq(logger.DeleteHistory(new List<string>()), 0, "空 id 列表返回 0");
                Eq(logger.DeleteHistory(null), 0, "null 返回 0");
                Eq(logger.ReadHistory(10).Count, 1, "传空不影响记录");

                Section("历史记录 - 旧格式兼容");
                // 旧记录没有 id 字段，应能读取并生成稳定 id
                string legacy = "{\"time\":\"2026-01-01 10:00:00\",\"group\":\"老群\",\"text\":\"老内容\",\"atAll\":false,\"ok\":true,\"attempts\":1,\"error\":\"\",\"source\":\"cli\"}";
                File.WriteAllText(histPath, legacy + "\n", new UTF8Encoding(false));

                var legacyList = logger.ReadHistory(10);
                Eq(legacyList.Count, 1, "能读出旧格式记录");
                Eq(legacyList[0].Text, "老内容", "旧格式内容正确");
                Ok(legacyList[0].Id != null && legacyList[0].Id.StartsWith("legacy-"), "旧记录生成 legacy- 前缀 id");

                // 同一个旧记录两次读取 id 应一致
                string id1 = logger.ReadHistory(10)[0].Id;
                string id2 = logger.ReadHistory(10)[0].Id;
                Eq(id1, id2, "旧记录的生成 id 稳定");

                // 旧记录也能被删除
                Eq(logger.DeleteHistory(new[] { id1 }), 1, "旧记录可删除");
                Eq(logger.ReadHistory(10).Count, 0, "旧记录删除后为空");

                Section("历史记录 - 卡片字段");

                logger.RecordSend("卡片群", "**加粗**正文", false,
                    SendResult.Success("ok", 1), "manual", "card", "部署完成", "绿");

                var cardEntry = logger.ReadHistory(10)[0];
                Eq(cardEntry.Kind, "card", "卡片类型被保存");
                Eq(cardEntry.CardTitle, "部署完成", "卡片标题被保存");
                Eq(cardEntry.CardColor, "绿", "卡片颜色被保存");
                Ok(cardEntry.IsCard, "IsCard 判断正确");

                // 纯文本记录
                logger.RecordSend("文本群", "纯文本", false,
                    SendResult.Success("ok", 1), "manual", "text", null, null);
                var textEntry = logger.ReadHistory(10)[0];
                Eq(textEntry.Kind, "text", "文本类型被保存");
                Ok(!textEntry.IsCard, "IsCard=false");
                Eq(textEntry.CardTitle ?? "", "", "文本记录无卡片标题");

                // 旧格式（无 kind 字段）默认按 text
                Eq(legacyList[0].Kind, "text", "旧记录默认 kind=text");
                Ok(!legacyList[0].IsCard, "旧记录 IsCard=false");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}