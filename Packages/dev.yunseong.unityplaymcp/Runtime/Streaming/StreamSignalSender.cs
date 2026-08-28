using System;
using Artel.Serialization;

namespace Artel.Streaming
{
    internal interface IStreamSignalSender
    {
        void Send(object message);
    }

    /// <summary>
    /// Puts signalling on the existing /ws/sdk connection.
    /// </summary>
    internal sealed class WebSocketStreamSignalSender : IStreamSignalSender
    {
        private readonly IJsonCodec jsonCodec;
        private readonly Func<IArtelWebSocketTransport> currentTransport;

        /// <param name="currentTransport">
        /// Resolved per send rather than captured once: the manager replaces and clears its
        /// transport over the process lifetime, and a session outliving one of those swaps must
        /// not keep writing to the socket that was there when it started.
        /// </param>
        public WebSocketStreamSignalSender(IJsonCodec jsonCodec, Func<IArtelWebSocketTransport> currentTransport)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.currentTransport = currentTransport ?? throw new ArgumentNullException(nameof(currentTransport));
        }

        public void Send(object message)
        {
            var transport = currentTransport();
            if (transport == null || !transport.IsConnected)
            {
                // Teardown reports STOPPED, and the socket dropping is one of the reasons a
                // session tears down, so an unsendable message here is ordinary rather than
                // exceptional. The SDK's own lease timer is what ends the stream either way.
                return;
            }

            transport.Send(jsonCodec.Serialize(message));
        }
    }
}
