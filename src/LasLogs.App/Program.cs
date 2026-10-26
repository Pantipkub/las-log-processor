using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LasLogs.Core;

namespace LasLogs.App
{
    public static class Program
    {
        public const string SummaryFile = "conditioning_summary.csv";

        private static readonly string[] Columns =
        {
            "file", "status", "reason", "las_version", "wrap", "records_loaded", "curves_declared",
            "depth_unit", "depth_top", "depth_base", "original_depth_order", "modal_step", "header_step",
            "gamma_source", "resistivity_source", "density_source", "neutron_source",
            "transformations", "warnings", "errors", "codes", "plot_file", "elapsed_ms",
        };

        public static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: LasLogs.App <input-folder> <output-folder>");
                return 2;
            }
            string input = args[0], output = args[1];
            if (!Directory.Exists(input))
            {
                Console.Error.WriteLine("Input folder not found: " + input);
                return 2;
            }

            CurveCatalog catalog;
            try
            {
                catalog = CurveCatalog.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CurveCatalog.FileName));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Cannot load the curve catalog: " + ex.Message);
                return 2;
            }

            // Not Directory.GetFiles(input, "*.las"): on Windows that pattern also matches ".lasx".
            string[] paths = Directory.GetFiles(input)
                .Where(p => string.Equals(Path.GetExtension(p), ".las", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Directory.CreateDirectory(output);
            var total = Stopwatch.StartNew();
            var statuses = new SortedDictionary<string, int>();

            using (var csv = new StreamWriter(Path.Combine(output, SummaryFile), false, new UTF8Encoding(false)))
            {
                csv.WriteLine(string.Join(",", Columns));
                for (int i = 0; i < paths.Length; i++)
                {
                    string[] row = ProcessFile(paths[i], catalog, output);
                    csv.WriteLine(string.Join(",", row.Select(Csv)));
                    int count;
                    statuses.TryGetValue(row[1], out count);
                    statuses[row[1]] = count + 1;
                    Console.WriteLine("[" + (i + 1) + "/" + paths.Length + "] " + row[1].PadRight(7) + " " + row[0] + " (" + row[row.Length - 1] + " ms)");
                }
            }

            Console.WriteLine(paths.Length + " files in " + total.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s: "
                + string.Join(", ", statuses.Select(s => s.Value + " " + s.Key)) + "; peak working set "
                + (Process.GetCurrentProcess().PeakWorkingSet64 >> 20) + " MB");
            Console.WriteLine("Summary: " + Path.Combine(output, SummaryFile));
            return 0;
        }

        // One file, isolated: any exception (reading, conditioning or plotting) becomes a Failed row and the batch moves on.
        private static string[] ProcessFile(string path, CurveCatalog catalog, string output)
        {
            var watch = Stopwatch.StartNew();
            string[] row;
            try
            {
                LasFile file = LasReader.Read(path);
                Conditioner.Run(file, catalog);
                string plot = LogPlotter.Plot(file, output);
                row = Row(file, plot);
            }
            catch (Exception ex)
            {
                row = new string[Columns.Length];
                row[0] = Path.GetFileName(path);
                row[1] = FileStatus.Failed.ToString();
                row[2] = ex.GetType().Name + ": " + ex.Message;
            }
            row[row.Length - 1] = watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
            return row;
        }

        private static string[] Row(LasFile file, string plot)
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
                file.RecordCount.ToString(CultureInfo.InvariantCulture),
                file.Curves.Count.ToString(CultureInfo.InvariantCulture),
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
                Count(file, Severity.Transformation),
                Count(file, Severity.Warning),
                Count(file, Severity.Error),
                string.Join(" ", file.Diagnostics.Select(d => d.Code).Distinct()),
                plot,
                "",   // elapsed_ms, filled by ProcessFile
            };
        }

        private static string Source(LasFile file, Concept concept)
        {
            return file.Concepts == null || file.Concepts[(int)concept].Source == null ? "" : file.Concepts[(int)concept].Source.Mnemonic;
        }

        private static string Count(LasFile file, Severity severity)
        {
            return file.Diagnostics.Count(d => d.Severity == severity).ToString(CultureInfo.InvariantCulture);
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
    }
}
