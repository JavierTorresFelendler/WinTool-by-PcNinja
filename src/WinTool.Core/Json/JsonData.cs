using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace WinTool.Core.Json
{
    /// <summary>
    /// A small JSON reader for WinTool's own files and PowerShell output, so the
    /// single EXE needs no System.Text.Json assemblies. Names match without regard
    /// to case. Records are built through their constructor; other types through
    /// a parameterless constructor and settable properties.
    /// </summary>
    public static class JsonData
    {
        public static T Deserialize<T>(string json) => (T)Convert(Parse(json), typeof(T));

        public static string Serialize(object value) => JsonText.Serialize(value);

        /// <summary>Objects become case-insensitive dictionaries; numbers become long or double.</summary>
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("Unexpected text after the JSON value");
            return value;
        }

        public static object Convert(object value, Type type)
        {
            if (value == null) return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(object)) return value;
            if (target == typeof(string)) return value as string ?? System.Convert.ToString(value, CultureInfo.InvariantCulture);
            if (target == typeof(bool)) return value is bool b ? b : bool.Parse(value.ToString());
            if (target.IsEnum) return value is string name ? Enum.Parse(target, name, true) : Enum.ToObject(target, System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
            if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (target == typeof(DateTime)) return DateTime.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (target == typeof(Guid)) return Guid.Parse((string)value);
            if (target == typeof(TimeSpan)) return TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
            if (target.IsPrimitive || target == typeof(decimal)) return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);

            if (value is List<object> list)
            {
                var element = ElementType(target);
                if (element == null) throw new FormatException("Cannot read a JSON array into " + target.Name + ".");
                var items = list.Select(item => Convert(item, element)).ToList();
                if (target.IsArray)
                {
                    var array = Array.CreateInstance(element, items.Count);
                    for (var i = 0; i < items.Count; i++) array.SetValue(items[i], i);
                    return array;
                }

                var typed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                foreach (var item in items) typed.Add(item);
                return typed;
            }

            if (value is Dictionary<string, object> map) return BuildObject(map, target);
            throw new FormatException("Cannot read " + value.GetType().Name + " into " + target.Name + ".");
        }

        private static Type ElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericArguments().Length == 1) return type.GetGenericArguments()[0];
            return null;
        }

        private static object BuildObject(Dictionary<string, object> map, Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
            {
                var valueType = type.GetGenericArguments()[1];
                var dictionary = (IDictionary)Activator.CreateInstance(type);
                foreach (var pair in map) dictionary[pair.Key] = Convert(pair.Value, valueType);
                return dictionary;
            }

            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length).ToList();
            var withParameters = constructors.FirstOrDefault(c => c.GetParameters().Length > 0 && c.GetParameters().All(p => map.ContainsKey(p.Name) || p.HasDefaultValue || IsOptionalType(p.ParameterType)));
            object instance;
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (withParameters != null && constructors.All(c => c.GetParameters().Length > 0 || c == withParameters || !c.IsPublic))
            {
                var parameters = withParameters.GetParameters();
                var arguments = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    object raw;
                    if (map.TryGetValue(parameters[i].Name, out raw))
                    {
                        arguments[i] = Convert(raw, parameters[i].ParameterType);
                        used.Add(parameters[i].Name);
                    }
                    else
                    {
                        arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Convert(null, parameters[i].ParameterType);
                    }
                }

                instance = withParameters.Invoke(arguments);
            }
            else
            {
                var parameterless = constructors.FirstOrDefault(c => c.GetParameters().Length == 0)
                    ?? throw new FormatException(type.Name + " has no constructor JSON can use.");
                instance = parameterless.Invoke(new object[0]);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object raw;
                if (used.Contains(property.Name) || !map.TryGetValue(property.Name, out raw)) continue;
                var setter = property.GetSetMethod(true);
                if (setter == null) continue;
                setter.Invoke(instance, new[] { Convert(raw, property.PropertyType) });
            }

            return instance;
        }

        private static bool IsOptionalType(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        private sealed class Reader
        {
            private readonly string text;
            private int position;

            public Reader(string text)
            {
                this.text = text;
                if (text.Length > 0 && text[0] == '\uFEFF') position = 1;
            }

            public bool AtEnd => position >= text.Length;

            public void SkipWhitespace()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            }

            public FormatException Error(string message) => new FormatException(message + " at position " + position + ".");

            public object ReadValue(int depth)
            {
                if (depth > 64) throw Error("JSON is nested too deeply");
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of JSON");
                var c = text[position];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c)) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                position++;
                SkipWhitespace();
                if (!AtEnd && text[position] == '}') { position++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[position] != '"') throw Error("Expected a property name");
                    var name = ReadString();
                    SkipWhitespace();
                    if (AtEnd || text[position] != ':') throw Error("Expected ':'");
                    position++;
                    result[name] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON");
                    if (text[position] == ',') { position++; continue; }
                    if (text[position] == '}') { position++; return result; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                position++;
                SkipWhitespace();
                if (!AtEnd && text[position] == ']') { position++; return result; }
                while (true)
                {
                    result.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON");
                    if (text[position] == ',') { position++; continue; }
                    if (text[position] == ']') { position++; return result; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                position++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    var c = text[position++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\') { builder.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    var e = text[position++];
                    switch (e)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length) throw Error("Bad unicode escape");
                            builder.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            position += 4;
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            private object ReadNumber()
            {
                var start = position;
                if (text[position] == '-') position++;
                while (position < text.Length && (char.IsDigit(text[position]) || "+-.eE".IndexOf(text[position]) >= 0)) position++;
                var token = text.Substring(start, position - start);
                long whole;
                if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole)) return whole;
                double real;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out real)) return real;
                throw Error("Bad number '" + token + "'");
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
                position += word.Length;
            }
        }
    }
}
