---
description: 本プロジェクトにおける単体テストコード作成ルール（4層対称配置、名前空間規則、sut命名、テストメソッド命名規則、日本語XMLコメント付与、モック・非同期方針）
---

# テストコード作成ルール

本プロジェクトにおいてテストコード（`AudioEffector.Tests`）を記述・追加・保守する際は、以下のルールを厳守すること。

---

## 1. テストプロジェクトの構造と名前空間（4層アーキテクチャ対称配置）

### 1.1 ディレクトリ配置の原則
テストコードの配置は、プロダクションコード（`AudioEffector`）のディレクトリ構造と**完全に対称**とします。
プロダクションの各層（`Domain/`, `Application/`, `Infrastructure/`, `Presentation/`）およびそのサブディレクトリに対応する同一のフォルダを `AudioEffector.Tests` 配下に作成し、対応するテストクラスを配置します。

- **ファイル命名規則**: `テスト対象クラス名Tests.cs`

#### 例:
| プロダクションコードのパス | テストコードのパス |
| :--- | :--- |
| `AudioEffector\Domain\Entities\Track.cs` | `AudioEffector.Tests\Domain\Entities\TrackTests.cs` |
| `AudioEffector\Domain\Services\SequentialPlaybackStrategy.cs` | `AudioEffector.Tests\Domain\Services\SequentialPlaybackStrategyTests.cs` |
| `AudioEffector\Application\ApplicationServices\PlaylistApplicationService.cs` | `AudioEffector.Tests\Application\ApplicationServices\PlaylistApplicationServiceTests.cs` |
| `AudioEffector\Infrastructure\Audio\AudioService.cs` | `AudioEffector.Tests\Infrastructure\Audio\AudioServiceTests.cs` |
| `AudioEffector\Presentation\ViewModels\PlaylistViewModel.cs` | `AudioEffector.Tests\Presentation\ViewModels\PlaylistViewModelTests.cs` |
| `AudioEffector\Presentation\Converters\EnumToBoolConverter.cs` | `AudioEffector.Tests\Presentation\Converters\EnumToBoolConverterTests.cs` |
| `AudioEffector\Presentation\Controls\SpectrumBarItem.cs` | `AudioEffector.Tests\Presentation\Controls\SpectrumBarItemTests.cs` |
| `AudioEffector\Presentation\Themes\DarkTheme.xaml` | `AudioEffector.Tests\Presentation\Themes\ThemeResourceTests.cs` |

### 1.2 名前空間（Namespace）の命名規則
テストクラスの名前空間は、**`AudioEffector.Tests` の配下とし、以降は `AudioEffector` のプロジェクト階層構造と同一** とします。

```csharp
// AudioEffector.Tests\Domain\Services\SequentialPlaybackStrategyTests.cs の場合
namespace AudioEffector.Tests.Domain.Services;

public sealed class SequentialPlaybackStrategyTests
{
    // ...
}
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
3. **UI / WPFスレッドの取り扱い**:
   - WPFリソースやコントロール等のテストでSTAスレッドが必要な場合は、STAスレッドヘルパー（`RunInStaThread`）を利用してテストを実行します。

---

## 7. 非同期処理テストの書き方

1. **戻り値**: 非同期処理をテストするメソッドは、必ず **`async Task`** とします（`async void` は禁止）。
2. **競合・遅延の検証**:
   - レースコンディションや最新状態の整合性を検証する際は、`TaskCompletionSource` などを利用して非同期完了タイミングを制御したスタブを用います。
