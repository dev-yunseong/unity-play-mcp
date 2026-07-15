using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel
{
    [Serializable]
    public sealed class GameStateMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("scene")]
        public SceneDto Scene { get; set; }
    }

    [Serializable]
    public sealed class SceneDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("children")]
        public List<SceneBlockDto> Children { get; set; } = new List<SceneBlockDto>();
    }

    [Serializable]
    public sealed class SceneBlockDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("components")]
        public List<SceneComponentDto> Components { get; set; } = new List<SceneComponentDto>();

        [JsonProperty("children")]
        public List<SceneBlockDto> Children { get; set; } = new List<SceneBlockDto>();
    }

    [Serializable]
    public sealed class SceneComponentDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
        public string Content { get; set; }

        [JsonProperty("placeholder", NullValueHandling = NullValueHandling.Ignore)]
        public string Placeholder { get; set; }

        [JsonProperty("states")]
        public List<StateDto> States { get; set; } = new List<StateDto>();

        [JsonProperty("actions")]
        public List<ActionInvocationDto> Actions { get; set; } = new List<ActionInvocationDto>();
    }

    [Serializable]
    public sealed class StateDto
    {
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }
    }

    [Serializable]
    public sealed class ActionInvocationDto
    {
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("returnValue")]
        public object ReturnValue { get; set; }

        [JsonProperty("timeStamp")]
        public string Timestamp { get; set; }
    }

    [Serializable]
    internal sealed class ArtelRequestDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("actions")]
        public List<ActionRequestDto> Actions { get; set; } = new List<ActionRequestDto>();
    }

    [Serializable]
    internal sealed class ActionRequestDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public List<object> Parameters { get; set; } = new List<object>();
    }

    [Serializable]
    public sealed class ActionResultMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("results")]
        public List<ActionResultDto> Results { get; set; } = new List<ActionResultDto>();
    }

    [Serializable]
    public sealed class ActionResultDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("success")]
        public bool IsSuccess { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        public static ActionResultDto Success(int id)
        {
            return new ActionResultDto { Id = id, IsSuccess = true, Error = string.Empty };
        }

        public static ActionResultDto Failure(int id, string error)
        {
            return new ActionResultDto { Id = id, IsSuccess = false, Error = error };
        }
    }

    [Serializable]
    public sealed class ErrorMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
