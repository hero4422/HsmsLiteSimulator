namespace HsmsLite.Gem
{
    /// <summary>Thrown when a received message doesn't match the expected GEM stream/function,
    /// or its SECS-II item structure doesn't match what that message is supposed to carry.</summary>
    public sealed class GemMessageException : Exception
    {
        public GemMessageException(string message) : base(message) { }
    }
}
