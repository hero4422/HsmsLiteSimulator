namespace HsmsLite.Gem
{
    /// <summary>Thrown when raw bytes don't decode as a valid (or supported) SECS-II item.</summary>
    public sealed class Secs2FormatException : Exception
    {
        public Secs2FormatException(string message) : base(message) { }
    }
}
