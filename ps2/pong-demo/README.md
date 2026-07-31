# Therabby Pong Demo for PlayStation 2

PS2実機で動作させる、シンプルなPONG型ホームブリューです。CRTの表示領域を考慮し、画面端から余白を取っています。

## 操作

### タイトル画面

- `×`：開始
- `△`：1人用（CPU戦）／2人用を切り替え
- `← / →`：ボール速度を3段階で変更
- `□`：パドルの大きさを3段階で変更

### プレイ中

- プレイヤー1：方向キー上下、または左スティック上下
- プレイヤー2：コントローラ端子2の方向キー上下、または左スティック上下
- `START`：一時停止／再開
- `SELECT`：タイトルへ戻る

7点先取です。

## MX4SIOへの配置

GitHub Actionsの成果物を展開し、`APPS`フォルダをmicroSDカードのルートへコピーします。

```text
microSD:/
└─ APPS/
   └─ THERABBY_PONG/
      ├─ THERABBY_PONG.ELF
      └─ title.cfg
```

OPLでMX4SIOのアプリ表示を有効にし、`Therabby Pong Demo`を選択します。

## ローカルビルド

PS2DEV、PS2SDK、gsKitが必要です。

```bash
make clean
make
make dist
```

生成物：`THERABBY_PONG.ELF`

## 現段階の位置づけ

臨床研究用の完成評価ツールではなく、PS2上でのホームブリュー実行、入力、速度・難易度調整を検証するMVPです。測定ログの保存機能はありません。
