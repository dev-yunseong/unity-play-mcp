using System;
using System.Collections.Generic;

namespace Artel
{
    [Serializable]
    public sealed class GameStateMessage
    {
        public string type;
        public long id;
        public SceneNode scene;
    }

    [Serializable]
    public sealed class SceneNode
    {
        public int id;
        public string type;
        public string name;
        public string content;
        public string placeholder;
        public List<SceneNode> children = new List<SceneNode>();
    }

    [Serializable]
    public sealed class ActionResultMessage
    {
        public string type;
        public long id;
        public List<ActionResultDto> results = new List<ActionResultDto>();
    }

    [Serializable]
    public sealed class ActionResultDto
    {
        public int id;
        public bool success;
        public string error;

        public static ActionResultDto Success(int id)
        {
            return new ActionResultDto { id = id, success = true, error = string.Empty };
        }

        public static ActionResultDto Failure(int id, string error)
        {
            return new ActionResultDto { id = id, success = false, error = error };
        }
    }

    [Serializable]
    public sealed class ErrorMessage
    {
        public string type;
        public long id;
        public string error;
    }
}
