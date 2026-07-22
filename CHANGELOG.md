# NightFront

## 1.4.0.0
- Aligns with NightFront app v1.4.0. No C# changed in this plugin, but - like 1.3.4.0 and unlike the
  version-only bumps - the `nightfront-cli.exe` deployed alongside it (what Replan shells out to)
  did, so **Replan produces different schedules this release**. Both of the scheduler's fitness
  objectives were reworked:
  - A filter with nothing left to shoot (Accepted >= Planned) no longer counts toward its target's
    feasible imaging window. Such a filter usually carries no moon-avoidance profile, so it stayed
    "feasible" long after the target's real filters were moon-blocked, manufacturing a window
    nothing could fill and pinning that target's score at zero for every candidate schedule.
  - Targets that cannot be imaged at all tonight - fully moon-blocked, or never high enough - are
    now excluded from the utilization average rather than averaged in as zero. No schedule can
    change whether they are feasible, so they only compressed the score's usable range.
  - The quality axis is now a convex blend of airmass and filter-preference satisfaction, bounded
    to 0-1. It was previously airmass multiplied by a preference boost, which could exceed 1 and
    let a preference rule that matches every hour act as a constant scale factor rather than
    something a schedule is actually rewarded for satisfying. Airmass still dominates; preferences
    nudge and break ties.
- **Scores are not comparable to previous releases.** A schedule that reported utilization 0.33
  before will report roughly 0.43 for the same plan, and quality now reads below 1 rather than
  above it. The numbers moved, not the underlying quality.
- The desktop app also gained a per-filter, per-night feasibility diagnosis (which targets and
  filters can actually be imaged, and why not, on each forecast night). That is app-only - it does
  not affect the plan JSON this plugin imports.

## 1.3.6.0
- Added "Before Target" and "After Target," two new sequence triggers that fire around each
  NightFront-planned target and run whatever instructions you drop into them - e.g. a Ground
  Station send announcing the target starting/finishing. "Before Target" fires just before a
  target's own slew/centering; "After Target" fires once it's done (completed, skipped, or
  failed), including after the very last target of the night. Both re-parent their action
  container onto the firing target for the duration of the run, so Ground Station's own
  `$$TARGET_NAME$$`/`$$TARGET_RA$$`/`$$TARGET_DEC$$` tokens resolve correctly - previously
  impossible from outside the NightFront Container, since Ground Station only looks upward for a
  target and everything NightFront imports lives below the container. Place either on the
  NightFront Container itself or one of its ancestors (e.g. the same branch as "Loop while safe"),
  not a sibling branch such as "Once Safe" - Validate() now warns if it can't see any targets from
  where it's placed.
- Aligns with NightFront app v1.3.6. The app change this release is GUI-only (the GA convergence
  charts on the nightly and long-range plan pages moved into a collapsed "Diagnostics" disclosure at
  the bottom of each page), so the `nightfront-cli.exe` Replan shells out to unchanged behavior.

## 1.3.5.0
- Version-only bump to align with NightFront app v1.3.5. The app change this release is GUI-only
  (a moon-brightness bar added to the nightly-plan forecast display, plus code-review fixes to it),
  so neither this plugin's C# nor the `nightfront-cli.exe` Replan shells out to changed behavior.

## 1.3.4.0
- Aligns with NightFront app v1.3.4. Like 1.3.1.0 - and unlike the version-only bumps below - no
  C# changed, but the `nightfront-cli.exe` deployed alongside this plugin (what Replan shells out
  to) did, so Replan behaves differently this release.
- Replan is now much faster on typical imaging hardware. The "Replan Effort Level" option
  (Fast/Balanced/Thorough) used to map to the desktop app's full GA effort presets - the same
  compute budget used to plan a whole multi-night roadmap. A replan only re-solves the remainder
  of one night, so those budgets were far larger than needed. The CLI now resolves these same three
  names against a dedicated, much smaller replan preset family: the default "Fast" drops from
  pop 100 / gen 200 to pop 60 / gen 50 (roughly a 2-second solve on a modest overnight PC), and
  even "Thorough" is now smaller than the old "Fast." If you'd picked Balanced or Thorough, you get
  the new replan-scaled tier with no plugin edit.
- Fixed Replan being able to schedule Planned (roadmap) targets into tonight's plan. The
  `session-config.json` the app exports carries your whole roadmap, Planned targets included, and
  those default to enabled - so the CLI's replan path was considering them as candidates even
  though the desktop app's nightly plan correctly excludes anything not marked Active. Replan now
  filters to enabled Active targets only, matching the nightly plan.

## 1.3.3.0
- Version-only bump to align with NightFront app v1.3.3 (a re-sweep of the desktop app's GA
  effort-preset values after the to-do #6/#7 changes, entirely on the app side). No plugin-side
  functional change this release - in particular, this did not touch the separate, smaller replan
  effort presets that Replan uses.

## 1.3.2.0
- Version-only bump to align with NightFront app v1.3.2 (Planned/Accepted/Remaining sub-frame
  tracking, completion-% influence on scheduling, AstroPM import fixes, and assorted GUI changes,
  entirely on the app side). No plugin-side functional change this release.

## 1.3.1.0
- Aligns with NightFront app v1.3.1. Unlike the version-only bumps below, this one does change
  plugin behavior, even though no C# changed: the `nightfront-cli.exe` deployed alongside this
  plugin is what Replan shells out to, and its Astrospheric usage was cut substantially.
- Replan is much cheaper to run repeatedly. Every Replan used to buy its own full Astrospheric
  forecast (80 API credits), because each run is a separate process with nothing shared between
  them - so a night that flapped unsafe/safe repeatedly paid for the same forecast over and over,
  unattended. The CLI now costs 65 credits (it no longer requests wind data, which nothing used)
  and caches each site's forecast on disk for 6 hours, matching how often Astrospheric refreshes
  the underlying model. In practice the first fetch of the night is the only one that costs
  anything, and every later Replan reuses it.
- This does not make Replan work off stale weather. The live cloud-cover reading this instruction
  already sends from your weather driver (and the seeing value from a Seeing Trigger, if you have
  one) still overrides the forecast for the hours around the replan - which is the window Replan
  actually schedules into. Only the further-out hours come from the cached forecast.
- Worth knowing if you were watching your credit balance: the NightFront app had long documented a
  "100 credits/day" cap. That figure is from Astrospheric's v1 API and never applied - the app has
  only ever called v2, which allows ~29,900 per month and refreshes on the 1st of the month, not
  daily. A verified live call reads the true remaining balance into the CLI log.

## 1.3.0.0
- Aligns with NightFront app v1.3.0, which puts pressure on completing Active targets in Long
  Range Planning (a new "Planned Target Weight" control, entirely on the app side). Unlike the
  recent version-only bumps below, this release also carries the two plugin-side fixes here.
- Fixed CenterAfterDriftTrigger centering on RA 0/Dec 0 for the whole night when placed outside
  the NightFront Container (the production template's own layout, on "Loop while safe") - NINA's
  built-in coordinate inheritance only looks upward from the trigger's own position and never finds
  a target nested below the container. A new NightFrontCenterAfterDriftCoordinator now actively
  pushes each target's live coordinates into any such trigger the moment NINA starts running it,
  mirroring how tcpalmer/nina.plugin.targetscheduler solves the same problem for its own container.
- Fixed Replan naming its output with the current calendar date instead of the date of the plan it's
  replacing, when run after local midnight. NightFrontApp routinely exports several nights' plan
  files at once, so a date-based folder lookup for "today's plan" doesn't just fail after midnight -
  it can wrongly match a *later* night's already-exported file, since that file's date now matches
  "today" just as validly. NightFrontContainer now remembers the exact file name
  (SourcePlanFileName) it was actually populated from at import/replan time, and Replan uses that
  directly instead of re-deriving it from the folder, so its output (and the
  progress-snapshot/replan-history names derived from it) always matches the plan it's actually
  replacing, keeping that same name across every replan of the same night.

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