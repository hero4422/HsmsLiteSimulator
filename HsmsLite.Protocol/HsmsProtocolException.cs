namespace HsmsLite.Protocol
{
    // 프로토콜 위반 에외
    /// <summary>Thrown when a message is not valid for the connection's current HSMS state
    /// (e.g. a Data Message arrives before Select.req/rsp has completed).</summary>
    public sealed class HsmsProtocolException : Exception
    {
        public HsmsProtocolException(string message) : base(message) { }
    }
}
