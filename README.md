# NightFront (NINA Plugin)

A [N.I.N.A.](https://nighttime-imaging.eu/) plugin that ingests the nightly plans produced by the
[NightFront](https://github.com/jpr86/NightFront) desktop app and drives their execution inside
your Advanced Sequencer sequence.

NightFront itself — the forecast-aware, multi-night genetic-algorithm optimizer — lives entirely
in the companion desktop app. This plugin is primarily the execution half: it reads the JSON plan
the app exports, builds real NINA sequence items from it, and lets NINA run them exactly as it
would run any hand-built sequence. It's not purely passive, though — it can respond to real
conditions mid-night by invoking the same optimizer for a limited replan scoped to the remainder
of that night's schedule, rather than only ever replaying a plan decided before dusk.

## How it fits into a sequence

1. Add a **Nightly Update** instruction near the start of the night. It looks in your configured
   NightFront data folder for today's plan file and populates a **NightFront Container** placed
   later in the same sequence branch (it doesn't need to be an immediate sibling — it can be
   nested inside a repeating imaging loop).
2. The container runs like any other part of the sequence: slew/center/rotate, filter changes,
   exposures, dithers, autofocus, meridian flips — all native NINA instructions, no custom
   execution engine.
3. As each target's `CenterAndRotate` completes, the plugin records the *measured* rotator
   position paired with that target's filters into a metadata file, so you can later loop over
   it for matching trained/sky flats without redundant re-shoots.
4. An optional **Replan** instruction, placed in a safety-recovery branch (e.g. `Once Safe`),
   snapshots progress, reads live cloud cover from NINA's own (ASCOM-backed) weather data
   mediator, and re-invokes NightFront's optimizer for the remainder of the night, swapping the
   updated plan into the running container.
5. An independent **Seeing Trigger** polls a real seeing-monitor data source in the background
   and fires whatever action you drop into it — typically a Replan — the moment a live reading
   actually crosses your threshold. It's edge-triggered (fires on the transition, not on every
   sample) with a re-arm window, so a persistently good or bad spell still produces a fresh
   replan periodically rather than firing once and going quiet.
6. **Before Target** and **After Target** triggers fire right around each target's own
   slew/centering — "before" just ahead of it, "after" once the target is done (however it
   ended), including after the night's last target. Drop in whatever you like, such as a Ground
   Station send announcing the target starting or finishing; `$$TARGET_NAME$$`/`$$TARGET_RA$$`/
   `$$TARGET_DEC$$` resolve correctly because the trigger's action container is re-parented onto
   the live target for the duration of the run.

## Relationship to NINA Target Scheduler

[The NINA Target Scheduler](https://tcpalmer.github.io/nina-scheduler/) is a dispatch
scheduler: its Scoring Engine picks the next target/exposure plan every exposure using weighted
rules — project priority, percent complete, meridian windows, target-switch penalty, moon
avoidance timing, and similar. That scoring runs continuously, but strictly over project/target
database state and target geometry — it has no live weather or seeing input, so for a given
moment and database state the outcome is effectively pre-determined; it can't actually react to
the sky changing.

This plugin's job, by contrast, is to execute a plan the NightFront desktop app already optimized
against a genuine multi-day weather forecast — and then, unlike Target Scheduler, to keep that
plan honest against *real* live conditions during the night:

- **Replan** reads live cloud cover from NINA's own (ASCOM-backed) weather data mediator when
  placed in a safety-recovery branch, and re-invokes the optimizer for the remainder of the
  night.
- **Seeing Trigger** independently polls a real seeing-monitor data source and fires whatever
  action you drop into it — typically a Replan — the moment a live reading actually crosses your
  threshold, not on a fixed schedule. It's edge-triggered (fires on the transition, not on every
  sample) with a re-arm window so a persistently good/bad spell still produces a fresh replan
  periodically rather than firing once and going quiet.

If you want per-exposure decisions driven by project priority and target geometry with no
weather awareness at all, Target Scheduler is the better fit. If you want a plan built against an
actual forecast up front, with genuine live-condition replanning (real cloud cover, real seeing)
when the sky itself changes mid-night, that's what NightFront is for.

## Requirements

- N.I.N.A. 3.0+
- A NightFront desktop app export folder (JSON sequence plans, `session-config.json`, and
  progress/metadata sidecars)

## Build & Test

```bash
dotnet build NightFront.slnx
dotnet test NightFront.Tests/NightFront.Tests.csproj
```

A post-build step copies the built plugin into your local NINA plugin folder
(`%localappdata%\NINA\Plugins\3.0.0\...`) automatically. Some importer tests require NINA's
`NOVAS31lib.dll`, which only exists on a machine with a real NINA install.

## See also

- [NightFront desktop app](https://github.com/jpr86/NightFront) — the actual scheduler; this
  plugin is its NINA-side execution component.
