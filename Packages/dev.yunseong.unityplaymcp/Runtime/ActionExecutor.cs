using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Scan;
using UnityPlayMcp.Capture;
using UnityPlayMcp.Protocol.Dto;
using UnityPlayMcp.Serialization;
using UnityEngine;

namespace UnityPlayMcp
{
    internal sealed class ActionExecutor
    {
        private readonly ActionContext context;
        private readonly ActionRegistry registry;

        public ActionExecutor(
            TargetLookup targetLookup,
            CursorController cursorController,
            PointerEventDispatcher pointerEvents,
            IScreenCapturer capturer = null,
            IReadingChannel readings = null,
            Action<bool> setOverlay = null)
        {
            context = new ActionContext(targetLookup, cursorController, pointerEvents, capturer, readings, setOverlay);
            registry = new ActionRegistry(context);
        }

        public IReadOnlyList<RegisteredActionDto> ProjectActions => registry.ProjectActions;

        public void RestoreTimeScale() => context.RestoreTimeScale();

        public IEnumerator Execute(
            int actionId, string method, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (method == ActionRegistry.ProjectMethod)
                return ExecuteProject(actionId, parameters, completed);
            if (registry.TryGetBuiltIn(method, out var action))
                return action.Execute(actionId, parameters, completed);
            return Fail(actionId, "Unsupported method: " + method, completed);
        }

        private static IEnumerator Fail(int actionId, string message, Action<ActionResultDto> completed)
        {
            completed(ActionResultDto.Failure(actionId, message));
            yield break;
        }

        private IEnumerator ExecuteProject(int id, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (parameters == null || parameters.Count == 0 || !(parameters[0] is string name))
            {
                completed(ActionResultDto.Failure(id, "unity_action requires params [name, ...params]."));
                yield break;
            }

            if (!registry.TryGetProject(name, out var action))
            {
                completed(ActionResultDto.Failure(id, "Unknown Unity action: " + name));
                yield break;
            }

            ActionResultDto result = null;
            Exception failure = null;
            IEnumerator routine = null;
            try
            {
                routine = action.Execute(id, parameters.GetRange(1, parameters.Count - 1), value => result = result ?? value);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (routine != null)
                yield return RunProjectRoutine(routine, error => failure = error);

            if (failure != null)
                completed(ActionResultDto.Failure(id, "Unity action failed: " + failure.GetBaseException().Message));
            else if (result == null || result.Id != id)
                completed(ActionResultDto.Failure(id, "Unity action did not return a result for this request."));
            else
                completed(SerializeProjectResult(result));
        }

        private static ActionResultDto SerializeProjectResult(ActionResultDto result)
        {
            try
            {
                return new ActionResultDto
                {
                    Id = result.Id,
                    IsSuccess = result.IsSuccess,
                    Error = result.Error,
                    ReturnValue = result.ReturnValue == null ? null : NewtonsoftJsonCodec.ToToken(result.ReturnValue)
                };
            }
            catch (Exception)
            {
                return ActionResultDto.Failure(result.Id, "Unity action returned a value that cannot be serialized.");
            }
        }

        // Unity가 중첩 IEnumerator를 직접 실행하면 그 안에서 난 예외를 배치가 처리할 수 없다.
        private static IEnumerator RunProjectRoutine(IEnumerator routine, Action<Exception> onError)
        {
            try
            {
                while (true)
                {
                    object step = null;
                    bool advanced;
                    try
                    {
                        advanced = routine.MoveNext();
                        if (advanced) step = routine.Current;
                    }
                    catch (Exception exception)
                    {
                        onError(exception);
                        advanced = false;
                    }

                    if (!advanced) yield break;
                    if (!(step is IEnumerator nested))
                    {
                        yield return step;
                        continue;
                    }

                    var failed = false;
                    void OnStepError(Exception error)
                    {
                        failed = true;
                        onError(error);
                    }

                    if (nested is CustomYieldInstruction wait)
                        yield return new GuardedWait(wait, OnStepError);
                    else
                        yield return RunProjectRoutine(nested, OnStepError);

                    if (failed) yield break;
                }
            }
            finally
            {
                try
                {
                    (routine as IDisposable)?.Dispose();
                }
                catch (Exception exception)
                {
                    onError(exception);
                }
            }
        }

        private sealed class GuardedWait : CustomYieldInstruction
        {
            private readonly CustomYieldInstruction wait;
            private readonly Action<Exception> onError;

            public GuardedWait(CustomYieldInstruction wait, Action<Exception> onError)
            {
                this.wait = wait;
                this.onError = onError;
            }

            public override bool keepWaiting
            {
                get
                {
                    try
                    {
                        return wait.keepWaiting;
                    }
                    catch (Exception exception)
                    {
                        onError(exception);
                        return false;
                    }
                }
            }
        }
    }
}
