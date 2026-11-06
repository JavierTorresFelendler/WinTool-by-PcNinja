using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace WinTool.Core.Json
{
    /// <summary>
    /// Writes any value as indented JSON for "--json" output: objects by their
    /// public properties (camelCase), dictionaries, lists, enums as names,
    /// dates as ISO 8601. Output only; the format is stable for scripts.
    /// </summary>
    public static class JsonText
    {
        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value, 0);
            return builder.ToString();
        }

        private static void Write(StringBuilder b, object value, int depth)
        {
            if (depth > 20) { b.Append("null"); return; }
            switch (value)
            {
                case null: b.Append("null"); return;
                case string s: WriteString(b, s); return;
                case bool flag: b.Append(flag ? "true" : "false"); return;
                case char c: WriteString(b, c.ToString()); return;
                case Enum e: WriteString(b, e.ToString()); return;
                case DateTime time: WriteString(b, time.ToString("s", CultureInfo.InvariantCulture)); return;
                case DateTimeOffset offset: WriteString(b, offset.ToString("o", CultureInfo.InvariantCulture)); return;
                case TimeSpan span: WriteString(b, span.ToString("c", CultureInfo.InvariantCulture)); return;
                case Guid guid: WriteString(b, guid.ToString()); return;
                case float f: b.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: b.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); return;
                case decimal m: b.Append(m.ToString(CultureInfo.InvariantCulture)); return;
            }

            if (value is IFormattable && value.GetType().IsPrimitive)
            {
                b.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary dictionary)
            {
                var keys = dictionary.Keys.Cast<object>().ToList();
                WriteObject(b, keys.Select(k => Convert.ToString(k, CultureInfo.InvariantCulture)).ToList(), keys.Select(k => dictionary[k]).ToList(), depth, false);
                return;
            }

            if (value is IEnumerable sequence)
            {
                var items = sequence.Cast<object>().ToList();
                if (items.Count == 0) { b.Append("[]"); return; }
                b.Append("[");
                for (var i = 0; i < items.Count; i++)
                {
                    b.Append(i == 0 ? "\n" : ",\n").Append(' ', (depth + 1) * 2);
                    Write(b, items[i], depth + 1);
                }

                b.Append('\n').Append(' ', depth * 2).Append(']');
                return;
            }

            var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToList();
            WriteObject(b, properties.Select(p => p.Name).ToList(), properties.Select(p => Read(p, value)).ToList(), depth, true);
        }

        private static object Read(PropertyInfo property, object owner)
        {
            try { return property.GetValue(owner, null); }
            catch (TargetInvocationException ex) { return "(error: " + ex.InnerException?.Message + ")"; }
        }

        private static void WriteObject(StringBuilder b, System.Collections.Generic.IList<string> names, System.Collections.Generic.IList<object> values, int depth, bool camelCase)
        {
            if (names.Count == 0) { b.Append("{}"); return; }
            b.Append("{");
            for (var i = 0; i < names.Count; i++)
            {
                b.Append(i == 0 ? "\n" : ",\n").Append(' ', (depth + 1) * 2);
                WriteString(b, camelCase ? CamelCase(names[i]) : names[i]);
                b.Append(": ");
                Write(b, values[i], depth + 1);
            }

            b.Append('\n').Append(' ', depth * 2).Append('}');
        }

        private static string CamelCase(string name) =>
            string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

        private static void WriteString(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }

            b.Append('"');
        }
    }
}
