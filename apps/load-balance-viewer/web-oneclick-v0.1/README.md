# Therabby Load Balance Viewer — WebHID One Click版

このディレクトリは、2026-08-29時点で動作確認したブラウザ版 Load Balance Viewer の保存スナップショットです。

## 起動

Windowsで `START_LOAD_BALANCE_VIEWER.bat` をダブルクリックします。

- .NET SDK 不要
- Python 不要
- URL手入力不要
- Windows標準のPowerShellでローカルサーバーを起動
- Edge（なければChrome）を自動起動
- WebHIDでWii Balance Boardへ接続

終了時にローカルサーバーを止める場合は `STOP_LOAD_BALANCE_VIEWER.bat` を実行します。

## 主な機能

- Wii Balance Board 4点荷重（LF / RF / LB / RB）
- 総荷重
- 左右荷重比
- 前後荷重比
- 正規化CoP表示
- ZERO
- SET CENTER / Relative CoP
- CSV + session.json ログ
- Mock Device
- REAL WBB / WebHID

## 実機接続

Windows側でWii Balance BoardをBluetoothペアリングし、Edge / Chromeで `REAL WBB` → `Wii Balance Boardを接続` を選択します。

WebHIDの仕様上、初回のデバイス接続許可はユーザー操作が必要です。

## 注意

本ソフトウェアは医療機器ではありません。表示値は評価・観察・練習を補助する情報として使用してください。

Snapshot: `web-oneclick-v0.1` / 2026-08-29
