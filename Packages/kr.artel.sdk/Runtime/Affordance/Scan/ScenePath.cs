using System.Text;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 객체를 그것이 앉은 자리로 이름 붙인다.
    /// </summary>
    /// <remarks>
    /// 명세가 작용할 수 있는 정체다. 인스턴스 id 는 재시작을 건너 아무 뜻도 없고 맨 이름은 유일한 일이 드물지만, 계층을
    /// 따라 내려간 경로는 사람이 에디터에서 읽는 것이고 테스트 실행기가 다시 찾아볼 수 있는 것이다.
    /// </remarks>
    internal static class ScenePath
    {
        /// <summary>경로를 일부만 남기기 전까지 계층을 얼마나 깊이 따라가는지.</summary>
        private const int MaxDepth = 64;

        internal static string Of(Transform transform)
        {
            return Of(transform, -1);
        }

        /// <summary>
        /// 같은 걷기인데, 각 걸음이 제 부모의 몇 번째 자식인지를 말하는 것.
        /// </summary>
        /// <remarks>
        /// 게임이 무언가를 만들어낼 때 이름은 정체가 아니다. 한 종류의 적 다섯은 한 경로 위의 객체 다섯이고 — 샘플 게임에서
        /// <c>TurnBattleScene/RangedCat(Clone)</c> 이 다섯 번 — 그것을 클릭하라고 들은 테스트는 아무것도 듣지 못한 것이다.
        /// 형제들 사이의 자리가 그것들을 가르고, 그것은 실행기가 스스로 셀 수 있는 것이다.
        ///
        /// 맨 경로를 대신하지 않고 그 옆에 쓴다. 맨 경로는 사람이 읽는 것이고 리포트의 나머지가 이미 그것으로 잇는 것이다.
        /// 이쪽은 다섯 중 하나를 골라야 하는 쪽을 위한 것이다.
        ///
        /// 이것은 그것이 어디 있었는지를 말하지 어느 것인지를 말하지 않는다. 자식이 앉는 순서는 씬이 작성될 때부터 있던 객체에
        /// 대해서는 고정이고, 게임이 만든 것에 대해서는 만들어진 순서이며 그 실행이 지속되는 동안 유지된다. 여기서 그 이상은
        /// 주장하지 않는다.
        /// </remarks>
        internal static string SelectorOf(Transform transform, int rootIndex)
        {
            return Of(transform, rootIndex);
        }

        private static string Of(Transform transform, int rootIndex = -1)
        {
            if (transform == null)
            {
                return null;
            }

            var numbered = rootIndex >= 0;
            var parts = new string[MaxDepth];
            var count = 0;
            var current = transform;

            while (current != null && count < MaxDepth)
            {
                parts[count++] = numbered
                    ? current.name + "[" + current.GetSiblingIndex() + "]"
                    : current.name;

                current = current.parent;
            }

            // 루트가 씬의 루트들 사이에서 갖는 자리는 sibling index 가 아니다 — Unity 는 루트가 몇이든 그것을 0 으로
            // 답하는데, 그래서 만들어진 적 다섯이 전부 `[0]` 이었다. 순서를 아는 것은 순회 쪽이므로 순회가 말한다.
            if (numbered && current == null && count > 0)
            {
                parts[count - 1] = transform.root.name + "[" + rootIndex + "]";
            }

            var path = new StringBuilder();

            // 경계보다 깊은 계층이나, 망가진 프리팹이 순환하게 만든 계층은, 실제로는 아닌 루트 레벨 객체로 보고하는 대신
            // 잘렸다고 말한다.
            if (current != null)
            {
                path.Append(".../");
            }

            for (var index = count - 1; index >= 0; index--)
            {
                path.Append(parts[index]);

                if (index > 0)
                {
                    path.Append('/');
                }
            }

            return path.ToString();
        }
    }
}
