using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class TrackedComponentDto : SceneComponentDto
    {
        [JsonIgnore]
        public string ComponentType { get; set; }

        public override string Type => ComponentType;
    }
}
