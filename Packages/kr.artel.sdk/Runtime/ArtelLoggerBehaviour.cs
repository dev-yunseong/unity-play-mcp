using UnityEngine;

namespace Artel
{
    public sealed class ArtelLoggerBehaviour : MonoBehaviour
    {
        [SerializeField]
        private string message = "[Artel] tick";

        [SerializeField]
        private float intervalSeconds = 1f;

        private float elapsedSeconds;

        private void Update()
        {
            elapsedSeconds += Time.deltaTime;

            if (elapsedSeconds < intervalSeconds)
            {
                return;
            }

            elapsedSeconds -= intervalSeconds;
            Debug.Log(message);
        }
    }
}
