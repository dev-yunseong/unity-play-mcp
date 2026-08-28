using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Auth;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine.Networking;

namespace Artel
{
    internal sealed class ArtelOverlayViewModel
    {
        private const long UnauthorizedStatusCode = 401;
        private const long ConflictStatusCode = 409;
        private const long NotFoundStatusCode = 404;
        private const string RetiredInstanceCode = "SDK_INSTANCE_RETIRED";

        private readonly ArtelSdkRegistrationClient registrationClient;
        private readonly ArtelSdkAuthClient authClient;
        private readonly IJsonCodec jsonCodec;
        private readonly List<SdkProjectDto> projects = new List<SdkProjectDto>();

        public ArtelOverlayViewModel(
            ArtelSdkRegistrationClient registrationClient,
            ArtelSdkAuthClient authClient,
            IJsonCodec jsonCodec)
        {
            this.registrationClient = registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
            this.authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            State = ArtelConnectionState.NeedsLogin;
            ShowPanel = true;
            Status = "Artel 계정으로 로그인해 주세요.";
        }

        public event Action Changed;

        public string Status { get; private set; }
        public ArtelConnectionState State { get; private set; }
        public bool ShowPanel { get; private set; }

        /// <summary>
        /// True while <see cref="Status"/> describes a failure. The failing statuses share no
        /// prefix — one of them has none at all — so substring matching cannot stand in for this.
        /// </summary>
        public bool HasError { get; private set; }

        /// <summary>
        /// True once <see cref="Register"/> has been entered at least once this session.
        /// </summary>
        public bool HasAttemptedRegistration { get; private set; }

        public bool HasToken { get; private set; }
        public string DisplayName { get; private set; } = string.Empty;
        public string SelectedProjectId { get; private set; } = string.Empty;

        /// <summary>고른 프로젝트까지 남아 있어 로그인 화면 없이 곧바로 등록할 수 있는 상태.</summary>
        public bool HasStoredSession
        {
            get { return HasToken && SelectedProjectId.Length > 0; }
        }

        public IReadOnlyList<SdkProjectDto> Projects { get { return projects; } }

        /// <summary>
        /// Whether the full-screen gate wants to be up.
        /// </summary>
        /// <remarks>
        /// <c>!HasStoredSession</c> keeps the gate away on the returning-user path, where
        /// <c>Start</c> fires registration immediately and the state is still
        /// <see cref="ArtelConnectionState.NeedsLogin"/> for one frame — showing the gate there
        /// is the flicker ARTEL-152 removed. <see cref="HasAttemptedRegistration"/> then brings
        /// the gate back after a failure that left the session in place, so the project buttons
        /// become the retry. Without it those failures strand the user with only the corner
        /// panel to find.
        /// </remarks>
        public bool ShowGate
        {
            get
            {
                // loopback을 걸 수 없는 플랫폼에서는 게이트를 아예 띄우지 않는다. 누를 것이
                // 없는 전체 화면 덮개는 게임을 가리기만 한다.
                if (!HasToken && !ArtelLoopbackLogin.IsSupported)
                {
                    return false;
                }

                if (State == ArtelConnectionState.ChoosingProject)
                {
                    return true;
                }

                return State == ArtelConnectionState.NeedsLogin &&
                       (!HasStoredSession || HasAttemptedRegistration);
            }
        }

        public bool CanLogIn
        {
            get { return State == ArtelConnectionState.NeedsLogin && ArtelLoopbackLogin.IsSupported; }
        }

        public bool CanConnect
        {
            get
            {
                return State != ArtelConnectionState.Registering &&
                       State != ArtelConnectionState.LoggingIn &&
                       HasToken &&
                       ArtelSdkSession.TryLoadInstanceId(out _);
            }
        }

        /// <summary>
        /// Loads the persisted session. Must run no earlier than <c>Start</c>, because
        /// <see cref="ArtelManager"/> adds the overlay controller before its own identity exists.
        /// </summary>
        public void Initialize()
        {
            projects.Clear();
            // 만료된 access 토큰이라도 refresh 토큰이 살아 있으면 세션은 아직 있는 것이다.
            // 여기서 없다고 보면 재발급으로 넘어갈 기회 없이 브라우저 로그인부터 시킨다.
            HasToken = ArtelSdkSession.TryLoadToken(out _) || ArtelSdkSession.TryLoadRefreshToken(out _);
            DisplayName = HasToken ? ArtelSdkSession.DisplayName : string.Empty;
            SelectedProjectId = ArtelSdkSession.TryLoadProjectId(out var projectId) && HasToken
                ? projectId
                : string.Empty;

            if (HasStoredSession)
            {
                State = ArtelConnectionState.NeedsLogin;
                ShowPanel = false;
                Status = "저장된 로그인으로 등록하는 중...";
            }
            else if (HasToken)
            {
                State = ArtelConnectionState.ChoosingProject;
                ShowPanel = true;
                Status = "연결할 프로젝트를 불러오는 중...";
            }
            else
            {
                State = ArtelConnectionState.NeedsLogin;
                ShowPanel = true;
                Status = ArtelLoopbackLogin.IsSupported
                    ? "Artel 계정으로 로그인해 주세요."
                    : "이 플랫폼에서는 브라우저 로그인을 지원하지 않습니다.";
            }

            HasError = false;
            NotifyChanged();
        }

        /// <summary>
        /// 브라우저 로그인을 걸고, 받은 code를 토큰으로 바꾼 뒤 프로젝트 목록까지 이어서 읽는다.
        /// </summary>
        public IEnumerator LogIn(Server server)
        {
            if (State == ArtelConnectionState.LoggingIn || State == ArtelConnectionState.Registering)
            {
                yield break;
            }

            Uri frontendOrigin;
            try
            {
                frontendOrigin = server.FrontendBaseUri;
            }
            catch (Exception exception)
            {
                Fail("설정 오류: " + exception.Message);
                yield break;
            }

            State = ArtelConnectionState.LoggingIn;
            HasError = false;
            SetStatus("브라우저에서 로그인을 완료해 주세요.");

            var login = default(ArtelLoginCode);
            yield return ArtelLoopbackLogin.Authorize(frontendOrigin, result => login = result);

            if (!login.IsSuccess)
            {
                Fail("로그인 실패: " + login.Error);
                yield break;
            }

            SetStatus("로그인을 확인하는 중...");

            UnityWebRequest request;
            try
            {
                request = authClient.CreateTokenRequest(server, login.Code, login.CodeVerifier);
            }
            catch (Exception exception)
            {
                Fail("설정 오류: " + exception.Message);
                yield break;
            }

            bool succeeded;
            long responseCode;
            string responseBody;
            using (request)
            {
                yield return request.SendWebRequest();

                succeeded = request.result == UnityWebRequest.Result.Success;
                responseCode = request.responseCode;
                responseBody = ReadBody(request);
            }

            if (!succeeded)
            {
                Fail("로그인 실패: " + DescribeFailure(responseCode, responseBody));
                yield break;
            }

            SdkTokenResponseDto token;
            try
            {
                token = jsonCodec.Deserialize<SdkTokenResponseDto>(responseBody);
            }
            catch (Exception exception)
            {
                Fail("로그인 실패: 응답을 읽지 못했습니다. " + exception.Message);
                yield break;
            }

            if (token == null || string.IsNullOrWhiteSpace(token.Token))
            {
                Fail("로그인 실패: 응답에 토큰이 없습니다.");
                yield break;
            }

            ArtelSdkSession.SaveToken(token.Token, token.ExpiresAt, token.DisplayName);
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                ArtelSdkSession.SaveRefreshToken(token.RefreshToken, token.RefreshExpiresAt);
            }

            HasToken = true;
            DisplayName = token.DisplayName ?? string.Empty;

            yield return LoadProjects(server);
        }

        /// <summary>
        /// 이 토큰으로 붙을 수 있는 프로젝트를 읽는다. 하나뿐이면 고르게 하지 않고 바로 쓴다.
        /// </summary>
        public IEnumerator LoadProjects(Server server)
        {
            var token = string.Empty;
            var refreshRejected = false;
            yield return EnsureToken(server, (value, rejected) =>
            {
                token = value;
                refreshRejected = rejected;
            });

            if (token.Length == 0)
            {
                FailTokenAcquisition(refreshRejected);
                yield break;
            }

            State = ArtelConnectionState.ChoosingProject;
            HasError = false;
            SetStatus("프로젝트 목록을 불러오는 중...");

            UnityWebRequest request;
            try
            {
                request = authClient.CreateProjectsRequest(server, token);
            }
            catch (Exception exception)
            {
                Fail("설정 오류: " + exception.Message);
                yield break;
            }

            bool succeeded;
            long responseCode;
            string responseBody;
            using (request)
            {
                yield return request.SendWebRequest();

                succeeded = request.result == UnityWebRequest.Result.Success;
                responseCode = request.responseCode;
                responseBody = ReadBody(request);
            }

            if (!succeeded)
            {
                if (responseCode == UnauthorizedStatusCode)
                {
                    ExpireSession("로그인이 만료되었습니다. 다시 로그인해 주세요.");
                }
                else
                {
                    Fail("프로젝트 목록을 불러오지 못했습니다: " + DescribeFailure(responseCode, responseBody));
                }

                yield break;
            }

            SdkProjectsResponseDto response;
            try
            {
                response = jsonCodec.Deserialize<SdkProjectsResponseDto>(responseBody);
            }
            catch (Exception exception)
            {
                Fail("프로젝트 목록을 읽지 못했습니다: " + exception.Message);
                yield break;
            }

            projects.Clear();
            foreach (var project in response?.Projects ?? new List<SdkProjectDto>())
            {
                if (project != null && !string.IsNullOrWhiteSpace(project.Id))
                {
                    projects.Add(project);
                }
            }

            if (projects.Count == 0)
            {
                Fail("접근할 수 있는 프로젝트가 없습니다. 대시보드에서 프로젝트를 먼저 만들어 주세요.");
                yield break;
            }

            if (projects.Count == 1)
            {
                SelectProject(projects[0].Id);
                yield break;
            }

            SetStatus("연결할 프로젝트를 선택해 주세요.");
        }

        public void SelectProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return;
            }

            SelectedProjectId = projectId.Trim();
            ArtelSdkSession.SaveProjectId(SelectedProjectId);
            HasError = false;
            SetStatus("프로젝트를 선택했습니다. 등록하는 중...");
        }

        public IEnumerator Register(
            Server server,
            string sdkUuid,
            string instanceName,
            string gameVersion,
            Action connect,
            SceneScanReportDto sceneScan = null)
        {
            if (State == ArtelConnectionState.Registering)
            {
                yield break;
            }

            var token = string.Empty;
            var refreshRejected = false;
            yield return EnsureToken(server, (value, rejected) =>
            {
                token = value;
                refreshRejected = rejected;
            });

            if (token.Length == 0)
            {
                FailTokenAcquisition(refreshRejected);
                yield break;
            }

            if (SelectedProjectId.Length == 0)
            {
                Fail("연결할 프로젝트를 선택해 주세요.");
                yield break;
            }

            // 서버는 빈 버전을 거절한다. 여기서 걸러내지 않으면 Player Settings를 비워둔
            // 프로젝트가 원인을 알 수 없는 400을 받는다.
            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                Fail("Player Settings의 Version이 비어 있습니다. 값을 설정한 뒤 다시 시도해 주세요.");
                yield break;
            }

            HasAttemptedRegistration = true;
            State = ArtelConnectionState.Registering;
            HasError = false;
            SetStatus("인스턴스를 등록하는 중...");

            bool succeeded;
            long responseCode;
            string responseBody;
            for (var attempt = 0; ; attempt++)
            {
                UnityWebRequest request;
                try
                {
                    request = registrationClient.CreateRequest(
                        server, token, SelectedProjectId, sdkUuid, instanceName, gameVersion, sceneScan);
                }
                catch (Exception exception)
                {
                    Fail("설정 오류: " + exception.Message);
                    yield break;
                }

                using (request)
                {
                    yield return request.SendWebRequest();

                    succeeded = request.result == UnityWebRequest.Result.Success;
                    responseCode = request.responseCode;
                    responseBody = ReadBody(request);
                }

                if (!succeeded && attempt == 0 && IsRetiredInstance(responseCode, responseBody))
                {
                    sdkUuid = ArtelSdkIdentity.ResetAndCreate();
                    continue;
                }

                break;
            }

            if (!succeeded)
            {
                if (responseCode == UnauthorizedStatusCode)
                {
                    ExpireSession("로그인이 만료되었습니다. 다시 로그인해 주세요.");
                }
                else if (responseCode == NotFoundStatusCode)
                {
                    // 그 사용자가 더 이상 붙을 수 없는 프로젝트다. 선택을 지워야 다음 화면이
                    // 같은 404를 반복하지 않고 목록부터 다시 고르게 한다.
                    SelectedProjectId = string.Empty;
                    Fail("등록 실패: 접근할 수 없는 프로젝트입니다. 프로젝트를 다시 선택해 주세요.");
                }
                else
                {
                    Fail("등록 실패: " + DescribeFailure(responseCode, responseBody));
                }

                yield break;
            }

            SdkRegistrationResponseDto registration;
            try
            {
                registration = jsonCodec.Deserialize<SdkRegistrationResponseDto>(responseBody);
            }
            catch (Exception exception)
            {
                Fail("등록 실패: 응답을 읽지 못했습니다. " + exception.Message);
                yield break;
            }

            // instanceId가 없으면 WebSocket도 캡처도 붙을 곳을 모른다. 등록만 성공했다고
            // 넘어가면 다음 실패가 연결 쪽에서 원인 없이 나타난다.
            if (registration == null || string.IsNullOrWhiteSpace(registration.InstanceId))
            {
                Fail("등록 실패: 응답에 instanceId가 없습니다.");
                yield break;
            }

            ArtelSdkSession.SaveInstanceId(registration.InstanceId);

            // gameBuildId 는 없어도 등록을 실패시키지 않는다. 없어서 막히는 것은 근거 문서 업로드 하나뿐이고 그 사유는
            // scan_evidence 의 결과에 실린다 — instanceId 처럼 연결과 캡처가 통째로 붙을 곳을 잃는 값이 아니다.
            if (!string.IsNullOrWhiteSpace(registration.GameBuildId))
            {
                ArtelSdkSession.SaveGameBuildId(registration.GameBuildId);
            }

            HasError = false;
            State = ArtelConnectionState.Connecting;
            SetStatus("등록에 성공했습니다. 실시간 서버에 연결하는 중...");
            Connect(connect);
        }

        public void Connect(Action connect)
        {
            if (connect == null)
            {
                throw new ArgumentNullException(nameof(connect));
            }

            if (!CanConnect)
            {
                return;
            }

            try
            {
                connect();
                State = ArtelConnectionState.Connected;
                HasError = false;
                SetStatus("실시간 서버 연결을 시작했습니다.");
            }
            catch (Exception exception)
            {
                State = ArtelConnectionState.NeedsLogin;
                ShowPanel = true;
                HasError = true;
                SetStatus("연결 실패: " + exception.Message);
            }
        }

        public void LogOut()
        {
            ForgetSession();
            HasError = false;
            SetStatus("로그아웃했습니다.");
        }

        /// <summary>
        /// 요청에 쓸 SDK 토큰. 만료됐으면 refresh 토큰으로 한 번 다시 받아 본다. 실패하면 빈 문자열이고,
        /// <c>rejected</c>가 세션 처분을 가른다 — true는 서버가 refresh 토큰을 거절했거나 토큰이
        /// 아예 없어 재로그인뿐인 경우, false는 네트워크 등 일시 장애라 90일짜리 refresh 토큰을
        /// 지우면 안 되는 경우다. (ARTEL-231: 일시 장애를 거절로 뭉뚱그리면 Clear가 멀쩡한
        /// refresh 토큰까지 키체인에서 지워 브라우저 재로그인을 강제한다.)
        /// </summary>
        /// <remarks>
        /// 로컬 만료 시각으로만 판단한다. 만료 전에 받은 401은 서버가 토큰 자체를 거절한 것이라
        /// 재발급해도 같은 답이 온다. 그 경로는 지금처럼 재로그인으로 보낸다.
        /// </remarks>
        private IEnumerator EnsureToken(Server server, Action<string, bool> onToken)
        {
            if (ArtelSdkSession.TryLoadToken(out var token))
            {
                onToken(token, false);
                yield break;
            }

            if (!ArtelSdkSession.TryLoadRefreshToken(out var refreshToken))
            {
                // 재발급 수단 자체가 없다. 지울 것도 없으니 거절과 같은 길(재로그인)로 보낸다.
                onToken(string.Empty, true);
                yield break;
            }

            SetStatus("로그인 세션을 갱신하는 중...");

            UnityWebRequest request;
            try
            {
                request = authClient.CreateRefreshRequest(server, refreshToken);
            }
            catch (Exception)
            {
                onToken(string.Empty, false);
                yield break;
            }

            bool succeeded;
            long responseCode;
            string responseBody;
            using (request)
            {
                yield return request.SendWebRequest();

                succeeded = request.result == UnityWebRequest.Result.Success;
                responseCode = request.responseCode;
                responseBody = ReadBody(request);
            }

            if (!succeeded)
            {
                // 401만 확실한 거절이다. 그 외(전송 실패·타임아웃·5xx)는 refresh 토큰의
                // 잘못이 아니므로 남겨 두고 다시 시도하게 한다.
                onToken(string.Empty, responseCode == UnauthorizedStatusCode);
                yield break;
            }

            SdkTokenResponseDto refreshed;
            try
            {
                refreshed = jsonCodec.Deserialize<SdkTokenResponseDto>(responseBody);
            }
            catch (Exception)
            {
                onToken(string.Empty, false);
                yield break;
            }

            if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.Token))
            {
                onToken(string.Empty, false);
                yield break;
            }

            // 표시 이름은 재발급 응답에 없다. 로그인 때 저장한 값을 그대로 둔다.
            ArtelSdkSession.SaveToken(refreshed.Token, refreshed.ExpiresAt, ArtelSdkSession.DisplayName);
            HasToken = true;
            onToken(refreshed.Token.Trim(), false);
        }

        /// <summary>
        /// <see cref="EnsureToken"/>이 빈 토큰을 준 뒤의 공통 처리. 거절이면 세션을 버리고
        /// 재로그인으로, 일시 장애면 <see cref="Fail"/>로 보낸다 — refresh 토큰이 살아 있어
        /// <see cref="HasToken"/>이 true이므로 Fail은 프로젝트 화면의 "다시 시도"를 남긴다.
        /// </summary>
        private void FailTokenAcquisition(bool refreshRejected)
        {
            if (refreshRejected)
            {
                ExpireSession("로그인이 필요합니다.");
            }
            else
            {
                Fail("로그인 세션을 갱신하지 못했습니다. 네트워크를 확인하고 다시 시도해 주세요.");
            }
        }

        private void ExpireSession(string status)
        {
            ForgetSession();
            HasError = true;
            SetStatus(status);
        }

        private void ForgetSession()
        {
            ArtelSdkSession.Clear();
            projects.Clear();
            HasToken = false;
            DisplayName = string.Empty;
            SelectedProjectId = string.Empty;
            State = ArtelConnectionState.NeedsLogin;
            ShowPanel = true;
        }

        // 실패한 뒤 돌아갈 화면은 토큰이 남아 있느냐로 갈린다. 토큰이 있으면 다시 로그인시킬
        // 이유가 없고, 사람이 손볼 수 있는 것은 프로젝트 선택뿐이다.
        private void Fail(string status)
        {
            State = HasToken ? ArtelConnectionState.ChoosingProject : ArtelConnectionState.NeedsLogin;
            ShowPanel = true;
            HasError = true;
            SetStatus(status);
        }

        private static string ReadBody(UnityWebRequest request)
        {
            return request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
        }

        private static string DescribeFailure(long responseCode, string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return "HTTP " + responseCode;
            }

            return responseBody;
        }

        internal bool IsRetiredInstance(long responseCode, string responseBody)
        {
            if (responseCode != ConflictStatusCode || string.IsNullOrWhiteSpace(responseBody))
            {
                return false;
            }

            try
            {
                return jsonCodec.Deserialize<ApiErrorDto>(responseBody)?.Code == RetiredInstanceCode;
            }
            catch
            {
                return false;
            }
        }

        private void SetStatus(string status)
        {
            Status = status;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }
    }
}
