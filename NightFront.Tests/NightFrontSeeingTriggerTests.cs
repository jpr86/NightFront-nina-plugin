using System;
using System.Collections.Generic;
using System.Linq;
using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontSeeingTriggerTests {

        private static readonly TimeSpan RearmAfter = TimeSpan.FromHours(2);

        // ── ShouldFire (pure edge-trigger logic) ────────────────────────────────────────────────

        [Fact]
        public void ShouldFire_FirstSampleAlreadyTrue_DoesNotFire() {
            // wasConditionTrue == null means "no sample evaluated yet" - the very first real sample
            // only establishes the baseline, even if it already reads on the "true" side of the
            // threshold. A plain `bool wasConditionTrue = false` default would wrongly treat this as
            // a fresh false->true crossing.
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: null, timeSinceFired: null, rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_FirstSampleAlreadyFalse_DoesNotFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: null, timeSinceFired: null, rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_FalseToTrueTransition_Fires() {
            Assert.True(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: true, wasConditionTrue: false, timeSinceFired: null, rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_TrueToTrueStreak_DoesNotFireAgain() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: true, wasConditionTrue: true, timeSinceFired: TimeSpan.FromMinutes(5), rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_TrueToFalseTransition_DoesNotFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: false, wasConditionTrue: true, timeSinceFired: TimeSpan.FromMinutes(5), rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_FalseToFalseStreak_DoesNotFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(conditionTrueNow: false, wasConditionTrue: false, timeSinceFired: null, rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_ReCrossingSequence_FiresOnceEachTimeItCrossesUp() {
            // A full sample sequence: baseline true (no fire), drop below (no fire), come back up
            // (fires), stay up (no re-fire), drop again (no fire), come back up again (fires again).
            bool? state = null;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(true, state, null, RearmAfter));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(false, state, null, RearmAfter));
            state = false;

            Assert.True(NightFrontSeeingTrigger.ShouldFire(true, state, null, RearmAfter));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(true, state, TimeSpan.FromMinutes(30), RearmAfter));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(false, state, TimeSpan.FromMinutes(45), RearmAfter));
            state = false;

            Assert.True(NightFrontSeeingTrigger.ShouldFire(true, state, null, RearmAfter));
        }

        // ── ShouldFire: re-arm after the live-data horizon elapses ─────────────────────────────

        [Fact]
        public void ShouldFire_StillTrueWithinTheHorizon_DoesNotReFire() {
            Assert.False(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: true, wasConditionTrue: true, timeSinceFired: TimeSpan.FromMinutes(90), rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_StillTrueExactlyAtTheHorizon_ReFires() {
            Assert.True(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: true, wasConditionTrue: true, timeSinceFired: RearmAfter, rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_StillTrueWellPastTheHorizon_ReFires() {
            Assert.True(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: true, wasConditionTrue: true, timeSinceFired: TimeSpan.FromHours(5), rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_PastTheHorizonButConditionNowFalse_DoesNotFire() {
            // The horizon expiring doesn't fire by itself - it only re-arms so the *next* true
            // reading can fire, exactly like a real false->true transition would.
            Assert.False(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: false, wasConditionTrue: true, timeSinceFired: TimeSpan.FromHours(5), rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_NoTimeSinceFiredRecorded_NeverForcesAReArm() {
            // wasConditionTrue == true with no firedAtUtc (timeSinceFired == null) happens right
            // after a Clone() or a fresh trigger whose state was seeded some other way - there's no
            // horizon to have expired, so it behaves like an ordinary true-to-true streak.
            Assert.False(NightFrontSeeingTrigger.ShouldFire(
                conditionTrueNow: true, wasConditionTrue: true, timeSinceFired: null, rearmAfter: RearmAfter));
        }

        [Fact]
        public void ShouldFire_PersistentConditionReFiresOnceEveryHorizon() {
            // Simulates seeing staying continuously good for well over two horizons: fires at the
            // initial crossing, then again each time the horizon elapses, never in between. state
            // stays true throughout - only timeSinceFired (measured from the real object's own
            // last-fired timestamp) changes each call.
            bool? state = false;

            Assert.True(NightFrontSeeingTrigger.ShouldFire(true, state, null, RearmAfter));
            state = true;

            Assert.False(NightFrontSeeingTrigger.ShouldFire(true, state, TimeSpan.FromHours(1), RearmAfter));
            Assert.True(NightFrontSeeingTrigger.ShouldFire(true, state, TimeSpan.FromHours(2), RearmAfter));
            // A real object resets its firedAtUtc clock to "now" the instant it re-fires, so the
            // very next check afterward reports a fresh (near-zero) timeSinceFired again.
            Assert.False(NightFrontSeeingTrigger.ShouldFire(true, state, TimeSpan.FromHours(1.9), RearmAfter));
            Assert.True(NightFrontSeeingTrigger.ShouldFire(true, state, TimeSpan.FromHours(2.1), RearmAfter));
        }

        // ── FindMostRecentlySampled ──────────────────────────────────────────────────────────

        [Fact]
        public void FindMostRecentlySampled_TriggerAttachedToAContainersTriggersCollection_IsFound() {
            // A trigger attaches via a container's Triggers collection, not its Items collection -
            // this is the main thing distinguishing this search from NightFrontContainer.FindAnywhere.
            var trigger = new NightFrontSeeingTrigger { LastFwhmArcsec = 1.2, LastSampleTimeUtc = DateTime.UtcNow };

            var rootMock = MockContainer(triggers: new ISequenceTrigger[] { trigger });
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);

            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Same(trigger, result);
        }

        [Fact]
        public void FindMostRecentlySampled_TriggerNestedInsideADescendantContainer_IsFound() {
            var trigger = new NightFrontSeeingTrigger { LastFwhmArcsec = 1.2, LastSampleTimeUtc = DateTime.UtcNow };
            var childMock = MockContainer(triggers: new ISequenceTrigger[] { trigger });
            var rootMock = MockContainer(items: new ISequenceItem[] { childMock.Object });
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);

            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Same(trigger, result);
        }

        [Fact]
        public void FindMostRecentlySampled_MultipleTriggers_ReturnsTheMostRecentlySampledOne() {
            var older = new NightFrontSeeingTrigger { LastFwhmArcsec = 3.0, LastSampleTimeUtc = DateTime.UtcNow.AddMinutes(-30) };
            var newer = new NightFrontSeeingTrigger { LastFwhmArcsec = 1.0, LastSampleTimeUtc = DateTime.UtcNow };

            var rootMock = MockContainer(triggers: new ISequenceTrigger[] { older, newer });
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Same(newer, result);
        }

        [Fact]
        public void FindMostRecentlySampled_TriggerWithNoSuccessfulSampleYet_IsIgnored() {
            var neverSampled = new NightFrontSeeingTrigger();
            var sampled = new NightFrontSeeingTrigger { LastFwhmArcsec = 2.0, LastSampleTimeUtc = DateTime.UtcNow };

            var rootMock = MockContainer(triggers: new ISequenceTrigger[] { neverSampled, sampled });
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Same(sampled, result);
        }

        [Fact]
        public void FindMostRecentlySampled_NoSeeingTriggerAnywhere_ReturnsNull() {
            var rootMock = MockContainer();
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Null(result);
        }

        [Fact]
        public void FindMostRecentlySampled_FromHasNoParent_ReturnsNull() {
            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns((ISequenceContainer)null);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Null(result);
        }

        [Fact]
        public void FindMostRecentlySampled_NestedInsideAnotherSeeingTriggersTriggerRunner_IsFound() {
            // Pathological but not impossible: a Seeing Trigger's own action container nests
            // another Seeing Trigger. FindMostRecentlySampled must still find the inner one.
            var inner = new NightFrontSeeingTrigger { LastFwhmArcsec = 0.8, LastSampleTimeUtc = DateTime.UtcNow };
            var outer = new NightFrontSeeingTrigger();
            outer.TriggerRunner.Add(inner);

            var rootMock = MockContainer(triggers: new ISequenceTrigger[] { outer });
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            var fromMock = new Mock<ISequenceItem>();
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontSeeingTrigger.FindMostRecentlySampled(fromMock.Object);

            Assert.Same(inner, result);
        }

        private static Mock<ISequenceContainer> MockContainer(IEnumerable<ISequenceItem> items = null, IEnumerable<ISequenceTrigger> triggers = null) {
            var mock = new Mock<ISequenceContainer>();
            mock.Setup(c => c.Items).Returns(new List<ISequenceItem>(items ?? Enumerable.Empty<ISequenceItem>()));
            mock.As<ITriggerable>().Setup(t => t.Triggers).Returns(new List<ISequenceTrigger>(triggers ?? Enumerable.Empty<ISequenceTrigger>()));
            return mock;
        }
    }
}
