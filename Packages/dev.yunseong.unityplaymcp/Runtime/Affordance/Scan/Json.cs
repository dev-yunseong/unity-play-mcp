using System.Text;

namespace Artel.Affordances.Scan
{
    /// <summary>문서를 쓰기에 딱 필요한 만큼의 JSON. 읽는 쪽은 없다.</summary>
    /// <remarks>
    /// 패키지가 직렬화 의존성을 게임 어셈블리로 들이지 않도록 손으로 썼다. 분석기가 구운 근거는 쓰인 그대로 통과하고
    /// 여기서 파싱되는 일이 없으므로, 쓰는 절반만 있으면 된다.
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
