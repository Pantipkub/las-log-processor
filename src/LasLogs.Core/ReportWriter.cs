using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LasLogs.Core
{
    /// <summary>
    /// The run outputs (investigation report section J): conditioning_summary.csv (one row per file),
    /// conditioning_events.csv (one row per diagnostic) and conditioning_report.md. Rows are written as each file
    /// finishes; for the Markdown only counts and the warning/error rows are kept, so one LAS file is in memory at a time.
    /// The constructor opens all three files, so a locked output fails before the batch starts.
    /// </summary>
    public sealed class ReportWriter : IDisposable
    {
        public const string SummaryFile = "conditioning_summary.csv", EventsFile = "conditioning_events.csv", ReportFile = "conditioning_report.md";

        private static readonly string[] SummaryColumns =
        {
            "file", "status", "reason", "las_version", "wrap", "records_loaded", "curves_declared",
            "depth_unit", "depth_top", "depth_base", "original_depth_order", "modal_step", "header_step",
            "gamma_source", "resistivity_source", "density_source", "neutron_source",
            "transformations", "warnings", "errors", "codes", "plot_file", "elapsed_ms",
        };
        private static readonly string[] EventColumns =
            { "file", "well_key", "status", "severity", "code", "curve", "count", "first_line", "last_line", "message" };

        private readonly string input;
        private StreamWriter summary, events, report;

        // ---- run totals for the Markdown ----
        private int files, records, wrapped;
        private readonly SortedDictionary<string, int> statuses = new SortedDictionary<string, int>(), versions = new SortedDictionary<string, int>(),
            wells = new SortedDictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, CodeTotal> codes = new Dictionary<string, CodeTotal>();
        // Indexed by Concept.
        private readonly int[] found = new int[4], notInFile = new int[4], allNull = new int[4], rejected = new int[4],
            outFiles = new int[4], outPoints = new int[4], outMost = new int[4];
        private readonly string[] outMostFile = new string[4];
        private readonly StringBuilder lowConfidence = new StringBuilder(), duplicates = new StringBuilder(), problems = new StringBuilder();
        private int problemFiles;

        private sealed class CodeTotal
        {
            public Severity Severity;
            public string Code, Example;
            public int Files, Count;
        }

        public ReportWriter(string outputFolder, string inputFolder)
        {
            input = inputFolder;
            try
            {
                summary = Open(outputFolder, SummaryFile);
                events = Open(outputFolder, EventsFile);
                report = Open(outputFolder, ReportFile);
            }
            catch
            {
                Dispose();
                throw;
            }
            summary.WriteLine(string.Join(",", SummaryColumns));
            events.WriteLine(string.Join(",", EventColumns));
        }

        private static StreamWriter Open(string folder, string name)
        {
            return new StreamWriter(Path.Combine(folder, name), false, new UTF8Encoding(false));
        }

        /// <summary>Writes the file's summary row and events, and adds it to the totals.</summary>
        public void Add(LasFile file, string plotFile, long elapsedMs)
        {
            string wellKey = file.WellKey();
            summary.WriteLine(string.Join(",", SummaryRow(file, plotFile, elapsedMs).Select(Csv)));
            foreach (Diagnostic d in file.Diagnostics)
                events.WriteLine(string.Join(",", new[]
                {
                    file.SourceName, wellKey, file.Status.ToString(), d.Severity.ToString(), d.Code, d.Curve, Int(d.Count),
                    Int(d.FirstLine), Int(d.LastLine), d.Message,
                }.Select(Csv)));

            files++;
            records += file.RecordCount;
            if (file.Wrap) wrapped++;
            Increment(statuses, file.Status.ToString());
            Increment(versions, double.IsNaN(file.Version) ? "unknown" : file.Version.ToString("0.0", CultureInfo.InvariantCulture));
            Increment(wells, wellKey);

            foreach (Diagnostic d in file.Diagnostics)
            {
                CodeTotal total;
                if (!codes.TryGetValue(d.Severity + " " + d.Code, out total))
                    codes[d.Severity + " " + d.Code] = total = new CodeTotal { Severity = d.Severity, Code = d.Code, Example = "`" + file.SourceName + "`: " + d.Message };
                total.Count += d.Count;
            }
            foreach (string key in file.Diagnostics.Select(d => d.Severity + " " + d.Code).Distinct()) codes[key].Files++;

            foreach (Diagnostic d in file.Diagnostics.Where(d => d.Code == "DUPLICATE_FILE"))
                duplicates.AppendLine("- `" + file.SourceName + "`: " + Cell(d.Message));

            if (file.Concepts != null)
                for (int i = 0; i < file.Concepts.Length; i++)
                    AddConcept(file, file.Concepts[i], i);

            List<Diagnostic> serious = file.Diagnostics.Where(d => d.Severity >= Severity.Warning).ToList();
            if (serious.Count == 0) return;
            problemFiles++;
            problems.AppendLine().AppendLine("### `" + file.SourceName + "`: " + file.Status
                + (plotFile.Length == 0 ? "" : " ([plot](<" + plotFile + ">))")).AppendLine()
                .AppendLine("| Severity | Code | Curve | Count | Lines | Message |").AppendLine("|---|---|---|---|---|---|");
            foreach (Diagnostic d in serious)
                problems.AppendLine("| " + d.Severity + " | `" + d.Code + "` | " + Cell(d.Curve) + " | " + Int(d.Count) + " | "
                    + (d.FirstLine == 0 ? "" : d.FirstLine == d.LastLine ? Int(d.FirstLine) : d.FirstLine + "-" + d.LastLine) + " | " + Cell(d.Message) + " |");
        }

        private void AddConcept(LasFile file, ConceptCurve curve, int i)
        {
            if (curve.Source == null)
            {
                if (curve.Missing.StartsWith("rejected")) rejected[i]++;
                else if (curve.Missing == "not in file") notInFile[i]++;
                else allNull[i]++;
                return;
            }
            found[i]++;
            if (curve.Entry.Confidence != "H")
                lowConfidence.AppendLine("| `" + file.SourceName + "` | " + curve.Concept + " | `" + curve.Source.Mnemonic + "` | " + curve.Entry.Confidence
                    + " | " + Cell(curve.Source.Description) + " |");
            // The plotter's OUT_OF_SCALE event names the curve definition line of the source curve.
            Diagnostic outOfScale = file.Diagnostics.FirstOrDefault(d => d.Code == "OUT_OF_SCALE" && d.FirstLine == curve.Source.Line);
            if (outOfScale == null) return;
            outFiles[i]++;
            outPoints[i] += outOfScale.Count;
            if (outOfScale.Count > outMost[i])
            {
                outMost[i] = outOfScale.Count;
                outMostFile[i] = file.SourceName;
            }
        }

        /// <summary>Writes the Markdown report. Call once, after the last <see cref="Add"/>.</summary>
        public void Finish(TimeSpan elapsed, long peakWorkingSetMb)
        {
            var md = new StringBuilder();
            md.AppendLine("# Conditioning report").AppendLine()
              .AppendLine("Run " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (local time), input `" + input + "`: "
                  + files + " files in " + elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s, peak working set " + peakWorkingSetMb + " MB.").AppendLine()
              .AppendLine("- `" + SummaryFile + "`: one row per file (status, curves used, counts, plot path).")
              .AppendLine("- `" + EventsFile + "`: one row per event (file, code, curve, count, first/last source line, message).")
              .AppendLine("- The source LAS files are the audit copy: events cite their line numbers. The rules are in `docs/DECISIONS.md`.")
              .AppendLine()
              .AppendLine("## Dataset overview").AppendLine()
              .AppendLine("| | |").AppendLine("|---|---|")
              .AppendLine("| Files | " + files + " (" + Join(statuses) + ") |")
              .AppendLine("| LAS versions | " + Join(versions) + " |")
              .AppendLine("| Wrapped files | " + wrapped + " |")
              .AppendLine("| Records loaded | " + records.ToString("N0", CultureInfo.InvariantCulture) + " |")
              .AppendLine("| Wells | " + wells.Count + " well keys (first 10 digits of UWI/API, else the file name); the plots are grouped by them |")
              .AppendLine("| Byte-identical copies | " + Files("DUPLICATE_FILE") + " files repeat an earlier file (processed and plotted anyway) |")
              .AppendLine();

            md.AppendLine("## Curves used for the plots").AppendLine()
              .AppendLine("Out-of-scale points are drawn at the track edge (plot only; stored values unchanged) and counted in the plot footer.").AppendLine()
              .AppendLine("| Concept | Files with a curve | Not in file | All null | Rejected | Files with out-of-scale points | Out-of-scale points | Most in one file |")
              .AppendLine("|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < found.Length; i++)
                md.AppendLine("| " + (Concept)i + " | " + found[i] + " | " + notInFile[i] + " | " + allNull[i] + " | " + rejected[i] + " | " + outFiles[i]
                    + " | " + outPoints[i].ToString("N0", CultureInfo.InvariantCulture) + " | "
                    + (outMostFile[i] == null ? "" : "`" + outMostFile[i] + "` (" + outMost[i].ToString("N0", CultureInfo.InvariantCulture) + ")") + " |");
            if (lowConfidence.Length > 0)
                md.AppendLine().AppendLine("Curves mapped with medium (M) or low (L) catalog confidence; worth a check:").AppendLine()
                  .AppendLine("| File | Concept | Curve | Confidence | Description |").AppendLine("|---|---|---|---|---|")
                  .Append(lowConfidence);
            md.AppendLine();

            md.AppendLine("## What was changed and what was only reported").AppendLine()
              .AppendLine("- **Transformation**: values, their order, their units or the curve naming were changed.")
              .AppendLine("- **Error**: rows or files that could not be loaded (they are left out, never guessed).")
              .AppendLine("- **Warning** / **Observation**: reported only; the data was left as it is.")
              .AppendLine("- Never done: resampling, interpolation, sorting or de-duplicating depths, clamping stored values, skipping or deleting files.")
              .AppendLine("- Plot only: out-of-scale values are drawn at the track edge (`OUT_OF_SCALE`); resistivity <= 0 is not drawn on the log axis (`RES_NONPOSITIVE`).")
              .AppendLine()
              .AppendLine("Count = the sum of the event counts (values, rows or curves, depending on the code).").AppendLine()
              .AppendLine("| Category | Code | Files | Count | First example |").AppendLine("|---|---|---|---|---|");
            foreach (CodeTotal t in codes.Values.OrderBy(t => t.Severity == Severity.Transformation ? 0 : t.Severity == Severity.Error ? 1 : 2)
                .ThenByDescending(t => t.Severity).ThenBy(t => t.Code, StringComparer.Ordinal))
                md.AppendLine("| " + t.Severity + " | `" + t.Code + "` | " + t.Files + " | " + t.Count.ToString("N0", CultureInfo.InvariantCulture) + " | " + Cell(t.Example) + " |");
            md.AppendLine();

            if (duplicates.Length > 0)
                md.AppendLine("## Byte-identical files (SHA-256)").AppendLine().Append(duplicates).AppendLine();

            md.AppendLine("## Wells (informational)").AppendLine()
              .AppendLine("| Well key | Files |").AppendLine("|---|---|");
            foreach (KeyValuePair<string, int> w in wells) md.AppendLine("| `" + w.Key + "` | " + w.Value + " |");
            md.AppendLine();

            md.AppendLine("## Files with warnings or errors (" + problemFiles + ")").AppendLine()
              .AppendLine("Warnings and errors only; transformations and observations are in `" + EventsFile + "`.")
              .Append(problems);

            report.Write(md.ToString());
        }

        public void Dispose()
        {
            foreach (StreamWriter w in new[] { summary, events, report })
                if (w != null) w.Dispose();
            summary = events = report = null;
        }

        private static string[] SummaryRow(LasFile file, string plotFile, long elapsedMs)
        {
            Diagnostic error = file.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error);
            bool loaded = file.RecordCount > 0;
            double[] depth = loaded ? file.Curves[0].Values : null;
            HeaderItem step = file.WellItems.FirstOrDefault(i => string.Equals(i.Mnemonic, "STEP", StringComparison.OrdinalIgnoreCase));
            return new[]
            {
                file.SourceName,
                file.Status.ToString(),
                error == null ? "" : error.Message,
                Num(file.Version),
                file.Wrap ? "YES" : "NO",
                Int(file.RecordCount),
                Int(file.Curves.Count),
                file.Curves.Count > 0 ? file.Curves[0].Unit : "",
                loaded ? Num(depth.Min()) : "",
                loaded ? Num(depth.Max()) : "",
                loaded ? file.DepthOrder.ToString() : "",
                Num(file.ModalStep),
                step == null ? "" : step.Value,
                Source(file, Concept.GammaRay),
                Source(file, Concept.DeepResistivity),
                Source(file, Concept.BulkDensity),
                Source(file, Concept.NeutronPorosity),
                Int(file.Diagnostics.Count(d => d.Severity == Severity.Transformation)),
                Int(file.Diagnostics.Count(d => d.Severity == Severity.Warning)),
                Int(file.Diagnostics.Count(d => d.Severity == Severity.Error)),
                string.Join(" ", file.Diagnostics.Select(d => d.Code).Distinct()),
                plotFile,
                elapsedMs.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string Source(LasFile file, Concept concept)
        {
            return file.Concepts == null || file.Concepts[(int)concept].Source == null ? "" : file.Concepts[(int)concept].Source.Mnemonic;
        }

        private int Files(string code)
        {
            return codes.Values.Where(t => t.Code == code).Sum(t => t.Files);
        }

        private static void Increment(IDictionary<string, int> counts, string key)
        {
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        private static string Join(IDictionary<string, int> counts)
        {
            return string.Join(", ", counts.Select(c => c.Key + ": " + c.Value));
        }

        private static string Int(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static string Num(double v)
        {
            return double.IsNaN(v) ? "" : v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Csv(string field)
        {
            if (field == null) return "";
            return field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        // A Markdown table cell: '|' would end the cell.
        private static string Cell(string text)
        {
            return text == null ? "" : text.Replace("|", "\\|");
        }
    }
}
