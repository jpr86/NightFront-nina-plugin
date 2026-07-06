# NightFront

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