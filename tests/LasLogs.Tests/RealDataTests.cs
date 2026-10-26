using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LasLogs.Core;
using Xunit;

namespace LasLogs.Tests
{
    /// <summary>
    /// The Part 1 acceptance table (docs/PLAN.md) over the real dataset. Opt-in, because the dataset is not in the repo:
    /// set LAS_TESTDATA to the folder (e.g. ..\TestData\TestData). Without it the test returns immediately.
    /// </summary>
    public class RealDataTests
    {
        [Fact]
        [Trait("Category", "RealData")]
        public void Whole_folder_matches_the_investigation()
        {
            string dir = Environment.GetEnvironmentVariable("LAS_TESTDATA");
            if (string.IsNullOrEmpty(dir)) return;

            var files = new Dictionary<string, LasFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(dir))
            {
                LasFile f = LasReader.Read(path);   // must not throw
                f.Curves.ForEach(c => c.Values = null);   // keep memory flat; counts are enough here
                files[f.SourceName] = f;
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
        }
    }
}
