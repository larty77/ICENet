using System;
using System.Net;

namespace ICENet.Core.Transport
{
    public interface IPeer
    {
        IPEndPoint LocalEndPoint { get; }
    }
}
