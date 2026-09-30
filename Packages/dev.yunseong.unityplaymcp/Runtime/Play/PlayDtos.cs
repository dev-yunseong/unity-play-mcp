using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityPlayMcp.Play
{
    // observe/act 도구 묶음의 wire payload. MCP server 의 `play-types.ts` 와 필드 이름을 맞춘다.

    internal sealed class EntityRefDto
    {
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("generation")] public int Generation { get; set; }
    }

    internal sealed class StampDto
    {
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("run", NullValueHandling = NullValueHandling.Ignore)] public string Run { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("sampledAtMonotonicMs")] public double SampledAtMonotonicMs { get; set; }
        [JsonProperty("gameTimeSeconds")] public double GameTimeSeconds { get; set; }
    }

    internal sealed class EvidenceDto
    {
        [JsonProperty("entity", NullValueHandling = NullValueHandling.Ignore)] public EntityRefDto Entity { get; set; }
        [JsonProperty("component", NullValueHandling = NullValueHandling.Ignore)] public string Component { get; set; }
        [JsonProperty("member", NullValueHandling = NullValueHandling.Ignore)] public string Member { get; set; }
        [JsonProperty("providerId", NullValueHandling = NullValueHandling.Ignore)] public string ProviderId { get; set; }
    }

    /// <summary>근거가 있는 값 하나. 근거가 없으면 <c>Status</c> 는 unknown/unsupported 이고 값은 싣지 않는다.</summary>
    internal sealed class FactDto
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)] public object Value { get; set; }
        [JsonProperty("unit", NullValueHandling = NullValueHandling.Ignore)] public string Unit { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("evidence")] public EvidenceDto Evidence { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }
    }

    internal sealed class RectDto
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("width")] public float Width { get; set; }
        [JsonProperty("height")] public float Height { get; set; }
    }

    internal sealed class Vec3Dto
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("z")] public float Z { get; set; }
    }

    internal sealed class TransformDto
    {
        [JsonProperty("space")] public string Space { get; set; } = "world";
        [JsonProperty("position")] public Vec3Dto Position { get; set; }
        [JsonProperty("scale")] public Vec3Dto Scale { get; set; }
        [JsonProperty("eulerAngles")] public Vec3Dto EulerAngles { get; set; }
        [JsonProperty("layer")] public int Layer { get; set; }
    }

    internal sealed class BoundsDto
    {
        [JsonProperty("space")] public string Space { get; set; } = "world";
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("center")] public Vec3Dto Center { get; set; }
        [JsonProperty("size")] public Vec3Dto Size { get; set; }
    }

    internal sealed class EntityStateDto
    {
        [JsonProperty("interactable", NullValueHandling = NullValueHandling.Ignore)] public bool? Interactable { get; set; }
        [JsonProperty("visible")] public bool Visible { get; set; }
        [JsonProperty("visibilityBasis")] public string VisibilityBasis { get; set; }
    }

    internal sealed class ActionSummaryDto
    {
        [JsonProperty("actionRef")] public string ActionRef { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("availability")] public string Availability { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
    }

    internal sealed class EntityDto
    {
        [JsonProperty("ref")] public EntityRefDto Ref { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("types")] public List<string> Types { get; set; } = new List<string>();
        [JsonProperty("active")] public bool Active { get; set; }
        [JsonProperty("transform", NullValueHandling = NullValueHandling.Ignore)] public TransformDto Transform { get; set; }
        [JsonProperty("bounds", NullValueHandling = NullValueHandling.Ignore)] public BoundsDto Bounds { get; set; }
        [JsonProperty("screenRect", NullValueHandling = NullValueHandling.Ignore)] public RectDto ScreenRect { get; set; }
        [JsonProperty("state")] public EntityStateDto State { get; set; }
        [JsonProperty("facts")] public List<FactDto> Facts { get; set; } = new List<FactDto>();
        [JsonProperty("actions")] public List<ActionSummaryDto> Actions { get; set; } = new List<ActionSummaryDto>();
        [JsonProperty("relations", NullValueHandling = NullValueHandling.Ignore)] public List<RelationDto> Relations { get; set; }
    }

    internal sealed class RelationDto
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("target")] public EntityRefDto Target { get; set; }
        [JsonProperty("providerId")] public string ProviderId { get; set; }
    }

    internal sealed class PolicyDto
    {
        [JsonProperty("scope")] public string Scope { get; set; }
        [JsonProperty("visibilityBasis")] public string VisibilityBasis { get; set; }
        [JsonProperty("visibilityGuarantee")] public string VisibilityGuarantee { get; set; }
        [JsonProperty("secretsRedacted")] public bool SecretsRedacted { get; set; } = true;
    }

    internal sealed class ImageDto
    {
        [JsonProperty("mimeType")] public string MimeType { get; set; }
        [JsonProperty("data")] public string Data { get; set; }
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
        [JsonProperty("screen")] public ImageScreenDto Screen { get; set; }
        [JsonProperty("region")] public RectDto Region { get; set; }
        [JsonProperty("scale")] public ImageScaleDto Scale { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("scene", NullValueHandling = NullValueHandling.Ignore)] public string Scene { get; set; }
        [JsonProperty("toScreen")] public string ToScreen { get; set; }
    }

    internal sealed class ImageScreenDto
    {
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
    }

    internal sealed class ImageScaleDto
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
    }

    internal sealed class ObservationDto
    {
        [JsonProperty("stamp")] public StampDto Stamp { get; set; }
        [JsonProperty("policy")] public PolicyDto Policy { get; set; }
        [JsonProperty("entities")] public List<EntityDto> Entities { get; set; } = new List<EntityDto>();
        [JsonProperty("omittedEntities")] public int OmittedEntities { get; set; }
        [JsonProperty("coherent")] public bool Coherent { get; set; } = true;
        [JsonProperty("imageStamp", NullValueHandling = NullValueHandling.Ignore)] public StampDto ImageStamp { get; set; }
        [JsonProperty("frameDelta", NullValueHandling = NullValueHandling.Ignore)] public int? FrameDelta { get; set; }
        [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)] public ImageDto Image { get; set; }
        [JsonProperty("lifecycles")] public Dictionary<string, string> Lifecycles { get; set; } = new Dictionary<string, string>();
        [JsonProperty("warnings")] public List<string> Warnings { get; set; } = new List<string>();
        [JsonProperty("inputRevision")] public long InputRevision { get; set; }
        [JsonProperty("encodeMs")] public double EncodeMs { get; set; }
    }

    // ---- requests -------------------------------------------------------------------------------------------

    internal sealed class FilterDto
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("component")] public string Component { get; set; }
        [JsonProperty("selector")] public string Selector { get; set; }
    }

    internal sealed class ObserveRequestDto
    {
        [JsonProperty("scope")] public string Scope { get; set; } = "player";
        [JsonProperty("filter")] public FilterDto Filter { get; set; }
        [JsonProperty("includeImage")] public bool IncludeImage { get; set; }
        [JsonProperty("imageMaxEdge")] public int ImageMaxEdge { get; set; }
        [JsonProperty("maxEntities")] public int MaxEntities { get; set; } = 50;
        [JsonProperty("factsPerEntity")] public int FactsPerEntity { get; set; } = 20;
        [JsonProperty("includeActions")] public bool IncludeActions { get; set; } = true;
        [JsonProperty("includeFacts")] public bool IncludeFacts { get; set; } = true;
        [JsonProperty("track")] public List<EntityRefDto> Track { get; set; } = new List<EntityRefDto>();
    }

    internal sealed class SampleTargetRequestDto
    {
        [JsonProperty("key")] public string Key { get; set; }
        [JsonProperty("ref")] public EntityRefDto Ref { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("component")] public string Component { get; set; }
    }

    internal sealed class SampleMemberRequestDto
    {
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("component")] public string Component { get; set; }
        [JsonProperty("member")] public string Member { get; set; }
    }

    internal sealed class SampleFactRequestDto
    {
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("providerId")] public string ProviderId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    internal sealed class SampleRequestDto
    {
        [JsonProperty("scope")] public string Scope { get; set; } = "player";
        [JsonProperty("targets")] public List<SampleTargetRequestDto> Targets { get; set; } = new List<SampleTargetRequestDto>();
        [JsonProperty("members")] public List<SampleMemberRequestDto> Members { get; set; } = new List<SampleMemberRequestDto>();
        [JsonProperty("facts")] public List<SampleFactRequestDto> Facts { get; set; } = new List<SampleFactRequestDto>();
    }

    // ---- sample response ------------------------------------------------------------------------------------

    internal sealed class TargetSampleDto
    {
        /// <summary>one / none / ambiguous</summary>
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("count")] public int Count { get; set; }
        [JsonProperty("ref", NullValueHandling = NullValueHandling.Ignore)] public EntityRefDto Ref { get; set; }
        /// <summary>present / inactive / destroyed / unobserved / out_of_scope</summary>
        [JsonProperty("lifecycle", NullValueHandling = NullValueHandling.Ignore)] public string Lifecycle { get; set; }
        [JsonProperty("active", NullValueHandling = NullValueHandling.Ignore)] public bool? Active { get; set; }
        [JsonProperty("interactable", NullValueHandling = NullValueHandling.Ignore)] public bool? Interactable { get; set; }
    }

    internal sealed class MemberSampleDto
    {
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("component")] public string Component { get; set; }
        [JsonProperty("member")] public string Member { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)] public object Value { get; set; }
        [JsonProperty("valueType", NullValueHandling = NullValueHandling.Ignore)] public string ValueType { get; set; }
        [JsonProperty("unit", NullValueHandling = NullValueHandling.Ignore)] public string Unit { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }
    }

    internal sealed class FactSampleDto
    {
        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)] public string Target { get; set; }
        [JsonProperty("providerId")] public string ProviderId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)] public object Value { get; set; }
        [JsonProperty("unit", NullValueHandling = NullValueHandling.Ignore)] public string Unit { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }
    }

    internal sealed class SampleDto
    {
        [JsonProperty("stamp")] public StampDto Stamp { get; set; }
        [JsonProperty("targets")] public Dictionary<string, TargetSampleDto> Targets { get; set; } = new Dictionary<string, TargetSampleDto>();
        [JsonProperty("members")] public List<MemberSampleDto> Members { get; set; } = new List<MemberSampleDto>();
        [JsonProperty("facts")] public List<FactSampleDto> Facts { get; set; } = new List<FactSampleDto>();
    }

    // ---- capabilities / operations / events -----------------------------------------------------------------

    internal sealed class ProviderStatusDto
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("slowCount")] public int SlowCount { get; set; }
        [JsonProperty("lastError", NullValueHandling = NullValueHandling.Ignore)] public string LastError { get; set; }
        [JsonProperty("lastElapsedMs")] public double LastElapsedMs { get; set; }
    }

    internal sealed class CapabilitiesDto
    {
        [JsonProperty("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonProperty("runtimeVersion")] public string RuntimeVersion { get; set; }
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("tools")] public List<string> Tools { get; set; }
        [JsonProperty("inputPaths")] public List<string> InputPaths { get; set; }
        [JsonProperty("inputSystem")] public string InputSystem { get; set; }
        [JsonProperty("physics2D")] public bool Physics2D { get; set; }
        [JsonProperty("physics3D")] public bool Physics3D { get; set; }
        [JsonProperty("ui")] public bool Ui { get; set; }
        [JsonProperty("providers")] public List<ProviderStatusDto> Providers { get; set; }
        [JsonProperty("observationPolicies")] public List<string> ObservationPolicies { get; set; }
        [JsonProperty("limits")] public Dictionary<string, int> Limits { get; set; }
    }

    internal sealed class BeginResultDto
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)] public string State { get; set; }
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("inputRevision")] public long InputRevision { get; set; }
    }

    internal sealed class EventStampDto
    {
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("gameTimeSeconds")] public double GameTimeSeconds { get; set; }
    }

    internal sealed class ProviderEventDto
    {
        [JsonProperty("seq")] public long Sequence { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("providerId")] public string ProviderId { get; set; }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        [JsonProperty("entity", NullValueHandling = NullValueHandling.Ignore)] public EntityRefDto Entity { get; set; }
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)] public object Data { get; set; }
        [JsonProperty("stamp")] public EventStampDto Stamp { get; set; }
        [JsonProperty("playerVisible")] public bool PlayerVisible { get; set; }
    }

    internal sealed class EventsResultDto
    {
        [JsonProperty("events")] public List<ProviderEventDto> Events { get; set; } = new List<ProviderEventDto>();
        [JsonProperty("next")] public long Next { get; set; }
        [JsonProperty("dropped")] public long Dropped { get; set; }
    }

    internal sealed class CheckpointDto
    {
        [JsonProperty("sceneChanged")] public bool SceneChanged { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
    }

    // ---- inspect --------------------------------------------------------------------------------------------

    internal sealed class InspectRequestDto
    {
        [JsonProperty("actionRef")] public string ActionRef { get; set; }
        [JsonProperty("scope")] public string Scope { get; set; } = "player";
    }

    internal sealed class PreconditionDto
    {
        /// <summary>MCP server 가 읽는 predicate v1 객체. Unity 는 내용을 해석하지 않고 전한다.</summary>
        [JsonProperty("predicate")] public object Predicate { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }
    }

    internal sealed class InspectResultDto
    {
        [JsonProperty("actionRef")] public string ActionRef { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("entity")] public EntityRefDto Entity { get; set; }
        [JsonProperty("availability")] public string Availability { get; set; }
        [JsonProperty("availabilityEvidence")] public List<FactDto> AvailabilityEvidence { get; set; } = new List<FactDto>();
        [JsonProperty("recipe")] public List<Dictionary<string, object>> Recipe { get; set; } = new List<Dictionary<string, object>>();
        [JsonProperty("preconditions")] public List<PreconditionDto> Preconditions { get; set; } = new List<PreconditionDto>();
        [JsonProperty("targetConstraints")] public List<FactDto> TargetConstraints { get; set; } = new List<FactDto>();
        [JsonProperty("costs")] public List<FactDto> Costs { get; set; } = new List<FactDto>();
        [JsonProperty("expectedEffects")] public List<FactDto> ExpectedEffects { get; set; } = new List<FactDto>();
        [JsonProperty("observedEffects")] public object ObservedEffects { get; set; }
        [JsonProperty("outcomePredicates")] public List<object> OutcomePredicates { get; set; } = new List<object>();
        [JsonProperty("acceptsTarget", NullValueHandling = NullValueHandling.Ignore)] public string AcceptsTarget { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("note")] public string Note { get; set; }
    }
}
