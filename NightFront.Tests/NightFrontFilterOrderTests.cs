using JeffRidder.NINA.Nightfront.Import;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontFilterOrderTests {

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_BlankInput_ReturnsEmptyList(string raw) {
            var result = NightFrontFilterOrder.Parse(raw);

            Assert.Empty(result);
        }

        [Fact]
        public void Parse_CommaSeparatedInput_ReturnsFiltersInOrder() {
            var result = NightFrontFilterOrder.Parse("L,B,G,R,OIII,Ha,SII");

            Assert.Equal(new[] { "L", "B", "G", "R", "OIII", "Ha", "SII" }, result);
        }

        [Fact]
        public void Parse_TrimsWhitespaceAroundEntries() {
            var result = NightFrontFilterOrder.Parse("  L , B ,  G  ");

            Assert.Equal(new[] { "L", "B", "G" }, result);
        }

        [Fact]
        public void Parse_DropsEmptyEntriesFromDoubleCommas() {
            var result = NightFrontFilterOrder.Parse("L,,B,");

            Assert.Equal(new[] { "L", "B" }, result);
        }
    }
}
