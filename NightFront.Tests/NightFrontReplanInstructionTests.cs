using JeffRidder.NINA.Nightfront.Sequencer;
using System.IO;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontReplanInstructionTests {

        // ── BuildCliOutputPath ────────────────────────────────────────────────────────────────

        [Fact]
        public void BuildCliOutputPath_IsInTheSameFolderAsTheFinalOutputPath() {
            var result = NightFrontReplanInstruction.BuildCliOutputPath(@"C:\NightFrontData", @"C:\NightFrontData\TargetsForTonight_2026-07-06.json");

            Assert.Equal(@"C:\NightFrontData", Path.GetDirectoryName(result));
        }

        [Fact]
        public void BuildCliOutputPath_NeverEqualsTheFinalOutputPath() {
            // The whole point: finalOutputPath routinely already exists on disk (it's tonight's
            // already-imported plan) - the CLI must never be told to write to that same path, or a
            // later File.Exists(cliOutputPath) check can't distinguish "the CLI wrote something new"
            // from "this file was already there before the CLI ran."
            var finalOutputPath = @"C:\NightFrontData\TargetsForTonight_2026-07-06.json";

            var result = NightFrontReplanInstruction.BuildCliOutputPath(@"C:\NightFrontData", finalOutputPath);

            Assert.NotEqual(finalOutputPath, result);
        }

        [Fact]
        public void BuildCliOutputPath_TwoCallsForTheSameFinalOutputPathNeverCollide() {
            var finalOutputPath = @"C:\NightFrontData\TargetsForTonight_2026-07-06.json";

            var first = NightFrontReplanInstruction.BuildCliOutputPath(@"C:\NightFrontData", finalOutputPath);
            var second = NightFrontReplanInstruction.BuildCliOutputPath(@"C:\NightFrontData", finalOutputPath);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void BuildCliOutputPath_IncludesTheFinalOutputPathsBaseNameForReadability() {
            var result = NightFrontReplanInstruction.BuildCliOutputPath(@"C:\NightFrontData", @"C:\NightFrontData\TargetsForTonight_2026-07-06.json");

            Assert.StartsWith("TargetsForTonight_2026-07-06.", Path.GetFileName(result));
        }

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
