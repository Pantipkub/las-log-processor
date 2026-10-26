using System;
using System.IO;
using System.Linq;
using LasLogs.App;
using LasLogs.Core;
using Xunit;

namespace LasLogs.Tests
{
    public class ProgramTests
    {
        private const string GoodLas =
            "~V\n VERS. 2.0 :\n WRAP. NO :\n~W\n STRT.F 100.0 :\n STOP.F 100.5 :\n STEP.F 0.5 :\n NULL. -999.25 :\n" +
            "~C\n DEPT.F : Depth\n GR.GAPI : Gamma ray\n~A\n100.0 50\n100.5 51\n";

        [Fact] // S2, S3, S23
        public void Broken_files_become_failed_rows_and_the_batch_continues()
        {
            string root = Path.Combine(Path.GetTempPath(), "laslogs_" + Guid.NewGuid().ToString("N"));
            string input = Path.Combine(root, "in"), output = Path.Combine(root, "out");
            Directory.CreateDirectory(input);
            try
            {
                File.WriteAllBytes(Path.Combine(input, "empty.las"), new byte[0]);
                File.WriteAllBytes(Path.Combine(input, "binary.LAS"), Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray());
                File.WriteAllText(Path.Combine(input, "good.las"), GoodLas);
                File.WriteAllText(Path.Combine(input, "good_copy.las"), GoodLas);
                File.WriteAllText(Path.Combine(input, "locked.las"), GoodLas);
                File.WriteAllText(Path.Combine(input, "notes.txt"), GoodLas);   // not a LAS file: skipped

                int exit;
                using (new FileStream(Path.Combine(input, "locked.las"), FileMode.Open, FileAccess.Read, FileShare.None))
                    exit = Program.Main(new[] { input, output });

                Assert.Equal(0, exit);
                string[][] rows = File.ReadAllLines(Path.Combine(output, ReportWriter.SummaryFile)).Skip(1).Select(l => l.Split(',')).ToArray();
                Assert.Equal(new[] { "binary.LAS", "empty.las", "good.las", "good_copy.las", "locked.las" }, rows.Select(r => r[0]));
                Assert.Equal(new[] { "Failed", "Failed", "Loaded", "Loaded", "Failed" }, rows.Select(r => r[1]));
                Assert.StartsWith("IOException", rows[4][2]);
                Assert.Equal("GR", rows[2][13]);   // gamma_source
                Assert.DoesNotContain("DUPLICATE_FILE", rows[2][20]);
                Assert.Contains("DUPLICATE_FILE", rows[3][20]);   // codes: still loaded and plotted
                Assert.NotEqual("", rows[3][21]);

                // One event row per diagnostic: file, well_key, status, severity, code, ...
                string[][] events = File.ReadAllLines(Path.Combine(output, ReportWriter.EventsFile)).Skip(1).Select(l => l.Split(',')).ToArray();
                Assert.Contains(events, e => e[0] == "good_copy.las" && e[3] == "Observation" && e[4] == "DUPLICATE_FILE");
                Assert.Contains(events, e => e[0] == "locked.las" && e[2] == "Failed" && e[3] == "Error" && e[4] == "FILE_FAILED");
                Assert.Contains(events, e => e[0] == "good.las" && e[4] == "CURVE_MAPPED" && e[5] == "GR");
                Assert.Contains("`DUPLICATE_FILE`", File.ReadAllText(Path.Combine(output, ReportWriter.ReportFile)));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Locked_output_file_stops_with_a_message_instead_of_crashing()
        {
            string root = Path.Combine(Path.GetTempPath(), "laslogs_" + Guid.NewGuid().ToString("N"));
            string input = Path.Combine(root, "in"), output = Path.Combine(root, "out");
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);
            try
            {
                File.WriteAllText(Path.Combine(input, "good.las"), GoodLas);
                int exit;
                using (new FileStream(Path.Combine(output, ReportWriter.SummaryFile), FileMode.Create, FileAccess.Write, FileShare.None))   // as Excel does
                    exit = Program.Main(new[] { input, output });

                Assert.Equal(3, exit);
                Assert.False(Directory.Exists(Path.Combine(output, "plots")));   // stopped before the batch
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
