using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using Artel.Capture;
using Artel.Evidence;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// 순회가 씬을 하나씩 읽는 동안 화면이 한 장씩 모이는가.
    /// </summary>
    /// <remarks>
    /// 이 수집기가 조용히 틀리는 방식이 몇 가지다. 화면을 못 떠도 아무 말 없이 넘어가면 사용자는 SDK 가 안 찍은 것과 못 찍은
    /// 것을 구분할 수 없고, 캡처 하나가 터져서 예외가 새어 나가면 순회 전체가 멎어 근거 문서가 아예 안 나온다. 같은 씬을 두 번
    /// 실으면 서버가 그 등록 전체를 400 으로 돌려준다. 전부 화면을 봐서는 알 수 없어 여기서 잡는다.
    /// </remarks>
    public sealed class SceneThumbnailCollectorTests
    {
        [TearDown]
        public void ClearHook()
        {
            // 건 자리를 남기면 다음 테스트의 순회가 이 테스트의 수집기로 들어간다.
            SceneWalkHooks.SceneRead = null;
        }

        [Test]
        public void 씬마다_한_장씩_모은다()
        {
            var collector = new SceneThumbnailCollector(new FakeCapturer());

            Drain(collector, "TitleScene");
            Drain(collector, "MapScene");

            Assert.That(collector.Thumbnails.Count, Is.EqualTo(2));
            Assert.That(collector.Thumbnails[0].SceneName, Is.EqualTo("TitleScene"));
            Assert.That(collector.Thumbnails[0].IsSuccess, Is.True);
            Assert.That(collector.Thumbnails[0].Width, Is.EqualTo(320));
            Assert.That(collector.Thumbnails[1].SceneName, Is.EqualTo("MapScene"));
        }

        /// <summary>
        /// 같은 씬을 두 번 밟아도 한 장이다.
        /// </summary>
        /// <remarks>
        /// 빌드 인덱스로 한 번, 주소로 한 번 — 순회는 실제로 같은 씬을 두 번 열 수 있다. 두 장을 실으면 서버는 그 등록을
        /// 통째로 거절하므로, 화면 한 장이 아니라 그 실행의 근거 전체를 잃는다.
        /// </remarks>
        [Test]
        public void 같은_씬을_두_번_읽어도_한_장만_남는다()
        {
            var collector = new SceneThumbnailCollector(new FakeCapturer());

            Drain(collector, "TitleScene");
            Drain(collector, "TitleScene");

            Assert.That(collector.Thumbnails.Count, Is.EqualTo(1));
        }

        /// <summary>이름이 없으면 서버가 붙일 씬 행을 못 찾는다. 실패로도 적지 않는다.</summary>
        [Test]
        public void 이름_없는_씬은_적지_않는다()
        {
            var collector = new SceneThumbnailCollector(new FakeCapturer());

            Drain(collector, string.Empty);
            Drain(collector, "   ");

            Assert.That(collector.Thumbnails, Is.Empty);
        }

        /// <summary>못 뜬 것도 사실이다. 빼면 화면이 "안 찍었다"와 "못 찍었다"를 구분할 수 없다.</summary>
        [Test]
        public void 캡처가_실패하면_이유를_적는다()
        {
            var collector = new SceneThumbnailCollector(
                new FakeCapturer(CapturedImage.Failed("no framebuffer")));

            Drain(collector, "TitleScene");

            Assert.That(collector.Thumbnails.Count, Is.EqualTo(1));
            Assert.That(collector.Thumbnails[0].IsSuccess, Is.False);
            Assert.That(collector.Thumbnails[0].FailureCode, Is.EqualTo("capture-failed"));
        }

        /// <summary>
        /// 캡처가 터져도 순회는 계속 간다.
        /// </summary>
        /// <remarks>
        /// 곁다리인 화면 한 장 때문에 근거 문서를 통째로 잃지 않는다는 것이 이 이슈의 제약이다. 예외가 새어 나가면
        /// 순회 코루틴이 그 자리에서 멎고, 그 실행은 문서 없이 끝난다.
        /// </remarks>
        [Test]
        public void 캡처가_터져도_순회는_이어진다()
        {
            var collector = new SceneThumbnailCollector(new ThrowingCapturer());

            Assert.DoesNotThrow(() => Drain(collector, "TitleScene"));

            Assert.That(collector.Thumbnails.Count, Is.EqualTo(1));
            Assert.That(collector.Thumbnails[0].FailureCode, Is.EqualTo("capture-failed"));
        }

        /// <summary>붙였다 뗀 자리는 비어 있어야 한다. 남으면 다음 실행의 화면이 이 수집기에 쌓인다.</summary>
        [Test]
        public void 뗀_뒤에는_순회가_이_수집기를_부르지_않는다()
        {
            var collector = new SceneThumbnailCollector(new FakeCapturer());

            collector.Attach();
            Assert.That(SceneWalkHooks.OnSceneRead("TitleScene"), Is.Not.Null);

            collector.Detach();
            Assert.That(SceneWalkHooks.OnSceneRead("TitleScene"), Is.Null);
        }

        /// <summary>남이 건 자리를 지우지 않는다. 지우면 그쪽은 아무 말 없이 화면을 잃는다.</summary>
        [Test]
        public void 남이_건_자리는_떼지_않는다()
        {
            var collector = new SceneThumbnailCollector(new FakeCapturer());
            Func<string, IEnumerator> other = _ => null;

            collector.Attach();
            SceneWalkHooks.SceneRead = other;
            collector.Detach();

            Assert.That(SceneWalkHooks.SceneRead, Is.SameAs(other));
        }

        /// <summary>훅을 건 쪽이 없으면 순회는 아무것도 하지 않는다.</summary>
        [Test]
        public void 아무도_안_걸면_순회는_그냥_지나간다()
        {
            SceneWalkHooks.SceneRead = null;

            Assert.That(SceneWalkHooks.OnSceneRead("TitleScene"), Is.Null);
        }

        /// <summary>코루틴을 만들다 터지는 것은 순회가 잡아 준다 — 그것까지 새면 순회가 멎는다.</summary>
        [Test]
        public void 훅이_시작도_전에_터지면_순회가_삼킨다()
        {
            SceneWalkHooks.SceneRead = _ => throw new InvalidOperationException("boom");

            IEnumerator step = null;
            Assert.DoesNotThrow(() => step = SceneWalkHooks.OnSceneRead("TitleScene"));
            Assert.That(step, Is.Null);
        }

        /// <summary>수집기를 씬 하나만큼 돌린다. 순회가 하는 일과 같다.</summary>
        private static void Drain(SceneThumbnailCollector collector, string sceneName)
        {
            collector.Attach();

            try
            {
                var step = SceneWalkHooks.OnSceneRead(sceneName);

                if (step == null)
                {
                    return;
                }

                Drain(step);
            }
            finally
            {
                collector.Detach();
            }
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }

        private sealed class FakeCapturer : IScreenCapturer
        {
            private readonly CapturedImage image;

            public FakeCapturer(CapturedImage? image = null)
            {
                this.image = image ?? new CapturedImage
                {
                    Bytes = new byte[] { 0xFF, 0xD8, 0xFF },
                    Width = 320,
                    Height = 180
                };
            }

            public IEnumerator Capture(CaptureRequest request, Rect? pixelRect, Action<CapturedImage> completed)
            {
                completed(image);
                yield break;
            }
        }

        private sealed class ThrowingCapturer : IScreenCapturer
        {
            public IEnumerator Capture(CaptureRequest request, Rect? pixelRect, Action<CapturedImage> completed)
            {
                return Run();
            }

            private static IEnumerator Run()
            {
                yield return null;
                throw new InvalidOperationException("the framebuffer went away");
            }
        }
    }
}
