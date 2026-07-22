using System;
using System.Collections;
using Artel.Domain;
using UnityEngine.Networking;

namespace Artel
{
    internal sealed class ArtelOnboardingViewModel
    {
        private const long NotFoundStatusCode = 404;

        private readonly ArtelSdkRegistrationClient registrationClient;
        private string keyInput = string.Empty;

        public ArtelOnboardingViewModel(ArtelSdkRegistrationClient registrationClient)
        {
            this.registrationClient = registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
            State = ArtelOnboardingState.NeedsKey;
            ShowPanel = true;
            Status = "대시보드에서 발급받은 인스턴스 키를 입력해 주세요.";
        }

        public event Action Changed;

        public string Status { get; private set; }
        public ArtelOnboardingState State { get; private set; }
        public bool ShowPanel { get; private set; }
        public bool HasStoredKey { get; private set; }

        public string KeyInput
        {
            get { return keyInput; }
            set
            {
                var newValue = value ?? string.Empty;
                if (string.Equals(keyInput, newValue, StringComparison.Ordinal))
                {
                    return;
                }

                keyInput = newValue;
                NotifyChanged();
            }
        }

        public bool CanRegister
        {
            get { return State != ArtelOnboardingState.Registering && !string.IsNullOrWhiteSpace(keyInput); }
        }

        public bool CanConnect
        {
            get { return State != ArtelOnboardingState.Registering && HasStoredKey; }
        }

        /// <summary>
        /// Loads the persisted instance key. Must run no earlier than <c>Start</c>, because
        /// <see cref="ArtelManager"/> adds the onboarding controller before its own identity exists.
        /// </summary>
        public void Initialize()
        {
            if (ArtelInstanceKey.TryLoad(out var storedKey))
            {
                HasStoredKey = true;
                keyInput = storedKey;
                ShowPanel = false;
                Status = "저장된 인스턴스 키로 등록하는 중...";
            }
            else
            {
                HasStoredKey = false;
                keyInput = string.Empty;
                ShowPanel = true;
                Status = "대시보드에서 발급받은 인스턴스 키를 입력해 주세요.";
            }

            State = ArtelOnboardingState.NeedsKey;
            NotifyChanged();
        }

        public IEnumerator Register(
            Server server,
            string instanceKey,
            string sdkUuid,
            string gameVersion,
            Action connect)
        {
            if (State == ArtelOnboardingState.Registering)
            {
                yield break;
            }

            var trimmedKey = (instanceKey ?? string.Empty).Trim();
            if (trimmedKey.Length == 0)
            {
                FailRegistration("인스턴스 키를 입력해 주세요.");
                yield break;
            }

            // 서버는 빈 버전을 거절한다. 여기서 걸러내지 않으면 Player Settings를 비워둔
            // 프로젝트가 원인을 알 수 없는 400을 받는다.
            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                FailRegistration("Player Settings의 Version이 비어 있습니다. 값을 설정한 뒤 다시 시도해 주세요.");
                yield break;
            }

            KeyInput = trimmedKey;
            State = ArtelOnboardingState.Registering;
            SetStatus("인스턴스 키를 등록하는 중...");

            UnityWebRequest request;
            try
            {
                request = registrationClient.CreateRequest(server, trimmedKey, sdkUuid, gameVersion);
            }
            catch (Exception exception)
            {
                FailRegistration("설정 오류: " + exception.Message);
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
                responseBody = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
            }

            if (!succeeded)
            {
                if (responseCode == NotFoundStatusCode)
                {
                    ArtelInstanceKey.Clear();
                    HasStoredKey = false;
                    FailRegistration("등록 실패: 알 수 없는 인스턴스 키입니다. 대시보드에서 키를 다시 확인해 주세요.");
                }
                else
                {
                    FailRegistration("등록 실패: " + DescribeFailure(responseCode, responseBody));
                }

                yield break;
            }

            ArtelInstanceKey.Save(trimmedKey);
            HasStoredKey = true;
            State = ArtelOnboardingState.Connecting;
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
                State = ArtelOnboardingState.Connected;
                SetStatus("실시간 서버 연결을 시작했습니다.");
            }
            catch (Exception exception)
            {
                State = ArtelOnboardingState.NeedsKey;
                ShowPanel = true;
                SetStatus("연결 실패: " + exception.Message);
            }
        }

        public void ClearStoredKey()
        {
            ArtelInstanceKey.Clear();
            HasStoredKey = false;
            keyInput = string.Empty;
            State = ArtelOnboardingState.NeedsKey;
            ShowPanel = true;
            SetStatus("저장된 인스턴스 키를 지웠습니다.");
        }

        private void FailRegistration(string status)
        {
            State = ArtelOnboardingState.NeedsKey;
            ShowPanel = true;
            SetStatus(status);
        }

        private static string DescribeFailure(long responseCode, string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return "HTTP " + responseCode;
            }

            return responseBody;
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
