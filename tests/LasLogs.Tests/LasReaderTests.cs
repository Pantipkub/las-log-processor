using System;
using System.IO;
using System.Linq;
using System.Text;
using LasLogs.Core;
using Xunit;

namespace LasLogs.Tests
{
    public class LasReaderTests
    {
        // Lines 1-13; data starts at line 14.
        private const string Header =
            "~VERSION INFORMATION\n" +
            " VERS.   2.0 : CWLS LAS 2.0\n" +
            " WRAP.   NO  : One line per depth step\n" +
            "~WELL INFORMATION\n" +
            " STRT.F  100.0 :\n" +
            " STOP.F  101.0 :\n" +
            " STEP.F  0.5   :\n" +
            " NULL.   -999.25 :\n" +
            "~CURVE INFORMATION\n" +
            " DEPT.F    : Depth\n" +
            " GR.GAPI   : Gamma ray\n" +
            " RHOB.G/C3 : Bulk density\n" +
            "~A\n";

        private static LasFile Parse(string text)
        {
            return LasReader.Read(new StringReader(text), "test.las");
        }

        private static LasFile ReadBytes(byte[] bytes)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, bytes);
                return LasReader.Read(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static LasFile Fixture(string name)
        {
            return LasReader.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name));
        }

        private static Diagnostic Single(LasFile file, string code)
        {
            return Assert.Single(file.Diagnostics, d => d.Code == code);
        }

        [Fact]
        public void Reads_a_plain_unwrapped_file()
        {
            LasFile f = Parse(Header + "100.0 50.1 2.45\n100.5 51.2 2.46\n101.0 -999.25 2.47\n");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(2.0, f.Version);
            Assert.False(f.Wrap);
            Assert.Equal(new[] { "DEPT", "GR", "RHOB" }, f.Curves.Select(c => c.Mnemonic));
            Assert.Equal(new[] { 100.0, 100.5, 101.0 }, f.Curves[0].Values);
            Assert.Equal(new[] { 50.1, 51.2, -999.25 }, f.Curves[1].Values);   // nulls are the conditioner's job
            Assert.Equal(new[] { 14, 15, 16 }, f.RecordLines);
            Assert.Empty(f.Diagnostics);
        }

        [Fact] // S2
        public void Empty_file_fails_without_throwing()
        {
            LasFile f = ReadBytes(new byte[0]);

            Assert.Equal(FileStatus.Failed, f.Status);
            Single(f, "NO_CURVES");
        }

        [Fact] // S3 (starts with FF FE, which must not switch the decoding to UTF-16)
        public void Binary_file_fails_without_throwing()
        {
            byte[] bytes = Enumerable.Range(0, 5000).Select(i => (byte)(255 - i % 256)).ToArray();
            LasFile f = ReadBytes(bytes);

            Assert.Equal(FileStatus.Failed, f.Status);
            Assert.Equal(0, f.RecordCount);
        }

        [Fact] // S4
        public void Missing_curve_section_fails()
        {
            LasFile f = Parse("~V\nVERS. 2.0 :\nWRAP. NO :\n~A\n100 1 2\n");

            Assert.Equal(FileStatus.Failed, f.Status);
            Single(f, "DATA_BEFORE_CURVES");
        }

        [Fact] // S4
        public void Missing_data_section_fails()
        {
            LasFile f = Parse(Header.Replace("~A\n", ""));

            Assert.Equal(FileStatus.Failed, f.Status);
            Single(f, "NO_DATA_SECTION");
        }

        [Fact] // S4
        public void Data_section_without_rows_fails()
        {
            LasFile f = Parse(Header + "\n");

            Assert.Equal(FileStatus.Failed, f.Status);
            Single(f, "NO_RECORDS");
        }

        [Fact] // S5: the columns are unknown when ~A comes first
        public void Data_before_curves_fails()
        {
            LasFile f = Parse("~V\nVERS. 2.0 :\nWRAP. NO :\n~A\n100 1 2\n~C\nDEPT.F :\nGR.GAPI :\nRHOB.G/C3 :\n");

            Assert.Equal(FileStatus.Failed, f.Status);
            Assert.Equal(4, Single(f, "DATA_BEFORE_CURVES").FirstLine);
            Single(f, "SECTION_SKIPPED");
        }

        [Fact] // S6
        public void Missing_wrap_defaults_to_no()
        {
            LasFile f = Parse(Header.Replace(" WRAP.   NO  : One line per depth step\n", "") + "100 1 2\n100.5 1 2\n");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.False(f.Wrap);
            Assert.Equal(2, f.RecordCount);
            Assert.Equal(Severity.Observation, Single(f, "WRAP_MISSING").Severity);
        }

        [Fact] // S7
        public void Wrapped_record_that_overflows_is_rejected_and_reading_resyncs()
        {
            string text =
                "~V\nVERS. 2.0 :\nWRAP. YES :\n~C\nDEPT.F :\nA. :\nB. :\nC. :\n~A\n" +  // lines 1-9
                "100\n1 2 3\n" +                                                     // 10-11: good
                "101\n4\n102\n7 8 9\n" +                                             // 12-15: 101 is short and swallows 102
                "103\n1 2 3\n";                                                      // 16-17: good again
            LasFile f = Parse(text);

            Assert.True(f.Wrap);
            Assert.Equal(new[] { 100.0, 103.0 }, f.Curves[0].Values);
            Assert.Equal(new[] { 10, 16 }, f.RecordLines);
            Diagnostic d = Single(f, "RECORD_REJECTED");
            Assert.Equal(12, d.FirstLine);
            Assert.Equal(15, d.LastLine);
            Assert.Equal(FileStatus.Partial, f.Status);
        }

        [Fact]
        public void Truncated_wrapped_record_is_rejected()
        {
            LasFile f = Parse("~V\nVERS. 2.0 :\nWRAP. YES :\n~C\nDEPT.F :\nA. :\nB. :\n~A\n100\n1 2\n101\n3\n");

            Assert.Equal(1, f.RecordCount);
            Diagnostic d = Single(f, "TRUNCATED_RECORD");
            Assert.Equal(11, d.FirstLine);
            Assert.Equal(12, d.LastLine);
            Assert.Equal(FileStatus.Partial, f.Status);
        }

        [Fact] // S8
        public void Row_with_an_extra_value_is_rejected()
        {
            LasFile f = Parse(Header + "100.0 1 2\n100.5 1 2 3\n101.0 1 2\n");

            Assert.Equal(new[] { 100.0, 101.0 }, f.Curves[0].Values);
            Diagnostic d = Single(f, "RECORD_REJECTED");
            Assert.Equal(Severity.Error, d.Severity);
            Assert.Equal(15, d.FirstLine);
            Assert.Equal(FileStatus.Partial, f.Status);
        }

        [Fact] // S9
        public void Non_numeric_tokens_become_NaN_with_a_warning_per_curve()
        {
            LasFile f = Parse(Header + "100.0 1E400 -Infinity\n100.5 NaN 1,5\n101.0 1.0D+02 --\n");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(3, f.RecordCount);
            Assert.All(f.Curves[1].Values.Concat(f.Curves[2].Values), v => Assert.True(double.IsNaN(v)));
            Diagnostic gr = Assert.Single(f.Diagnostics, d => d.Code == "NONNUMERIC_VALUE" && d.Curve == "GR");
            Assert.Equal(3, gr.Count);
            Assert.Equal(14, gr.FirstLine);
            Assert.Equal(16, gr.LastLine);
            Assert.Equal(3, Assert.Single(f.Diagnostics, d => d.Code == "NONNUMERIC_VALUE" && d.Curve == "RHOB").Count);
        }

        [Fact] // S19
        public void Utf8_bom_is_ignored()
        {
            byte[] bom = { 0xEF, 0xBB, 0xBF };
            LasFile f = ReadBytes(bom.Concat(Encoding.ASCII.GetBytes(Header + "100 1 2\n")).ToArray());

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(2.0, f.Version);
            Assert.Equal(1, f.RecordCount);
        }

        [Fact] // S22
        public void Very_long_line_is_read()
        {
            LasFile f = Parse(Header + "100 1" + new string(' ', 200000) + "2\n");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(2.0, f.Curves[2].Values[0]);
        }

        [Fact] // S24
        public void Las12_well_values_come_from_the_description_field()
        {
            LasFile f = Parse(
                "~VERSION INFORMATION\nVERS.   1.2 :CWLS Log ASCII Standard - VERSION 1.2\nWRAP.   NO  :One line per depth step\n" +
                "~WELL INFORMATION\nSTRT .F      189.0000        :START DEPTH\nWELL .                    WELL:  UNIVERSITY LANDS 3-11#1\n" +
                "~CURVE INFORMATION\nDEPT .F   :\n~A\n189\n");

            Assert.Equal(1.2, f.Version);
            HeaderItem well = f.WellItems.Single(i => i.Mnemonic == "WELL");
            Assert.Equal("UNIVERSITY LANDS 3-11#1", well.Value);
            Assert.Equal("WELL", well.Description);
            Assert.Equal("189.0000", f.WellItems.Single(i => i.Mnemonic == "STRT").Value);
        }

        [Fact] // S25 + trailing Ctrl-Z
        public void Comment_blank_and_ctrl_z_lines_in_data_are_skipped()
        {
            LasFile f = Parse(Header + "# note\n100 1 2\n\n   \n# more\n100.5 1 2\n\x1A\n");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(2, f.RecordCount);
            Assert.Equal(2, Single(f, "COMMENT_LINE").Count);
            Assert.Equal(2, Single(f, "BLANK_LINE").Count);
            Assert.Equal(20, Single(f, "CTRL_Z").FirstLine);
        }

        [Fact]
        public void Undeclared_wrap_that_does_not_divide_evenly_fails()
        {
            LasFile f = Parse(Header + "100\n1\n2 3 4\n100.5\n1 2\n");

            Assert.Equal(FileStatus.Failed, f.Status);
            Assert.Equal(Severity.Error, Single(f, "UNDECLARED_WRAP").Severity);
        }

        [Fact] // DECISIONS §9: in LAS 3.0, ~Comment_Definition is not a ~Curve section
        public void Las3_sections_are_matched_by_name()
        {
            LasFile f = Parse(
                "~Version\nVERS. 3.0 :\nWRAP. NO :\n~Curve\nDEPT.F :\nGR.GAPI :\n" +
                "~Comment_Definition\nCMNT. : Comment text\n~Ascii | Curve\n100 1\n");

            Assert.Equal(2, f.Curves.Count);
            Assert.Equal(1, f.RecordCount);
            Single(f, "SECTION_SKIPPED");
        }

        // ---- excerpts of real files (see Fixtures/README.md) ----

        [Fact]
        public void Fixture_39399_wrapped()
        {
            LasFile f = Fixture("39399_excerpt.las");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.True(f.Wrap);
            Assert.Equal(44, f.Curves.Count);
            Assert.Equal(new[] { 164.0, 164.5, 165.0 }, f.Curves[0].Values);
            Assert.Equal(new[] { 75, 85, 95 }, f.RecordLines);
            Assert.Empty(f.Diagnostics);
        }

        [Fact]
        public void Fixture_MAINCOR_undeclared_wrap_is_recovered()
        {
            LasFile f = Fixture("4200340122MAINCOR_excerpt.las");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.True(f.Wrap);
            Assert.Equal(26, f.Curves.Count);
            Assert.Equal(new[] { 10382.0, 10381.5, 10381.0, 10380.5, 10380.0 }, f.Curves[0].Values);
            Assert.Equal(Severity.Warning, Single(f, "UNDECLARED_WRAP").Severity);
        }

        [Fact]
        public void Fixture_PnrUniv_junk_line_and_overflow_tokens()
        {
            LasFile f = Fixture("4200340740PnrUniv7-23_1_excerpt.las");

            Assert.Equal(12, f.RecordCount);
            Assert.Equal(107, Single(f, "BINARY_LINE").FirstLine);
            Assert.Equal(112, Single(f, "BLANK_LINE").FirstLine);
            Diagnostic den = Single(f, "NONNUMERIC_VALUE");
            Assert.Equal("DEN", den.Curve);
            Assert.Equal(103, den.FirstLine);
            Assert.True(double.IsNaN(f.Curves[13].Values[4]));
            // The records on both sides of the junk line stay aligned.
            Assert.Equal(new[] { 5304.5, 5304.0, 5303.5, 5303.0 }, f.Curves[0].Values.Skip(6).Take(4));
            Assert.Equal(FileStatus.Partial, f.Status);
        }

        [Fact]
        public void Fixture_masmp_empty_mnemonics_keep_their_columns()
        {
            LasFile f = Fixture("4200345990_masmp_excerpt.las");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(125, f.Curves.Count);
            Assert.Equal("CURVE_2", f.Curves[1].Mnemonic);
            Assert.Equal("CURVE_3", f.Curves[2].Mnemonic);
            Assert.Equal(2, f.Diagnostics.Count(d => d.Code == "EMPTY_MNEMONIC"));
            Assert.Equal(21, Single(f, "COMMENT_LINE").Count);
            Assert.Equal(new[] { 85.0, 85.1 }, f.Curves[0].Values);
        }

        [Fact]
        public void Fixture_las3_comma_mud_log()
        {
            LasFile f = Fixture("4200346370_mudlog_comma_excerpt.las");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.Equal(3.0, f.Version);
            Assert.Equal(',', f.Delimiter);
            Assert.Equal(40, f.Curves.Count);
            Assert.Equal(new[] { 5936.0, 5937, 5938, 14498, 14499, 14500 }, f.Curves[0].Values);
            Single(f, "COMMENT_LINE");
            Single(f, "BLANK_LINE");
            Assert.Equal(2, f.Diagnostics.Count(d => d.Code == "SECTION_SKIPPED"));
        }
    }
}
