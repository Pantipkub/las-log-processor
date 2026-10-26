using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LasLogs.Core;
using Xunit;
using Xunit.Abstractions;

namespace LasLogs.Tests
{
    /// <summary>
    /// The Part 1 and Part 2 acceptance tables (docs/PLAN.md) over the real dataset. Opt-in, because the dataset is not
    /// in the repo: set LAS_TESTDATA to the folder (e.g. ..\TestData\TestData). Without it the test returns immediately.
    /// </summary>
    public class RealDataTests
    {
        private readonly ITestOutputHelper output;

        public RealDataTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        [Trait("Category", "RealData")]
        public void Whole_folder_matches_the_investigation()
        {
            string dir = Environment.GetEnvironmentVariable("LAS_TESTDATA");
            if (string.IsNullOrEmpty(dir)) return;

            CurveCatalog catalog = CurveCatalog.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CurveCatalog.FileName));
            var files = new Dictionary<string, LasFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(dir))
            {
                LasFile f = LasReader.Read(path);   // must not throw
                int records = f.RecordCount;
                Conditioner.Run(f, catalog);
                Assert.Equal(records, f.RecordCount);   // no depth row is rejected in this dataset
                // Keep memory flat: diagnostics, choices and medians are enough here.
                f.Curves.ForEach(c => c.Values = null);
                if (f.Concepts != null) foreach (ConceptCurve c in f.Concepts) c.Values = null;
                files[f.SourceName] = f;
            }
            foreach (string name in new[] { "4238337564_UNIV_10_13_5H_triplecmi_004_MD.las", "4246135292_MAIN.las", "4246135949_MAIN.las",
                "4200340122MAINCOR.las", "4200343170_STATE_AQ_8_RUN2.las", "4238336177MAIN.las", "4200340125MAIN.las" })
            {
                output.WriteLine("== " + name);
                foreach (Diagnostic d in files[name].Diagnostics.Where(d => d.Severity != Severity.Observation)) output.WriteLine("  " + d);
            }

            Assert.Equal(203, files.Count);
            Assert.Equal(140, files.Values.Count(f => f.Version == 2.0));
            Assert.Equal(52, files.Values.Count(f => f.Version == 1.2));
            Assert.Equal(11, files.Values.Count(f => f.Version == 3.0));

            Assert.Equal(23435, files["39399.LAS"].RecordCount);

            LasFile maincor = files["4200340122MAINCOR.las"];
            Assert.Equal(20645, maincor.RecordCount);
            Assert.Contains(maincor.Diagnostics, d => d.Code == "UNDECLARED_WRAP" && d.Severity == Severity.Warning);

            LasFile shortRows = files["4246135949_MAIN.las"];
            Assert.Equal(17307, shortRows.RecordCount);
            Diagnostic rejected = shortRows.Diagnostics.Single(d => d.Code == "RECORD_REJECTED");
            Assert.Equal(3054, rejected.Count);
            Assert.Equal(8247, rejected.FirstLine);
            Assert.Equal(11300, rejected.LastLine);

            LasFile truncated = files["4200340123MAIN.las"];
            Assert.Equal(13341, truncated.RecordCount);
            Assert.Equal(13447, truncated.Diagnostics.Single(d => d.Code == "RECORD_REJECTED").FirstLine);
            Assert.Equal(FileStatus.Partial, truncated.Status);

            LasFile junk = files["4200340740PnrUniv7-23_1.las"];
            Assert.Equal(20817, junk.RecordCount);
            Assert.Equal(10508, junk.Diagnostics.Single(d => d.Code == "BINARY_LINE").FirstLine);

            LasFile masmp = files["4200345990_20140426_LAS(masmp).LAS"];
            Assert.Equal(125, masmp.Curves.Count);
            Assert.Equal(79033, masmp.RecordCount);
            Assert.Equal(2, masmp.Diagnostics.Count(d => d.Code == "EMPTY_MNEMONIC"));

            LasFile comma = files["4200346370_20150224_LAS(UA_3001HL_lateral_hole_Mud_log).las"];
            Assert.Equal(40, comma.Curves.Count);
            Assert.Equal(8565, comma.RecordCount);

            Assert.Equal(8616, files["4231741110_20180523_LAS(UL COMANCHE A4144 UNIT 4146 9365ST2COMBINED_2_0).las"].RecordCount);

            // ---- Part 2 ----
            Func<Concept, int> coverage = c => files.Values.Count(f => f.Concepts != null && f.Concepts[(int)c].Source != null);
            output.WriteLine("coverage GR " + coverage(Concept.GammaRay) + " / RES " + coverage(Concept.DeepResistivity)
                + " / RHOB " + coverage(Concept.BulkDensity) + " / NPHI " + coverage(Concept.NeutronPorosity));
            Assert.Equal(182, coverage(Concept.GammaRay));          // 182 - 1 ('aapi' unit not mapped) + 1 (MAINCOR)
            Assert.Equal(110, coverage(Concept.DeepResistivity));   // 109 + MAINCOR
            Assert.Equal(101, coverage(Concept.BulkDensity));       // 100 + MAINCOR
            Assert.Equal(125, coverage(Concept.NeutronPorosity));   // 124 + MAINCOR

            Assert.Equal(27, files.Values.Count(f => f.DepthOrder == DepthOrder.Decreasing));   // 26 + MAINCOR
            Assert.Equal(new[] { "4200343170_STATE_AQ_8_RUN2.las" }, files.Values.Where(f => f.DepthOrder == DepthOrder.NonMonotonic).Select(f => f.SourceName));
            Diagnostic nonMonotonic = files["4200343170_STATE_AQ_8_RUN2.las"].Diagnostics.Single(d => d.Code == "DEPTH_NON_MONOTONIC");
            Assert.Equal(4, nonMonotonic.Count);
            Assert.Equal(4465, nonMonotonic.FirstLine);
            Assert.Contains("200 repeated depth values", nonMonotonic.Message);

            Assert.Equal(5, files.Values.Count(f => f.Diagnostics.Any(d => d.Code == "NULL_IS_ZERO")));
            Assert.Equal(11840, files["4238336177MAIN.las"].Diagnostics.Single(d => d.Code == "SENTINEL_MISMATCH").Count);

            Assert.Equal(new[] { "4238337564_UNIV_10_13_5H_triplecmi_004_MD.las" },
                files.Values.Where(f => f.Diagnostics.Any(d => d.Code == "UNIT_CONVERTED" && d.Message.StartsWith("BulkDensity"))).Select(f => f.SourceName));
            Assert.Equal(2.518, files["4238337564_UNIV_10_13_5H_triplecmi_004_MD.las"].Concepts[(int)Concept.BulkDensity].Median, 3);

            Assert.Contains(files["4246135949_MAIN.las"].Diagnostics, d => d.Code == "IMPLAUSIBLE_VALUES" && d.Curve == "DEN");
            Assert.Contains("Limestone", files["4246135292_MAIN.las"].Concepts[(int)Concept.NeutronPorosity].Source.Description);
            Assert.Contains(files["4200340125MAIN.las"].Diagnostics, d => d.Code == "STEP_MISMATCH");
        }
    }
}
