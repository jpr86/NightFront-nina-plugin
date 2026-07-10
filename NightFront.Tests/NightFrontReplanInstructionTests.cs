using JeffRidder.NINA.Nightfront.Sequencer;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontReplanInstructionTests {

        [Fact]
        public void BuildReplanArguments_NoSelectionFile_OmitsTheOptionalTrailingArgument() {
            var args = NightFrontReplanInstruction.BuildReplanArguments(
                "Fast", "config.json", "progress.json", "none", "output.json", selectionPathOrNull: null);

            Assert.Equal(new[] { "replan", "--effort=Fast", "config.json", "progress.json", "none", "output.json" }, args);
        }

        [Fact]
        public void BuildReplanArguments_WithSelectionFile_AppendsItLast() {
            var args = NightFrontReplanInstruction.BuildReplanArguments(
                "Balanced", "config.json", "progress.json", "weather.json", "output.json", "selection.json");

            Assert.Equal(
                new[] { "replan", "--effort=Balanced", "config.json", "progress.json", "weather.json", "output.json", "selection.json" },
                args);
        }

        [Fact]
        public void BuildReplanArguments_EffortLevelIsPassedThroughVerbatimAsAnEffortFlag() {
            // NightFront's own EffortPreset.parse() is case-insensitive and matches by name or
            // label, so this instruction doesn't need to normalize casing itself - whatever the
            // ReplanEffortLevel setting holds ("Fast"/"Balanced"/"Thorough") is passed straight
            // through.
            var args = NightFrontReplanInstruction.BuildReplanArguments(
                "Thorough", "c", "p", "none", "o", null);

            Assert.Contains("--effort=Thorough", args);
        }

        [Fact]
        public void BuildReplanArguments_ArgumentOrderMatchesNightFrontsReplanUsage() {
            // Main.kt's REPLAN_USAGE: replan [--effort=...] <config> <progress> <weather|none>
            // <output> [selection]. The --effort flag's position doesn't matter to NightFront's own
            // parser (it's stripped out wherever it appears), but keeping it first here matches
            // REPLAN_USAGE's documented ordering for anyone reading a captured command line.
            var args = NightFrontReplanInstruction.BuildReplanArguments(
                "Fast", "CONFIG", "PROGRESS", "WEATHER", "OUTPUT", "SELECTION");

            Assert.Equal("replan", args[0]);
            Assert.Equal("--effort=Fast", args[1]);
            Assert.Equal("CONFIG", args[2]);
            Assert.Equal("PROGRESS", args[3]);
            Assert.Equal("WEATHER", args[4]);
            Assert.Equal("OUTPUT", args[5]);
            Assert.Equal("SELECTION", args[6]);
            Assert.Equal(7, args.Count);
        }
    }
}
