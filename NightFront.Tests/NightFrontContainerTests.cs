using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontContainerTests {

        [Fact]
        public void NightFrontContainer_IsExportedAsBothSequenceItemAndSequenceContainer() {
            // NINA deserializes a saved sequence's containers via a completely separate MEF export
            // list than plain items (see NINA.Sequencer.SequencerFactory.GetContainer<T>(), which only
            // looks at ISequenceContainer exports). Missing the ISequenceContainer export here means
            // NightFrontContainer round-trips as an "Unknown Instruction" placeholder after save/reload,
            // even though it still works fine when dragged in fresh from the palette (which only needs
            // the ISequenceItem export).
            var exportedContracts = typeof(NightFrontContainer)
                .GetCustomAttributes(typeof(ExportAttribute), inherit: false)
                .Cast<ExportAttribute>()
                .Select(a => a.ContractType)
                .ToList();

            Assert.Contains(typeof(ISequenceItem), exportedContracts);
            Assert.Contains(typeof(ISequenceContainer), exportedContracts);
        }

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

        [Fact]
        public void FindNext_ContainerNestedInsideFollowingSibling_ReturnsIt() {
            var after = Mock.Of<ISequenceItem>();
            var container = new NightFrontContainer();
            var loopContainer = MockContainerWithItems(container);
            var siblings = new List<ISequenceItem> { after, loopContainer };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Same(container, result);
        }

        [Fact]
        public void FindNext_ContainerNestedSeveralLevelsDeep_ReturnsIt() {
            // Mirrors todos/NightFront.json: Update's sibling ("Do Imaging") isn't the container
            // itself, but a container ("Imaging") nested inside it is.
            var after = Mock.Of<ISequenceItem>();
            var container = new NightFrontContainer();
            var innerContainer = MockContainerWithItems(container);
            var outerContainer = MockContainerWithItems(innerContainer);
            var siblings = new List<ISequenceItem> { after, outerContainer };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Same(container, result);
        }

        [Fact]
        public void FindNext_EmptyFollowingSiblingSubtree_SearchesLaterSiblings() {
            var after = Mock.Of<ISequenceItem>();
            var container = new NightFrontContainer();
            var emptyContainer = MockContainerWithItems();
            var siblings = new List<ISequenceItem> { after, emptyContainer, container };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Same(container, result);
        }

        [Fact]
        public void FindNext_ContainerIsItsOwnDescendant_DoesNotStackOverflowAndReturnsNull() {
            var after = Mock.Of<ISequenceItem>();
            var mock = new Mock<ISequenceContainer>();
            var items = new List<ISequenceItem>();
            mock.Setup(c => c.Items).Returns(items);
            items.Add(mock.Object);
            var siblings = new List<ISequenceItem> { after, mock.Object };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Null(result);
        }

        [Fact]
        public void FindNext_MutuallyReferencingContainers_DoesNotStackOverflowAndReturnsNull() {
            var after = Mock.Of<ISequenceItem>();
            var mockA = new Mock<ISequenceContainer>();
            var mockB = new Mock<ISequenceContainer>();
            mockA.Setup(c => c.Items).Returns(new List<ISequenceItem> { mockB.Object });
            mockB.Setup(c => c.Items).Returns(new List<ISequenceItem> { mockA.Object });
            var siblings = new List<ISequenceItem> { after, mockA.Object };

            var result = NightFrontContainer.FindNext(siblings, after);

            Assert.Null(result);
        }

        private static ISequenceContainer MockContainerWithItems(params ISequenceItem[] items) {
            var mock = new Mock<ISequenceContainer>();
            mock.Setup(c => c.Items).Returns(new List<ISequenceItem>(items));
            return mock.Object;
        }
    }
}
