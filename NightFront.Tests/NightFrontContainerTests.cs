using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Sequencer.SequenceItem;
using System.Collections.Generic;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontContainerTests {

        [Fact]
        public void FindNext_ContainerImmediatelyAfter_ReturnsIt() {
            var after = Mock.Of<ISequenceItem>();
            var container = new NightFrontContainer();
            var siblings = new List<ISequenceItem> { after, container };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Same(container, result);
        }

        [Fact]
        public void FindNext_ContainerSeveralSiblingsLater_ReturnsIt() {
            var after = Mock.Of<ISequenceItem>();
            var container = new NightFrontContainer();
            var siblings = new List<ISequenceItem> { after, Mock.Of<ISequenceItem>(), Mock.Of<ISequenceItem>(), container };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Same(container, result);
        }

        [Fact]
        public void FindNext_NoContainerFollowing_ReturnsNull() {
            var after = Mock.Of<ISequenceItem>();
            var siblings = new List<ISequenceItem> { after, Mock.Of<ISequenceItem>() };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Null(result);
        }

        [Fact]
        public void FindNext_ContainerBeforeAfter_IsIgnored() {
            var container = new NightFrontContainer();
            var after = Mock.Of<ISequenceItem>();
            var siblings = new List<ISequenceItem> { container, after };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Null(result);
        }

        [Fact]
        public void FindNext_AfterNotInSiblingList_ReturnsNull() {
            var after = Mock.Of<ISequenceItem>();
            var siblings = new List<ISequenceItem> { new NightFrontContainer() };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Null(result);
        }
    }
}
