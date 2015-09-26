﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JsonParser
{
    //Going to reflect over structures to parse them on the fly. No non-standard JSON supported. No use of JIT emission. Minimal GC allocs and wastage. Maximum 32 KB of JSON is supported.
    public static class JSONParser
    {
        static Stack<List<string>> splitArrayPool = new Stack<List<string>>();
        static StringBuilder stringBuilder = new StringBuilder();

        public static T FromJson<T>(this string json)
        {
            //Remove all whitespace not within quotes
            stringBuilder.Length = 0;
            bool insideString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\"')
                {
                    i = AppendUntilStringEnd(true, stringBuilder, i, json);
                    continue;
                }
                else if (char.IsWhiteSpace(c) && !insideString)
                    continue;

                stringBuilder.Append(c);
            }

            //Parse the thing!
            return (T)ParseValue(typeof(T), stringBuilder.ToString());
        }

        static int AppendUntilStringEnd(bool appendEscapeCharacter, StringBuilder stringBuilder, int startIdx, string json)
        {
            stringBuilder.Append(json[startIdx]);
            for (int i = startIdx+1; i<json.Length; i++)
            {
                if (json[i] == '\\')
                {
                    if (appendEscapeCharacter)
                        stringBuilder.Append(json[i]);
                    stringBuilder.Append(json[i + 1]);
                    i++;//Skip next character as it is escaped
                }
                else if (json[i] == '\"')
                {
                    stringBuilder.Append(json[i]);
                    return i;
                }
                else
                    stringBuilder.Append(json[i]);
            }
            return json.Length - 1;
        }

        //Splits { <value>:<value>, <value>:<value> } and [ <value>, <value> ] into a list of <value> strings
        static List<string> Split(string json)
        {
            List<string> splitArray = splitArrayPool.Count > 0 ? splitArrayPool.Pop() : new List<string>();
            splitArray.Clear();
            int parseDepth = 0;
            stringBuilder.Length = 0;
            for (int i = 1; i<json.Length-1; i++)
            {
                switch (json[i])
                {
                    case '[':
                    case '{':
                        parseDepth++;
                        break;
                    case ']':
                    case '}':
                        parseDepth--;
                        break;
                    case '\"':
                        i = AppendUntilStringEnd(true, stringBuilder, i, json);
                        continue;
                    case ',':
                    case ':':
                        if (parseDepth == 0)
                        {
                            splitArray.Add(stringBuilder.ToString());
                            stringBuilder.Length = 0;
                            continue;
                        }
                        break;
                }

                stringBuilder.Append(json[i]);
            }

            splitArray.Add(stringBuilder.ToString());

            return splitArray;
        }

        static object ParseValue(Type type, string json)
        {
            if (type == typeof(string))
            {
                string str = json.Substring(1, json.Length - 2);
                return str.Replace("\\", string.Empty);
            }
            else if (type == typeof(int))
            {
                int result;
                int.TryParse(json, out result);
                return result;
            }
            else if (type == typeof(float))
            {
                float result;
                float.TryParse(json, out result);
                return result;
            }
            else if (type == typeof(double))
            {
                double result;
                double.TryParse(json, out result);
                return result;
            }
            else if (type == typeof(bool))
            {
                return json.ToLower() == "true";
            }
            else if (json == "null")
            {
                return null;
            }
            else if (type.IsArray)
            {
                Type arrayType = type.GetElementType();
                if (json[0] != '[' || json[json.Length - 1] != ']')
                    return null;

                List<string> elems = Split(json);
                Array newArray = Array.CreateInstance(arrayType, elems.Count);
                for (int i = 0; i < elems.Count; i++)
                    newArray.SetValue(ParseValue(arrayType, elems[i]), i);
                splitArrayPool.Push(elems);
                return newArray;
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type listType = type.GetGenericArguments()[0];
                if (json[0] != '[' || json[json.Length - 1] != ']')
                    return null;

                List<string> elems = Split(json);
                IList list = (IList)type.GetConstructor(new Type[] { typeof(int) }).Invoke(new object[] { elems.Count });
                for (int i = 0; i < elems.Count; i++)
                    list.Add(ParseValue(listType, elems[i]));
                splitArrayPool.Push(elems);
                return list;
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type keyType, valueType;
                {
                    Type[] args = type.GetGenericArguments();
                    keyType = args[0];
                    valueType = args[1];
                }

                //Refuse to parse dictionary keys that aren't of type string
                if (keyType != typeof(string))
                    return null;
                //Must be a valid dictionary element
                if (json[0] != '{' || json[json.Length - 1] != '}')
                    return null;
                //The list is split into key/value pairs only, this means the split must be divisible by 2 to be valid JSON
                List<string> elems = Split(json);
                if (elems.Count % 2 != 0)
                    return null;

                IDictionary dictionary = (IDictionary)type.GetConstructor(new Type[] { typeof(int) }).Invoke(new object[] { elems.Count / 2 });
                for (int i = 0; i < elems.Count; i += 2)
                {
                    string keyValue = elems[i].Substring(1, elems[i].Length - 2);
                    object value = ParseValue(valueType, elems[i + 1]);
                    dictionary.Add(keyValue, value);
                }
                return dictionary;
            }
            else if (json[0] == '{' && json[json.Length - 1] == '}')
            {
                object instance = FormatterServices.GetUninitializedObject(type);

                //The list is split into key/value pairs only, this means the split must be divisible by 2 to be valid JSON
                List<string> elems = Split(json);
                if (elems.Count % 2 != 0)
                    return instance;

                //TODO: cache this and build name -> set method map?
                FieldInfo[] fields = type.GetFields();
                PropertyInfo[] properties = type.GetProperties();
                for (int i = 0; i < elems.Count; i += 2)
                {
                    string key = elems[i].Substring(1, elems[i].Length - 2);
                    string value = elems[i + 1];

                    bool found = false;
                    for (int j = 0; j < fields.Length; j++)
                    {
                        if (fields[j].Name == key)
                        {
                            fields[j].SetValue(instance, ParseValue(fields[j].FieldType, value));
                            found = true;
                            break;
                        }
                    }
                    if (found)
                        continue;
                    for (int j = 0; j<properties.Length; j++)
                    {
                        if (properties[j].Name == key)
                        {
                            properties[j].SetValue(instance, ParseValue(properties[j].PropertyType, value));
                            break;
                        }
                    }
                }

                return instance;
            }
            
            return null;
        }
    }
}