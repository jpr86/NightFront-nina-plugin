using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Builds test object graphs by running real plan JSON through NightFrontJsonImporter (same
    // approach as NightFrontCenterAfterDriftCoordinatorTests/NightFrontMetadataRecorderTests) -
    // required so DeepSkyObjectContainer.Target is a real, populated InputTarget (TargetName/
    // InputCoordinates) rather than a mock, and so the container behaves like a genuine
    // ISequenceContainer when it's actually Run (needed for the Execute re-parenting tests, which
    // exercise NightFrontTargetTriggerBase.Execute for real - including running TriggerRunner).
    public class NightFrontTargetTriggerTests {

        private static string BuildTargetJson(string name, double raHours, double decDegrees) {
            var negativeDec = decDegrees < 0;
            return $@"{{
              ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
              ""Name"": ""{name}"",
              ""Target"": {{
                ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
                ""TargetName"": ""{name}"",
                ""PositionAngle"": 0,
                ""InputCoordinates"": {{
                  ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
                  ""RAHours"": {raHours}, ""RAMinutes"": 0, ""RASeconds"": 0,
                  ""NegativeDec"": {negativeDec.ToString().ToLowerInvariant()}, ""DecDegrees"": {decDegrees}, ""DecMinutes"": 0, ""DecSeconds"": 0
                }}
              }},
              ""Items"": {{
                ""$values"": [
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": 0 }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": {{ ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""Ha"" }} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""Gain"": -1, ""Offset"": -1 }}
                ]
              }}
            }}";
        }

        private static string BuildPlanJson(params string[] targetsJson) {
            return $@"{{
              ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
              ""Items"": {{ ""$values"": [ {string.Join(",", targetsJson)} ] }}
            }}";
        }

        private static NightFrontJsonImporter CreateImporter(IProfileService profileService) {
            // See NightFrontCenterAfterDriftCoordinatorTests' identical comment: CenterAndRotate/
            // SwitchFilter/TakeExposure each call their own Validate() eagerly from
            // AfterParentChanged() during import, so their mediators need a real (if minimal)
            // GetInfo() stub rather than a bare Mock.Of<T>().
            var telescopeMediator = new Mock<ITelescopeMediator>();
            telescopeMediator.Setup(m => m.GetInfo()).Returns(new TelescopeInfo { Connected = true });
            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(m => m.GetInfo()).Returns(new RotatorInfo { Connected = true });
            var filterWheelMediator = new Mock<IFilterWheelMediator>();
            filterWheelMediator.Setup(m => m.GetInfo()).Returns(new FilterWheelInfo { Connected = true });
            var cameraMediator = new Mock<ICameraMediator>();
            cameraMediator.Setup(m => m.GetInfo()).Returns(new CameraInfo { Connected = true });

            return new NightFrontJsonImporter(
                profileService,
                cameraMediator.Object,
                Mock.Of<IImagingMediator>(),
                Mock.Of<IImageSaveMediator>(),
                Mock.Of<IImageHistoryVM>(),
                filterWheelMediator.Object,
                Mock.Of<IGuiderMediator>(),
                Mock.Of<IFocuserMediator>(),
                Mock.Of<IAutoFocusVMFactory>(),
                telescopeMediator.Object,
                rotatorMediator.Object,
                Mock.Of<IDomeMediator>(),
                Mock.Of<IDomeFollower>(),
                Mock.Of<IPlateSolverFactory>(),
                Mock.Of<IWindowServiceFactory>(),
                Mock.Of<INighttimeCalculator>(),
                Mock.Of<IFramingAssistantVM>(),
                Mock.Of<IApplicationMediator>(),
                Mock.Of<IPlanetariumFactory>(),
                new List<IDateTimeProvider>());
        }

        private static IProfileService CreateProfileServiceWithFilters(params string[] filterNames) {
            var filters = filterNames.Select(name => new FilterInfo(name, 0, 0)).ToArray();

            var astrometrySettings = new Mock<IAstrometrySettings>();
            astrometrySettings.SetupGet(x => x.Latitude).Returns(45.0);
            astrometrySettings.SetupGet(x => x.Longitude).Returns(-93.0);
            astrometrySettings.SetupGet(x => x.Horizon).Returns((CustomHorizon)null!);

            var filterWheelSettings = new Mock<IFilterWheelSettings>();
            filterWheelSettings.SetupGet(x => x.FilterWheelFilters).Returns(new ObserveAllCollection<FilterInfo>(filters));

            var imageFileSettings = new Mock<IImageFileSettings>();

            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettings.Object);
            profile.SetupGet(x => x.FilterWheelSettings).Returns(filterWheelSettings.Object);
            profile.SetupGet(x => x.ImageFileSettings).Returns(imageFileSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(x => x.ActiveProfile).Returns(profile.Object);

            return profileService.Object;
        }

        private static DeepSkyObjectContainer ImportSingleTarget(string name = "NGC 7000") {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson(name, raHours: 10, decDegrees: 20)));
            return imported.OfType<DeepSkyObjectContainer>().Single();
        }

        // ── ShouldTrigger (Before Target) / IsNightFrontTarget filter ───────────────────────────

        [Fact]
        public void ShouldTrigger_True_ForDeepSkyObjectContainerParentedUnderNightFrontContainer() {
            var dso = ImportSingleTarget();
            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { dso });

            var trigger = new NightFrontBeforeTargetTrigger();

            Assert.True(trigger.ShouldTrigger(null, dso));
            Assert.Equal(dso.Target.TargetName, trigger.TargetName);
        }

        [Fact]
        public void ShouldTrigger_False_ForDeepSkyObjectContainerParentedElsewhere() {
            var dso = ImportSingleTarget();
            var plainContainer = new SequentialContainer();
            plainContainer.Add(dso);

            var trigger = new NightFrontBeforeTargetTrigger();

            Assert.False(trigger.ShouldTrigger(null, dso));
        }

        [Fact]
        public void ShouldTrigger_False_ForNonDeepSkyObjectContainerItem() {
            var container = new NightFrontContainer();
            var otherItem = Mock.Of<ISequenceItem>();
            container.PopulateItems(new[] { otherItem });

            var trigger = new NightFrontBeforeTargetTrigger();

            Assert.False(trigger.ShouldTrigger(null, otherItem));
        }

        [Fact]
        public void ShouldTrigger_False_ForNull() {
            var trigger = new NightFrontBeforeTargetTrigger();

            Assert.False(trigger.ShouldTrigger(null, null));
        }

        // ── ShouldTriggerAfter (After Target) mirrors IsNightFrontTarget on previousItem ────────

        [Fact]
        public void ShouldTriggerAfter_True_ForDeepSkyObjectContainerParentedUnderNightFrontContainer_WithARealNextItem() {
            var previous = ImportSingleTarget("Target A");
            var next = ImportSingleTarget("Target B");
            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { previous, next });

            var trigger = new NightFrontAfterTargetTrigger();

            Assert.True(trigger.ShouldTriggerAfter(previous, next));
        }

        [Fact]
        public void ShouldTriggerAfter_True_WhenNextItemIsNull_TheLastTargetCase() {
            var previous = ImportSingleTarget();
            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { previous });

            var trigger = new NightFrontAfterTargetTrigger();

            Assert.True(trigger.ShouldTriggerAfter(previous, null));
        }

        [Fact]
        public void ShouldTriggerAfter_False_ForDeepSkyObjectContainerParentedElsewhere() {
            var previous = ImportSingleTarget();
            var plainContainer = new SequentialContainer();
            plainContainer.Add(previous);

            var trigger = new NightFrontAfterTargetTrigger();

            Assert.False(trigger.ShouldTriggerAfter(previous, null));
        }

        [Fact]
        public void ShouldTriggerAfter_False_ForNonDeepSkyObjectContainerItem() {
            var container = new NightFrontContainer();
            var otherItem = Mock.Of<ISequenceItem>();
            container.PopulateItems(new[] { otherItem });

            var trigger = new NightFrontAfterTargetTrigger();

            Assert.False(trigger.ShouldTriggerAfter(otherItem, null));
        }

        [Fact]
        public void ShouldTriggerAfter_False_ForNull() {
            var trigger = new NightFrontAfterTargetTrigger();

            Assert.False(trigger.ShouldTriggerAfter(null, null));
        }

        [Fact]
        public void ShouldTrigger_AlwaysFalse_OnTheAfterTargetTrigger() {
            var dso = ImportSingleTarget();
            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { dso });

            var trigger = new NightFrontAfterTargetTrigger();

            Assert.False(trigger.ShouldTrigger(null, dso));
        }

        // ── Execute: re-parents TriggerRunner onto the target while running, restores after ────

        [Fact]
        public async Task Execute_ReparentsTriggerRunnerOntoTheTarget_ThenRestoresTheOriginalParentWhenDone() {
            var dso = ImportSingleTarget();
            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { dso });

            var originalAncestor = Mock.Of<ISequenceContainer>();
            var trigger = new NightFrontBeforeTargetTrigger();
            trigger.AttachNewParent(originalAncestor);

            Assert.True(trigger.ShouldTrigger(null, dso));

            ISequenceContainer parentDuringRun = null;
            var mockItem = new Mock<ISequenceItem>();
            mockItem.SetupProperty(i => i.Status, SequenceEntityStatus.CREATED);
            mockItem
                .Setup(i => i.Run(It.IsAny<IProgress<ApplicationStatus>>(), It.IsAny<CancellationToken>()))
                .Callback(() => {
                    parentDuringRun = trigger.TriggerRunner.Parent;
                    mockItem.Object.Status = SequenceEntityStatus.FINISHED;
                })
                .Returns(Task.CompletedTask);
            trigger.TriggerRunner.Add(mockItem.Object);

            await trigger.Execute(Mock.Of<ISequenceContainer>(), null, CancellationToken.None);

            Assert.Same(dso, parentDuringRun);
            Assert.Same(originalAncestor, trigger.TriggerRunner.Parent);
        }

        [Fact]
        public async Task Execute_RestoresTheOriginalParent_EvenWhenTriggerRunnerThrows() {
            var dso = ImportSingleTarget();
            var container = new NightFrontContainer();
            container.PopulateItems(new ISequenceItem[] { dso });

            var originalAncestor = Mock.Of<ISequenceContainer>();
            var trigger = new NightFrontBeforeTargetTrigger();
            trigger.AttachNewParent(originalAncestor);

            Assert.True(trigger.ShouldTrigger(null, dso));

            // A real (non-mocked) item so NINA's own SequentialStrategy actually enters its inner
            // while loop and hits `token.ThrowIfCancellationRequested()` - an already-cancelled
            // token thrown there propagates as OperationCanceledException all the way out of
            // TriggerRunner.Run(progress, token), unlike a plain item-Execute failure (NINA swallows
            // those internally via its own default ErrorBehavior and never rethrows them to the
            // caller of Run()).
            trigger.TriggerRunner.Add(Mock.Of<ISequenceItem>(i => i.Status == SequenceEntityStatus.CREATED));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exception = await Record.ExceptionAsync(() =>
                trigger.Execute(Mock.Of<ISequenceContainer>(), null, cts.Token));

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.Same(originalAncestor, trigger.TriggerRunner.Parent);
        }

        // ── Validate ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Validate_ReportsAnIssue_WhenNoNightFrontContainerIsReachableAnywhere() {
            var trigger = new NightFrontBeforeTargetTrigger();

            var result = trigger.Validate();

            Assert.False(result);
            Assert.NotEmpty(trigger.Issues);
        }

        [Fact]
        public void Validate_ReportsAnIssue_WhenTheReachableNightFrontContainerIsNotADescendantOfThisTriggersParent() {
            // Mirrors the "Once Safe" misplacement the plan calls out: a NightFrontContainer exists
            // somewhere in the tree (found via FindAnywhere), but this trigger sits in a sibling
            // branch that never actually sees that container's targets execute.
            var nightFrontContainer = new NightFrontContainer();
            var loopWhileSafe = new SequentialContainer();
            loopWhileSafe.Add(nightFrontContainer);

            var onceSafe = new SequentialContainer();
            var root = new SequentialContainer();
            root.Add(loopWhileSafe);
            root.Add(onceSafe);

            var trigger = new NightFrontBeforeTargetTrigger();
            onceSafe.Add(trigger);

            var result = trigger.Validate();

            Assert.False(result);
            Assert.NotEmpty(trigger.Issues);
        }

        [Fact]
        public void Validate_Passes_WhenThisTriggerIsAnAncestorOfTheNightFrontContainer() {
            var nightFrontContainer = new NightFrontContainer();
            var loopWhileSafe = new SequentialContainer();
            loopWhileSafe.Add(nightFrontContainer);

            var trigger = new NightFrontBeforeTargetTrigger();
            loopWhileSafe.Add(trigger);

            var result = trigger.Validate();

            Assert.True(result);
            Assert.Empty(trigger.Issues);
        }

        [Fact]
        public void Validate_Passes_WhenThisTriggerSitsDirectlyOnTheNightFrontContainer() {
            var nightFrontContainer = new NightFrontContainer();
            var trigger = new NightFrontBeforeTargetTrigger();
            nightFrontContainer.Add(trigger);

            var result = trigger.Validate();

            Assert.True(result);
            Assert.Empty(trigger.Issues);
        }
    }
}
