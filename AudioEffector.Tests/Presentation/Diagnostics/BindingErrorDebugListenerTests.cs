using System;
using System.IO;
using AudioEffector.Presentation.Diagnostics;
using Xunit;

namespace AudioEffector.Tests.Presentation.Diagnostics;

/// <summary>
/// <see cref="BindingErrorDebugListener"/> の動作を検証する単体テストクラス。
/// デバッグログ出力、メッセージ判定、破棄処理の整合性を検証します。
/// </summary>
public sealed class BindingErrorDebugListenerTests : IDisposable
{
    private readonly string _testLogFile;

    /// <summary>
    /// テスト初期化。テスト用ログファイルパスを設定します
    /// </summary>
    public BindingErrorDebugListenerTests()
    {
        _testLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"test_binding_errors_{Guid.NewGuid():N}.log");
    }

    /// <summary>
    /// テスト終了処理。生成されたテストログファイルを削除します
    /// </summary>
    public void Dispose()
    {
        if (File.Exists(_testLogFile))
        {
            try
            {
                File.Delete(_testLogFile);
            }
            catch
            {
                // テスト後クリーンアップの例外は無視
            }
        }
    }

    /// <summary>
    /// 初期化時にログファイルが生成され、開始ヘッダーが出力されることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_リスナー生成時_ログファイルと初期ヘッダーが出力されること()
    {
        // Arrange & Act
        using var sut = new BindingErrorDebugListener(_testLogFile);

        // Assert
        Assert.True(File.Exists(_testLogFile));
        var content = File.ReadAllText(_testLogFile);
        Assert.Contains("DataBinding Error Trace Started", content);
    }

    /// <summary>
    /// バインディングエラーに該当するメッセージを受信した際、ログファイルに追記されることを検証します。
    /// </summary>
    [Fact]
    public void WriteLine_バインディングエラーメッセージ受信時_ログファイルにメッセージが出力されること()
    {
        // Arrange
        using var sut = new BindingErrorDebugListener(_testLogFile);
        const string errorMessage = "System.Windows.Data Error: 40 : BindingExpression path error: 'NonExistentProp' property not found on 'object'";

        // Act
        sut.WriteLine(errorMessage);

        // Assert
        var logContent = File.ReadAllText(_testLogFile);
        Assert.Contains("NonExistentProp", logContent);
    }

    /// <summary>
    /// 通常の空メッセージを受信した際は無効なログが出力されないことを検証します。
    /// </summary>
    [Fact]
    public void WriteLine_空メッセージ受信時_空行が追加されないこと()
    {
        // Arrange
        using var sut = new BindingErrorDebugListener(_testLogFile);
        var initialLength = new FileInfo(_testLogFile).Length;

        // Act
        sut.WriteLine(string.Empty);
        sut.WriteLine("   ");

        // Assert
        var currentLength = new FileInfo(_testLogFile).Length;
        Assert.Equal(initialLength, currentLength);
    }

    /// <summary>
    /// Dispose呼び出しにより正常にリスナーが解除され、完了ログが記録されることを検証します。
    /// </summary>
    [Fact]
    public void Dispose_リスナー破棄時_完了ヘッダーがログに追記されること()
    {
        // Arrange
        var sut = new BindingErrorDebugListener(_testLogFile);

        // Act
        sut.Dispose();

        // Assert
        var logContent = File.ReadAllText(_testLogFile);
        Assert.Contains("DataBinding Error Trace Stopped", logContent);
    }
}
