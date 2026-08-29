using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class ArtelTestPageTests
    {
        [Test]
        public void Html_OffersLocalWebRtcViewerControls()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"stream-start\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"stream-stop\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"stream-video\" autoplay playsinline muted"));
            Assert.That(ArtelTestPage.Html, Does.Contain("event.streams[0] || new MediaStream([event.track])"));
        }

        [Test]
        public void Html_UsesExistingStreamingWireContract()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_START'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_RENEW'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'STREAM_STOP'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("message.type === 'WEBRTC_OFFER'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("type: 'WEBRTC_ANSWER'"));
            Assert.That(ArtelTestPage.Html, Does.Contain("message.type === 'WEBRTC_ICE'"));
        }

        [Test]
        public void Html_DeclaresTheViewerCleanupBoundaries()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("ws.onclose = () => { status.textContent = 'closed'; stopStream(false); }"));
            Assert.That(ArtelTestPage.Html, Does.Contain("window.addEventListener('beforeunload', () => stopStream(true))"));
            Assert.That(ArtelTestPage.Html, Does.Contain("clearInterval(streamRenewTimer)"));
            Assert.That(ArtelTestPage.Html, Does.Contain("streamVideo.srcObject = null"));
        }

        [Test]
        public void Html_SerializesCapturesSoTheirResultsRemainAddressable()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("captureButton.disabled = true"));
            Assert.That(ArtelTestPage.Html, Does.Contain("entry.id === pendingCaptureId"));
            Assert.That(ArtelTestPage.Html, Does.Contain("captureButton.disabled = false"));
        }
    }
}
