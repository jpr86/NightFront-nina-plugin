# NightFront

## 1.0.0.2
- Renamed "NightFront Update" to "Nightly Update" and changed it to run as a sibling immediately before the NightFront Container it populates, instead of as a child inside it. Existing sequences will need the instruction moved out of the container.
- Nightly Update now shows a live status indicator (success/failure and message) in the sequencer.
- Fixed the sequencer palette icon, which previously rendered as the literal text "Te" instead of the NightFront mark.
- NightFront Container now shows a live per-target summary (name, coordinates, rotation, scheduled time window, and sub-frame progress) instead of just an instruction count.
- Nightly Update now writes a `<plan>.metadata.json` file alongside the imported plan, recording the filters and rotation angles used, for future calibration-frame tooling.

## 1.0.0.1
- Initial release