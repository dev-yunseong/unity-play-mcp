using UnityEngine;

namespace UnityPlayMcp
{
    public sealed class LoadedNoticeBehaviour : MonoBehaviour
    {
        [SerializeField] private string message = "Unity Play MCP loaded.";

        private void Start()
        {
            Debug.Log("[Unity Play MCP] " + message);
        }
    }
}
