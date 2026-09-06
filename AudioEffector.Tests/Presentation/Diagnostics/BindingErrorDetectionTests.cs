using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Xunit;

namespace AudioEffector.Tests.Presentation.Diagnostics;

/// <summary>
/// WPFデータバインディングエラー自動検出機構（<see cref="BindingErrorTraceListener"/>）の
/// エラー捕捉能力および検証機能を検証するテストクラス。
/// </summary>
public sealed class BindingErrorDetectionTests
{
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
                // STAスレッド終了前にDispatcherをシャットダウンし、
                // 後続テストでdispatcher.Invoke()がハングするのを防止する
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

    private sealed class DummyTarget
    {
        public string ValidProperty => "ValidValue";
    }

    /// <summary>
    /// 存在しないプロパティへのバインディング式を評価した際、BindingErrorTraceListener が
    /// 正確にエラーメッセージを捕捉し HasErrors が true になることを検証します。
    /// </summary>
    [Fact]
    public void TraceListener_不正なプロパティバインディング評価時_エラーを正確に検出すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var sut = new BindingErrorTraceListener();

            var textBlock = new TextBlock();
            var binding = new Binding("NonExistentPropertyOnDummy")
            {
                Source = new DummyTarget()
            };
            textBlock.SetBinding(TextBlock.TextProperty, binding);

            // Act
            var expr = BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty);
            expr?.UpdateTarget();

            var window = new Window { Content = textBlock };
            window.Measure(new Size(100, 100));
            window.Arrange(new Rect(0, 0, 100, 100));
            window.UpdateLayout();

            // Assert
            Assert.True(sut.HasErrors, $"意図的なバインディングエラーが検知されるべきです。検出件数: {sut.ErrorCount}");
            Assert.Contains(sut.Errors, err => err.Contains("NonExistentPropertyOnDummy"));
        });
    }

    /// <summary>
    /// 正常なプロパティへのバインディング式を評価した際、BindingErrorTraceListener が
    /// エラーを報告せず HasErrors が false を維持することを検証します。
    /// </summary>
    [Fact]
    public void TraceListener_正常なプロパティバインディング評価時_エラーが検出されないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var sut = new BindingErrorTraceListener();

            var textBlock = new TextBlock();
            var binding = new Binding("ValidProperty")
            {
                Source = new DummyTarget()
            };
            textBlock.SetBinding(TextBlock.TextProperty, binding);

            // Act
            var expr = BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty);
            expr?.UpdateTarget();

            var window = new Window { Content = textBlock };
            window.Measure(new Size(100, 100));
            window.Arrange(new Rect(0, 0, 100, 100));
            window.UpdateLayout();

            // Assert
            Assert.False(sut.HasErrors, $"正常なバインディングではエラーが検出されてはなりません。検出内容: {string.Join("; ", sut.Errors)}");
            Assert.Equal(0, sut.ErrorCount);
        });
    }
}
