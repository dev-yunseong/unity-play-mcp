using System;
using System.Diagnostics;

namespace Artel.Auth
{
    /// <summary>
    /// macOS 로그인 키체인에 generic password 항목으로 둔다.
    /// </summary>
    /// <remarks>
    /// Security.framework를 직접 부르지 않고 <c>/usr/bin/security</c>를 거친다. 직접 부르면
    /// 항목의 ACL이 호출한 바이너리에 묶이는데, 서명되지 않은 유니티 빌드는 다시 빌드할 때마다
    /// 다른 바이너리가 되어 매번 키체인 허용 팝업이 뜬다. CLI를 거치면 만드는 쪽도 읽는 쪽도
    /// 같은 바이너리라 팝업이 없다.
    ///
    /// 대신 같은 사용자의 다른 프로세스도 팝업 없이 이 항목을 읽는다. 여기서 얻는 것은
    /// 디스크 평문 제거와 사용자 계정 단위 격리까지이고, 그 이상은 아니다.
    ///
    /// 항목을 앱별로 나누지 않는다. SDK 토큰은 앱이 아니라 사람의 것이라, 같은 사람이 만드는
    /// 여러 프로젝트가 한 번의 로그인을 나눠 쓰는 편이 맞다.
    /// </remarks>
    internal sealed class KeychainSecretStore : IArtelSecretStore
    {
        private const string SecurityPath = "/usr/bin/security";
        private const string ServiceName = "kr.artel.sdk";

        // 없는 항목을 물어보면 security가 이 코드로 끝난다. 실패와 구분해야 "아직 로그인하지
        // 않음"이 오류로 보이지 않는다.
        private const int ItemNotFoundExitCode = 44;

        // 키체인이 잠겨 있으면 security가 잠금 해제 창을 띄우고 사람을 기다린다. 그동안
        // 유니티 메인 스레드가 멈추므로 무한정 기다리지 않는다.
        private const int TimeoutMilliseconds = 15000;

        public bool TryLoad(string key, out string value)
        {
            var result = Run(new[] { "find-generic-password", "-s", ServiceName, "-a", key, "-w" });
            if (result.ExitCode == ItemNotFoundExitCode)
            {
                value = string.Empty;
                return false;
            }

            ThrowIfFailed(result, "읽지");

            // -w는 값 뒤에 줄바꿈 하나를 붙여 내보낸다.
            value = result.StandardOutput.TrimEnd('\n');
            return value.Length > 0;
        }

        public void Save(string key, string value)
        {
            // 읽을 때 -w가 값 끝에 줄바꿈을 하나 붙여 내보내는데, 값 자체가 줄바꿈으로 끝나면
            // 그 둘을 구분할 수 없다. 토큰에는 없는 문자지만 조용히 잘리는 것보다 막는 게 낫다.
            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            {
                throw new ArgumentException("Secret must not contain line breaks.", nameof(value));
            }

            // 값을 -w 인자로 넘긴다. 표준 입력으로 넘기는 길도 있지만 그쪽은 security가
            // readpassphrase로 받아 128바이트에서 잘라 버린다 — JWT는 그보다 길어서 앞부분만
            // 저장되고, 서버는 "Missing second delimiter"로 401을 준다.
            //
            // 인자로 넘기면 그동안 같은 사용자의 다른 프로세스가 ps로 값을 볼 수 있다. 어차피
            // 그 프로세스들은 이 키체인 항목 자체를 팝업 없이 읽을 수 있으므로(위 remarks)
            // 새로 열리는 구멍은 아니고, 노출은 프로세스 하나가 사는 동안으로 끝난다.
            //
            // -U는 이미 있는 항목을 덮어쓴다 — 없으면 두 번째 로그인이 "항목이 이미 있다"로 실패한다.
            var result = Run(
                new[] { "add-generic-password", "-U", "-s", ServiceName, "-a", key, "-w", value });

            ThrowIfFailed(result, "쓰지");
        }

        public void Delete(string key)
        {
            var result = Run(new[] { "delete-generic-password", "-s", ServiceName, "-a", key });
            if (result.ExitCode == ItemNotFoundExitCode)
            {
                return;
            }

            ThrowIfFailed(result, "지우지");
        }

        private static CommandResult Run(string[] arguments)
        {
            var startInfo = new ProcessStartInfo(SecurityPath, Join(arguments))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                // 출력을 읽기 전에 기다린다. 순서를 뒤집으면 ReadToEnd가 프로세스가 끝날 때까지
                // 막혀 타임아웃이 아무 역할도 못 한다. 여기 출력은 토큰 한 줄이거나 짧은 오류라
                // 파이프 버퍼 안에 다 들어가므로 security가 쓰기에서 막힐 일은 없다.
                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    process.Kill();
                    throw new TimeoutException(
                        "security 명령이 " + TimeoutMilliseconds + "ms 안에 끝나지 않았습니다. " +
                        "키체인이 잠겨 있는지 확인해 주세요.");
                }

                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = process.StandardOutput.ReadToEnd(),
                    StandardError = process.StandardError.ReadToEnd()
                };
            }
        }

        private static void ThrowIfFailed(CommandResult result, string action)
        {
            if (result.ExitCode == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                "키체인에서 값을 " + action + " 못했습니다 (exit " + result.ExitCode + "). " +
                result.StandardError.Trim());
        }

        private static string Join(string[] arguments)
        {
            var quoted = new string[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
            {
                quoted[index] = "\"" + arguments[index]
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"") + "\"";
            }

            return string.Join(" ", quoted);
        }

        private struct CommandResult
        {
            public int ExitCode;
            public string StandardOutput;
            public string StandardError;
        }
    }
}
