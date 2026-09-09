# MockupRenderer (XAML オフスクリーンレンダリング＆画面キャプチャツール)

## 概要
WPF の XAML コードとデザインシステムリソース（`DarkTheme.xaml` 等）を直接解釈し、メモリ上でオフスクリーンレンダリングして実物ピクセルパーフェクトなスクリーンショット（PNG）を自動生成する開発・設計支援CLIツールです。

AI画像生成ツールに依存せず、常に**実際のXAMLレンダリング結果**をUI仕様書へ掲載するために利用します。

## 主な機能
- **STAスレッド駆動のオフスクリーン描画**: GUIウィンドウを物理的に開かずにバックグラウンドで `RenderTargetBitmap` + `PngBitmapEncoder` を実行。
- **本番デザインシステムの自動適用**: `AudioEffector` アセンブリ内のリソース（テーマカラー、ブラシ、ボタンスタイル、スクロールバースタイル等）を動的にマージ。
- **プリセットビュー対応**: 主要な画面構成（全体レイアウト、サイドバー、再生キュー、EQ等）の即時生成。
- **外部XAMLファイル指定対応**: 任意のXAMLファイルを指定して、その場でプレビューPNGを出力可能。

## コマンド一覧・オプション

```bash
dotnet run --project tools/MockupRenderer/MockupRenderer.csproj -- [options]
```

| オプション | 短縮 | デフォルト値 | 説明 |
|---|---|---|---|
| `--view` | `-v` | `overalllayout` | レンダリング対象のプリセットビュー名 (`overalllayout`, `sidebar`, `playqueue`, `equalizer`) |
| `--xaml` | `-x` | (なし) | レンダリング対象の外部XAMLファイルパス（指定時は `--view` より優先） |
| `--output` | `-o` | `mockup_output.png` | 出力先 PNG 画像ファイルパス |
| `--width` | `-w` | `1280` | 生成画像の横幅 (px) |
| `--height` | (なし) | `720` | 生成画像の縦幅 (px) |
| `--theme` | `-t` | `Dark` | テーマ (`Dark` または `Light`) |
| `--help` | `-h` | - | ヘルプメッセージを表示 |

## 実行例

### 1. 全体レイアウトモックアップの生成
```bash
dotnet run --project tools/MockupRenderer/MockupRenderer.csproj -- --view overalllayout --output "設計/画面設計/全体レイアウト・ペイン構成/images/overall_layout_mockup.png" --width 1280 --height 720
```

### 2. サイドバー単体キャプチャの生成
```bash
dotnet run --project tools/MockupRenderer/MockupRenderer.csproj -- --view sidebar --output "設計/画面設計/サイドバー/images/sidebar_mockup.png" --width 240 --height 720
```

### 3. 任意のXAMLファイルをレンダリング
```bash
dotnet run --project tools/MockupRenderer/MockupRenderer.csproj -- --xaml "path/to/MyView.xaml" --output "output.png" --width 800 --height 600
```
