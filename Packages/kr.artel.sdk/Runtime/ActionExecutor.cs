using System.Collections.Generic;

namespace Artel
{
    internal sealed class ActionExecutor
    {
        private readonly SceneScanner scanner;

        public ActionExecutor(SceneScanner scanner)
        {
            this.scanner = scanner;
        }

        public ActionResultDto Execute(int actionId, string method, List<object> parameters)
        {
            if (method == "button_click")
            {
                return ExecuteButtonClick(actionId, parameters);
            }

            if (method == "enter_text")
            {
                return ExecuteEnterText(actionId, parameters);
            }

            return ActionResultDto.Failure(actionId, "Unsupported method: " + method);
        }

        private ActionResultDto ExecuteButtonClick(int actionId, List<object> parameters)
        {
            if (!TryReadId(parameters, out var targetId))
            {
                return ActionResultDto.Failure(actionId, "button_click requires params [targetId].");
            }

            if (!scanner.TryGetTarget(targetId, out var target))
            {
                return ActionResultDto.Failure(actionId, "Unknown target id: " + targetId);
            }

            return target.Click()
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(actionId, "Target is not a Button: " + targetId);
        }

        private ActionResultDto ExecuteEnterText(int actionId, List<object> parameters)
        {
            if (!TryReadId(parameters, out var targetId) || parameters.Count < 2)
            {
                return ActionResultDto.Failure(actionId, "enter_text requires params [targetId, value].");
            }

            if (!scanner.TryGetTarget(targetId, out var target))
            {
                return ActionResultDto.Failure(actionId, "Unknown target id: " + targetId);
            }

            var value = parameters[1] == null ? string.Empty : parameters[1].ToString();
            return target.EnterText(value)
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(actionId, "Target is not an EditText: " + targetId);
        }

        private static bool TryReadId(List<object> parameters, out int id)
        {
            id = 0;
            if (parameters == null || parameters.Count == 0 || parameters[0] == null)
            {
                return false;
            }

            if (parameters[0] is long longValue)
            {
                id = (int)longValue;
                return true;
            }

            if (parameters[0] is int intValue)
            {
                id = intValue;
                return true;
            }

            return int.TryParse(parameters[0].ToString(), out id);
        }
    }
}
