using System;
using System.IO;
using System.Linq;
using LasLogs.Core;
using Xunit;

namespace LasLogs.Tests
{
    public class ConditionerTests
    {
        private static readonly CurveCatalog Catalog = CurveCatalog.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CurveCatalog.FileName));

        // Lines 4-8; with DefaultWell and two curve lines after DEPT, data starts at line 14.
        private const string DefaultWell =
            " STRT.F  100.0 :\n" +
            " STOP.F  101.0 :\n" +
            " STEP.F  0.5   :\n" +
            " NULL.   -999.25 :\n";
        private const string Dept = " DEPT.F : Depth\n";
        private const string GrRhob = Dept + " GR.GAPI : Gamma ray\n RHOB.G/C3 : Bulk density\n";

        private static LasFile Run(string curves, string data, string well = DefaultWell)
        {
            string text = "~V\n VERS. 2.0 :\n WRAP. NO :\n~W\n" + well + "~C\n" + curves + "~A\n" + data;
            LasFile file = LasReader.Read(new StringReader(text), "test.las");
            Conditioner.Run(file, Catalog);
            return file;
        }

        private static Diagnostic Single(LasFile file, string code, string curve = null)
        {
            return Assert.Single(file.Diagnostics, d => d.Code == code && (curve == null || d.Curve == curve));
        }

        private static void None(LasFile file, string code)
        {
            Assert.DoesNotContain(file.Diagnostics, d => d.Code == code);
        }

        private static ConceptCurve Concept(LasFile file, Concept concept)
        {
            return file.Concepts[(int)concept];
        }

        [Fact]
        public void Declared_null_becomes_NaN_and_is_counted_per_curve()
        {
            LasFile f = Run(GrRhob, "100.0 -999.25 2.45\n100.5 50 -999.25\n101.0 -999.25 2.47\n");

            Assert.Equal(new[] { double.NaN, 50, double.NaN }, f.Curves[1].Values);
            Assert.Equal(new[] { 2.45, double.NaN, 2.47 }, f.Curves[2].Values);
            Diagnostic gr = Single(f, "NULL_TO_MISSING", "GR");
            Assert.Equal(Severity.Transformation, gr.Severity);
            Assert.Equal(2, gr.Count);
            Assert.Equal(14, gr.FirstLine);
            Assert.Equal(16, gr.LastLine);
            Assert.Equal(1, Single(f, "NULL_TO_MISSING", "RHOB").Count);
            None(f, "SENTINEL_MISMATCH");
            Assert.Equal(FileStatus.Loaded, f.Status);
        }

        [Theory] // S10
        [InlineData("")]
        [InlineData(" NULL.   NONE :\n")]
        public void Missing_or_non_numeric_null_still_treats_minus_999_25_as_missing(string nullLine)
        {
            LasFile f = Run(GrRhob, "100.0 -999.25 2.45\n100.5 50 2.46\n",
                " STRT.F 100.0 :\n STOP.F 100.5 :\n STEP.F 0.5 :\n" + nullLine);

            Assert.Equal(Severity.Warning, Single(f, "NULL_MISSING").Severity);
            Assert.True(double.IsNaN(f.Curves[1].Values[0]));
            Assert.Equal(1, Single(f, "SENTINEL_MISMATCH").Count);
        }

        [Fact]
        public void Null_zero_is_honoured_with_a_warning_and_minus_999_25_also_goes()
        {
            LasFile f = Run(GrRhob, "100.0 0 2.45\n100.5 50 0.000\n101.0 -999.25 2.47\n",
                " STRT.F 100.0 :\n STOP.F 101.0 :\n STEP.F 0.5 :\n NULL. 0.0 :\n");

            Assert.Equal(2, Single(f, "NULL_IS_ZERO").Count);
            Assert.Equal(1, Single(f, "SENTINEL_MISMATCH").Count);
            Assert.Equal(new[] { double.NaN, 50, double.NaN }, f.Curves[1].Values);
            Assert.Equal(new[] { 2.45, double.NaN, 2.47 }, f.Curves[2].Values);
        }

        [Fact]
        public void Undeclared_placeholder_values_stay_as_data_with_a_warning()
        {
            LasFile f = Run(GrRhob, "100.0 -9999 2.45\n100.5 -999 2.46\n101.0 50 -999.2\n");

            Assert.Equal(new[] { -9999.0, -999, 50 }, f.Curves[1].Values);
            Assert.Equal(2, Single(f, "SUSPECTED_SENTINEL", "GR").Count);
            Assert.Equal(1, Single(f, "SUSPECTED_SENTINEL", "RHOB").Count);
            Assert.Equal("GR", Concept(f, LasLogs.Core.Concept.GammaRay).Source.Mnemonic);   // one real value is enough
        }

        [Fact] // GAMMA in 4200343170_STATE_AQ_8_MUDLOG.las
        public void Curve_with_only_placeholder_values_is_not_plotted_but_kept()
        {
            LasFile f = Run(GrRhob, "100.0 -999.2 2.45\n100.5 -999.25 2.46\n101.0 -999.2 2.47\n");

            ConceptCurve gr = Concept(f, LasLogs.Core.Concept.GammaRay);
            Assert.Null(gr.Source);
            Assert.Equal("rejected: GR has only placeholder values (-999.2)", gr.Missing);
            Assert.Equal(new[] { -999.2, double.NaN, -999.2 }, f.Curves[1].Values);
            Assert.Equal(2, Single(f, "SUSPECTED_SENTINEL", "GR").Count);
            Assert.Contains(f.Diagnostics, d => d.Code == "CONCEPT_UNAVAILABLE" && d.Message.StartsWith("GammaRay"));
        }

        [Fact] // S11
        public void Strictly_decreasing_depth_is_reversed_with_values_aligned()
        {
            LasFile f = Run(GrRhob, "101.0 52 2.47\n100.5 51 2.46\n100.0 50 2.45\n",
                " STRT.F 101.0 :\n STOP.F 100.0 :\n STEP.F -0.5 :\n NULL. -999.25 :\n");

            Assert.Equal(DepthOrder.Decreasing, f.DepthOrder);
            Assert.Equal(new[] { 100.0, 100.5, 101.0 }, f.Curves[0].Values);
            Assert.Equal(new[] { 50.0, 51, 52 }, f.Curves[1].Values);
            Assert.Equal(new[] { 2.45, 2.46, 2.47 }, f.Curves[2].Values);
            Assert.Equal(new[] { 16, 15, 14 }, f.RecordLines);
            Assert.Equal(Severity.Transformation, Single(f, "DEPTH_REVERSED").Severity);
            None(f, "STEP_MISMATCH");
            None(f, "STRT_MISMATCH");
        }

        [Fact] // S12
        public void Repeated_depths_in_monotonic_data_are_kept_with_a_warning()
        {
            LasFile f = Run(GrRhob, "100.0 50 2.45\n100.5 51 2.46\n100.5 52 2.47\n101.0 53 2.48\n");

            Assert.Equal(DepthOrder.Increasing, f.DepthOrder);
            Assert.Equal(4, f.RecordCount);
            Assert.Equal(1, Single(f, "DEPTH_DUPLICATES").Count);
        }

        [Fact]
        public void Non_monotonic_depth_keeps_the_file_order()
        {
            LasFile f = Run(GrRhob, "100.0 1 2\n100.5 2 2\n101.0 3 2\n100.5 4 2\n101.0 5 2\n101.5 6 2\n",
                " STRT.F 100.0 :\n STOP.F 101.5 :\n STEP.F 0.5 :\n NULL. -999.25 :\n");

            Assert.Equal(DepthOrder.NonMonotonic, f.DepthOrder);
            Assert.Equal(new[] { 100.0, 100.5, 101, 100.5, 101, 101.5 }, f.Curves[0].Values);
            Diagnostic d = Single(f, "DEPTH_NON_MONOTONIC");
            Assert.Equal(2, d.Count);          // down at line 17, up again at line 18
            Assert.Equal(17, d.FirstLine);
            Assert.Equal(18, d.LastLine);
            Assert.Contains("2 repeated depth values", d.Message);
            None(f, "DEPTH_REVERSED");
            None(f, "DEPTH_DUPLICATES");
        }

        [Fact] // S13
        public void Step_zero_with_irregular_intervals_is_an_observation_only()
        {
            LasFile f = Run(GrRhob, "100.0 1 2\n100.5 2 2\n101.25 3 2\n101.5 4 2\n",
                " STRT.F 100.0 :\n STOP.F 101.5 :\n STEP.F 0 :\n NULL. -999.25 :\n");

            Assert.Equal(Severity.Observation, Single(f, "STEP_IRREGULAR").Severity);
            None(f, "STEP_MISMATCH");
            None(f, "DEPTH_GAP");
        }

        [Fact]
        public void Wrong_header_step_and_gaps_are_reported_and_data_is_untouched()
        {
            LasFile f = Run(GrRhob, "100.0 1 2\n100.5 2 2\n101.0 3 2\n101.5 4 2\n103.0 5 2\n",
                " STRT.F 100.0 :\n STOP.F 103.0 :\n STEP.F 0.75 :\n NULL. -999.25 :\n");

            Assert.Equal(0.5, f.ModalStep);
            Assert.Equal(Severity.Warning, Single(f, "STEP_MISMATCH").Severity);
            Diagnostic gap = Single(f, "DEPTH_GAP");
            Assert.Equal(1, gap.Count);
            Assert.Equal(18, gap.FirstLine);
            Assert.Equal(new[] { 100.0, 100.5, 101, 101.5, 103 }, f.Curves[0].Values);
        }

        [Fact]
        public void Strt_and_stop_disagreements_are_observations()
        {
            LasFile f = Run(GrRhob, "100.0 1 2\n100.5 2 2\n101.0 3 2\n",
                " STRT.F 99.0 :\n STOP.F 105.0 :\n STEP.F 0.5 :\n NULL. -999.25 :\n");

            Assert.Equal(Severity.Observation, Single(f, "STRT_MISMATCH").Severity);
            Assert.Equal(Severity.Observation, Single(f, "STOP_MISMATCH").Severity);
        }

        [Fact] // S14
        public void Unknown_curve_names_only_leave_every_concept_unavailable()
        {
            LasFile f = Run(Dept + " XYZ.UNK : something\n", "100.0 1\n100.5 2\n101.0 3\n");

            Assert.Equal(FileStatus.Loaded, f.Status);
            Assert.All(f.Concepts, c => Assert.Null(c.Source));
            Assert.All(f.Concepts, c => Assert.Equal("not in file", c.Missing));
            Assert.Equal(4, f.Diagnostics.Count(d => d.Code == "CONCEPT_UNAVAILABLE"));
            Single(f, "NO_REQUIRED_CURVES");
        }

        [Theory] // S15
        [InlineData("PU", "15", 0.15, "UNIT_CONVERTED")]
        [InlineData("%", "15", 0.15, "UNIT_CONVERTED")]
        [InlineData("V/V", "0.15", 0.15, "CURVE_MAPPED")]
        [InlineData("", "0.15", 0.15, "UNIT_ASSUMED")]
        [InlineData("", "15", 0.15, "UNIT_ASSUMED")]
        [InlineData("V/V", "15", 15, "IMPLAUSIBLE_VALUES")]   // the declared unit is trusted, the conflict flagged
        public void Neutron_units_are_converted_assumed_or_flagged(string unit, string raw, double expected, string code)
        {
            LasFile f = Run(Dept + " NPHI." + unit + " : Neutron porosity\n", "100.0 " + raw + "\n100.5 " + raw + "\n101.0 " + raw + "\n");

            ConceptCurve n = Concept(f, LasLogs.Core.Concept.NeutronPorosity);
            Assert.Equal("NPHI", n.Source.Mnemonic);
            Assert.All(n.Values, v => Assert.Equal(expected, v, 9));
            Assert.Contains(f.Diagnostics, d => d.Code == code);
            Assert.Equal(raw, n.Source.Values[0].ToString(System.Globalization.CultureInfo.InvariantCulture));   // the file curve is not converted
        }

        [Fact] // S16
        public void Zero_and_negative_resistivity_are_kept_and_counted()
        {
            LasFile f = Run(Dept + " ILD.OHMM : Deep induction\n", "100.0 10\n100.5 0\n101.0 -1\n");

            Assert.Equal(new[] { 10.0, 0, -1 }, Concept(f, LasLogs.Core.Concept.DeepResistivity).Values);
            Assert.Equal(2, Single(f, "RES_NONPOSITIVE").Count);
        }

        [Fact] // S17
        public void All_null_candidate_leaves_the_concept_present_but_all_null()
        {
            LasFile f = Run(GrRhob, "100.0 -999.25 2.45\n100.5 -999.25 2.46\n101.0 -999.25 2.47\n");

            ConceptCurve gr = Concept(f, LasLogs.Core.Concept.GammaRay);
            Assert.Null(gr.Source);
            Assert.Equal("present but all null", gr.Missing);
            Assert.Contains(f.Diagnostics, d => d.Code == "CONCEPT_UNAVAILABLE" && d.Message.StartsWith("GammaRay"));
        }

        [Fact] // S18
        public void Metric_depth_keeps_its_unit_and_values()
        {
            LasFile f = Run(" DEPT.M : Depth\n GR.GAPI : Gamma ray\n", "30.0 50\n30.1524 51\n30.3048 52\n",
                " STRT.M 30.0 :\n STOP.M 30.3048 :\n STEP.M 0.1524 :\n NULL. -999.25 :\n");

            Assert.Equal("M", f.Curves[0].Unit);
            Assert.Equal(new[] { 30.0, 30.1524, 30.3048 }, f.Curves[0].Values);
            Assert.Equal(0.1524, f.ModalStep);
            None(f, "STEP_MISMATCH");
        }

        [Fact] // S20
        public void Records_with_a_missing_depth_are_rejected()
        {
            LasFile f = Run(Dept + " GR.GAPI : Gamma ray\n", "100.0 50\n-999.25 51\n*** 52\n100.5 53\n",
                " STRT.F 100.0 :\n STOP.F 100.5 :\n STEP.F 0.5 :\n NULL. -999.25 :\n");

            Diagnostic d = Single(f, "DEPTH_INVALID");
            Assert.Equal(Severity.Error, d.Severity);
            Assert.Equal(2, d.Count);
            Assert.Equal(14, d.FirstLine);   // data starts at line 13 here (one curve after DEPT)
            Assert.Equal(15, d.LastLine);
            Assert.Equal(FileStatus.Partial, f.Status);
            Assert.Equal(new[] { 100.0, 100.5 }, f.Curves[0].Values);
            Assert.Equal(new[] { 50.0, 53 }, f.Curves[1].Values);
            Assert.Equal(new[] { 13, 16 }, f.RecordLines);
        }

        [Fact] // S21
        public void Duplicate_mnemonic_among_candidates_takes_the_first_and_warns()
        {
            LasFile f = Run(Dept + " GR.GAPI : Gamma ray\n GR.GAPI : Gamma ray again\n", "100.0 50 60\n100.5 51 61\n101.0 52 62\n");

            Assert.Equal(1, Concept(f, LasLogs.Core.Concept.GammaRay).Source.Index);
            Assert.Equal(Severity.Warning, Single(f, "AMBIGUOUS_CURVE").Severity);
        }

        [Fact] // the duplicate NPRL in 4246135292_MAIN.las
        public void Excluded_description_is_skipped_without_an_ambiguity_warning()
        {
            LasFile f = Run(Dept + " NPRL.V/V : Crossplot Porosity\n NPRL.V/V : Limestone Neutron Porosity\n", "100.0 0.2 0.1\n100.5 0.2 0.1\n101.0 0.2 0.1\n");

            Assert.Equal(2, Concept(f, LasLogs.Core.Concept.NeutronPorosity).Source.Index);
            None(f, "AMBIGUOUS_CURVE");
        }

        [Fact] // DECISIONS 5: the exclusion words apply to neutron only
        public void Excluded_words_do_not_reject_density()
        {
            LasFile f = Run(Dept + " ZDNC.G/C3 : Borehole size/mud weight corrected density\n", "100.0 2.5\n100.5 2.5\n101.0 2.5\n");

            Assert.Equal("ZDNC", Concept(f, LasLogs.Core.Concept.BulkDensity).Source.Mnemonic);
        }

        [Fact]
        public void Higher_priority_wins_over_column_order()
        {
            LasFile f = Run(Dept + " RHOZ.G/C3 : Standard density\n RHOB.G/C3 : Bulk density\n", "100.0 2.4 2.5\n100.5 2.4 2.5\n101.0 2.4 2.5\n");

            Assert.Equal("RHOB", Concept(f, LasLogs.Core.Concept.BulkDensity).Source.Mnemonic);
            Assert.Contains("RHOZ (col 2): lower priority", Single(f, "CURVE_MAPPED").Message);
        }

        [Fact]
        public void Density_in_kg_per_m3_is_converted()
        {
            LasFile f = Run(Dept + " DEN.K/M3 : Compensated density\n", "100.0 2518\n100.5 2518\n101.0 -999.25\n");

            ConceptCurve d = Concept(f, LasLogs.Core.Concept.BulkDensity);
            Assert.Equal(2.518, d.Values[0], 9);
            Assert.Equal(2.518, d.Median, 9);
            Assert.Equal(2518, d.Source.Values[0]);
            Assert.Equal(Severity.Transformation, Single(f, "UNIT_CONVERTED").Severity);
        }

        [Fact]
        public void Unknown_unit_spelling_is_not_mapped()
        {
            LasFile f = Run(Dept + " GR.AAPI : Gamma ray\n", "100.0 50\n100.5 51\n101.0 52\n");

            ConceptCurve gr = Concept(f, LasLogs.Core.Concept.GammaRay);
            Assert.Null(gr.Source);
            Assert.StartsWith("rejected: GR unit 'AAPI'", gr.Missing);
            Single(f, "UNIT_UNRECOGNIZED");
        }

        [Fact]
        public void M_ohm_is_read_as_ohm_m_with_a_warning()
        {
            LasFile f = Run(Dept + " MLR4.M.OHM : RTeX deep resistivity 4\n", "100.0 40\n100.5 41\n101.0 42\n");

            Assert.Equal("MLR4", Concept(f, LasLogs.Core.Concept.DeepResistivity).Source.Mnemonic);
            Single(f, "UNIT_ALIAS");
        }

        [Fact] // the column-order problem in 4246135949_MAIN.las
        public void Implausible_density_median_is_flagged_and_kept()
        {
            LasFile f = Run(GrRhob, "100.0 50 0.19\n100.5 51 0.2\n101.0 52 0.18\n");

            Assert.Equal(0.19, Concept(f, LasLogs.Core.Concept.BulkDensity).Median);
            Assert.Equal("RHOB", Single(f, "IMPLAUSIBLE_VALUES").Curve);
        }

        [Fact]
        public void File_without_records_is_left_alone()
        {
            LasFile f = Run(GrRhob, "");

            Assert.Equal(FileStatus.Failed, f.Status);
            Assert.Null(f.Concepts);
        }
    }
}
