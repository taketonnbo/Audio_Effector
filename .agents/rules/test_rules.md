---
description: 本プロジェクトにおける単体テストコード作成ルール（5テストプロジェクト1対1対称配置、名前空間規則、sut命名、テストメソッド命名規則、日本語XMLコメント付与、モック・WPFアニメーション/非同期待機パターン・フレーク対策方針）
---

# テストコード作成ルール

本プロジェクトにおいてテストコード（プロダクションと 1 対 1 対称の 5 テストプロジェクト）を記述・追加・保守する際は、以下のルールを厳守すること。

---

## 1. テストプロジェクトの構造と名前空間（プロダクションとの 1 対 1 対称配置）

### 1.1 プロジェクト・ディレクトリ配置の原則
テストコードは、プロダクションの 5 プロジェクトと **1 対 1 で対称なテストプロジェクト** に配置し、各プロジェクト内のディレクトリ構造もプロダクション側と完全に対称とします。

| テストプロジェクト | 対象本番プロジェクト | TFM / WPF | 並列実行 |
| :--- | :--- | :--- | :--- |
| `AudioEffector.Domain.Tests` | `AudioEffector.Domain` | `net8.0` / なし | 有効（xUnit 既定） |
| `AudioEffector.Application.Tests` | `AudioEffector.Application` | `net8.0` / なし | 有効（xUnit 既定） |
| `AudioEffector.Infrastructure.Tests` | `AudioEffector.Infrastructure` | `net8.0-windows` / なし | 有効（xUnit 既定） |
| `AudioEffector.McpServer.Tests` | `AudioEffector.McpServer` | `net8.0-windows` / なし | 有効（xUnit 既定） |
| `AudioEffector.Tests` | `AudioEffector` (Presentation / 起動) | `net8.0-windows` / **WPF** | 無効（STA 競合防止） |

- **共通テスト設定**: xUnit / Moq / coverlet / `Microsoft.NET.Test.Sdk` / `<Using Include="Xunit" />` / 共通 `NoWarn` / 対称 `<InternalsVisibleTo Include="$(MSBuildProjectName).Tests" />` はソリューションルートの `Directory.Build.props` で一元管理されます。各テストプロジェクトの `.csproj` には固有設定と参照先プロジェクトのみを記述します。
- **層境界の厳守**: 各テストプロジェクトは対応する本番プロジェクトおよびその下位層のみを参照し、上位層や WPF への越境参照を行わないこと。
- **ファイル命名規則**: `テスト対象クラス名Tests.cs`

#### 例:
| プロダクションコードのパス | テストコードのパス |
| :--- | :--- |
| `AudioEffector.Domain\Entities\Track.cs` | `AudioEffector.Domain.Tests\Entities\TrackTests.cs` |
| `AudioEffector.Domain\Services\SequentialPlaybackStrategy.cs` | `AudioEffector.Domain.Tests\Services\SequentialPlaybackStrategyTests.cs` |
| `AudioEffector.Application\ApplicationServices\PlaylistApplicationService.cs` | `AudioEffector.Application.Tests\ApplicationServices\PlaylistApplicationServiceTests.cs` |
| `AudioEffector.Infrastructure\Audio\AudioService.cs` | `AudioEffector.Infrastructure.Tests\Audio\AudioServiceTests.cs` |
| `AudioEffector.McpServer\Tools\LibraryTools.cs` | `AudioEffector.McpServer.Tests\Tools\LibraryToolsTests.cs` |
| `AudioEffector\Presentation\ViewModels\PlaylistViewModel.cs` | `AudioEffector.Tests\Presentation\ViewModels\PlaylistViewModelTests.cs` |
| `AudioEffector\Presentation\Converters\EnumToBoolConverter.cs` | `AudioEffector.Tests\Presentation\Converters\EnumToBoolConverterTests.cs` |
| `AudioEffector\Presentation\Controls\SpectrumBarItem.cs` | `AudioEffector.Tests\Presentation\Controls\SpectrumBarItemTests.cs` |
| `AudioEffector\Presentation\Themes\DarkTheme.xaml` | `AudioEffector.Tests\Presentation\Themes\ThemeResourceTests.cs` |

### 1.2 名前空間（Namespace）の命名規則
テストクラスの名前空間は、**`<テストプロジェクト名>` の配下とし、以降は対応する本番プロジェクト内のディレクトリ階層と同一** とします。

```csharp
// AudioEffector.Domain.Tests\Services\SequentialPlaybackStrategyTests.cs の場合
namespace AudioEffector.Domain.Tests.Services;

public sealed class SequentialPlaybackStrategyTests
{
    // ...
}

// AudioEffector.Tests\Presentation\ViewModels\PlaylistViewModelTests.cs の場合
namespace AudioEffector.Tests.Presentation.ViewModels;
```

---

## 2. テストフレームワークと基本方針

1. **フレームワーク**: **xUnit**（.NET 8）を採用します。
   - 単体ケース: `[Fact]`
   - パラメータ化ケース: `[Theory]` + `[InlineData]` または `[MemberData]`
2. **AAA (Arrange - Act - Assert) パターンの必須適用**:
   - 各テストメソッド内は、準備（Arrange）、実行（Act）、検証（Assert）の3ブロックに構造化します。
   - **それぞれの処理の区切りに、`// Arrange`, `// Act`, `// Assert` のようにコメントを必ず記載してください。**
     - ※各フェーズの詳しい処理内容は必要に応じて記載（必須ではありません）。
     - 例:
       ```csharp
       // Arrange
       var sut = new Track
       {
           SampleRate = 44100,
           BitsPerSample = 16
       };

       // Act
       sut.IsHiRes = sut.SampleRate > 48000 || sut.BitsPerSample > 16;

       // Assert
       Assert.False(sut.IsHiRes);
       ```
3. **独立性と再現性**:
   - テストケース間で状態を共有せず、単体で独立して実行・検証可能であること。
   - 実行順序に依存しない設計とします。

---

## 3. テスト対象のインスタンス名 (`sut`)
テスト対象となるインスタンス（System Under Test）の変数名は、統一して **`sut`** とすること。

```csharp
// Arrange
var sut = new Track
{
    SampleRate = 44100,
    BitsPerSample = 16
};

// Act
sut.IsHiRes = sut.SampleRate > 48000 || sut.BitsPerSample > 16;

// Assert
Assert.False(sut.IsHiRes);
```

---

## 4. テストメソッドの命名規則
テストメソッドの名称は、以下のフォーマットに従って日本語で記述すること。

**`テスト対象の処理_テスト事前条件_期待される結果`**

### 例:
- `QualityLabel_サンプリングレートが48000超の場合_HiResを返す`
- `QualityLabel_拡張子がmp3の場合_Losslessとならないこと`
- `SelectPlaylistAsync_古い読み込みが後から完了する_最新プレイリストの楽曲を維持する`
- `Convert_一致するEnum値_Trueを返す`
- `PropertyChanged_Value変更時_通知が発火し値が更新されること`

---

## 5. 日本語XMLコメントの付与
各テストメソッドには、必ず**日本語のXMLコメント**（`<summary>`タグ）を付与し、そのテストで「何を検証するのか」を明確に記述すること。

```csharp
/// <summary>
/// サンプリングレートとビット深度の組み合わせにより、ハイレゾ音源として正しく判定されるかを検証します。
/// </summary>
[Theory]
[InlineData(96000, 24, true)]
public void IsHiRes判定_サンプリングレートとビット深度の組み合わせ_期待されるHiRes判定結果を返す(int sampleRate, int bitsPerSample, bool expectedIsHiRes)
{
    // ...
}
```

---

## 6. モック・スタブ・フェイク作成方針

1. **インターフェース依存**:
   - 上位層（Application層・Presentation層）のテストでは、依存するリポジトリや外部エンジンなどのインターフェース（例: `ITrackRepository`, `IEventBus`）に対してテスト用のフェイク/スタブクラスを作成して注入します。
   - 外部モックライブラリ（Moq等）に過度に依存せず、インメモリ実装（例: `InMemoryEventBus`, `DelayedTrackRepository`）を活用して軽量・高速かつ型安全なテストを維持します。
2. **具象I/Oの分離**:
   - ファイルI/O、NAudio等の音声デバイス操作、TagLibSharpによるファイルパースなどの具象クラスは単体テストで直接触れず、インターフェースを介して差し替えます。
3. **UI / WPFスレッドの取り扱いとリソース解放・デッドロック防止（必須厳守）**:
   - **プロダクションコード変更の禁止**:
     テストを通しやすくする（テストの都合に合わせる）目的で、本体（プロダクションコード）を安易に変更してはなりません。テスト側でリソース管理・後始末を徹底してください。
   - **STAスレッド用 `Application` の終了モード設定と Dispatcher シャットダウン必須化**:
     - STA スレッド内で `System.Windows.Application` を生成して複数ウィンドウ／コントロールを検証する場合は、`ShutdownMode = ShutdownMode.OnExplicitShutdown` を設定すること（既定の `OnLastWindowClose` のままだと、1 つ目のテスト用ホスト `Window` を `Close()` したタイミングで `Application` が自動終了し、同一スレッド内や後続テストのウィンドウ描画が失敗する原因となる）。
     - あわせて、スレッド終了時に必ず `finally` 句で `System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();` を呼び出して Dispatcher リソースを明示的に解放・シャットダウンすること。これを怠ると、プロセス内に生存しないスレッドに紐づく `Application.Current` / `Dispatcher` が残留し、後続テストでの `dispatcher.Invoke()` が完了せず永久ハング・デッドロックを引き起こします。
     - **参照実装**: `AudioEffector.Tests\Presentation\Views\LyricsViewTests.cs` の `RunInStaThread(Action action)`。
   - **仮想化 `ListBox`（`VirtualizingStackPanel` + `ScrollViewer.CanContentScroll="True"`）の実レンダリング検証パターン**:
     - UI 仮想化を用いる `ListBox` ビューでは、単なる `BindingErrorTraceListener` によるバインディングエラー 0 件検証や添付プロパティ値の確認にとどまらず、実サイズレイアウト（オフスクリーンホスト `Window` での `Show()` および `UpdateLayout()`）上で以下を検証すること。
       - 内部 `ScrollViewer.ViewportHeight` が `ItemsPresenter.Margin` 等のレイアウト設定により圧縮・潰れていないこと（`ListBox.ActualHeight` と概ね一致すること）。
       - 複数行データに対する自動スクロール実行後に、アクティブ行コンテナが `ItemContainerGenerator.ContainerFromIndex(...)` で `null` にならず取得でき、`TransformToAncestor(scrollViewer)` による描画座標がビューポート内に収まっていること（中央スクロール仕様の場合はビューポート中央付近に収まっていること）。
     - **参照実装**: `AudioEffector.Tests\Presentation\Views\LyricsViewTests.cs` の `LyricsView_通常およびコンパクトモードで30行同期歌詞表示時_ViewportHeightが潰れずアクティブ行が中央付近に収まること`。
   - **コマンド活性（`Button.IsEnabled`）の実 WPF 反映検証パターン**:
     - `CommandManager.RequerySuggested` 委譲型のコマンド（`RelayCommand` 等）をバインドする UI 要素は、ViewModel 単体の `CanExecute(...)` 呼び出し検証だけでは `CommandManager.InvalidateRequerySuggested()`（または明示的 `CanExecuteChanged` 発火）の漏れによるボタン非活性固着を検知できない。
     - 実 WPF ホスト `Window` 上にビューを展開した状態で状態遷移（手動スクロール一時停止、イベント購読の接続／解除、ローディング遷移等）を起こし、追加のユーザー入力なしで `UpdateLayout()` 直後に `Button.IsEnabled` が期待通り `true` / `false` へ遷移することを検証すること。
     - **参照実装**: `AudioEffector.Tests\Presentation\Views\LyricsViewTests.cs` の `LyricsView_手動スクロール一時停止およびLRC生成フック接続直後_対応ボタンのIsEnabledが即時Trueになること`。
   - **WPF アニメーション（`AnimationClock` / `Storyboard`）の一時停止・状態反映検証**:
     - **アンチパターン**: `Thread.Sleep` のみや固定間隔ポーリングで「プロパティ値が複数回変化しなかったこと」をもって一時停止完了・静止とみなす手法。並列テスト実行時などの CPU 高負荷下では WPF `TimeManager` ティックが遅延し、`AnimationClock.Controller.Pause()` 適用前の同一フレーム値を複数回読み取って誤判定（フレーク）します。
     - **推奨パターン**: `CompositionTarget.Rendering` イベントを購読して WPF レンダリングフレーム（`TimeManager` ティック）を確実に跨ぐ待機ヘルパーを用います。
       - 必ず `try / finally` で `CompositionTarget.Rendering -= OnRendering;` による購読解除を行うこと。
       - 待機ヘルパーはタイムアウト成否を `bool` で返し、呼び出し側で `Assert.True(...)` により戻り値を検証してサイレント通過を防ぐこと。
       - **参照実装**: `AudioEffector.Tests\Presentation\Views\MiniPlayerWindowTests.cs` の `WaitForRenderFrames(int frameCount, int timeoutMs = 3000)`。
       ```csharp
       private static bool WaitForRenderFrames(int frameCount, int timeoutMs = 3000)
       {
           int observedFrames = 0;
           void OnRendering(object? sender, EventArgs e) => observedFrames++;

           CompositionTarget.Rendering += OnRendering;
           try
           {
               WaitUntil(() => observedFrames >= frameCount, timeoutMs);
               return observedFrames >= frameCount;
           }
           finally
           {
               CompositionTarget.Rendering -= OnRendering;
           }
       }
       ```
     - **Verifier 保護・プロダクションコード非改変との整合**: テスト検証のためだけにプロダクションコードへ安易にテスト専用状態やフックを追加せず、まず `CompositionTarget.Rendering` 等の WPF 標準イベントによるテスト側同期手段を検討すること（[loop_engineering_rules.md](loop_engineering_rules.md) 準拠）。

---

## 7. 非同期処理テストの書き方

1. **戻り値**: 非同期処理をテストするメソッドは、必ず **`async Task`** とします（`async void` は禁止）。
2. **競合・遅延の検証**:
   - レースコンディションや最新状態の整合性を検証する際は、`TaskCompletionSource` などを利用して非同期完了タイミングを制御したスタブを用います。
3. **バックグラウンド起動する IPC サーバ・非同期サービスの起動／停止遷移検証**:
   - **アンチパターン**: `Task.Run` 等でバックグラウンド待受を開始するサーバ（例: `AppIpcServer.Start()`）の起動直後や、`Dispose()` 直後の OS リソース（Named Pipe ハンドル等）解放待ちに対して、短い固定タイムアウト（例: 150ms）でプローブを待機なしに 1 回だけ呼び出すこと。並列テスト実行時などの高 CPU 負荷下では、スレッドプール遅延・初回 JSON シリアライズ遅延・OS ハンドル解放遅延により間欠的に失敗（フレーク）します。
   - **推奨パターン**:
     - 1 回あたりのプローブタイムアウトは実運用相当（例: `NamedPipeIpcReadinessProbe.DefaultConnectTimeout` 相当の `300ms`）とします。
     - 起動中（`true`）および停止後（`false`）への状態遷移は、上限時間付き・短間隔ポーリング（例: 上限 3 秒・25ms 間隔）で期待状態になるまで待機します。通常時は初回一致で即座に返却されるため、テスト総実行時間を悪化させません。
     - **参照実装**: `AudioEffector.McpServer.Tests\Gateways\NamedPipeIpcReadinessProbeTests.cs` の `WaitForReadinessAsync(NamedPipeIpcReadinessProbe probe, bool expected, TimeSpan timeout)`。
     ```csharp
     private static async Task<bool> WaitForReadinessAsync(
         NamedPipeIpcReadinessProbe probe,
         bool expected,
         TimeSpan timeout)
     {
         var sw = Stopwatch.StartNew();
         bool lastResult;
         do
         {
             lastResult = await probe.IsReadyAsync();
             if (lastResult == expected)
             {
                 return lastResult;
             }

             if (sw.Elapsed >= timeout)
             {
                 break;
             }

             await Task.Delay(PollInterval);
         }
         while (sw.Elapsed < timeout);

         return lastResult;
     }
     ```
4. **フレーク修正時の Verifier 保護と再現確認（必須厳守）**:
   - フレーク解消を目的とした **テストケースの削除、`Skip` 属性の付与、アサーション条件の弱体化・コメントアウトは一切禁止** します（[loop_engineering_rules.md](loop_engineering_rules.md) の Verifier 保護に準拠）。
   - 修正後は、対象テストの **単独連続実行** および **`.\tools\verify_loop.ps1 -Full` によるソリューション全体の並列実行** の両方で安定合格することを確認してください。
