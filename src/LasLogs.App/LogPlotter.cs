using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LasLogs.Core;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace LasLogs.App
{
    /// <summary>
    /// One 3-track log per file: GR | deep resistivity (log axis) | bulk density + neutron porosity (docs/DECISIONS.md
    /// sections 7 and 8). Visual rules only: the file's values never change.
    /// </summary>
    public static class LogPlotter
    {
        public const int Width = 1200, Height = 2400;

        // Display scales and layout, indexed by Concept. Density and neutron share track 3; neutron runs right to left.
        private static readonly double[] Min = { 0, 0.2, 1.95, -0.15 };
        private static readonly double[] Max = { 150, 20000, 2.95, 0.45 };
        private static readonly string[] Units = { "gAPI", "ohm.m", "g/cm3", "v/v" };
        private static readonly string[] Labels = { "GR", "Deep resistivity", "Bulk density", "Neutron porosity" };
        private static readonly OxyColor[] Colors = { OxyColors.Green, OxyColors.Black, OxyColors.Red, OxyColors.Blue };
        // Horizontal extent of each track, as fractions of the plot width.
        private static readonly double[] Left = { 0, 0.35, 0.70, 0.70 };
        private static readonly double[] Right = { 0.30, 0.65, 1, 1 };

        /// <summary>
        /// Writes <c>plots/&lt;wellKey&gt;/&lt;file&gt;.png</c> under <paramref name="outputFolder"/> and returns that relative
        /// path, or "" when the file has no records. Adds one <c>OUT_OF_SCALE</c> Observation per curve that has any.
        /// </summary>
        public static string Plot(LasFile file, string outputFolder)
        {
            if (file.Concepts == null) return "";
            double[] depth = file.Curves[0].Values;
            double top = depth.Min(), bottom = depth.Max();
            if (top == bottom) { top -= 1; bottom += 1; }
            string unit = file.Curves[0].Unit;
            string well = Header(file, "WELL");
            // Break lines where the Conditioner reported a DEPTH_GAP (it checks regularly sampled, monotonic files only).
            double maxStep = file.Diagnostics.Any(d => d.Code == "DEPTH_GAP") ? 1.5 * file.ModalStep : double.PositiveInfinity;

            var model = new PlotModel
            {
                Title = file.SourceName,
                Subtitle = (well.Length > 0 ? well + "  |  " : "") + Num(top) + " - " + Num(bottom) + " " + unit + "  |  " + file.Status,
                Background = OxyColors.White,
            };
            model.Axes.Add(new LinearAxis
            {
                Key = "depth", Position = AxisPosition.Left, StartPosition = 1, EndPosition = 0,   // depth increases downward
                Minimum = top, Maximum = bottom, Title = "Depth" + (unit.Length > 0 ? " (" + unit + ")" : ""),
                MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = OxyColor.FromAColor(40, OxyColors.Black),
            });

            for (int i = 0; i < file.Concepts.Length; i++)
            {
                ConceptCurve curve = file.Concepts[i];
                bool log = (Concept)i == Concept.DeepResistivity, neutron = (Concept)i == Concept.NeutronPorosity;
                string name = Labels[i] + (curve.Source == null ? "" : " (" + curve.Source.Mnemonic + ")") + " [" + Units[i] + "]";
                string footer = "";

                if (curve.Values == null)
                {
                    model.Annotations.Add(new TextAnnotation
                    {
                        XAxisKey = "top" + i, YAxisKey = "depth",
                        TextPosition = new DataPoint(log ? Math.Sqrt(Min[i] * Max[i]) : (Min[i] + Max[i]) / 2, (top + bottom) / 2),
                        Text = Labels[i] + ": " + curve.Missing, TextColor = Colors[i], Stroke = OxyColors.Transparent,
                        Background = OxyColor.FromAColor(220, OxyColors.White),
                        TextHorizontalAlignment = HorizontalAlignment.Center,
                        // Density and neutron share a track: one label above the middle, one below.
                        TextVerticalAlignment = neutron ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    });
                }
                else
                {
                    int below, above, nonPositive;
                    var series = new LineSeries { XAxisKey = "top" + i, YAxisKey = "depth", Color = Colors[i], StrokeThickness = 1 };
                    series.Points.AddRange(Points(depth, curve.Values, Min[i], Max[i], log, maxStep, out below, out above, out nonPositive));
                    model.Series.Add(series);

                    if (below > 0) footer += ", " + Count(below) + " < " + Num(Min[i]);
                    if (above > 0) footer += ", " + Count(above) + " > " + Num(Max[i]);
                    footer = footer.Length == 0 ? "all points within scale" : "drawn at the track edge:" + footer.Substring(1);
                    if (nonPositive > 0) footer += "; " + Count(nonPositive) + " <= 0 not drawn";
                    if (below + above > 0)
                        file.Add(Severity.Observation, "OUT_OF_SCALE", (Concept)i + " <- " + curve.Source.Mnemonic + ": " + below + " below " + Num(Min[i])
                            + " and " + above + " above " + Num(Max[i]) + " " + Units[i] + ", drawn at the track edge",
                            curve.Source.Line, curve.Source.Line, curve.Source.Mnemonic, below + above);
                }

                // The scale on top, this curve's out-of-scale counts below; neutron sits one tier further out.
                model.Axes.Add(TrackAxis(i, "top" + i, AxisPosition.Top, name));
                model.Axes.Add(TrackAxis(i, "bottom" + i, AxisPosition.Bottom, footer));
            }

            string relative = "plots/" + Safe(WellKey(file)) + "/" + Safe(Path.GetFileNameWithoutExtension(file.SourceName)) + ".png";
            // "\\?\" lifts the 260-character path limit: some dataset file names are over 100 characters long.
            string path = @"\\?\" + Path.GetFullPath(Path.Combine(outputFolder, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path))
                new OxyPlot.WindowsForms.PngExporter { Width = Width, Height = Height }.Export(model, stream);
            return relative;
        }

        /// <summary>
        /// The display copy of one curve. Values outside [min, max] are pinned to the track edge and counted; on a log
        /// track values &lt;= 0 become gaps and are counted. NaN stays a gap. A gap is also inserted where the depth jumps
        /// by more than <paramref name="maxStep"/> or turns back (non-monotonic files), so no line crosses either.
        /// </summary>
        public static List<DataPoint> Points(double[] depth, double[] values, double min, double max, bool log, double maxStep,
            out int below, out int above, out int nonPositive)
        {
            below = above = nonPositive = 0;
            var points = new List<DataPoint>(values.Length + 8);
            int direction = 0;
            for (int i = 0; i < values.Length; i++)
            {
                double delta = i == 0 ? 0 : depth[i] - depth[i - 1];
                int step = Math.Sign(delta);
                // The jump itself is not drawn, and the next step sets the new direction.
                if (Math.Abs(delta) > maxStep || (step != 0 && direction != 0 && step != direction))
                {
                    points.Add(DataPoint.Undefined);
                    direction = 0;
                }
                else if (step != 0) direction = step;

                double v = values[i];
                if (log && v <= 0) { nonPositive++; v = double.NaN; }
                else if (v < min) { below++; v = min; }
                else if (v > max) { above++; v = max; }
                points.Add(new DataPoint(v, depth[i]));   // NaN breaks the line: nothing is interpolated
            }
            return points;
        }

        private static Axis TrackAxis(int i, string key, AxisPosition position, string title)
        {
            bool neutron = (Concept)i == Concept.NeutronPorosity;
            Axis axis = (Concept)i == Concept.DeepResistivity ? new LogarithmicAxis() : (Axis)new LinearAxis();
            axis.Key = key;
            axis.Position = position;
            axis.StartPosition = neutron ? Right[i] : Left[i];
            axis.EndPosition = neutron ? Left[i] : Right[i];
            axis.PositionTier = neutron ? 1 : 0;
            axis.Minimum = Min[i];
            axis.Maximum = Max[i];
            axis.Title = title;
            axis.ClipTitle = false;   // a long footer may run into the gap between tracks; never cut it
            axis.TitleColor = axis.TextColor = axis.TicklineColor = Colors[i];
            axis.AxislineStyle = LineStyle.Solid;
            axis.AxislineColor = Colors[i];
            // One set of vertical grid lines per track (from the top scale of the first curve in it).
            if (position == AxisPosition.Top && !neutron)
            {
                axis.MajorGridlineStyle = LineStyle.Solid;
                axis.MajorGridlineColor = OxyColor.FromAColor(40, OxyColors.Black);
            }
            return axis;
        }

        /// <summary>First 10 digits of the UWI or API header (state + county + well), else the file name without extension.</summary>
        public static string WellKey(LasFile file)
        {
            foreach (string mnemonic in new[] { "UWI", "API" })
            {
                HeaderItem item = file.WellItems.FirstOrDefault(h => string.Equals(h.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase));
                if (item == null) continue;
                // Some LAS 1.2 files put the number in the other field ("API . API NUMBER: 42-461-37900").
                foreach (string text in new[] { item.Value, item.Description })
                {
                    string digits = new string(text.Where(char.IsDigit).ToArray());
                    if (digits.Length >= 10) return digits.Substring(0, 10);
                }
            }
            return Path.GetFileNameWithoutExtension(file.SourceName);
        }

        private static string Header(LasFile file, string mnemonic)
        {
            HeaderItem item = file.WellItems.FirstOrDefault(h => string.Equals(h.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase));
            return item == null ? "" : item.Value;
        }

        private static string Safe(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Count(int n)
        {
            return n.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
