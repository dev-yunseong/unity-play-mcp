using System;
using System.Collections;

namespace Artel.Capture
{
    /// <summary>
    /// Issues the capture's ticket itself and serves the image from the local test page.
    /// </summary>
    /// <remarks>
    /// <see cref="CaptureUploader"/> 는 두 홉이다 — 오케스트레이션에서 서명 URL 을 받고, 바이트를
    /// 스토리지로 PUT 한다. 그 첫 홉이 테스트 페이지에서는 항상 막힌다. 티켓 엔드포인트가 실행 중인 QA 가
    /// 없는 인스턴스를 <c>409 conflict</c> 로 거절하기 때문이고, 그 거절은 옳다 — 캡처는 실행에 붙는
    /// 근거이지 아무 때나 남기는 파일이 아니다.
    ///
    /// 그래서 우회하지 않고 대체한다. 두 홉을 다 지역에서 끝내고, 이미 열려 있는 테스트 페이지 서버를
    /// 가리키는 URL 을 돌려준다. 브라우저는 <c>&lt;img&gt;</c> 하나로 그것을 읽는다. 네트워크도, 세션도,
    /// 실행 중인 QA 도 필요 없다.
    /// </remarks>
    internal sealed class LocalCaptureUploader : ICaptureUploader
    {
        private readonly LocalCaptureStore store;

        /// <summary>
        /// 지연해서 읽는다. 이 업로더는 페이지 서버가 포트를 열기 전에 만들어질 수 있다.
        /// </summary>
        private readonly Func<string> pageUrl;

        public LocalCaptureUploader(LocalCaptureStore store, Func<string> pageUrl)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.pageUrl = pageUrl ?? throw new ArgumentNullException(nameof(pageUrl));
        }

        public IEnumerator Upload(
            CapturedImage image,
            CaptureRequest request,
            Action<CaptureUpload> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            var id = store.Add(image.Bytes, request.ContentType);
            completed(new CaptureUpload
            {
                CaptureId = id,
                Url = pageUrl().TrimEnd('/') + ArtelTestPageServer.CapturePath + id,

                // 만료가 없다. 이 이미지는 저장소가 아니라 이 프로세스의 메모리에 있고, 에디터를 멈추면
                // 함께 사라진다. 빈 값으로 두는 편이 지키지 못할 시각을 적는 것보다 낫다.
                ExpiresAt = null
            });

            yield break;
        }
    }
}
