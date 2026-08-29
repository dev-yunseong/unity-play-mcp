using System;
using System.Security.Cryptography;
using System.Text;
using Artel.Protocol.Dto;
using Artel.Serialization;

namespace Artel.Tracking
{
    internal sealed class SceneStateHashTracker
    {
        private readonly IJsonCodec jsonCodec;
        private string lastHash;

        public SceneStateHashTracker(IJsonCodec jsonCodec)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
        }

        public bool Observe(SceneDto scene)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            var serializedScene = jsonCodec.Serialize(scene);
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
