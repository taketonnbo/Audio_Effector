# MockupRenderer

本番のWPF XAMLとApp.xamlのリソースを読み込み、画面を表示せずPNGを描画する。画面仕様の[画像一覧](captures.json)は36件、[仕様書一覧](../../設計/画面設計/index.md)は26件。

## 再生成

リポジトリルートのPowerShellで実行する。ビルド先を隔離し、デバッグ中のアプリの出力を上書きしない。

```powershell
$uiBuild = Join-Path (Get-Location) '.agent_build/'
dotnet build tools/MockupRenderer/MockupRenderer.csproj -p:OutDir="$uiBuild"
& tools/MockupRenderer/Render-All.ps1
```

単体で生成する場合:

```powershell
& .agent_build/MockupRenderer.exe --view LibraryView --state grid --output "設計/画面設計/アルバム一覧/images/アルバム一覧_グリッド.png" --width 800 --height 500
```

## 引数

| 引数 | 内容 |
| :--- | :--- |
| --view / -v | Views配下のXAMLクラス名。defaultはoveralllayout |
| --state | 固定サンプル状態。default、grid、expanded、favorites、transferring、history、empty、closed、maximized、spectrum、audio、effects、shortcuts、data |
| --xaml / -x | XAMLファイル。--viewより優先 |
| --output / -o | 出力PNGパス。defaultはmockup_output.png |
| --width / -w | ピクセル幅。defaultは1280 |
| --height | ピクセル高。defaultは720 |
| --theme / -t | DarkまたはLight。defaultはDark |
| --help / -h | ヘルプ |

互換名はoveralllayout→MainWindow、sidebar→SidebarControl、playqueue→PlayQueueSidePanel、equalizer→EqualizerView。個別の入力例はcaptures.jsonを参照する。

## 描画の範囲

- SourceViewLoaderが本番XAMLの構造、Style、ControlTemplate、Bindingを読み込む。XAMLのx:Class、デザイン時属性、コードビハインドのイベント接続だけを外す。CLR名前空間とpack URIにはAudioEffectorアセンブリを補う。
- App.xamlのResourceDictionaryをそのまま使い、メイン配下のビューにはMainWindowのリソースとペイン背景を継承させる。テーマ読込失敗はエラー終了とする。
- Windowを論理的な親に保持し、AncestorType=WindowのBindingとフォント／前景の継承を維持する。Window.ShowやアプリのOnStartupは呼ばない。ユーザーのライブラリ、設定、音声デバイスは初期化しない。
- SampleDataは画面構造を生成せず、固定の名称・再生位置・選択状態を供給する。アートは未指定、波形は固定値。defaultは撮影シナリオ名で、アプリの初期設定とは別である。
- サイドバー展開、キューのスライド位置はコードによる切替後の値を設定して撮影する。ShortcutInputBoxは本番クラスに既定のShortcutKeyConfigを設定する。
- Measure／Arrange／UpdateLayout後、Dispatcherで900ms（600msの状態遷移を含む）待ってから再レイアウトし、96dpiで描画する。ルートのMarginは出力寸法から差し引き、末尾のボタン等を切り落とさない。
- OSの非クライアント枠は撮影範囲外。カスタムタイトルバーとXAMLのウィンドウ枠は撮影範囲に含む。画像生成は静的な表示の検証であり、クリックや外部機器への転送の動作テストではない。

## 終了と後始末

STAのDispatcherはfinallyでシャットダウンする。Render-All.ps1は描画失敗時に停止する。検証後は、この作業で作成した `.agent_build` の絶対パスがリポジトリ内であることを確認してから削除する。
