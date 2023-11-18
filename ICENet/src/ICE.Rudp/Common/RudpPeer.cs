using System;

namespace ICENet.Rudp.Common
{
    internal abstract class RudpPeer
    {
        public enum ClientState
        {
            Disconnected,

            Connecting,

            Connected
        }

        public enum RudpHeaders : byte
        {
            ConnectRequest = 1,

            ConnectResponse,

            HearbeatRequest,

            HeartbeatResponse,

            Unreliable,

            Reliable,      

            Ack,
        }

        public const int DisconnectTimeout = 3000;

        public const int HeartbeatInterval = 750;

        public const int MinResendTime = 200;

        public const int MaxResendTime = 600;

        public const int MaxResendCount = 8;

        #region Common Calculations

        private int _smoothRtt = 0;

        protected int _rtt = 0;

        protected int CalculateResendTime() => Math.Min(Math.Max(MinResendTime, _smoothRtt * 2), MaxResendTime);

        protected int CalculateSmoothRtt() => _smoothRtt = (int)((0.2 * _rtt) + (0.8 * _smoothRtt));  

        #endregion
    }
}
