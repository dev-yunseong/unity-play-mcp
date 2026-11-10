using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UnityActionAttribute : UnityEngine.Scripting.PreserveAttribute
    {
        public UnityActionAttribute(string name, string description, string parameters = "[]")
        {
            Name = name;
            Description = description;
            Parameters = parameters;
        }

        public string Name { get; }
        public string Description { get; }
        public string Parameters { get; }
    }

    /// <summary>Unity 메인 스레드의 액션 배치에서 실행된다. 종료하기 전에 결과를 한 번 전달한다.</summary>
    public interface IUnityAction
    {
        IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed);
    }

    internal abstract class BuiltInUnityAction : IUnityAction
    {
        internal ActionContext Context { get; set; }

        public abstract IEnumerator Execute(
            int actionId, List<object> parameters, Action<ActionResultDto> completed);
    }
}
