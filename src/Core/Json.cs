using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LarkSender.Core
{
    /// <summary>
    /// 极简 JSON 读写。
    ///
    /// 为什么不直接用 System.Text.Json / Newtonsoft：
    /// 本项目只依赖 .NET Framework 4.x 自带的 csc.exe 编译，不引入任何 NuGet 包，
    /// 保持「clone 下来 build.cmd 就能编译」这个特性。
    /// System.Web.Extensions 里的 JavaScriptSerializer 已废弃，故手写一个够用的实现。
    ///
    /// 能力范围（刻意保持最小）：
    ///   - 解析：对象、数组、字符串、数字、bool、null
    ///   - 序列化：同上，对象用 Dictionary&lt;string, object&gt; 表示
    ///   - 不支持：注释、NaN/Infinity、超深嵌套
    /// </summary>
    public static class Json
    {
        // ==================== 解析 ====================

        /// <summary>把 JSON 文本解析为 Dictionary / List / string / double / bool / null</summary>
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("输入为 null");
            var p = new Parser(text);
            p.SkipWhitespace();
            var v = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new JsonException("解析结束后仍有残留内容，位置 " + p.Position);
            return v;
        }

        /// <summary>解析并断言根节点是对象</summary>
        public static Dictionary<string, object> ParseObject(string text)
        {
            var v = Parse(text) as Dictionary<string, object>;
            if (v == null) throw new JsonException("根节点不是 JSON 对象");
            return v;
        }

        private class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; _i = 0; }

            public int Position { get { return _i; } }
            public bool AtEnd { get { return _i >= _s.Length; } }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _i++; continue; }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new JsonException("意外的文本结尾");

                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObj();
                    case '[': return ParseArr();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new JsonException("无法识别的字符 '" + c + "'，位置 " + _i);
                }
            }

            private void Expect(string word)
            {
                if (_i + word.Length > _s.Length) throw new JsonException("期望 " + word);
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                    throw new JsonException("期望 " + word + "，位置 " + _i);
                _i += word.Length;
            }

            private Dictionary<string, object> ParseObj()
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return dict; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw new JsonException("对象键必须是字符串，位置 " + _i);
                    string key = ParseString();

                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw new JsonException("缺少冒号，位置 " + _i);
                    _i++;

                    var val = ParseValue();
                    dict[key] = val;

                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("对象未闭合");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return dict; }
                    throw new JsonException("对象中出现意外字符 '" + _s[_i] + "'，位置 " + _i);
                }
            }

            private List<object> ParseArr()
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }

                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("数组未闭合");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw new JsonException("数组中出现意外字符 '" + _s[_i] + "'，位置 " + _i);
                }
            }

            private string ParseString()
            {
                _i++; // 起始引号
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new JsonException("字符串未闭合");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (AtEnd) throw new JsonException("转义符后文本结束");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new JsonException("\\u 转义不完整");
                            string hex = _s.Substring(_i, 4);
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new JsonException("非法的 \\u 转义: " + hex);
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw new JsonException("未知转义 \\" + e);
                    }
                }
            }

            private object ParseNumber()
            {
                int start = _i;
                if (!AtEnd && _s[_i] == '-') _i++;
                while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                if (!AtEnd && _s[_i] == '.')
                {
                    _i++;
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }

                string num = _s.Substring(start, _i - start);
                double d;
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new JsonException("非法数字: " + num);
                return d;
            }
        }

        // ==================== 序列化 ====================

        /// <summary>序列化为紧凑 JSON</summary>
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        /// <summary>序列化为带缩进的 JSON（便于人工查看配置文件）</summary>
        public static string SerializeIndented(object value)
        {
            var sb = new StringBuilder();
            WriteValueIndented(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }

            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
                // 整数形态的 double 不带小数点
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }

            WriteString(sb, v.ToString());
        }

        private static void WriteValueIndented(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (!(v is IDictionary<string, object>) && !(v is System.Collections.IEnumerable && !(v is string)))
            {
                WriteValue(sb, v);
                return;
            }

            string pad = new string(' ', depth * 2);
            string padInner = new string(' ', (depth + 1) * 2);

            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int n = 0;
                foreach (var kv in dict)
                {
                    sb.Append(padInner);
                    WriteString(sb, kv.Key);
                    sb.Append(": ");
                    WriteValueIndented(sb, kv.Value, depth + 1);
                    if (++n < dict.Count) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(pad).Append('}');
                return;
            }

            var list = (System.Collections.IEnumerable)v;
            bool any = false;
            var inner = new StringBuilder();
            foreach (var item in list)
            {
                if (any) inner.Append(",\n");
                inner.Append(padInner);
                WriteValueIndented(inner, item, depth + 1);
                any = true;
            }
            if (!any) { sb.Append("[]"); return; }
            sb.Append("[\n").Append(inner).Append('\n').Append(pad).Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ==================== 取值辅助 ====================

        /// <summary>从字典安全取字符串</summary>
        public static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            if (dict == null) return fallback;
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return fallback;
            return v as string ?? v.ToString();
        }

        /// <summary>从字典安全取 bool</summary>
        public static bool GetBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            if (dict == null) return fallback;
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            bool b;
            if (bool.TryParse(v.ToString(), out b)) return b;
            return fallback;
        }

        /// <summary>从字典安全取 int</summary>
        public static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            if (dict == null) return fallback;
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double)
            {
                double d = (double)v;
                if (d >= int.MinValue && d <= int.MaxValue) return (int)d;
                return fallback;
            }
            int i;
            if (int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            return fallback;
        }

        /// <summary>从字典安全取数组</summary>
        public static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return new List<object>();
            object v;
            if (!dict.TryGetValue(key, out v)) return new List<object>();
            var l = v as List<object>;
            return l ?? new List<object>();
        }

        /// <summary>从字典安全取子对象</summary>
        public static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object v;
            if (!dict.TryGetValue(key, out v)) return null;
            return v as Dictionary<string, object>;
        }
    }

    /// <summary>JSON 解析异常</summary>
    public class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}