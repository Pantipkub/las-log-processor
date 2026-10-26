using LasLogs.Core;
using Xunit;

namespace LasLogs.Tests
{
    public class HeaderItemTests
    {
        // S1, plus real lines from the dataset (report §F.3).
        [Theory]
        [InlineData(" DEPT.F                   00 000 00 00: Depth (MD)", "DEPT", "F", "00 000 00 00", "Depth (MD)")]
        [InlineData("STRT .F      189.0000        :START DEPTH", "STRT", "F", "189.0000", "START DEPTH")]
        [InlineData("TIME. 13:45:00 : Time", "TIME", "", "13:45:00", "Time")]
        [InlineData("GR.GAPI:Gamma", "GR", "GAPI", "", "Gamma")]
        [InlineData(" NULL.              -999.25:", "NULL", "", "-999.25", "")]
        [InlineData(" EKB .F         3000.0              : Elevation: K.B.", "EKB", "F", "3000.0", "Elevation: K.B.")]
        [InlineData(" CUTTYP .  94 930 83 00      :27     Type of Cut: (NONE=0, TRACE=1)", "CUTTYP", "", "94 930 83 00", "27     Type of Cut: (NONE=0, TRACE=1)")]
        [InlineData("DATE.          Mon Apr 23 16:40:23 2007: LOG DATE", "DATE", "", "Mon Apr 23 16:40:23 2007", "LOG DATE")]
        [InlineData(" FL2 .       SURVEY: UNIVERSITY LANDS     : Field Location 2", "FL2", "", "SURVEY: UNIVERSITY LANDS", "Field Location 2")]
        [InlineData(".DT57.USEC/FT                 : Depth Derived Delta T", "", "DT57.USEC/FT", "", "Depth Derived Delta T")]
        [InlineData("WELL .                    WELL:  UNIVERSITY LANDS 3-11#1", "WELL", "", "WELL", "UNIVERSITY LANDS 3-11#1")]
        public void Splits_mnemonic_unit_value_description(string line, string mnemonic, string unit, string value, string description)
        {
            bool hasColon;
            HeaderItem item = HeaderItem.Parse(line, 7, out hasColon);

            Assert.True(hasColon);
            Assert.Equal(mnemonic, item.Mnemonic);
            Assert.Equal(unit, item.Unit);
            Assert.Equal(value, item.Value);
            Assert.Equal(description, item.Description);
            Assert.Equal(7, item.Line);
        }

        [Fact]
        public void Line_without_colon_keeps_the_value()
        {
            bool hasColon;
            HeaderItem item = HeaderItem.Parse("STOP.FT      10361", 6, out hasColon);

            Assert.False(hasColon);
            Assert.Equal("FT", item.Unit);
            Assert.Equal("10361", item.Value);
            Assert.Equal("", item.Description);
        }

        [Fact]
        public void Line_without_dot_is_not_an_item()
        {
            bool hasColon;
            Assert.Null(HeaderItem.Parse("   continued remark text", 1, out hasColon));
        }
    }
}
