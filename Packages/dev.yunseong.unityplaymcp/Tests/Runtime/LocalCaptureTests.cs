using System.Collections;
using Artel.Capture;
using NUnit.Framework;

namespace Artel.Tests.Capture
{
    /// <summary>
    /// 테스트 페이지가 캡처를 스스로 내주는 경로.
    /// </summary>
    /// <remarks>
    /// 오케스트레이션의 티켓 엔드포인트는 실행 중인 QA 가 없는 인스턴스를 <c>409</c> 로 거절하므로,
    /// 테스트 페이지에서 찍은 캡처는 그 경로로 한 장도 나가지 못한다. 여기서 재는 것은 그 대체 경로가
    /// 브라우저가 실제로 되물을 수 있는 URL 을 내놓는가다.
    /// </remarks>
    public sealed class LocalCaptureTests
    {
        [Test]
        public void Upload_PointsAtThePageServerAndLeavesTheBytesThere()
        {
            var store = new LocalCaptureStore();
            var uploader = new LocalCaptureUploader(store, () => "http://127.0.0.1:17310/");

            var upload = Upload(uploader, store, new byte[] { 1, 2, 3 }, out var captureId);

            Assert.That(upload.IsSuccess, Is.True);
            Assert.That(upload.Url, Is.EqualTo("http://127.0.0.1:17310/captures/" + captureId));

            Assert.That(store.TryGet(captureId, out var stored), Is.True);
            Assert.That(stored.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));

            // 전체 화면은 JPEG 로 나간다. 서버가 그대로 Content-Type 에 싣는 값이라, 틀리면 브라우저가
            // 이미지를 그리지 않고 내려받는다.
            Assert.That(stored.ContentType, Is.EqualTo("image/jpeg"));
        }

        /// <summary>
        /// 만료를 적지 않는다.
        /// </summary>
        /// <remarks>
        /// 이 이미지는 저장소가 아니라 프로세스 메모리에 있고 에디터를 멈추면 사라진다. 지키지 못할 시각을
        /// 적는 것보다 비워 두는 편이 정직하다.
        /// </remarks>
        [Test]
        public void Upload_ReportsNoExpiry()
        {
            var store = new LocalCaptureStore();
            var uploader = new LocalCaptureUploader(store, () => "http://127.0.0.1:17310/");

            var upload = Upload(uploader, store, new byte[] { 9 }, out _);

            Assert.That(upload.ExpiresAt, Is.Null);
        }

        /// <summary>
        /// 페이지 URL 은 업로드할 때 읽는다.
        /// </summary>
        /// <remarks>
        /// 업로더는 페이지 서버가 포트를 열기 전에 만들어질 수 있다. 생성 시점의 값을 굳혔다면 첫 캡처가
        /// 옛 주소를 가리킨다.
        /// </remarks>
        [Test]
        public void Upload_ReadsThePageUrlLate()
        {
            var store = new LocalCaptureStore();
            var pageUrl = "http://127.0.0.1:1/";
            var uploader = new LocalCaptureUploader(store, () => pageUrl);

            pageUrl = "http://127.0.0.1:17310/";
            var upload = Upload(uploader, store, new byte[] { 4 }, out var captureId);

            Assert.That(upload.Url, Is.EqualTo("http://127.0.0.1:17310/captures/" + captureId));
        }

        /// <summary>
        /// 오래된 캡처는 밀려난다.
        /// </summary>
        /// <remarks>
        /// 에디터 한 세션이 캡처를 수백 장 찍는데 브라우저가 되묻는 것은 화면에 걸린 한 장뿐이다. 전부
        /// 들고 있으면 그 세션의 메모리가 캡처 크기 곱하기 횟수로 자란다.
        /// </remarks>
        [Test]
        public void Store_ForgetsTheOldestPastItsLimit()
        {
            var store = new LocalCaptureStore();
            var first = store.Add(new byte[] { 1 }, "image/png");

            for (var i = 0; i < 8; i++)
            {
                store.Add(new byte[] { 2 }, "image/png");
            }

            Assert.That(store.TryGet(first, out _), Is.False);
        }

        /// <summary>id 를 다시 쓰지 않는다. 같은 URL 이 다른 그림을 가리키면 브라우저 캐시가 거짓말을 한다.</summary>
        [Test]
        public void Store_NeverReusesAnId()
        {
            var store = new LocalCaptureStore();

            var first = store.Add(new byte[] { 1 }, "image/png");
            var second = store.Add(new byte[] { 2 }, "image/png");

            Assert.That(second, Is.Not.EqualTo(first));
        }

        private static CaptureUpload Upload(
            LocalCaptureUploader uploader,
            LocalCaptureStore store,
            byte[] bytes,
            out string captureId)
        {
            var image = new CapturedImage { Bytes = bytes, Width = 4, Height = 2 };
            var result = default(CaptureUpload);
            Drain(uploader.Upload(image, default(CaptureRequest), value => result = value));
            captureId = result.CaptureId;
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
            }
        }
    }
}
