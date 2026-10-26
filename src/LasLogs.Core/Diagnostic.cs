namespace LasLogs.Core
{
    public enum Severity
    {
        Observation,     // a fact; nothing was changed
        Transformation,  // data or naming changed
        Warning,         // a human should look
        Error,           // something could not be loaded
    }

    /// <summary>
    /// One aggregated event: a stable <see cref="Code"/> plus how often it happened and where
    /// (source line numbers; the original file is the audit copy).
    /// </summary>
    public sealed class Diagnostic
    {
        public Diagnostic(Severity severity, string code, string message, int firstLine, int lastLine, string curve = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            FirstLine = firstLine;
            LastLine = lastLine;
            Curve = curve;
            Count = 1;
        }

        public Severity Severity { get; }
        public string Code { get; }
        /// <summary>Built from the first occurrence.</summary>
        public string Message { get; }
        public string Curve { get; }
        public int Count { get; set; }
        /// <summary>1-based line number; 0 when the event is not tied to a line.</summary>
        public int FirstLine { get; }
        public int LastLine { get; set; }

        public override string ToString()
        {
            return Severity + " " + Code + (Curve == null ? "" : " [" + Curve + "]") + " x" + Count
                + " lines " + FirstLine + "-" + LastLine + ": " + Message;
        }
    }
}
