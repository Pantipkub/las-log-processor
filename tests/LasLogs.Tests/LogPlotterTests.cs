using System;
using System.IO;
using System.Linq;
using LasLogs.App;
using LasLogs.Core;
using Xunit;

namespace LasLogs.Tests
{
    public class LogPlotterTests
    {
        private static readonly CurveCatalog Catalog = CurveCatalog.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CurveCatalog.FileName));

        [Fact]
        public void Plots_a_file_with_a_gap_a_missing_track_and_a_depth_reversal()
        {
            // No density curve; GR has a null and one value above 150; ILD has a value <= 0; depth turns back at 101.0.
            const string text =
                "~V\n VERS. 2.0 :\n WRAP. NO :\n~W\n STRT.F 100.0 :\n STOP.F 101.5 :\n STEP.F 0.5 :\n NULL. -999.25 :\n" +
                " WELL. TEST 1 : Well\n UWI. 42-003-40122 : Unique Well ID\n" +
                "~C\n DEPT.F : Depth\n GR.GAPI : Gamma ray\n ILD.OHMM : Deep induction\n NPHI.V/V : Neutron porosity\n" +
                "~A\n100.0 50 10 0.20\n100.5 -999.25 12 0.22\n101.0 200 -1 0.25\n100.5 60 11 0.21\n101.5 70 9 0.18\n";
            LasFile file = LasReader.Read(new StringReader(text), "synthetic.las");
            Conditioner.Run(file, Catalog);
            Assert.Equal(DepthOrder.NonMonotonic, file.DepthOrder);
            Assert.Equal("not in file", file.Concepts[(int)Concept.BulkDensity].Missing);

            string output = Path.Combine(Path.GetTempPath(), "laslogs_" + Guid.NewGuid().ToString("N"));
            try
            {
                string plot = LogPlotter.Plot(file, output);

                Assert.Equal("plots/4200340122/synthetic.png", plot);
                Assert.True(new FileInfo(Path.Combine(output, plot)).Length > 0);
                Diagnostic gr = Assert.Single(file.Diagnostics, d => d.Code == "OUT_OF_SCALE");
                Assert.Equal("GR", gr.Curve);
                Assert.Equal(1, gr.Count);
            }
            finally
            {
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }

        [Fact]
        public void Points_pins_out_of_scale_values_and_breaks_at_gaps_and_reversals()
        {
            double[] depth = { 1, 2, 3, 4, 3, 3.5, 10 };
            double[] res = { 0.1, -1, double.NaN, 30000, 5, 6, 7 };
            int below, above, nonPositive;

            var points = LogPlotter.Points(depth, res, 0.2, 20000, true, 1.5, out below, out above, out nonPositive);

            Assert.Equal(1, below);
            Assert.Equal(1, above);
            Assert.Equal(1, nonPositive);
            // 0.1 and 30000 pinned to the edges; -1 and NaN are gaps; a break where depth turns back (4 -> 3), none on the
            // next step (3 -> 3.5, same new direction), and a break at the depth gap (3.5 -> 10).
            Assert.Equal(new[] { 0.2, double.NaN, double.NaN, 20000, double.NaN, 5, 6, double.NaN, 7 }, points.Select(p => p.X));
        }
    }
}
