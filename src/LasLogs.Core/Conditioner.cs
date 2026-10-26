using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LasLogs.Core
{
    /// <summary>
    /// Applies the conditioning policy (docs/DECISIONS.md sections 1-3) to a loaded file in place, then maps the four
    /// concepts with the catalog. Every change is recorded as a diagnostic by the code that makes it.
    /// Never resamples, interpolates, sorts or clamps: the only reorder is reversing a strictly decreasing log.
    /// </summary>
    public static class Conditioner
    {
        private const double StandardNull = -999.25;
        // Undeclared values that look like placeholders: kept as data, but flagged. A curve with nothing else is not plotted (CurveCatalog).
        internal static readonly double[] SuspectedSentinels = { -9999, -999, -999.2 };

        public static void Run(LasFile file, CurveCatalog catalog)
        {
            if (file.RecordCount == 0) return;
            double nullValue = DeclaredNull(file);
            RejectMissingDepths(file, nullValue);
            if (file.RecordCount > 0)
            {
                ApplyNulls(file, nullValue);
                CheckDepth(file);
                file.Concepts = catalog.Select(file);
            }
            file.UpdateStatus();
        }

        // NaN when NULL is missing or not a number; the -999.25 rule below still applies then.
        private static double DeclaredNull(LasFile file)
        {
            HeaderItem item = Header(file, "NULL");
            if (item == null)
            {
                file.Add(Severity.Warning, "NULL_MISSING", "no NULL declared in ~W; only -999.25 is treated as missing");
                return double.NaN;
            }
            double value = Number(item.Value);
            if (double.IsNaN(value))
                file.Add(Severity.Warning, "NULL_MISSING", "NULL '" + item.Value + "' is not a number; only -999.25 is treated as missing", item.Line, item.Line);
            return value;
        }

        private static bool IsNull(double v, double nullValue)
        {
            // The LAS convention -999.25 counts as missing even when another NULL is declared (DECISIONS 1).
            return v == nullValue || v == StandardNull;
        }

        // A record without a usable depth cannot be placed: reject the whole row (Error).
        private static void RejectMissingDepths(LasFile file, double nullValue)
        {
            double[] depth = file.Curves[0].Values;
            var keep = new bool[depth.Length];
            int rejected = 0, first = 0, last = 0;
            for (int r = 0; r < depth.Length; r++)
            {
                keep[r] = !double.IsNaN(depth[r]) && !IsNull(depth[r], nullValue);
                if (keep[r]) continue;
                rejected++;
                if (first == 0) first = file.RecordLines[r];
                last = file.RecordLines[r];
            }
            if (rejected == 0) return;

            file.Add(Severity.Error, "DEPTH_INVALID", rejected + " records have a missing or non-numeric depth and were rejected", first, last, file.Curves[0].Mnemonic, rejected);
            foreach (CurveDef c in file.Curves)
                c.Values = c.Values.Where((v, r) => keep[r]).ToArray();
            file.RecordLines = file.RecordLines.Where((v, r) => keep[r]).ToArray();
        }

        private static void ApplyNulls(LasFile file, double nullValue)
        {
            bool mismatch = nullValue != StandardNull;   // also true when no NULL is declared
            int standardCount = 0, standardCurves = 0, standardFirst = 0, standardLast = 0, nullCount = 0;
            int[] lines = file.RecordLines;

            foreach (CurveDef c in file.Curves.Skip(1))
            {
                double[] v = c.Values;
                int nulls = 0, nullFirst = 0, nullLast = 0, standard = 0;
                var suspected = new int[SuspectedSentinels.Length];
                int suspectedFirst = 0, suspectedLast = 0;
                for (int r = 0; r < v.Length; r++)
                {
                    if (v[r] == nullValue)
                    {
                        v[r] = double.NaN;
                        if (nulls++ == 0) nullFirst = lines[r];
                        nullLast = lines[r];
                    }
                    else if (v[r] == StandardNull)
                    {
                        v[r] = double.NaN;
                        standard++;
                        if (standardFirst == 0 || lines[r] < standardFirst) standardFirst = lines[r];
                        standardLast = Math.Max(standardLast, lines[r]);
                    }
                    else
                    {
                        int k = Array.IndexOf(SuspectedSentinels, v[r]);
                        if (k < 0) continue;
                        suspected[k]++;
                        if (suspectedFirst == 0) suspectedFirst = lines[r];
                        suspectedLast = lines[r];
                    }
                }

                if (nulls > 0)
                    file.Add(Severity.Transformation, "NULL_TO_MISSING", c.Mnemonic + ": " + nulls + " of " + v.Length + " values equal NULL "
                        + Header(file, "NULL").Value + " -> missing", nullFirst, nullLast, c.Mnemonic, nulls);
                if (standard > 0)
                {
                    standardCount += standard;
                    standardCurves++;
                }
                nullCount += nulls;
                if (suspectedFirst > 0)
                    file.Add(Severity.Warning, "SUSPECTED_SENTINEL", c.Mnemonic + ": "
                        + string.Join(", ", SuspectedSentinels.Select((s, k) => suspected[k] == 0 ? null : F(s) + " x" + suspected[k]).Where(s => s != null))
                        + " kept as data; looks like an undeclared null placeholder", suspectedFirst, suspectedLast, c.Mnemonic, suspected.Sum());
            }

            if (mismatch && standardCount > 0)
                file.Add(Severity.Warning, "SENTINEL_MISMATCH", "NULL is " + (double.IsNaN(nullValue) ? "not declared" : "declared as " + F(nullValue))
                    + ", but -999.25 appears " + standardCount + " times in " + standardCurves + " curves; also treated as missing",
                    standardFirst, standardLast, null, standardCount);
            if (nullValue == 0)
                file.Add(Severity.Warning, "NULL_IS_ZERO", "NULL is declared as 0: " + nullCount
                    + " zero values treated as missing; real zeros cannot be told apart", Header(file, "NULL").Line, Header(file, "NULL").Line, null, nullCount);
        }

        private static void CheckDepth(LasFile file)
        {
            CurveDef depthCurve = file.Curves[0];
            double[] d = depthCurve.Values;
            int[] lines = file.RecordLines;
            int n = d.Length;
            string unit = depthCurve.Unit.Trim().Length == 0 ? "" : " " + depthCurve.Unit.Trim();

            int negative = d.Count(x => x < 0);
            if (negative > 0)
                file.Add(Severity.Observation, "NEGATIVE_DEPTH", negative + " depths are negative (min " + F(d.Min()) + unit + "); kept", 0, 0, depthCurve.Mnemonic, negative);
            if (n < 2) return;

            int up = 0, down = 0, reversals = 0, lastSign = 0, firstReversal = -1, lastReversal = -1;
            var steps = new Dictionary<double, int>();
            for (int i = 1; i < n; i++)
            {
                double diff = d[i] - d[i - 1];
                int sign = Math.Sign(diff);
                if (sign == 0) continue;
                if (sign > 0) up++;
                else down++;
                if (lastSign != 0 && sign != lastSign)
                {
                    reversals++;
                    if (firstReversal < 0) firstReversal = i;
                    lastReversal = i;
                }
                lastSign = sign;
                double key = Math.Round(Math.Abs(diff), 6);
                int count;
                steps.TryGetValue(key, out count);
                steps[key] = count + 1;
            }
            int duplicates = n - new HashSet<double>(d).Count;
            double modal = double.NaN;
            int modalCount = 0;
            if (steps.Count > 0)
            {
                KeyValuePair<double, int> top = steps.OrderByDescending(s => s.Value).ThenBy(s => s.Key).First();
                modal = top.Key;
                modalCount = top.Value;
            }
            file.ModalStep = modal;

            // Header STRT/STOP are compared with the file order, before any reversal.
            double step = Number(HeaderValue(file, "STEP"));
            double tolerance = 0.5 * (step != 0 && !double.IsNaN(step) ? Math.Abs(step) : double.IsNaN(modal) ? 0 : modal) + 1e-6;
            double strt = Number(HeaderValue(file, "STRT"));
            double stop = Number(HeaderValue(file, "STOP"));
            if (Math.Abs(d[0] - strt) > tolerance)
                file.Add(Severity.Observation, "STRT_MISMATCH", "STRT " + F(strt) + " but the first depth is " + F(d[0]) + " (line " + lines[0] + ")", lines[0], lines[0]);
            if (Math.Abs(d[n - 1] - stop) > tolerance)
                file.Add(Severity.Observation, "STOP_MISMATCH", "STOP " + F(stop) + " but the last depth is " + F(d[n - 1]) + " (line " + lines[n - 1] + ")", lines[n - 1], lines[n - 1]);

            if (up > 0 && down > 0)
            {
                file.DepthOrder = DepthOrder.NonMonotonic;
                file.Add(Severity.Warning, "DEPTH_NON_MONOTONIC", "depth changes direction " + reversals + " times, first at line " + lines[firstReversal]
                    + " (" + F(d[firstReversal - 1]) + " -> " + F(d[firstReversal]) + "); " + duplicates + " repeated depth values; file order kept",
                    lines[firstReversal], lines[lastReversal], depthCurve.Mnemonic, reversals);
            }
            else if (down > 0)
            {
                file.DepthOrder = DepthOrder.Decreasing;
                file.Add(Severity.Transformation, "DEPTH_REVERSED", "depth decreases " + F(d[0]) + " -> " + F(d[n - 1]) + unit + "; "
                    + n + " records reversed to increasing depth, values unchanged", lines[0], lines[n - 1], depthCurve.Mnemonic, n);
                foreach (CurveDef c in file.Curves) Array.Reverse(c.Values);
                Array.Reverse(lines);
            }
            else if (up > 0) file.DepthOrder = DepthOrder.Increasing;

            if (file.DepthOrder != DepthOrder.NonMonotonic && duplicates > 0)
                file.Add(Severity.Warning, "DEPTH_DUPLICATES", duplicates + " repeated depth values kept", 0, 0, depthCurve.Mnemonic, duplicates);
            if (double.IsNaN(modal)) return;

            if (step == 0)
            {
                if (steps.Count > 1)
                    file.Add(Severity.Observation, "STEP_IRREGULAR", "STEP 0 declared (irregular sampling); " + (up + down - modalCount)
                        + " of " + (up + down) + " intervals differ from the most common one (" + F(modal) + ")");
                return;
            }
            if (!double.IsNaN(step) && Math.Abs(modal - Math.Abs(step)) > 0.01 * Math.Abs(step))
                file.Add(Severity.Warning, "STEP_MISMATCH", "STEP " + F(step) + " declared, but the most common depth interval is " + F(modal)
                    + " (" + modalCount + " of " + (up + down) + " intervals)", Header(file, "STEP").Line, Header(file, "STEP").Line);

            if (file.DepthOrder == DepthOrder.NonMonotonic) return;   // its jumps are in the warning above
            int gaps = 0, gapFirst = 0, gapLast = 0;
            double largest = 0;
            for (int i = 1; i < n; i++)
            {
                double diff = d[i] - d[i - 1];
                if (!(diff > 1.5 * modal)) continue;
                if (gaps++ == 0) gapFirst = lines[i];
                gapLast = lines[i];
                largest = Math.Max(largest, diff);
            }
            if (gaps > 0)
                file.Add(Severity.Warning, "DEPTH_GAP", gaps + " depth intervals are larger than 1.5 x the usual " + F(modal) + unit
                    + " (largest " + F(largest) + "); nothing interpolated", gapFirst, gapLast, depthCurve.Mnemonic, gaps);
        }

        private static HeaderItem Header(LasFile file, string mnemonic)
        {
            return file.WellItems.FirstOrDefault(i => string.Equals(i.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase));
        }

        private static string HeaderValue(LasFile file, string mnemonic)
        {
            HeaderItem item = Header(file, mnemonic);
            return item == null ? "" : item.Value;
        }

        private static double Number(string text)
        {
            double v;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsInfinity(v) ? v : double.NaN;
        }

        private static string F(double v)
        {
            return CurveCatalog.F(v);
        }
    }
}
