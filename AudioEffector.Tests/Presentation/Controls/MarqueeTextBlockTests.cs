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

    /// <summary>
    /// テキストが見切れている状態で IsHovered を true にし、HoverDelay が経過した際に IsScrolling が true になることを検証します。
    /// </summary>
    [Fact]
    public void IsHovered_見切れ状態でホバー時_スクロールが開始されIsScrollingがTrueになること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock
            {
                Text = "Very long long text to cause overflow and scrolling animation",
                HoverDelay = TimeSpan.FromMilliseconds(50),
                FontSize = 14
            };
            sut.Measure(new Size(50, 30));
            sut.Arrange(new Rect(0, 0, 50, 30));

            // Act
            sut.IsHovered = true;

            // DispatcherTimer の発火を待機
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            // Assert
            Assert.True(sut.IsScrolling);

            // Act: ホバー解除
            sut.IsHovered = false;

            // Assert
            Assert.False(sut.IsScrolling);
        });
    }

    /// <summary>
    /// テキストが見切れていない状態で IsHovered を true にしても、IsScrolling は false のままであることを検証します。
    /// </summary>
    [Fact]
    public void IsHovered_見切れていない状態でホバー時_スクロールは開始されずIsScrollingがFalseであること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock
            {
                Text = "Short",
                HoverDelay = TimeSpan.FromMilliseconds(50),
                FontSize = 14
            };
            sut.Measure(new Size(500, 30));
            sut.Arrange(new Rect(0, 0, 500, 30));

            // Act
            sut.IsHovered = true;

            // DispatcherTimer の発火待機
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            // Assert
            Assert.False(sut.IsScrolling);
        });
    }

    /// <summary>
    /// コンストラクタ初期化時に、通常表示用TextBlockとスクロール用Canvas（2連TextBlock構造）が正しく配置されることを検証します。
    /// </summary>
    [Fact]
    public void Constructor_ビジュアルツリー構造_静止用TextBlockと2連スクロール用Canvasが配置されること()
    {
        RunInStaThread(() =>
        {
            // Arrange & Act
            var sut = new MarqueeTextBlock();

            // Assert
            var grid = Assert.IsType<System.Windows.Controls.Grid>(sut.Content);
            Assert.True(grid.ClipToBounds);
            Assert.Equal(2, grid.Children.Count);

            var staticTb = Assert.IsType<System.Windows.Controls.TextBlock>(grid.Children[0]);
            Assert.Equal(TextTrimming.CharacterEllipsis, staticTb.TextTrimming);

            var scrollCanvas = Assert.IsType<System.Windows.Controls.Canvas>(grid.Children[1]);
            Assert.Equal(Visibility.Collapsed, scrollCanvas.Visibility);
            Assert.Equal(2, scrollCanvas.Children.Count);

            var scrollTb1 = Assert.IsType<System.Windows.Controls.TextBlock>(scrollCanvas.Children[0]);
            var scrollTb2 = Assert.IsType<System.Windows.Controls.TextBlock>(scrollCanvas.Children[1]);
            Assert.Equal(TextTrimming.None, scrollTb1.TextTrimming);
            Assert.Equal(TextTrimming.None, scrollTb2.TextTrimming);
        });
    }

    /// <summary>
    /// Text プロパティ更新時、静止用およびスクロール用の全TextBlockに値が反映されることを検証します。
    /// </summary>
    [Fact]
    public void Text変更時_全内部TextBlock_新しいテキストが反映されること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new MarqueeTextBlock();

            // Act
            sut.Text = "TECHNOPOLIS (2018 Bob Ludwig Remaster)";

            // Assert
            var grid = Assert.IsType<System.Windows.Controls.Grid>(sut.Content);
            var staticTb = Assert.IsType<System.Windows.Controls.TextBlock>(grid.Children[0]);
            var scrollCanvas = Assert.IsType<System.Windows.Controls.Canvas>(grid.Children[1]);
            var scrollTb1 = Assert.IsType<System.Windows.Controls.TextBlock>(scrollCanvas.Children[0]);
            var scrollTb2 = Assert.IsType<System.Windows.Controls.TextBlock>(scrollCanvas.Children[1]);

            Assert.Equal("TECHNOPOLIS (2018 Bob Ludwig Remaster)", staticTb.Text);
            Assert.Equal("TECHNOPOLIS (2018 Bob Ludwig Remaster)", scrollTb1.Text);
            Assert.Equal("TECHNOPOLIS (2018 Bob Ludwig Remaster)", scrollTb2.Text);
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
