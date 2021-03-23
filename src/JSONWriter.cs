using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace TinyJson
{
    //Really simple JSON writer
    //- Outputs JSON structures from an object
    //- Really simple API (new List<int> { 1, 2, 3 }).ToJson() == "[1,2,3]"
    //- Will only output public fields and property getters on objects
    public static class JSONWriter
    {
        [ThreadStatic] static bool outputNullObjectProperties;

        public static string ToJson(this object item, bool outputNullObjectProperties = false)
        {
            JSONWriter.outputNullObjectProperties = outputNullObjectProperties;
            StringBuilder stringBuilder = new StringBuilder();
            AppendValue(stringBuilder, item);
            return stringBuilder.ToString();
        }

        static void AppendValue(StringBuilder stringBuilder, object item)
        {
            if (item == null)
            {
                stringBuilder.Append("null");
                return;
            }

            Type type = item.GetType();
            if (type == typeof(string) || type == typeof(char))
            {
                stringBuilder.Append('"');
                string str = item.ToString();
                for (int i = 0; i < str.Length; ++i)
                    if (str[i] < ' ' || str[i] == '"' || str[i] == '\\')
                    {
                        stringBuilder.Append('\\');
                        int j = "\"\\\n\r\t\b\f".IndexOf(str[i]);
                        if (j >= 0)
                            stringBuilder.Append("\"\\nrtbf"[j]);
                        else
                            stringBuilder.AppendFormat("u{0:X4}", (UInt32)str[i]);
                    }
                    else
                        stringBuilder.Append(str[i]);
                stringBuilder.Append('"');
            }
            else if (type == typeof(byte) || type == typeof(sbyte))
            {
                stringBuilder.Append(item.ToString());
            }
            else if (type == typeof(short) || type == typeof(ushort))
            {
                stringBuilder.Append(item.ToString());
            }
            else if (type == typeof(int) || type == typeof(uint))
            {
                stringBuilder.Append(item.ToString());
            }
            else if (type == typeof(long) || type == typeof(ulong))
            {
                stringBuilder.Append(item.ToString());
            }
            else if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            {
                stringBuilder.Append(item.ToString());
                stringBuilder.AppendFormat(" /* 0x{0} */", type == typeof(IntPtr) ? ((IntPtr)item).ToInt64().ToString("X8") : ((UIntPtr)item).ToUInt64().ToString("X8"));
            }
            else if (type == typeof(float))
            {
                stringBuilder.Append(((float)item).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (type == typeof(double))
            {
                stringBuilder.Append(((double)item).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (type == typeof(decimal))
            {
                stringBuilder.Append(((decimal)item).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (type == typeof(bool))
            {
                stringBuilder.Append(((bool)item) ? "true" : "false");
            }
            else if (type == typeof(DateTime))
            {
                stringBuilder.Append('"');
                stringBuilder.Append(((DateTime)item).ToString("o"));
                stringBuilder.Append('"');
            }
            else if (type == typeof(TimeSpan))
            {
                stringBuilder.Append('"');
                stringBuilder.Append(((TimeSpan)item).ToString("c"));
                stringBuilder.Append('"');
            }
            else if (type.IsEnum)
            {
                stringBuilder.Append('"');
                stringBuilder.Append(item.ToString());
                stringBuilder.Append('"');
            }
            else if (item is IDictionary || (item is IEnumerable && type.GetGenericArguments().FirstOrDefault() == typeof(DictionaryEntry)))
            {
                IDictionary dict = item as IDictionary ?? (item as IEnumerable).Cast<DictionaryEntry>().ToDictionary(x => x.Key, x => x.Value);
                stringBuilder.Append('{');
                bool isFirst = true;
                //Refuse to output dictionary keys that aren't of type string
                foreach (object key in dict.Keys.Cast<object>().Where(x => x.GetType() == typeof(string)))
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        stringBuilder.Append(',');
                    stringBuilder.Append('\"');
                    stringBuilder.Append(key.ToString());
                    stringBuilder.Append("\":");
                    AppendValue(stringBuilder, dict[key]);
                }
                stringBuilder.Append('}');
            }
            else if (item is IEnumerable)
            {
                stringBuilder.Append('[');
                bool isFirst = true;
                foreach (var enumItem in item as IEnumerable)
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        stringBuilder.Append(',');
                    AppendValue(stringBuilder, enumItem);
                }
                stringBuilder.Append(']');
            }
            else
            {
                stringBuilder.Append('{');

                bool isFirst = true;
                FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                for (int i = 0; i < fieldInfos.Length; i++)
                {
                    if (fieldInfos[i].IsDefined(typeof(IgnoreDataMemberAttribute), true))
                        continue;

                    object value = fieldInfos[i].GetValue(item);
                    if (value != null || outputNullObjectProperties)
                    {
                        if (isFirst)
                            isFirst = false;
                        else
                            stringBuilder.Append(',');
                        stringBuilder.Append('\"');
                        stringBuilder.Append(GetMemberName(fieldInfos[i]));
                        stringBuilder.Append("\":");
                        AppendValue(stringBuilder, value);
                    }
                }
                PropertyInfo[] propertyInfo = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                for (int i = 0; i < propertyInfo.Length; i++)
                {
                    if (!propertyInfo[i].CanRead || propertyInfo[i].IsDefined(typeof(IgnoreDataMemberAttribute), true))
                        continue;

                    object value = propertyInfo[i].GetValue(item, null);
                    if (value != null || outputNullObjectProperties)
                    {
                        if (isFirst)
                            isFirst = false;
                        else
                            stringBuilder.Append(',');
                        stringBuilder.Append('\"');
                        stringBuilder.Append(GetMemberName(propertyInfo[i]));
                        stringBuilder.Append("\":");
                        AppendValue(stringBuilder, value);
                    }
                }

                stringBuilder.Append('}');
            }
        }

        static string GetMemberName(MemberInfo member)
        {
            if (member.IsDefined(typeof(DataMemberAttribute), true))
            {
                DataMemberAttribute dataMemberAttribute = (DataMemberAttribute)Attribute.GetCustomAttribute(member, typeof(DataMemberAttribute), true);
                if (!string.IsNullOrEmpty(dataMemberAttribute.Name))
                    return dataMemberAttribute.Name;
            }

            return member.Name;
        }
    }
}
