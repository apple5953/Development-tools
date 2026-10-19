using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DevelopmentTools.ProjectStandards.Infrastructure.Json
{
    /// <summary>
    /// Deterministic, canonical JSON serializer that ensures identical objects
    /// serialize to bit-for-bit identical JSON strings by sorting properties and dictionary keys.
    /// </summary>
    public static class SimpleJson
    {
        public static string SerializeCanonical(object obj)
        {
            var sb = new StringBuilder();
            WriteValue(obj, sb);
            return sb.ToString();
        }

        private static void WriteValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string)
            {
                WriteString((string)value, sb);
            }
            else if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
            }
            else if (value is double)
            {
                sb.Append(Math.Round((double)value, 6).ToString("0.######", CultureInfo.InvariantCulture));
            }
            else if (value is float)
            {
                sb.Append(Math.Round((float)value, 6).ToString("0.######", CultureInfo.InvariantCulture));
            }
            else if (value is decimal)
            {
                sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
            }
            else if (value is int || value is long || value is short || value is byte || value is uint || value is ulong || value is ushort)
            {
                sb.Append(value.ToString());
            }
            else if (value is Guid)
            {
                WriteString(((Guid)value).ToString("D").ToLowerInvariant(), sb);
            }
            else if (value is Enum)
            {
                WriteString(value.ToString(), sb);
            }
            else if (value is IDictionary)
            {
                WriteDictionary((IDictionary)value, sb);
            }
            else if (value is IEnumerable)
            {
                WriteArray((IEnumerable)value, sb);
            }
            else
            {
                WriteObject(value, sb);
            }
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('\"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('\"');
        }

        private static void WriteArray(IEnumerable enumerable, StringBuilder sb)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(item, sb);
            }
            sb.Append(']');
        }

        private static void WriteDictionary(IDictionary dict, StringBuilder sb)
        {
            sb.Append('{');
            var keys = new List<string>();
            var map = new Dictionary<string, object>();
            foreach (DictionaryEntry entry in dict)
            {
                var k = entry.Key != null ? entry.Key.ToString() : string.Empty;
                keys.Add(k);
                map[k] = entry.Value;
            }
            keys.Sort(StringComparer.Ordinal);

            bool first = true;
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (!first) sb.Append(',');
                first = false;
                WriteString(k, sb);
                sb.Append(':');
                WriteValue(map[k], sb);
            }
            sb.Append('}');
        }

        private static void WriteObject(object obj, StringBuilder sb)
        {
            sb.Append('{');
            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sortedProps = new List<PropertyInfo>(props);
            sortedProps.Sort(new Comparison<PropertyInfo>(ComparePropertyNames));

            bool first = true;
            for (int i = 0; i < sortedProps.Count; i++)
            {
                PropertyInfo p = sortedProps[i];
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length > 0) continue;

                var val = p.GetValue(obj, null);
                if (!first) sb.Append(',');
                first = false;
                WriteString(p.Name, sb);
                sb.Append(':');
                WriteValue(val, sb);
            }
            sb.Append('}');
        }

        private static int ComparePropertyNames(PropertyInfo p1, PropertyInfo p2)
        {
            return string.CompareOrdinal(p1.Name, p2.Name);
        }
    }
}
