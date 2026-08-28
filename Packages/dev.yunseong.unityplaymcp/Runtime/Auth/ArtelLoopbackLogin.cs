using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR || UNITY_STANDALONE
using System.Net;
using System.Net.Sockets;
#endif

namespace Artel.Auth
{
    /// <summary>
    /// 브라우저가 돌려준 일회용 code와, 그 code를 토큰으로 바꿀 때 함께 보낼 PKCE verifier.
    /// </summary>
    internal struct ArtelLoginCode
    {
        public string Code;
        public string CodeVerifier;

        /// <summary>성공하면 null.</summary>
        public string Error;

        public bool IsSuccess { get { return Error == null; } }

        public static ArtelLoginCode Failed(string error)
        {
            return new ArtelLoginCode { Error = error };
        }
    }

    /// <summary>
    /// 로컬 loopback 서버를 잠깐 띄우고 브라우저 로그인을 기다린다.
    /// </summary>
    /// <remarks>
    /// 서버가 세션을 HttpOnly 쿠키로 들고 있어 loopback 주소로는 전달되지 않는다. 그래서
    /// 브라우저에서 일회용 code만 받아 오고 토큰 교환은 SDK가 직접 한다. 공개 클라이언트라
    /// code를 가로채는 것을 막을 비밀이 없으므로 PKCE로 대신한다.
    ///
    /// HttpListener는 Editor와 Standalone에만 있다. 다른 플랫폼에서는 <see cref="IsSupported"/>가
    /// false이고, 오버레이가 그것을 보고 로그인 UI 자체를 띄우지 않는다.
    /// </remarks>
    internal static class ArtelLoopbackLogin
    {
        internal const string CallbackPath = "/callback";

        private const string LoginPath = "/sdk-login";
        private const float TimeoutSeconds = 180f;

        // 외부 리소스를 부르지 않는다. 로그인 직후라 네트워크는 살아 있겠지만, 이 페이지가
        // 뜨는 것 자체가 "성공했다"는 신호라 CDN 하나 때문에 깨져 보이면 안 된다.
        private const string CompletedHtml =
            "<!doctype html><html lang=\"ko\"><meta charset=\"utf-8\">" +
            "<title>Artel SDK 로그인</title>" +
            "<body style=\"font-family:sans-serif;text-align:center;padding:80px 24px\">" +
            "<h1 style=\"font-size:20px\">로그인이 완료되었습니다.</h1>" +
            "<p style=\"color:#666\">이 창은 닫아도 됩니다.</p></body></html>";

        private const string RejectedHtml =
            "<!doctype html><html lang=\"ko\"><meta charset=\"utf-8\">" +
            "<title>Artel SDK 로그인</title>" +
            "<body style=\"font-family:sans-serif;text-align:center;padding:80px 24px\">" +
            "<h1 style=\"font-size:20px\">로그인을 확인하지 못했습니다.</h1>" +
            "<p style=\"color:#666\">게임으로 돌아가 다시 시도해 주세요.</p></body></html>";

        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                return HttpListener.IsSupported;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 브라우저를 열고 콜백을 기다린다. 성공하면 code와 verifier가, 실패하면 사람이 읽을
        /// 이유가 <paramref name="completed"/>로 온다.
        /// </summary>
        public static IEnumerator Authorize(Uri frontendOrigin, Action<ArtelLoginCode> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            if (frontendOrigin == null)
            {
                completed(ArtelLoginCode.Failed("로그인 페이지 주소가 설정되지 않았습니다."));
                yield break;
            }

            if (!HttpListener.IsSupported)
            {
                completed(ArtelLoginCode.Failed("이 플랫폼에서는 브라우저 로그인을 지원하지 않습니다."));
                yield break;
            }

            var codeVerifier = CreateCodeVerifier();
            var state = CreateCodeVerifier();

            HttpListener listener = null;
            CallbackWaiter waiter = null;
            string setupError = null;
            try
            {
                var port = ReserveLoopbackPort();
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Start();

                waiter = new CallbackWaiter(listener, state);
                waiter.Listen();
                Application.OpenURL(BuildLoginUrl(frontendOrigin, port, state, CreateCodeChallenge(codeVerifier)));
            }
            catch (Exception exception)
            {
                // 방화벽이 loopback 바인딩을 막으면 여기로 온다. 원인을 감추면 사용자가
                // 로그인 버튼만 반복해서 누른다.
                listener?.Close();
                setupError = "로컬 로그인 서버를 열지 못했습니다: " + exception.Message;
            }

            if (setupError != null)
            {
                completed(ArtelLoginCode.Failed(setupError));
                yield break;
            }

            try
            {
                var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (waiter.Code == null && waiter.Error == null)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        completed(ArtelLoginCode.Failed("로그인을 기다리다 시간이 지났습니다. 다시 시도해 주세요."));
                        yield break;
                    }

                    yield return null;
                }
            }
            finally
            {
                // 리스너는 성공하든 실패하든 닫는다. 열어 둔 채로 남으면 다음 로그인이 새
                // 포트를 잡고, 이전 포트로 온 콜백을 아무도 읽지 않는다.
                listener.Close();
            }

            completed(waiter.Error != null
                ? ArtelLoginCode.Failed(waiter.Error)
                : new ArtelLoginCode { Code = waiter.Code, CodeVerifier = codeVerifier });
#else
            completed(ArtelLoginCode.Failed("이 플랫폼에서는 브라우저 로그인을 지원하지 않습니다."));
            yield break;
#endif
        }

        /// <summary>PKCE code_verifier. base64url은 unreserved 문자만 쓰고, 32바이트가 43자가 된다.</summary>
        internal static string CreateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return ToBase64Url(bytes);
        }

        internal static string CreateCodeChallenge(string codeVerifier)
        {
            if (string.IsNullOrEmpty(codeVerifier))
            {
                throw new ArgumentException("Code verifier is required.", nameof(codeVerifier));
            }

            using (var sha256 = SHA256.Create())
            {
                return ToBase64Url(sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)));
            }
        }

        internal static bool IsCallback(string rawUrl)
        {
            return string.Equals(ReadPath(rawUrl), CallbackPath, StringComparison.Ordinal);
        }

        /// <summary>
        /// 콜백 URL에서 code를 꺼낸다. state가 우리가 보낸 값과 다르면 거절한다 — 아무 페이지나
        /// 이 포트로 code를 밀어 넣어 남의 계정에 붙이는 것을 막는 유일한 검사다.
        /// </summary>
        internal static bool TryReadCallback(
            string rawUrl, string expectedState, out string code, out string error)
        {
            code = null;
            var query = ReadQuery(rawUrl);

            if (!string.Equals(ReadParameter(query, "state"), expectedState, StringComparison.Ordinal))
            {
                error = "로그인 응답의 state가 일치하지 않습니다.";
                return false;
            }

            var receivedCode = ReadParameter(query, "code");
            if (string.IsNullOrEmpty(receivedCode))
            {
                error = "로그인 응답에 code가 없습니다.";
                return false;
            }

            code = receivedCode;
            error = null;
            return true;
        }

        internal static string BuildLoginUrl(Uri frontendOrigin, int port, string state, string codeChallenge)
        {
            return new Uri(frontendOrigin, LoginPath).AbsoluteUri +
                   "?port=" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   "&state=" + Uri.EscapeDataString(state) +
                   "&challenge=" + Uri.EscapeDataString(codeChallenge);
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string ReadPath(string rawUrl)
        {
            var url = rawUrl ?? string.Empty;
            var queryStart = url.IndexOf('?');
            return queryStart < 0 ? url : url.Substring(0, queryStart);
        }

        private static string ReadQuery(string rawUrl)
        {
            var url = rawUrl ?? string.Empty;
            var queryStart = url.IndexOf('?');
            return queryStart < 0 ? string.Empty : url.Substring(queryStart + 1);
        }

        private static string ReadParameter(string query, string name)
        {
            foreach (var pair in query.Split('&'))
            {
                var separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                if (!string.Equals(pair.Substring(0, separator), name, StringComparison.Ordinal))
                {
                    continue;
                }

                return Uri.UnescapeDataString(pair.Substring(separator + 1).Replace('+', ' '));
            }

            return null;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        // 0번 포트로 잡으면 커널이 비어 있는 번호를 고른다. 닫고 HttpListener로 다시 잡는
        // 사이에 다른 프로세스가 채갈 수 있지만, 후보 포트를 순회하는 방식도 같은 창을 갖는다.
        // ponytail: 실패하면 사용자가 로그인을 다시 누른다. 재시도 루프는 그때.
        private static int ReserveLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        /// <summary>
        /// HttpListener는 스레드를 잡고 기다린다. 유니티 메인 스레드는 그동안 프레임을 돌려야
        /// 하므로 콜백은 스레드풀에서 받고, 코루틴은 매 프레임 이 결과만 들여다본다.
        /// </summary>
        private sealed class CallbackWaiter
        {
            private readonly HttpListener listener;
            private readonly string expectedState;

            private volatile string code;
            private volatile string error;

            public CallbackWaiter(HttpListener listener, string expectedState)
            {
                this.listener = listener;
                this.expectedState = expectedState;
            }

            public string Code { get { return code; } }
            public string Error { get { return error; } }

            public void Listen()
            {
                try
                {
                    listener.BeginGetContext(OnContext, null);
                }
                catch (Exception exception)
                {
                    error = "로그인 응답을 받지 못했습니다: " + exception.Message;
                }
            }

            private void OnContext(IAsyncResult asyncResult)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.EndGetContext(asyncResult);
                }
                catch (Exception)
                {
                    // 리스너가 닫혔다. 타임아웃이거나 이미 결과가 정해진 뒤의 정리다.
                    return;
                }

                var rawUrl = context.Request.RawUrl ?? string.Empty;

                // 브라우저는 /favicon.ico 같은 것도 물어본다. 콜백이 아닌 요청으로 실패를
                // 확정하면 진짜 콜백이 오기 전에 로그인이 끝나 버린다.
                if (!IsCallback(rawUrl))
                {
                    Respond(context, 404, string.Empty);
                    Listen();
                    return;
                }

                if (TryReadCallback(rawUrl, expectedState, out var receivedCode, out var failure))
                {
                    Respond(context, 200, CompletedHtml);
                    code = receivedCode;
                    return;
                }

                Respond(context, 400, RejectedHtml);
                error = failure;
            }

            private static void Respond(HttpListenerContext context, int statusCode, string html)
            {
                try
                {
                    var body = Encoding.UTF8.GetBytes(html);
                    context.Response.StatusCode = statusCode;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength64 = body.LongLength;
                    context.Response.OutputStream.Write(body, 0, body.Length);
                }
                catch (Exception)
                {
                    // 브라우저가 먼저 끊었다. 받아 둔 code는 이미 유효하다.
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }
#endif
    }
}
