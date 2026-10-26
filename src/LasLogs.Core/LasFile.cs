using System.Collections.Generic;
using System.Linq;

namespace LasLogs.Core
{
    public enum FileStatus
    {
        Loaded,   // every record parsed (value-level warnings allowed)
        Partial,  // some lines or records could not be loaded
        Failed,   // no usable data
    }

    public enum DepthOrder
    {
        Unknown,       // fewer than 2 records, or every depth the same
        Increasing,
        Decreasing,    // reversed by the Conditioner
        NonMonotonic,  // kept in file order
    }

    /// <summary>
    /// A LAS file. The reader leaves values raw; <see cref="Conditioner.Run"/> then applies the null policy and depth
    /// order in place and fills the conditioning results.
    /// </summary>
    public sealed class LasFile
    {
        public LasFile(string sourceName)
        {
            SourceName = sourceName;
        }

        public string SourceName { get; }
        /// <summary>VERS as a number (1.2, 2.0, 3.0); NaN when missing or unreadable.</summary>
        public double Version { get; set; } = double.NaN;
        /// <summary>How the data was read: true for wrapped, including recovered undeclared wrap.</summary>
        public bool Wrap { get; set; }
        /// <summary>',' for DLM COMMA; otherwise ' ' (any whitespace).</summary>
        public char Delimiter { get; set; } = ' ';
        public List<HeaderItem> VersionItems { get; } = new List<HeaderItem>();
        public List<HeaderItem> WellItems { get; } = new List<HeaderItem>();
        public List<HeaderItem> ParameterItems { get; } = new List<HeaderItem>();
        /// <summary>In column order. Mnemonics may repeat: identify curves by position.</summary>
        public List<CurveDef> Curves { get; } = new List<CurveDef>();
        /// <summary>Source line of each loaded record (its first line when wrapped).</summary>
        public int[] RecordLines { get; set; } = new int[0];
        public int RecordCount { get { return RecordLines.Length; } }
        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
        public FileStatus Status { get; set; }

        // ---- set by the Conditioner ----
        /// <summary>Depth order as found in the file, before any reversal.</summary>
        public DepthOrder DepthOrder { get; set; }
        /// <summary>Most common absolute depth interval; NaN when unknown.</summary>
        public double ModalStep { get; set; } = double.NaN;
        /// <summary>One entry per <see cref="Concept"/>, indexed by its value; null when nothing was loaded.</summary>
        public ConceptCurve[] Concepts { get; set; }

        public Diagnostic Add(Severity severity, string code, string message, int firstLine = 0, int lastLine = 0, string curve = null, int count = 1)
        {
            var d = new Diagnostic(severity, code, message, firstLine, lastLine, curve) { Count = count };
            Diagnostics.Add(d);
            return d;
        }

        /// <summary>Failed = no records; Partial = any Error diagnostic; otherwise Loaded.</summary>
        public void UpdateStatus()
        {
            Status = RecordCount == 0 ? FileStatus.Failed
                : Diagnostics.Any(d => d.Severity == Severity.Error) ? FileStatus.Partial
                : FileStatus.Loaded;
        }
    }

    /// <summary>One <c>MNEM.UNIT VALUE : DESCRIPTION</c> header line.</summary>
    public sealed class HeaderItem
    {
        public HeaderItem(string mnemonic, string unit, string value, string description, int line)
        {
            Mnemonic = mnemonic;
            Unit = unit;
            Value = value;
            Description = description;
            Line = line;
        }

        public string Mnemonic { get; }
        public string Unit { get; }
        public string Value { get; }
        public string Description { get; }
        public int Line { get; }

        /// <summary>
        /// Splits a header line: the first '.' ends the mnemonic and the first whitespace after it ends
        /// the unit. The description starts at the first ':' that follows whitespace, or else at the
        /// last ':'. That keeps "13:45" in values and "Type of Cut: (...)" in descriptions.
        /// Returns null for a line without a dot.
        /// </summary>
        public static HeaderItem Parse(string line, int lineNumber, out bool hasColon)
        {
            hasColon = false;
            int dot = line.IndexOf('.');
            if (dot < 0) return null;

            int colon = -1;
            for (int i = dot + 1; i < line.Length && colon < 0; i++)
                if (line[i] == ':' && char.IsWhiteSpace(line[i - 1])) colon = i;
            if (colon < 0 && line.LastIndexOf(':') > dot) colon = line.LastIndexOf(':');
            hasColon = colon >= 0;
            int end = hasColon ? colon : line.Length;

            int unitEnd = dot + 1;
            while (unitEnd < end && !char.IsWhiteSpace(line[unitEnd])) unitEnd++;

            return new HeaderItem(
                line.Substring(0, dot).Trim(),
                line.Substring(dot + 1, unitEnd - dot - 1),
                line.Substring(unitEnd, end - unitEnd).Trim(),
                hasColon ? line.Substring(colon + 1).Trim() : "",
                lineNumber);
        }
    }

    public sealed class CurveDef
    {
        public CurveDef(int index, string mnemonic, string unit, string apiCode, string description, int line)
        {
            Index = index;
            Mnemonic = mnemonic;
            Unit = unit;
            ApiCode = apiCode;
            Description = description;
            Line = line;
        }

        /// <summary>0-based column position; column 0 is the depth index.</summary>
        public int Index { get; }
        public string Mnemonic { get; }
        public string Unit { get; }
        public string ApiCode { get; }
        public string Description { get; }
        public int Line { get; }
        /// <summary>One value per record. Raw: NaN only where the token was not a number. Conditioned: nulls are NaN too.</summary>
        public double[] Values { get; set; } = new double[0];
    }
}
