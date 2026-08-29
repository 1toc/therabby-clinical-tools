# Wii Balance Board implementation notes

v0.3 uses the same Windows HID approach as v0.2.

- Nintendo Vendor ID: 0x057E
- Product IDs accepted: 0x0306 / 0x0330
- Input report: 0x32
- Sensor report order:
  - Top Right
  - Bottom Right
  - Top Left
  - Bottom Left
- Factory calibration:
  - 0 kg
  - 17 kg
  - 34 kg
- Extension initialization:
  - 0xA400F0 = 0x55
  - 0xA400FB = 0x00

Architecture change in v0.3:
- HID acquisition is native C#.
- WebView2 receives only display state.
- UI rendering cannot block or crash acquisition.
- Logger stores unsmoothed ZERO-adjusted values.
- Display uses EMA alpha 0.26.
