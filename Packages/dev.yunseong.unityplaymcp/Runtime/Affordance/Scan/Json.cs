using System.Text;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>문서 작성에 필요한 JSON 쓰기만 구현한다.</summary>
    /// <remarks>
    /// 게임 어셈블리에 직렬화 의존성을 들이지 않으려고 직접 썼다. 분석기가 만든 `evidence` 는 그대로 통과하므로 파서는 필요 없다.
    /// </remarks>
    internal static class Json
    {
        internal static void Property(StringBuilder text, string name, string value)
        {
            String(text, name);
            text.Append(':');
            String(text, value);
        }

        internal static void Property(StringBuilder text, string name, int value)
        {
            String(text, name);
            text.Append(':').Append(value);
        }

        internal static void Property(StringBuilder text, string name, bool value)
        {
            String(text, name);
            text.Append(value ? ":true" : ":false");
        }

        internal static void String(StringBuilder text, string value)
        {
            if (value == null)
            {
                text.Append("null");
                return;
            }

            text.Append('"');

            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 32)
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            text.Append(character);
                        }

                        break;
                }
            }

            text.Append('"');
        }
    }
}
