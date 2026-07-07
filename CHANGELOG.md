# NightFront

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