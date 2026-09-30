using System.Text;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 객체를 계층 경로로 이름 붙인다.
    /// </summary>
    /// <remarks>
    /// 인스턴스 id 는 재시작하면 바뀌고 이름만으로는 유일하지 않으므로, 사람이 읽고 테스트가 다시 찾을 수 있는 경로를 쓴다.
    /// </remarks>
    internal static class ScenePath
    {
        /// <summary>따라가는 계층의 최대 깊이. 넘으면 경로 앞을 자른다.</summary>
        private const int MaxDepth = 64;

        internal static string Of(Transform transform)
        {
            return Of(transform, -1);
        }

        /// <summary>
        /// 각 단계에 sibling index 를 붙인 경로. `selector` 로 쓴다.
        /// </summary>
        /// <remarks>
        /// 생성된 객체는 같은 경로를 공유하므로 sibling index 로 구분한다. 맨 경로는 대신하지 않고 옆에 쓴다.
        /// 생성된 객체의 index 는 생성 순서이며 그 실행 동안만 유지된다.
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

            // 루트의 GetSiblingIndex 는 항상 0 이므로 루트 순서는 순회가 건넨 rootIndex 를 쓴다.
            if (numbered && current == null && count > 0)
            {
                parts[count - 1] = transform.root.name + "[" + rootIndex + "]";
            }

            var path = new StringBuilder();

            // 깊이 제한에 걸렸거나 계층이 순환하면 루트 객체로 오인되지 않게 잘렸다고 표시한다.
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
