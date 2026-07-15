using System;
using System.Security.Cryptography;
using System.Text;

namespace Artel.Tracking
{
    internal sealed class SceneStateHashTracker
    {
        private string lastHash;

        public bool Observe(string serializedScene)
        {
            if (serializedScene == null)
            {
                throw new ArgumentNullException(nameof(serializedScene));
            }

            var currentHash = ComputeHash(serializedScene);
            if (lastHash == null)
            {
                lastHash = currentHash;
                return false;
            }

            if (string.Equals(lastHash, currentHash, StringComparison.Ordinal))
            {
                return false;
            }

            lastHash = currentHash;
            return true;
        }

        public void Reset()
        {
            lastHash = null;
        }

        private static string ComputeHash(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                return Convert.ToBase64String(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }
    }
}
