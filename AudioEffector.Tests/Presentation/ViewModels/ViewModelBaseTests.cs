using System.Collections.Generic;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="ViewModelBase"/> のプロパティ変更通知機能を検証するテストクラス。
/// </summary>
public class ViewModelBaseTests
{
    private sealed class TestViewModel : ViewModelBase
    {
        private string _text = string.Empty;
        private int _number;

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }

        public void FireCustomProperty(string propertyName) => OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// 値が変更された場合、SetPropertyがtrueを返しPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void SetProperty_値が変更された場合_Trueを返しPropertyChangedイベントを発行する()
    {
        // Arrange
        var sut = new TestViewModel();
        var changedProps = new List<string?>();
        sut.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName);

        // Act
        sut.Text = "Hello";

        // Assert
        Assert.Equal("Hello", sut.Text);
        Assert.Single(changedProps);
        Assert.Equal(nameof(TestViewModel.Text), changedProps[0]);
    }

    /// <summary>
    /// 同じ値が再代入された場合、SetPropertyがfalseを返しPropertyChangedイベントが発火しないことを検証します。
    /// </summary>
    [Fact]
    public void SetProperty_同じ値が設定された場合_Falseを返しイベントは発行しない()
    {
        // Arrange
        var sut = new TestViewModel { Number = 10 };
        var changedProps = new List<string?>();
        sut.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName);

        // Act
        sut.Number = 10;

        // Assert
        Assert.Empty(changedProps);
    }

    /// <summary>
    /// OnPropertyChangedを直接呼び出した場合、指定したプロパティ名でイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void OnPropertyChanged_プロパティ名指定_指定した名前でイベントが発行される()
    {
        // Arrange
        var sut = new TestViewModel();
        string? changedName = null;
        sut.PropertyChanged += (s, e) => changedName = e.PropertyName;

        // Act
        sut.FireCustomProperty("CustomName");

        // Assert
        Assert.Equal("CustomName", changedName);
    }
}
