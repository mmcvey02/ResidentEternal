using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// A small JSON reader/writer. Cities: Skylines runs on Mono's .NET 3.5 profile, which has no JSON library.
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length)
                throw new FormatException("trailing characters at " + i);
            return v;
        }

        /// <summary>Parses one message; null if it isn't a JSON object with a string "t".</summary>
        public static Dictionary<string, object> ParseMessage(string line)
        {
            try
            {
                var o = Parse(line) as Dictionary<string, object>;
                if (o == null || !(Get(o, "t") is string))
                    return null;
                return o;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static object Get(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(Dictionary<string, object> o, string key, string fallback = "")
        {
            return Get(o, key) as string ?? fallback;
        }

        public static double Num(Dictionary<string, object> o, string key, double fallback = 0)
        {
            object v = Get(o, key);
            return v is double ? (double)v : fallback;
        }

        public static bool Bool(Dictionary<string, object> o, string key, bool fallback = false)
        {
            object v = Get(o, key);
            return v is bool ? (bool)v : fallback;
        }

        /// <summary>Reads a numeric array into dst; false if it is missing, short or not numeric.</summary>
        public static bool Nums(Dictionary<string, object> o, string key, double[] dst)
        {
            var list = Get(o, key) as List<object>;
            if (list == null || list.Count < dst.Length)
                return false;
            for (int i = 0; i < dst.Length; i++)
            {
                if (!(list[i] is double))
                    return false;
                dst[i] = (double)list[i];
            }
            return true;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length)
                throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
                return ParseObject(s, ref i);
            if (c == '[')
                return ParseArray(s, ref i);
            if (c == '"')
                return ParseString(s, ref i);
            if (Lit(s, ref i, "true"))
                return true;
            if (Lit(s, ref i, "false"))
                return false;
            if (Lit(s, ref i, "null"))
                return null;
            return ParseNumber(s, ref i);
        }

        static bool Lit(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                return false;
            i += word.Length;
            return true;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var o = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return o;
            }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("expected key at " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("expected ':' at " + i);
                i++;
                o[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length)
                    throw new FormatException("unterminated object");
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == '}')
                {
                    i++;
                    return o;
                }
                throw new FormatException("expected ',' or '}' at " + i);
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return a;
            }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length)
                    throw new FormatException("unterminated array");
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == ']')
                {
                    i++;
                    return a;
                }
                throw new FormatException("expected ',' or ']' at " + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    break;
                char e = s[i++];
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
                        if (i + 4 > s.Length)
                            throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default:
                        throw new FormatException("bad escape at " + i);
                }
            }
            throw new FormatException("unterminated string");
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
                i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+'))
                i++;
            double d;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("bad value at " + start);
            return d;
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null)
                sb.Append("null");
            else if (v is string)
                WriteString(sb, (string)v);
            else if (v is bool)
                sb.Append((bool)v ? "true" : "false");
            else if (v is double || v is float || v is int || v is long || v is uint || v is ushort || v is byte || v is short || v is ulong)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d))
                    sb.Append("null");
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (v is IDictionary)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in (IDictionary)v)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, e.Value);
                }
                sb.Append('}');
            }
            else if (v is IEnumerable)
            {
                sb.Append('[');
                bool first = true;
                foreach (object x in (IEnumerable)v)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    WriteValue(sb, x);
                }
                sb.Append(']');
            }
            else
                WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
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
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Builds an object inline: Json.Obj("t", "city", "inf", 0.3).</summary>
        public static Dictionary<string, object> Obj(params object[] kv)
        {
            var o = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2)
                o[(string)kv[i]] = kv[i + 1];
            return o;
        }
    }
}
