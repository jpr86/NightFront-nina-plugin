# NightFront

## Unreleased
- Fixed CenterAfterDriftTrigger centering on RA 0/Dec 0 for the whole night when placed outside
  the NightFront Container (the production template's own layout, on "Loop while safe") - NINA's
  built-in coordinate inheritance only looks upward from the trigger's own position and never finds
  a target nested below the container. A new NightFrontCenterAfterDriftCoordinator now actively
  pushes each target's live coordinates into any such trigger the moment NINA starts running it,
  mirroring how tcpalmer/nina.plugin.targetscheduler solves the same problem for its own container.

## 1.2.1.0
- Version-only bump to align with NightFront app v1.2.1 (Long Range Planning effort-preset
  values differentiated from a real sweep, entirely on the app side). No plugin-side functional
  change this release.

## 1.2.0.0
- Version-only bump to align with NightFront app v1.2.0 (nav-rail consolidation, long-range plan
  Gantt visualization, and an Active-before-Planned water-filling fix, entirely on the app side).
  No plugin-side functional change this release.

## 1.1.2.0
- Version-only bump to align with NightFront app v1.1.2 (which adds AstroPM target/exposure-plan
  import, entirely on the app side) so the plugin/app version numbers signal compatibility. No
  plugin-side functional change this release.

## 1.1.1.0
- Added "Seeing Trigger," a new sequence trigger: periodically samples a real-time seeing-monitor
  data source (e.g. an Alcor "Current Condition" telemetry page, OCR'd via Tesseract) and, only on
  the transition to a user-configured FWHM threshold, runs whatever instructions you drop into its
  own action container - independent of NightFront's own plan-execution machinery, so dropping a
  Replan instruction inside gets seeing-triggered replanning for free. Re-arms after 2 hours (the
  same live-data horizon a replan trusts a reading for) if the condition stays continuously true,
  so a persistent good/bad spell produces a fresh replan periodically rather than firing only once.
  A new "Seeing Data Source URL" plugin option configures the default data source (overridable
  per-trigger).
- Replan now blends a live seeing reading into the forecast the same way it already blends live
  cloud cover: if a Seeing Trigger elsewhere in the sequence has a fresh sample, its FWHM reading is
  included alongside cloud cover in the live-conditions override handed to the NightFront CLI.
- Fixed NightFront While Same Rotation stopping early and skipping a still-outstanding,
  lower-filter-order-ranked calibration requirement genuinely at the current rotation angle, in
  favor of a higher-ranked filter sitting at a completely different angle. Confirmed against a real
  production metadata file (an "L" and "B" requirement within 0.05deg of each other; a second,
  unrelated "L" requirement ~45deg away jumped the queue ahead of "B" once the first "L" completed).
  NightFront Sky Flats/Trained Flats now also prefer whatever's outstanding at the rotator's current
  physical angle before falling back to the globally-next-best entry.

## 1.1.0.0
- Added "Replan After Safety Recovery," a new sequence instruction for unattended recovery from a
  safety-monitor interruption: place it inside your sequence's own "Once Safe" recovery branch and
  it reads tonight's live progress and current weather, spawns the NightFront CLI to re-solve the
  remainder of the night, and repopulates the NightFront Container with the fresh plan before your
  sequence's native recovery logic restarts execution - so already-completed targets aren't
  silently re-shot after every interruption. Two new plugin options support it: "Replan Effort
  Level" (Fast/Balanced/Thorough GA compute budget, defaulting to Fast since most imaging PCs run
  overnight on modest hardware) and "NightFront CLI Path" (where to find/launch the NightFront
  CLI - note there's no packaged NightFront CLI executable yet, so this must point at a
  user-provided wrapper script for now). Requires an updated NightFront app that also exports the
  new `session-config.json`/`selection.json` sidecars this instruction depends on.
- The previous plan file is now archived to a timestamped copy in a `replan-history` subfolder
  before each replan overwrites it, so it stays available for later comparison instead of being
  silently lost.
- Fixed calibration metadata (measured rotation angle + filter/gain/offset) being recorded as soon as a target's CenterAndRotate finished, before any of its light exposures had actually completed. It's now recorded per filter, right after that filter's own exposure(s) finish - so a target whose sequence stops partway through its filter list still gets correct metadata for whichever filters actually completed, and never records a filter that hasn't been shot yet.
- Replaced the separate `archived.metadata.json` file with completion tracking in the single live metadata file: every calibration requirement now carries a `DateAdded` and a `FlatsCompletedDate` (null until a Sky/Trained Flats instruction completes it). NightFront While Same Rotation/While Calibration Remains and the flats instructions all skip completed entries. A new "Flat Refresh Days" plugin option prunes completed entries older than that many days, so a stale one is naturally recreated (and reshot) the next time its target/filter/rotation combination is encountered.
- Added a "Flat Filter Order" plugin option (comma-separated filter names, e.g. `L, B, G, R, OIII, Ha, SII`) that determines the order Sky Flats, Trained Flats, and their loop conditions claim outstanding calibration requirements in, regardless of the order they were recorded in - useful since twilight sky flats often need specific filters shot while the sky is a particular brightness.

## 1.0.5.0
- Fixed NightFront While Same Rotation and While Calibration Remains showing up with no icon or name in the sequencer's Loop Conditions list - their templates were missing the SequenceBlockView wrapper that draws that chrome, unlike every other item/condition. Added their mini-sequencer templates too.
- NightFront Sky Flats and NightFront Trained Flats now expose their actual editable fields (min/max exposure, histogram mean target/tolerance, dither, amount for Sky Flats; amount for Trained Flats) in the sequencer - previously only the metadata file name was shown, with no way to configure them.
- Bumped the plugin's version numbering to align with the NightFront app's own version scheme.

## 1.0.0.8
- Removed the example FITS-keyword/image-pattern demo left over from the plugin template. It was injecting placeholder `STRKEYWD`/`INTKEYWD`/`DBLKEYWD` header values and a `$$EXAMPLEPATTERN$$` file pattern option into every saved image; none of it was NightFront behavior.
- Removed dead Entity Framework/SQLite configuration from `app.config` carried over from the template; NightFront has no such dependency.
- Fixed a stale `MyPlugin.Properties` namespace left in `Properties/Settings.Settings` (the generated `Settings.Designer.cs` was already `JeffRidder.NINA.Nightfront.Properties`) and cleaned up leftover template comments.

## 1.0.0.7
- Removed the fake random-data weather device/provider left over from the plugin template. It was unrelated to NightFront's plan-import feature.

## 1.0.0.6
- Removed the altitude-chart imaging-tab panel left over from the plugin template. It was unrelated to NightFront's plan-import feature.

## 1.0.0.5
- Fixed NightFront Container turning into an "Unknown Instruction" placeholder after saving and reopening a sequence. It was only MEF-exported as an `ISequenceItem`, so NINA's own sequence-container deserializer (which resolves containers from a separate `ISequenceContainer` export list, e.g. `DeepSkyObjectContainer`) could never find it. Now exported as both.

## 1.0.0.4
- Nightly Update no longer requires the NightFront Container to be its immediate sibling. It now scans forward through its later siblings and their descendant containers, so the Container can be nested several levels deep - e.g. inside a container that loops repeatedly until dawn, while Update itself runs once near the top of the night's sequence.

## 1.0.0.3
- Removed the vestigial "Default Notification Message" and "Profile Specific Notification Message" options left over from the plugin template.
- Removed the remaining misplaced color icon from the NightFront Container block; the sequencer's own chrome already shows the icon and name.
- Set the plugin list's featured icon to the NightFront app icon.
- The `<plan>.metadata.json` calibration file now records each target's *measured* rotator position (read from the rotator right after that target's slew/center/rotate finishes) instead of the plan's input Sky PA, and dedupes filter/rotation-angle pairs within 1 degree of each other.

## 1.0.0.2
- Renamed "NightFront Update" to "Nightly Update" and changed it to run as a sibling immediately before the NightFront Container it populates, instead of as a child inside it. Existing sequences will need the instruction moved out of the container.
- Nightly Update now shows a live status indicator (success/failure and message) in the sequencer.
- Fixed the sequencer palette icon, which previously rendered as the literal text "Te" instead of the NightFront mark.
- NightFront Container now shows a live per-target summary (name, coordinates, rotation, scheduled time window, and sub-frame progress) instead of just an instruction count.
- Nightly Update now writes a `<plan>.metadata.json` file alongside the imported plan, recording the filters and rotation angles used, for future calibration-frame tooling.

## 1.0.0.1
- Initial release