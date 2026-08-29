# Therabby Load Balance Viewer v0.3

WindowsネイティブのWii Balance Board Viewerです。

## v0.3の構造

- 外部ブラウザ：使用しません
- localhost：使用しません
- Python：使用しません
- UI：アプリ内のMicrosoft Edge WebView2
- Wii Balance Board通信：C# / Windows HID
- ZERO / CENTER / CSVログ：C#側
- UI表示：HTML / CSS / JavaScript
- UI更新：約30fps
- センサーログ：UI平滑化前のデータを保存

## 初回ビルド

`build-and-run.bat` をダブルクリックしてください。

必要：
- Windows 10 / 11
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

.NET 8 SDKが入っていれば、NuGetからMicrosoft.Web.WebView2を取得し、
ビルド後にアプリを自動起動します。

## 2回目以降

以下を直接起動できます。

`bin\Release\net8.0-windows\Therabby.LoadBalanceViewer.exe`

## 配布用EXEを作る

`publish-win-x64.bat`

を実行してください。

`publish\Therabby.LoadBalanceViewer.exe`

が生成されます。

WebView2 RuntimeはWindows側に必要です。
Windows 11では通常インストール済みです。

## 起動時

v0.3はREAL WBBを既定モードにしています。
起動直後、一度だけ自動接続を試みます。

接続できない場合：
1. WindowsでWii Balance BoardをBluetoothペアリング
2. ボードのPOWERを押す
3. 「Wii Balance Boardを接続」を押す

## ZERO

ボードから完全に降りた状態で実行します。
約1秒分を平均してゼロ基準にします。

## SET CENTER

ボード上に立った状態で実行します。
現在のCoPを個人内の基準位置として登録します。

## LOG

ログはEXEと同じ場所の `logs` フォルダに保存します。

- sensor_log.csv
- session.json

画面表示にはEMA平滑化を使いますが、
CSVは表示用平滑化前のZERO補正済み値を保存します。

## v0.3で改善した点

- WinFormsの複雑なパネル配置を廃止
- Web版v0.1のUIをWebView2内へ移植
- 外部ブラウザ・localhostを廃止
- UIとセンサー処理を完全分離
- UIは30fpsに制限
- 表示のみEMA平滑化
- ログは未平滑化データ
- 初期ウィンドウ1440×900
- 最小1120×720
- REAL WBBを既定モード
- 自動接続を一度試行
- Mockは必要な時だけ表示

## 医療用途

本ソフトウェアは医療機器ではありません。
表示値は評価・観察・練習を補助する情報として使用してください。
