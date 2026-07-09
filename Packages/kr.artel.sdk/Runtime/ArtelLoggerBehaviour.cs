using UnityEngine;

namespace Artel
{
    public sealed class ArtelLoggerBehaviour : MonoBehaviour
    {
        [SerializeField] private string message = "Artel SDK loaded.";

        private void Start()
        {
            Debug.Log("[Artel] " + message);
        }
    }
}
