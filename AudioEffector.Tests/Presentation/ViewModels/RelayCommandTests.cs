using System;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="RelayCommand"/> のコマンド実行および実行可否判定を検証するテストクラス。
/// </summary>
public class RelayCommandTests
{
    /// <summary>
    /// パラメータ付きコンストラクタで初期化されたコマンドが、渡されたパラメータで実行されることを検証します。
    /// </summary>
    [Fact]
    public void Execute_パラメータ付きアクション_正しく実行される()
    {
        // Arrange
        object? executedParam = null;
        var sut = new RelayCommand(p => executedParam = p);

        // Act
        sut.Execute("test-param");

        // Assert
        Assert.Equal("test-param", executedParam);
    }

    /// <summary>
    /// 引数なしアクションで初期化されたコマンドが実行されることを検証します。
    /// </summary>
    [Fact]
    public void Execute_引数なしアクション_正しく実行される()
    {
        // Arrange
        bool wasCalled = false;
        var sut = new RelayCommand(() => wasCalled = true);

        // Act
        sut.Execute(null);

        // Assert
        Assert.True(wasCalled);
    }

    /// <summary>
    /// canExecute述語が指定された場合、その判定結果を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanExecute_述語が指定されている場合_述語の結果を返す(bool canExecuteResult)
    {
        // Arrange
        var sut = new RelayCommand(() => { }, () => canExecuteResult);

        // Act
        var result = sut.CanExecute(null);

        // Assert
        Assert.Equal(canExecuteResult, result);
    }

    /// <summary>
    /// canExecute述語が未指定の場合、常にtrueを返すことを検証します。
    /// </summary>
    [Fact]
    public void CanExecute_述語が未指定の場合_常にTrueを返す()
    {
        // Arrange
        var sut = new RelayCommand(() => { });

        // Act
        var result = sut.CanExecute(null);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// executeアクションにnullが渡された場合、ArgumentNullExceptionをスローすることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_ExecuteがNullの場合_ArgumentNullExceptionをスローする()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelayCommand((Action<object?>)null!));
    }
}
