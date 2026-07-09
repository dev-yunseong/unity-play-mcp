using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Artel
{
    internal static class MiniJson
    {
        public static Dictionary<string, object> ParseObject(string json)
        {
            var parser = new Parser(json);
            var value = parser.ParseValue();
            if (value is Dictionary<string, object> obj)
            {
                return obj;
            }

            throw new FormatException("Root JSON value must be an object.");
        }

        public static string GetString(Dictionary<string, object> obj, string key, string defaultValue = "")
        {
            return obj.TryGetValue(key, out var value) && value != null ? value.ToString() : defaultValue;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int defaultValue = 0)
        {
            if (!obj.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            if (value is long longValue)
            {
                return (int)longValue;
            }

            return int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            return obj.TryGetValue(key, out var value) && value is List<object> array ? array : new List<object>();
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json ?? string.Empty;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (index >= json.Length)
                {
                    throw new FormatException("Unexpected end of JSON.");
                }

                var c = json[index];
                if (c == '{')
                {
                    return ParseObjectValue();
                }

                if (c == '[')
                {
                    return ParseArrayValue();
                }

                if (c == '"')
                {
                    return ParseStringValue();
                }

                if (c == '-' || char.IsDigit(c))
                {
                    return ParseNumberValue();
                }

                if (Consume("true"))
                {
                    return true;
                }

                if (Consume("false"))
                {
                    return false;
                }

                if (Consume("null"))
                {
                    return null;
                }

                throw new FormatException("Unexpected JSON token at " + index + ".");
            }

            private Dictionary<string, object> ParseObjectValue()
            {
                Expect('{');
                var result = new Dictionary<string, object>();
                SkipWhitespace();
                if (TryExpect('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseStringValue();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();

                    if (TryExpect('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArrayValue()
            {
                Expect('[');
                var result = new List<object>();
                SkipWhitespace();
                if (TryExpect(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryExpect(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseStringValue()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (index < json.Length)
                {
                    var c = json[index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        throw new FormatException("Invalid JSON escape.");
                    }

                    var escape = json[index++];
                    switch (escape)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escape);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw new FormatException("Invalid JSON escape: " + escape);
                    }
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private object ParseNumberValue()
            {
                var start = index;
                if (json[index] == '-')
                {
                    index++;
                }

                while (index < json.Length && char.IsDigit(json[index]))
                {
                    index++;
                }

                var isDecimal = false;
                if (index < json.Length && json[index] == '.')
                {
                    isDecimal = true;
                    index++;
                    while (index < json.Length && char.IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    isDecimal = true;
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    {
                        index++;
                    }

                    while (index < json.Length && char.IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                var token = json.Substring(start, index - start);
                if (isDecimal)
                {
                    return double.Parse(token, CultureInfo.InvariantCulture);
                }

                return long.Parse(token, CultureInfo.InvariantCulture);
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length)
                {
                    throw new FormatException("Invalid unicode escape.");
                }

                var hex = json.Substring(index, 4);
                index += 4;
                return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            private bool Consume(string token)
            {
                SkipWhitespace();
                if (index + token.Length > json.Length)
                {
                    return false;
                }

                if (string.Compare(json, index, token, 0, token.Length, StringComparison.Ordinal) != 0)
                {
                    return false;
                }

                index += token.Length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            private void Expect(char c)
            {
                SkipWhitespace();
                if (index >= json.Length || json[index] != c)
                {
                    throw new FormatException("Expected '" + c + "' at " + index + ".");
                }

                index++;
            }

            private bool TryExpect(char c)
            {
                SkipWhitespace();
                if (index < json.Length && json[index] == c)
                {
                    index++;
                    return true;
                }

                return false;
            }
        }
    }
}
