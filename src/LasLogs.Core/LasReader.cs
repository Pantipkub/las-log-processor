using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LasLogs.Core
{
    /// <summary>
    /// Hand-written LAS 1.2 / 2.0 reader, plus the LAS 2.0-compatible subset of LAS 3.0.
    /// Streams line by line and never throws on bad content: problems become diagnostics.
    /// Only the first data set is read; everything after the first ~A section is skipped.
    /// </summary>
    public sealed class LasReader
    {
        // Undeclared-wrap check: if most of the first data lines don't hold one value per curve, read as wrapped.
        private const int ProbeLines = 20;
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private readonly LasFile file;
        private char section;       // letter of the current section; '\0' before the first '~', '-' when skipping
        private bool dataSeen;      // the first ~A has started; later sections are skipped
        private bool wrap;
        private bool undeclaredWrap;
        private int curveCount;
        private List<double>[] columns;
        private readonly List<int> recordLines = new List<int>();
        private Diagnostic[] nonNumeric;                      // per column
        private List<KeyValuePair<int, string[]>> probe;      // first data lines (line, tokens), held for the wrap check
        private string[] pending;                             // wrapped record being assembled
        private int[] pendingLines;
        private int pendingCount;
        private Diagnostic resync;                            // wrapped: rejection extended until the next record start

        private LasReader(string sourceName)
        {
            file = new LasFile(sourceName);
        }

        public static LasFile Read(string path)
        {
            // No BOM detection: a stray FF FE must not switch the decoding to UTF-16.
            using (var reader = new StreamReader(path, Latin1, false, 1 << 16))
                return Read(reader, Path.GetFileName(path));
        }

        public static LasFile Read(TextReader reader, string sourceName)
        {
            return new LasReader(sourceName).Run(reader);
        }

        private LasFile Run(TextReader reader)
        {
            string line;
            int n = 0;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                if (n == 1) line = StripBom(line);
                int first = FirstNonSpace(line);
                if (first >= 0 && line[first] == '~')
                    StartSection(line.Substring(first + 1).Trim(), n);
                else if (section == 'A')
                    DataLine(line, first, n);
                else if (first >= 0 && line[first] != '#' && "VWPC".IndexOf(section) >= 0)
                    HeaderLine(line, n);
            }
            EndData();
            Finish();
            return file;
        }

        // ---- sections ----

        private void StartSection(string name, int n)
        {
            EndData();
            char letter = SectionLetter(name);
            if (dataSeen)
            {
                if (letter == 'A') Add(Severity.Observation, "MULTIPLE_DATASETS", n, "another ~A data set at line " + n + " not loaded; only the first is read");
                else Add(Severity.Observation, "SECTION_SKIPPED", n, "~" + name + " after the data section skipped");
                section = '-';
                return;
            }
            switch (letter)
            {
                case 'V': case 'W': case 'P': case 'C': case 'O':
                    section = letter;
                    break;
                case 'A':
                    dataSeen = true;
                    StartData(n);
                    break;
                default:
                    Add(Severity.Observation, "SECTION_SKIPPED", n, "~" + name + " skipped (not a supported section)");
                    section = '-';
                    break;
            }
        }

        private char SectionLetter(string name)
        {
            if (name.Length == 0) return '?';
            if (!(file.Version >= 3)) return char.ToUpperInvariant(name[0]);   // LAS 1.2/2.0: only the first letter counts

            // LAS 3.0: names matter (~Comment_Data is not ~Curve). "~Ascii | Curve" -> "ASCII".
            int end = name.IndexOfAny(new[] { ' ', '\t', '|' });
            switch ((end < 0 ? name : name.Substring(0, end)).ToUpperInvariant())
            {
                case "V": case "VERSION": return 'V';
                case "W": case "WELL": return 'W';
                case "P": case "PARAMETER": case "LOG_PARAMETER": return 'P';
                case "C": case "CURVE": case "LOG_DEFINITION": return 'C';
                case "A": case "ASCII": case "LOG_DATA": return 'A';
                case "O": case "OTHER": return 'O';
                default: return '?';
            }
        }

        // ---- header lines ----

        private void HeaderLine(string line, int n)
        {
            bool hasColon;
            HeaderItem item = HeaderItem.Parse(line, n, out hasColon);
            if (item == null)
            {
                Note(Severity.Observation, "HEADER_NO_DOT", n, n, "header line without '.' ignored");
                return;
            }
            if (!hasColon)
                Note(Severity.Observation, "HEADER_NO_COLON", n, n, "header line without ':'; value '" + item.Value + "' kept, description empty");

            switch (section)
            {
                case 'V':
                    file.VersionItems.Add(item);
                    double version;
                    if (Is(item, "VERS"))
                        file.Version = double.TryParse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out version) ? version : double.NaN;
                    break;
                case 'W':
                    // LAS 1.2 puts ~W values in the description field, except for STRT/STOP/STEP/NULL.
                    if (file.Version < 2 && !(Is(item, "STRT") || Is(item, "STOP") || Is(item, "STEP") || Is(item, "NULL")))
                        item = new HeaderItem(item.Mnemonic, item.Unit, item.Description, item.Value, item.Line);
                    file.WellItems.Add(item);
                    break;
                case 'P':
                    file.ParameterItems.Add(item);
                    break;
                case 'C':
                    int index = file.Curves.Count;
                    string mnemonic = item.Mnemonic;
                    if (mnemonic.Length == 0)
                    {
                        // Keep the column: dropping it would shift every following curve.
                        mnemonic = "CURVE_" + (index + 1);
                        Add(Severity.Warning, "EMPTY_MNEMONIC", n, "curve " + (index + 1) + " has no mnemonic; named " + mnemonic, mnemonic);
                    }
                    file.Curves.Add(new CurveDef(index, mnemonic, item.Unit, item.Value, item.Description, n));
                    break;
            }
        }

        // ---- data ----

        private void StartData(int n)
        {
            curveCount = file.Curves.Count;
            if (curveCount == 0)
            {
                Add(Severity.Error, "DATA_BEFORE_CURVES", n, "~A at line " + n + " has no ~C curve definitions before it");
                section = '-';
                return;
            }
            section = 'A';

            string wrapText = Value("WRAP").ToUpperInvariant();
            wrap = file.Wrap = wrapText.StartsWith("Y");
            if (!wrap && !wrapText.StartsWith("N"))
                Add(Severity.Observation, "WRAP_MISSING", n, "WRAP missing or unrecognised ('" + Value("WRAP") + "'); NO assumed");

            string dlm = Value("DLM").ToUpperInvariant();
            if (dlm.StartsWith("COMMA")) file.Delimiter = ',';
            else if (dlm.Length > 0 && !dlm.StartsWith("SPACE") && !dlm.StartsWith("TAB"))
                Add(Severity.Warning, "DLM_UNKNOWN", n, "DLM '" + Value("DLM") + "' not recognised; whitespace assumed");

            columns = new List<double>[curveCount];
            for (int i = 0; i < curveCount; i++) columns[i] = new List<double>();
            nonNumeric = new Diagnostic[curveCount];
            pending = new string[curveCount];
            pendingLines = new int[curveCount];
            if (!wrap && curveCount > 1) probe = new List<KeyValuePair<int, string[]>>();
        }

        private void DataLine(string line, int first, int n)
        {
            if (first < 0)
            {
                Note(Severity.Observation, "BLANK_LINE", n, n, "blank line in ~A skipped");
                return;
            }
            if (line[first] == '#')
            {
                Note(Severity.Observation, "COMMENT_LINE", n, n, "comment line in ~A skipped");
                return;
            }
            if (HasControlChar(line))
            {
                string cut = line.TrimEnd().TrimEnd('\x1A');
                if (HasControlChar(cut))
                {
                    Note(Severity.Error, "BINARY_LINE", n, n, "line with binary/control characters rejected");
                    return;
                }
                Note(Severity.Observation, "CTRL_Z", n, n, "DOS end-of-file marker (Ctrl-Z) ignored");
                if (FirstNonSpace(cut) < 0) return;
                line = cut;
            }

            string[] tokens = file.Delimiter == ','
                ? line.Split(',').Select(t => t.Trim()).ToArray()
                : line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            if (probe != null)
            {
                probe.Add(new KeyValuePair<int, string[]>(n, tokens));
                if (probe.Count == ProbeLines) EndProbe();
                return;
            }
            if (wrap) Wrapped(tokens, n);
            else Unwrapped(tokens, n);
        }

        private void EndProbe()
        {
            List<KeyValuePair<int, string[]>> lines = probe;
            probe = null;
            if (lines.Count(l => l.Value.Length == curveCount) * 2 < lines.Count)
                wrap = undeclaredWrap = true;
            foreach (var l in lines)
            {
                if (wrap) Wrapped(l.Value, l.Key);
                else Unwrapped(l.Value, l.Key);
            }
        }

        private void Unwrapped(string[] tokens, int n)
        {
            if (tokens.Length == curveCount) AddRecord(tokens, null, n);
            else Note(Severity.Error, "RECORD_REJECTED", n, n, "expected " + curveCount + " values, line " + n + " has " + tokens.Length);
        }

        // A wrapped record starts with the depth alone on its line (LAS 2.0) and continues until it holds one
        // value per curve. A record that overflows is rejected; reading resumes at the next record start.
        // ponytail: framing is by token count, so a record short by exactly one value swallows the next depth
        // line; the record after it is then rejected, so the spot still shows up as an Error.
        private void Wrapped(string[] tokens, int n)
        {
            bool canStart = tokens.Length == 1 || tokens.Length == curveCount;
            if (resync != null)
            {
                if (!canStart)
                {
                    resync.LastLine = n;
                    return;
                }
                resync = null;
            }
            if (pendingCount == 0 && !canStart)
            {
                resync = Note(Severity.Error, "RECORD_REJECTED", n, n, "line " + n + " cannot start a wrapped record (" + tokens.Length + " values; expected the depth alone)");
                return;
            }
            if (pendingCount + tokens.Length > curveCount)
            {
                resync = Note(Severity.Error, "RECORD_REJECTED", pendingLines[0], n,
                    "wrapped record from line " + pendingLines[0] + " has more than " + curveCount + " values by line " + n);
                pendingCount = 0;
                return;
            }
            foreach (string t in tokens)
            {
                pending[pendingCount] = t;
                pendingLines[pendingCount++] = n;
            }
            if (pendingCount == curveCount)
            {
                AddRecord(pending, pendingLines, pendingLines[0]);
                pendingCount = 0;
            }
        }

        private void AddRecord(string[] tokens, int[] tokenLines, int line)
        {
            for (int i = 0; i < curveCount; i++)
            {
                double v;
                if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v))
                {
                    // "1E400" fails to parse on .NET Framework; "NaN"/"Infinity" parse but are not data.
                    v = double.NaN;
                    int at = tokenLines == null ? line : tokenLines[i];
                    if (nonNumeric[i] == null)
                    {
                        string token = tokens[i].Length > 20 ? tokens[i].Substring(0, 20) + "..." : tokens[i];
                        nonNumeric[i] = Add(Severity.Warning, "NONNUMERIC_VALUE", at, "non-numeric token '" + token + "' in column " + (i + 1) + " read as missing", file.Curves[i].Mnemonic);
                    }
                    else
                    {
                        nonNumeric[i].Count++;
                        nonNumeric[i].LastLine = at;
                    }
                }
                columns[i].Add(v);
            }
            recordLines.Add(line);
        }

        private void EndData()
        {
            if (section != 'A') return;
            if (probe != null) EndProbe();
            if (pendingCount > 0)
            {
                Note(Severity.Error, "TRUNCATED_RECORD", pendingLines[0], pendingLines[pendingCount - 1],
                    "last wrapped record has " + pendingCount + " of " + curveCount + " values");
                pendingCount = 0;
            }
            resync = null;
            section = '-';
        }

        private void Finish()
        {
            if (undeclaredWrap)
            {
                string problem = WrapProblem();
                if (problem == null)
                {
                    file.Wrap = true;
                    Add(Severity.Warning, "UNDECLARED_WRAP", 0, "WRAP NO declared, but each record spans several lines; read as wrapped ("
                        + recordLines.Count + " records, constant depth step)");
                }
                else
                {
                    Add(Severity.Error, "UNDECLARED_WRAP", 0, "data lines do not hold " + curveCount + " values each, and reading them as wrapped failed: " + problem);
                    recordLines.Clear();
                    columns = null;
                }
            }

            for (int i = 0; i < file.Curves.Count && columns != null; i++)
                file.Curves[i].Values = columns[i].ToArray();
            file.RecordLines = recordLines.ToArray();

            bool hasError = file.Diagnostics.Any(d => d.Severity == Severity.Error);
            if (file.RecordCount == 0 && !hasError)
            {
                if (file.Curves.Count == 0) Add(Severity.Error, "NO_CURVES", 0, "no ~C curve definitions found");
                else if (!dataSeen) Add(Severity.Error, "NO_DATA_SECTION", 0, "no ~A data section found");
                else Add(Severity.Error, "NO_RECORDS", 0, "~A section has no data records");
            }
            file.UpdateStatus();
        }

        // Undeclared wrap is accepted only if the tokens divide evenly into records and the depth step is constant.
        private string WrapProblem()
        {
            if (file.Diagnostics.Any(d => d.Code == "RECORD_REJECTED" || d.Code == "TRUNCATED_RECORD"))
                return "the values do not divide evenly into records";
            if (recordLines.Count < 2) return "fewer than 2 records";
            List<double> depth = columns[0];
            double step = depth[1] - depth[0];
            for (int i = 1; i < depth.Count; i++)
                if (!(step != 0 && Math.Abs(depth[i] - depth[i - 1] - step) <= 0.01 * Math.Abs(step)))
                    return "depth step is not constant (line " + recordLines[i] + ")";
            return null;
        }

        // ---- helpers ----

        private Diagnostic Add(Severity severity, string code, int line, string message, string curve = null)
        {
            var d = new Diagnostic(severity, code, message, line, line, curve);
            file.Diagnostics.Add(d);
            return d;
        }

        /// <summary>Aggregates repeated file-level events into one diagnostic per code.</summary>
        private Diagnostic Note(Severity severity, string code, int firstLine, int lastLine, string message)
        {
            Diagnostic d = file.Diagnostics.FirstOrDefault(x => x.Code == code && x.Curve == null);
            if (d == null)
            {
                d = new Diagnostic(severity, code, message, firstLine, lastLine);
                file.Diagnostics.Add(d);
            }
            else
            {
                d.Count++;
                d.LastLine = lastLine;
            }
            return d;
        }

        private string Value(string mnemonic)
        {
            HeaderItem item = file.VersionItems.FirstOrDefault(i => Is(i, mnemonic));
            return item == null ? "" : item.Value;
        }

        private static bool Is(HeaderItem item, string mnemonic)
        {
            return string.Equals(item.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripBom(string line)
        {
            if (line.StartsWith("ï»¿", StringComparison.Ordinal)) return line.Substring(3);  // UTF-8 BOM read as Latin-1
            if (line.Length > 0 && line[0] == '﻿') return line.Substring(1);
            return line;
        }

        private static int FirstNonSpace(string line)
        {
            for (int i = 0; i < line.Length; i++)
                if (!char.IsWhiteSpace(line[i])) return i;
            return -1;
        }

        private static bool HasControlChar(string line)
        {
            foreach (char c in line)
                if (c < ' ' && c != '\t') return true;
            return false;
        }
    }
}
