using System;
using System.Collections;
using Artel.Domain;
using UnityEngine.Networking;

namespace Artel
{
    internal sealed class ArtelOnboardingViewModel
    {
        private readonly ArtelSdkRegistrationClient registrationClient;
        private bool registered;
        private bool requestInProgress;

        public ArtelOnboardingViewModel(ArtelSdkRegistrationClient registrationClient)
        {
            this.registrationClient = registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
            Status = "SDK ID를 서버에 등록해 주세요.";
        }

        public event Action Changed;

        public string Status { get; private set; }
        public bool CanRegister { get { return !requestInProgress; } }
        public bool CanConnect { get { return registered && !requestInProgress; } }

        public IEnumerator Register(Server server, string sdkId)
        {
            if (!CanRegister)
            {
                yield break;
            }

            requestInProgress = true;
            registered = false;
            SetStatus("SDK ID 등록 중...");

            UnityWebRequest request;
            try
            {
                request = registrationClient.CreateRequest(server, sdkId);
            }
            catch (Exception exception)
            {
                requestInProgress = false;
                SetStatus("설정 오류: " + exception.Message);
                yield break;
            }

            using (request)
            {
                yield return request.SendWebRequest();

                registered = request.result == UnityWebRequest.Result.Success;
                var response = string.IsNullOrWhiteSpace(request.downloadHandler.text)
                    ? "HTTP " + request.responseCode
                    : request.downloadHandler.text;
                Status = (registered ? "등록 성공: " : "등록 실패: ") + response;
            }

            requestInProgress = false;
            NotifyChanged();
        }

        public void Connect(Action connect)
        {
            if (!CanConnect)
            {
                return;
            }

            try
            {
                connect();
                registered = false;
                SetStatus("실시간 서버 연결을 시작했습니다.");
            }
            catch (Exception exception)
            {
                SetStatus("연결 실패: " + exception.Message);
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
