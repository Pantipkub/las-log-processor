using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LasLogs.Core;

namespace LasLogs.App
{
    public static class Program
    {
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

            ReportWriter report;
            try
            {
                Directory.CreateDirectory(output);
                report = new ReportWriter(output, input);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Typically conditioning_summary.csv still open in Excel.
                Console.Error.WriteLine("Cannot write the reports in " + output + ": " + ex.Message);
                Console.Error.WriteLine("Close the file if another program (e.g. Excel) has it open, then run again.");
                return 3;
            }

            var total = Stopwatch.StartNew();
            var statuses = new SortedDictionary<string, int>();
            var firstByHash = new Dictionary<string, string>();
            using (report)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    var watch = Stopwatch.StartNew();
                    string plot;
                    LasFile file = ProcessFile(paths[i], catalog, output, firstByHash, out plot);
                    report.Add(file, plot, watch.ElapsedMilliseconds);
                    int count;
                    statuses.TryGetValue(file.Status.ToString(), out count);
                    statuses[file.Status.ToString()] = count + 1;
                    Console.WriteLine("[" + (i + 1) + "/" + paths.Length + "] " + file.Status.ToString().PadRight(7) + " " + file.SourceName + " (" + watch.ElapsedMilliseconds + " ms)");
                }
                long peakMb = Process.GetCurrentProcess().PeakWorkingSet64 >> 20;
                report.Finish(total.Elapsed, peakMb);
                Console.WriteLine(paths.Length + " files in " + total.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s: "
                    + string.Join(", ", statuses.Select(s => s.Value + " " + s.Key)) + "; peak working set " + peakMb + " MB");
            }
            Console.WriteLine("Reports: " + string.Join(", ", new[] { ReportWriter.SummaryFile, ReportWriter.EventsFile, ReportWriter.ReportFile }
                .Select(f => Path.Combine(output, f))));
            return 0;
        }

        // One file, isolated: any exception (reading, hashing, conditioning or plotting) becomes a Failed file with a
        // FILE_FAILED error, and the batch moves on.
        private static LasFile ProcessFile(string path, CurveCatalog catalog, string output, Dictionary<string, string> firstByHash, out string plot)
        {
            plot = "";
            try
            {
                LasFile file = LasReader.Read(path);
                string hash = Sha256(path), first;
                if (firstByHash.TryGetValue(hash, out first))
                    file.Add(Severity.Observation, "DUPLICATE_FILE", "byte-identical to " + first + " (SHA-256 " + hash.Substring(0, 8) + "); processed anyway");
                else firstByHash[hash] = file.SourceName;
                Conditioner.Run(file, catalog);
                plot = LogPlotter.Plot(file, output);
                return file;
            }
            catch (Exception ex)
            {
                var failed = new LasFile(Path.GetFileName(path));
                failed.Add(Severity.Error, "FILE_FAILED", ex.GetType().Name + ": " + ex.Message);
                failed.UpdateStatus();
                return failed;
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
