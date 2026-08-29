# Wii Balance Board 実機接続メモ

実機アダプタは以下を前提にしています。

- Nintendo Vendor ID: `0x057E`
- Wii Balance Board Product ID: `0x0306`
- Friendly name: `Nintendo RVL-WBC-01`
- continuous report mode: `0x32`
- sensor byte order: Top Right / Bottom Right / Top Left / Bottom Left
- calibration table: extension register `0xA40024` から 24 bytes
  - 0 kg calibration × 4
  - 17 kg calibration × 4
  - 34 kg calibration × 4
- kg変換: 0→17kg、17→34kg の区間ごとの線形補間
- extension init:
  - `0xA400F0 = 0x55`
  - `0xA400FB = 0x00`

参考:
- https://wiibrew.org/wiki/Wii_Balance_Board
- https://wiibrew.org/wiki/Wiimote
- https://github.com/Nyamochi/WiiFitToVRC/blob/main/docs/BALANCE_BOARD.md

注意:
この実行環境では物理Wii Balance Boardを接続できないため、実機アダプタはコード実装までで、実機検証は未実施です。
