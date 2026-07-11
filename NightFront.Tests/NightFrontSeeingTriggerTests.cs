using JeffRidder.NINA.Nightfront.Sequencer;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontSeeingTriggerTests {

        // ── ShouldFire (pure edge-trigger logic) ────────────────────────────────────────────────

        [Fact]
        public void ShouldFire_FirstSampleAlreadyTrue_DoesNotFire() {
            // wasConditionTrue == null means "no sample evaluated yet" - the very first real sample
            // only establishes the baseline, even if it already reads on the "true" side of the
            // threshold. A plain `bool wasConditionTrue = false` default would wrongly treat this as
            // a fresh false->true crossing.
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: null));
        }

        [Fact]
        public void ShouldFire_FirstSampleAlreadyFalse_DoesNotFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: null));
        }

        [Fact]
        public void ShouldFire_FalseToTrueTransition_Fires() {
            Assert.True(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: false));
        }

        [Fact]
        public void ShouldFire_TrueToTrueStreak_DoesNotFireAgain() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: true));
        }

        [Fact]
        public void ShouldFire_TrueToFalseTransition_DoesNotFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: true));
        }

        [Fact]
        public void ShouldFire_FalseToFalseStreak_DoesNotFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: false));
        }

        [Fact]
        public void ShouldFire_ReCrossingSequence_FiresOnceEachTimeItCrossesUp() {
            // A full sample sequence: baseline true (no fire), drop below (no fire), come back up
            // (fires), stay up (no re-fire), drop again (no fire), come back up again (fires again).
            bool? state = null;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: state));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: state));
            state = false;

            Assert.True(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: state));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: state));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: state));
            state = false;

            Assert.True(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: state));
        }
    }
}
