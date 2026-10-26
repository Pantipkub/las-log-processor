using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LasLogs.Core
{
    /// <summary>The four curves the log plot needs. The enum value indexes <see cref="LasFile.Concepts"/>.</summary>
    public enum Concept
    {
        GammaRay,
        DeepResistivity,
        BulkDensity,
        NeutronPorosity,
    }

    public sealed class CatalogEntry
    {
        public CatalogEntry(Concept concept, string mnemonic, int priority, string confidence)
        {
            Concept = concept;
            Mnemonic = mnemonic;
            Priority = priority;
            Confidence = confidence;
        }

        public Concept Concept { get; }
        public string Mnemonic { get; }
        public int Priority { get; }
        public string Confidence { get; }
    }

    /// <summary>The curve chosen for one concept, in the concept's canonical unit, or why there is none.</summary>
    public sealed class ConceptCurve
    {
        public ConceptCurve(Concept concept)
        {
            Concept = concept;
        }

        public Concept Concept { get; }
        /// <summary>The selected file curve; null when the concept is unavailable.</summary>
        public CurveDef Source { get; set; }
        public CatalogEntry Entry { get; set; }
        /// <summary>Canonical-unit values aligned with the depth column (the source array itself when the factor is 1).</summary>
        public double[] Values { get; set; }
        public double Factor { get; set; } = 1;
        public double Median { get; set; } = double.NaN;
        /// <summary>Plot label when unavailable: "not in file", "present but all null" or "rejected: ...".</summary>
        public string Missing { get; set; }
    }

    /// <summary>
    /// Curve-name catalog (an editable CSV data file) plus the fixed unit table and QC ranges per concept:
    /// docs/DECISIONS.md sections 4, 5 and 8.
    /// </summary>
    public sealed class CurveCatalog
    {
        public const string FileName = "curve_catalog.csv";

        // Indexed by Concept.
        private static readonly string[] CanonicalUnits = { "gAPI", "ohm.m", "g/cm3", "v/v" };
        // Accepted unit spellings (upper case) and the factor to the canonical unit.
        private static readonly Dictionary<string, double>[] Units =
        {
            new Dictionary<string, double> { { "GAPI", 1 }, { "API", 1 } },
            new Dictionary<string, double> { { "OHMM", 1 }, { "OHM-M", 1 }, { "OHM.M", 1 } },
            new Dictionary<string, double> { { "G/C3", 1 }, { "G/CC", 1 }, { "G/CM3", 1 }, { "K/M3", 0.001 }, { "KG/M3", 0.001 } },
            new Dictionary<string, double>
            {
                { "CFCF", 1 }, { "V/V", 1 }, { "FRAC", 1 }, { "DECP", 1 }, { "DECI", 1 }, { "FT3/FT3", 1 },
                { "PERC", 0.01 }, { "PU", 0.01 }, { "%", 0.01 },
            },
        };
        // "m.ohm" means ohm.m here, not milliohm (same-file evidence, report F.8): accepted, but flagged.
        private const string ResistivityAlias = "M.OHM";
        private static readonly string[] BlankUnits = { "", "UNKNOWN", "U", "----" };
        // Blank unit: the factor to assume, by the largest 99th percentile it allows; tried in order. None fits = ambiguous.
        private static readonly double[][][] BlankRules =
        {
            new[] { new[] { double.PositiveInfinity, 1 } },   // GR has one unit family
            new[] { new[] { double.PositiveInfinity, 1 } },   // so has resistivity (conductivity has its own mnemonics)
            new[] { new[] { 5.0, 1 } },
            new[] { new[] { 1.5, 1 }, new[] { 150.0, 0.01 } },
        };
        // QC: the median is expected within these (canonical units). Warnings only.
        private static readonly double[][] TypicalMedian = { new[] { 5.0, 300 }, new[] { 0.1, 10000 }, new[] { 1.5, 3.1 }, new[] { 0.005, 0.6 } };
        // A neutron candidate whose description names another measurement (other matrix, crossplot) is not used.
        private static readonly string[] ExcludedWords = { "crossplot", "sandstone", "dolomite", "correction", "caliper", "standoff", "mud", "matrix" };

        private CurveCatalog(List<CatalogEntry> entries)
        {
            Entries = entries;
        }

        /// <summary>Sorted by priority; ties keep the file order.</summary>
        public IReadOnlyList<CatalogEntry> Entries { get; }

        public static string CanonicalUnit(Concept concept)
        {
            return CanonicalUnits[(int)concept];
        }

        /// <summary>Reads <c>concept,mnemonic,priority,confidence</c> rows; '#' lines and the header row are skipped.</summary>
        public static CurveCatalog Load(string path)
        {
            var entries = new List<CatalogEntry>();
            int n = 0;
            foreach (string raw in File.ReadLines(path, Encoding.UTF8))
            {
                n++;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("concept,", StringComparison.OrdinalIgnoreCase)) continue;
                string[] f = line.Split(',').Select(s => s.Trim()).ToArray();
                Concept concept;
                int priority;
                if (f.Length < 3 || f[1].Length == 0
                    || !Enum.TryParse(f[0], true, out concept) || !Enum.IsDefined(typeof(Concept), concept)
                    || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
                    throw new InvalidDataException(path + " line " + n + ": expected concept,mnemonic,priority[,confidence]");
                entries.Add(new CatalogEntry(concept, f[1], priority, f.Length > 3 ? f[3] : ""));
            }
            return new CurveCatalog(entries.OrderBy(e => e.Priority).ToList());   // OrderBy is stable
        }

        /// <summary>
        /// Picks one curve per concept (run after the null policy, so "all null" is known) and records each choice,
        /// conversion and QC finding on the file.
        /// </summary>
        public ConceptCurve[] Select(LasFile file)
        {
            var result = new ConceptCurve[CanonicalUnits.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Select(file, (Concept)i);
            if (result.All(c => c.Source == null))
                file.Add(Severity.Observation, "NO_REQUIRED_CURVES", "none of the four log concepts found; the plot gets empty tracks");
            return result;
        }

        private ConceptCurve Select(LasFile file, Concept concept)
        {
            var chosen = new ConceptCurve(concept);
            var notUsed = new List<string>();
            string firstReason = null;
            double factor = 1;
            string unitKind = null;

            foreach (CatalogEntry entry in Entries.Where(e => e.Concept == concept))
            {
                foreach (CurveDef curve in file.Curves.Where(c => c.Index > 0 && string.Equals(c.Mnemonic.Trim(), entry.Mnemonic, StringComparison.OrdinalIgnoreCase)))
                {
                    string label = curve.Mnemonic + " (col " + (curve.Index + 1) + ")";
                    if (chosen.Source != null && !string.Equals(curve.Mnemonic.Trim(), chosen.Source.Mnemonic.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        notUsed.Add(label + ": lower priority");
                        continue;
                    }

                    double f;
                    string kind;
                    string why = Check(file, concept, curve, out f, out kind);
                    if (why != null)
                    {
                        notUsed.Add(label + ": " + why);
                        if (firstReason == null) firstReason = curve.Mnemonic + " " + why;
                    }
                    else if (chosen.Source == null)
                    {
                        chosen.Source = curve;
                        chosen.Entry = entry;
                        factor = f;
                        unitKind = kind;
                    }
                    else
                    {
                        notUsed.Add(label + ": same name, first one used");
                        file.Add(Severity.Warning, "AMBIGUOUS_CURVE", concept + ": " + curve.Mnemonic + " appears more than once (cols "
                            + (chosen.Source.Index + 1) + " and " + (curve.Index + 1) + "); the first is used", curve.Line, curve.Line, curve.Mnemonic);
                    }
                }
            }

            if (chosen.Source == null)
            {
                chosen.Missing = firstReason == null ? "not in file"
                    : firstReason.EndsWith(" all null") ? "present but all null"
                    : "rejected: " + firstReason;
                file.Add(Severity.Observation, "CONCEPT_UNAVAILABLE", concept + ": " + (firstReason == null ? "not in file" : string.Join("; ", notUsed)));
                return chosen;
            }

            Use(file, chosen, factor, unitKind, notUsed);
            return chosen;
        }

        // Returns null when the curve can be used, otherwise the reason. kind: "exact", "alias" or "blank".
        private static string Check(LasFile file, Concept concept, CurveDef curve, out double factor, out string kind)
        {
            factor = 1;
            kind = null;
            string unit = curve.Unit.Trim().ToUpperInvariant();
            if (Units[(int)concept].TryGetValue(unit, out factor)) kind = "exact";
            else if (concept == Concept.DeepResistivity && unit == ResistivityAlias) kind = "alias";
            else if (BlankUnits.Contains(unit)) kind = "blank";
            else
            {
                factor = 1;
                file.Add(Severity.Warning, "UNIT_UNRECOGNIZED", concept + ": " + curve.Mnemonic + " unit '" + curve.Unit + "' is not a known "
                    + CanonicalUnit(concept) + " spelling; curve not used", curve.Line, curve.Line, curve.Mnemonic);
                return "unit '" + curve.Unit + "' not recognised";
            }

            // Neutron only (DECISIONS 5, 2026-10-26): elsewhere the words hit good curves, e.g. ZDNC "...mud weight corrected density".
            string word = concept != Concept.NeutronPorosity ? null
                : ExcludedWords.FirstOrDefault(w => curve.Description.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            if (word != null) return "description '" + curve.Description + "' excluded ('" + word + "')";

            double[] sorted = SortedValid(curve.Values);
            if (sorted.Length == 0) return "all null";
            // DECISIONS 1 (2026-10-27): e.g. GAMMA = -999.2 in every record of 4200343170_STATE_AQ_8_MUDLOG.las.
            if (sorted.All(v => Array.IndexOf(Conditioner.SuspectedSentinels, v) >= 0))
                return "has only placeholder values (" + string.Join(", ", sorted.Distinct().Select(F)) + ")";

            if (kind == "blank")
            {
                double p99 = sorted[(int)(0.99 * (sorted.Length - 1))];
                double[] rule = BlankRules[(int)concept].FirstOrDefault(r => p99 <= r[0]);
                if (rule == null) return "blank unit and the values do not tell which unit (p99 " + F(p99) + ")";
                factor = rule[1];
            }
            return null;
        }

        private static void Use(LasFile file, ConceptCurve chosen, double factor, string unitKind, List<string> notUsed)
        {
            CurveDef c = chosen.Source;
            Concept concept = chosen.Concept;
            string unit = CanonicalUnit(concept);
            chosen.Factor = factor;
            chosen.Values = factor == 1 ? c.Values : c.Values.Select(v => v * factor).ToArray();

            file.Add(Severity.Transformation, "CURVE_MAPPED", concept + " <- " + c.Mnemonic + " [" + c.Unit + "] '" + c.Description + "' (col "
                + (c.Index + 1) + ", priority " + chosen.Entry.Priority + ", confidence " + chosen.Entry.Confidence + ")"
                + (notUsed.Count == 0 ? "" : "; not used: " + string.Join("; ", notUsed)), c.Line, c.Line, c.Mnemonic);
            if (unitKind == "alias")
                file.Add(Severity.Warning, "UNIT_ALIAS", concept + ": unit '" + c.Unit + "' read as " + unit, c.Line, c.Line, c.Mnemonic);
            if (unitKind == "blank")
                file.Add(Severity.Warning, "UNIT_ASSUMED", concept + ": unit '" + c.Unit + "' is blank; assumed "
                    + (factor == 1 ? unit : "percent") + " from the values", c.Line, c.Line, c.Mnemonic);
            if (factor != 1)
                file.Add(Severity.Transformation, "UNIT_CONVERTED", concept + " <- " + c.Mnemonic + ": " + (c.Unit.Trim().Length == 0 ? "(blank)" : c.Unit)
                    + " -> " + unit + " x" + F(factor) + " (" + chosen.Values.Count(v => !double.IsNaN(v)) + " values)", c.Line, c.Line, c.Mnemonic);

            double[] sorted = SortedValid(chosen.Values);
            chosen.Median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
            double[] range = TypicalMedian[(int)concept];
            if (!(chosen.Median >= range[0] && chosen.Median <= range[1]))
                file.Add(Severity.Warning, "IMPLAUSIBLE_VALUES", concept + " <- " + c.Mnemonic + ": median " + F(chosen.Median) + " " + unit
                    + " outside the typical " + F(range[0]) + "-" + F(range[1]) + "; possible unit or column mismatch", c.Line, c.Line, c.Mnemonic);

            if (concept == Concept.DeepResistivity)
            {
                int nonPositive = sorted.TakeWhile(v => v <= 0).Count();
                if (nonPositive > 0)
                    file.Add(Severity.Warning, "RES_NONPOSITIVE", concept + " <- " + c.Mnemonic + ": " + nonPositive
                        + " values <= 0 cannot be drawn on the log axis", c.Line, c.Line, c.Mnemonic, nonPositive);
            }
        }

        private static double[] SortedValid(double[] values)
        {
            double[] valid = values.Where(v => !double.IsNaN(v)).ToArray();
            Array.Sort(valid);
            return valid;
        }

        internal static string F(double v)
        {
            return v.ToString("G7", CultureInfo.InvariantCulture);
        }
    }
}
