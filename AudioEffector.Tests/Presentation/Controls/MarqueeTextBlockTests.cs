using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using AudioEffector.Presentation.Controls;
using Xunit;

namespace AudioEffector.Tests.Presentation.Controls;

/// <summary>
/// <see cref="MarqueeTextBlock"/> の単体テストクラス。
/// </summary>
public class MarqueeTextBlockTests
{
    /// <summary>
    /// コンストラクタ初期化時に、既定のプロパティ値およびビジュアル構造が正しく構成されることを検証します。
    /// </summary>
    [Fact]
    public void Constructor_初期化時_既定プロパティとコンテナ構造が正しく設定されること()
    {
        RunInStaThread(() =>
        {
            // Arrange & Act
            var sut = new MarqueeTextBlock();

            // Assert
            Assert.NotNull(sut);
            Assert.False(sut.Focusable);
            Assert.False(sut.IsTabStop);
            Assert.Equal(string.Empty, sut.Text);
            Assert.Equal(40.0, sut.ScrollSpeed);
            Assert.Equal(TimeSpan.FromMilliseconds(500), sut.HoverDelay);
            Assert.Equal(TimeSpan.FromMilliseconds(1200), sut.EndDelay);
            Assert.False(sut.IsHovered);
            Assert.False(sut.IsScrolling);
            Assert.NotNull(sut.Content);
        });
    }

    /// <summary>
    /// 表示枠幅がテキスト幅以上の場合、見切れ判定（IsOverflowing）が false を返すことを検証します。
    /// </summary>
    [Fact]
    public void IsOverflowing_テキスト幅が表示枠幅以下の場合_Falseを返すこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock
            {
                Text = "Short",
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI")
            };

            // Act: 十分に広い幅（500px）をシミュレート
            sut.Measure(new Size(500, 30));
            sut.Arrange(new Rect(0, 0, 500, 30));

            // Assert
            var textWidth = sut.MeasureTextWidth();
            Assert.True(textWidth < 500);
            Assert.False(sut.IsOverflowing());
        });
    }

    /// <summary>
    /// 表示枠幅がテキスト幅未満の場合、見切れ判定（IsOverflowing）が true を返すことを検証します。
    /// </summary>
    [Fact]
    public void IsOverflowing_テキスト幅が表示枠幅を超える場合_Trueを返すこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock
            {
                Text = "This is a very long text that will definitely exceed the narrow available container width",
                FontSize = 14,
                FontFamily = new FontFamily("Segoe UI")
            };

            // Act: 狭い幅（50px）をシミュレート
            sut.Measure(new Size(50, 30));
            sut.Arrange(new Rect(0, 0, 50, 30));

            // Assert
            var textWidth = sut.MeasureTextWidth();
            Assert.True(textWidth > 50);
            Assert.True(sut.IsOverflowing());
        });
    }

    /// <summary>
    /// MeasureTextWidth メソッドにより、テキストに応じた正の描画幅が正しく計測されることを検証します。
    /// </summary>
    [Fact]
    public void MeasureTextWidth_文字列設定時_正の描画幅を計測できること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock
            {
                Text = "Sample Album Title",
                FontSize = 14,
                FontFamily = new FontFamily("Segoe UI")
            };

            // Act
            var width = sut.MeasureTextWidth();

            // Assert
            Assert.True(width > 0);
        });
    }

    /// <summary>
    /// Text プロパティ更新時、内部テキストが正常に更新されることを検証します。
    /// </summary>
    [Fact]
    public void Text変更時_プロパティ更新_新しい値が設定されること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock();

            // Act
            sut.Text = "Updated Track Name";

            // Assert
            Assert.Equal("Updated Track Name", sut.Text);
        });
    }

    /// <summary>
    /// IsHovered プロパティの変更により、外部ホバーフラグが正しく設定されることを検証します。
    /// </summary>
    [Fact]
    public void IsHovered変更時_ホバー状態_プロパティ値が正しく更新されること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock();

            // Act
            sut.IsHovered = true;

            // Assert
            Assert.True(sut.IsHovered);

            // Act: 解除
            sut.IsHovered = false;

            // Assert
            Assert.False(sut.IsHovered);
        });
    }

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw threadEx;
        }
    }
}
