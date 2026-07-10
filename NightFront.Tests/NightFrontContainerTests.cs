using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
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

        // ── FindAnywhere ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void FindAnywhere_ContainerInACompletelySeparateSiblingBranch_IsFound() {
            // Mirrors the maintainer's real production template: the item calling FindAnywhere (a
            // stand-in for NightFrontReplanInstruction) sits inside "Once Safe," a sibling branch of
            // "Loop while safe" (where the real NightFrontContainer lives) - NOT a preceding sibling
            // of the container the way NightFrontUpdateInstruction's FindNext requires. A forward
            // sibling scan from "from"'s own position could never reach across into that other branch.
            var container = new NightFrontContainer();
            var loopWhileSafeMock = MockContainer(container);

            var replanInstructionMock = new Mock<ISequenceItem>();
            var onceSafeMock = MockContainer(replanInstructionMock.Object);

            var rootMock = MockContainer(loopWhileSafeMock.Object, onceSafeMock.Object);
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            loopWhileSafeMock.Setup(c => c.Parent).Returns(rootMock.Object);
            onceSafeMock.Setup(c => c.Parent).Returns(rootMock.Object);
            replanInstructionMock.Setup(i => i.Parent).Returns(onceSafeMock.Object);

            var result = NightFrontContainer.FindAnywhere(replanInstructionMock.Object);

            Assert.Same(container, result);
        }

        [Fact]
        public void FindAnywhere_FromHasNoParent_ReturnsNull() {
            // Either "from" IS the root, or isn't attached to a sequence at all - either way there's
            // nothing to walk up to and nothing to search.
            var from = new Mock<ISequenceItem>();
            from.Setup(i => i.Parent).Returns((ISequenceContainer)null);

            var result = NightFrontContainer.FindAnywhere(from.Object);

            Assert.Null(result);
        }

        [Fact]
        public void FindAnywhere_NoContainerAnywhereInTheTree_ReturnsNull() {
            var fromMock = new Mock<ISequenceItem>();
            var rootMock = MockContainer(Mock.Of<ISequenceItem>(), Mock.Of<ISequenceItem>());
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontContainer.FindAnywhere(fromMock.Object);

            Assert.Null(result);
        }

        [Fact]
        public void FindAnywhere_CyclicContainerGraph_DoesNotStackOverflowAndReturnsNull() {
            var mockA = new Mock<ISequenceContainer>();
            var mockB = new Mock<ISequenceContainer>();
            mockA.Setup(c => c.Items).Returns(new List<ISequenceItem> { mockB.Object });
            mockB.Setup(c => c.Items).Returns(new List<ISequenceItem> { mockA.Object });

            var fromMock = new Mock<ISequenceItem>();
            var rootMock = MockContainer(mockA.Object);
            rootMock.Setup(c => c.Parent).Returns((ISequenceContainer)null);
            fromMock.Setup(i => i.Parent).Returns(rootMock.Object);

            var result = NightFrontContainer.FindAnywhere(fromMock.Object);

            Assert.Null(result);
        }

        private static Mock<ISequenceContainer> MockContainer(params ISequenceItem[] items) {
            var mock = new Mock<ISequenceContainer>();
            mock.Setup(c => c.Items).Returns(new List<ISequenceItem>(items));
            return mock;
        }

        [Fact]
        public void BuildProgressSnapshot_MapsPlannedAndCompletedCountsFromEachTarget() {
            var profileService = CreateProfileService();

            // A target part-way through a 12-exposure filter loop.
            var inProgress = CreateDeepSkyObjectContainer("M 63", profileService);
            var loop = new SequentialContainer();
            loop.Add(new LoopCondition { Iterations = 12, CompletedIterations = 5 });
            inProgress.Add(loop);

            // A target queued for tonight but not yet started - a real filter loop with 0 completed,
            // not an empty container (which would test "has no plan" rather than "hasn't started").
            var notStarted = CreateDeepSkyObjectContainer("LDN 43", profileService);
            var queuedLoop = new SequentialContainer();
            queuedLoop.Add(new LoopCondition { Iterations = 10, CompletedIterations = 0 });
            notStarted.Add(queuedLoop);

            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { inProgress, notStarted });

            var snapshot = container.BuildProgressSnapshot();

            Assert.Equal(2, snapshot.Targets.Count);
            var m63 = snapshot.Targets.Single(t => t.Name == "M 63");
            Assert.Equal(12, m63.PlannedCount);
            Assert.Equal(5, m63.CompletedCount);
            // Neither target's sequence actually executed here (CompletedIterations was set directly,
            // not produced by running the sequence), so Status stays CREATED for both - it's tracked
            // independently of the synthetic progress this test injects.
            Assert.Equal("CREATED", m63.Status);
            var ldn43 = snapshot.Targets.Single(t => t.Name == "LDN 43");
            Assert.Equal(10, ldn43.PlannedCount);
            Assert.Equal(0, ldn43.CompletedCount);
            Assert.Equal("CREATED", ldn43.Status);
        }

        [Fact]
        public void BuildProgressSnapshot_NonTargetTopLevelItem_GetsFallbackRowWithNullCounts() {
            var profileService = CreateProfileService();
            var target = CreateDeepSkyObjectContainer("M 63", profileService);
            var rawItem = Mock.Of<ISequenceItem>(i => i.Name == "Some Raw Item" && i.Status == SequenceEntityStatus.CREATED);

            var container = new NightFrontContainer();
            container.PopulateItems(new[] { (ISequenceItem)target, rawItem });

            var snapshot = container.BuildProgressSnapshot();

            // The raw item still gets a row - its progress is unknown, but it's not silently absent
            // from the snapshot (an absent row would be indistinguishable from "not part of tonight's
            // plan at all").
            Assert.Equal(2, snapshot.Targets.Count);
            var fallbackRow = snapshot.Targets.Single(t => t.Name == "Some Raw Item");
            Assert.Null(fallbackRow.PlannedCount);
            Assert.Null(fallbackRow.CompletedCount);
            Assert.Equal("CREATED", fallbackRow.Status);
        }

        [Fact]
        public void BuildProgressSnapshot_EmptyContainer_ReturnsEmptyTargetsList() {
            var container = new NightFrontContainer();

            var snapshot = container.BuildProgressSnapshot();

            Assert.Empty(snapshot.Targets);
        }

        /// <summary>
        /// Builds a real DeepSkyObjectContainer with no Target set - NightFrontTargetSummary falls
        /// back to the container's own Name in that case (see its constructor), so this avoids ever
        /// touching InputCoordinates/DeepSkyObject.Coordinates, which transitively requires NINA
        /// .Astrometry's native NOVAS31lib.dll (see the note atop NightFrontJsonImporterTests) - not
        /// needed here since progress-snapshot mapping doesn't depend on a target's position.
        /// </summary>
        private static DeepSkyObjectContainer CreateDeepSkyObjectContainer(string name, IProfileService profileService) {
            var dso = new DeepSkyObjectContainer(
                profileService,
                Mock.Of<INighttimeCalculator>(),
                Mock.Of<IFramingAssistantVM>(),
                Mock.Of<IApplicationMediator>(),
                Mock.Of<IPlanetariumFactory>(),
                Mock.Of<ICameraMediator>(),
                Mock.Of<IFilterWheelMediator>());
            dso.Name = name;
            return dso;
        }

        private static IProfileService CreateProfileService() {
            var filter = new FilterInfo("Ha", 0, 0);

            var astrometrySettings = new Mock<IAstrometrySettings>();
            astrometrySettings.SetupGet(x => x.Latitude).Returns(45.0);
            astrometrySettings.SetupGet(x => x.Longitude).Returns(-93.0);
            astrometrySettings.SetupGet(x => x.Horizon).Returns((CustomHorizon)null!);

            var filterWheelSettings = new Mock<IFilterWheelSettings>();
            filterWheelSettings.SetupGet(x => x.FilterWheelFilters).Returns(new ObserveAllCollection<FilterInfo>(new[] { filter }));

            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettings.Object);
            profile.SetupGet(x => x.FilterWheelSettings).Returns(filterWheelSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(x => x.ActiveProfile).Returns(profile.Object);
            return profileService.Object;
        }
    }
}
