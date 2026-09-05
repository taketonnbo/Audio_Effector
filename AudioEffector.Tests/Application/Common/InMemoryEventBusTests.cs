using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Events;
using Xunit;

namespace AudioEffector.Tests.Application.Common;

/// <summary>
/// InMemoryEventBusのイベント発行、複数購読、購読解除、および例外保護を検証するテストクラス
/// </summary>
public sealed class InMemoryEventBusTests
{
    private sealed record TestSampleEvent(string Message) : IDomainEvent
    {
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
    }

    private sealed record AnotherSampleEvent(int Value) : IDomainEvent
    {
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 購読者が存在しないイベントを発行した場合、例外をスローせず安全に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task PublishAsync_未購読イベント_例外なく安全に完了すること()
    {
        // Arrange
        var sut = new InMemoryEventBus();
        var domainEvent = new TestSampleEvent("Hello World");

        // Act
        var exception = await Record.ExceptionAsync(() => sut.PublishAsync(domainEvent));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// 単一のハンドラーを購読後、イベント発行時にハンドラーが正しく実行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SubscribeおよびPublishAsync_単一購読者_イベント発行時にハンドラーが実行されること()
    {
        // Arrange
        var sut = new InMemoryEventBus();
        TestSampleEvent? receivedEvent = null;

        sut.Subscribe<TestSampleEvent>((e, _) =>
        {
            receivedEvent = e;
            return Task.CompletedTask;
        });

        var expectedEvent = new TestSampleEvent("Test Message");

        // Act
        await sut.PublishAsync(expectedEvent);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("Test Message", receivedEvent.Message);
    }

    /// <summary>
    /// 同一イベントに対して複数のハンドラーが購読している場合、すべてのハンドラーが実行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SubscribeおよびPublishAsync_同一イベントに複数購読者_すべてのハンドラーが並行実行されること()
    {
        // Arrange
        var sut = new InMemoryEventBus();
        var executedList = new List<int>();

        sut.Subscribe<TestSampleEvent>((_, _) =>
        {
            lock (executedList) executedList.Add(1);
            return Task.CompletedTask;
        });

        sut.Subscribe<TestSampleEvent>((_, _) =>
        {
            lock (executedList) executedList.Add(2);
            return Task.CompletedTask;
        });

        // Act
        await sut.PublishAsync(new TestSampleEvent("Broadcast"));

        // Assert
        Assert.Equal(2, executedList.Count);
        Assert.Contains(1, executedList);
        Assert.Contains(2, executedList);
    }

    /// <summary>
    /// Unsubscribeで購読解除したハンドラーが、以降のイベント発行で実行されないことを検証します。
    /// </summary>
    [Fact]
    public async Task Unsubscribe_購読解除後_イベントが発行されてもハンドラーが実行されないこと()
    {
        // Arrange
        var sut = new InMemoryEventBus();
        int callCount = 0;

        Func<TestSampleEvent, CancellationToken, Task> handler = (_, _) =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        sut.Subscribe(handler);
        await sut.PublishAsync(new TestSampleEvent("First"));
        Assert.Equal(1, callCount);

        // Act
        sut.Unsubscribe(handler);
        await sut.PublishAsync(new TestSampleEvent("Second"));

        // Assert
        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// 1つのハンドラーで例外が発生した場合でも、他のハンドラーの実行が阻害されず安全に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task PublishAsync_特定ハンドラーで例外発生時_他のハンドラーの実行が阻害されず完了すること()
    {
        // Arrange
        var sut = new InMemoryEventBus();
        bool secondHandlerExecuted = false;

        sut.Subscribe<TestSampleEvent>((_, _) =>
        {
            throw new InvalidOperationException("Boom in handler 1!");
        });

        sut.Subscribe<TestSampleEvent>((_, _) =>
        {
            secondHandlerExecuted = true;
            return Task.CompletedTask;
        });

        // Act
        var exception = await Record.ExceptionAsync(() => sut.PublishAsync(new TestSampleEvent("SafeExecution")));

        // Assert
        Assert.Null(exception);
        Assert.True(secondHandlerExecuted);
    }

    /// <summary>
    /// domainEventまたはhandler引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public async Task 引数null検証_domainEventまたはhandlerがnullの場合_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var sut = new InMemoryEventBus();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishAsync<TestSampleEvent>(null!));
        Assert.Throws<ArgumentNullException>(() => sut.Subscribe<TestSampleEvent>(null!));
        Assert.Throws<ArgumentNullException>(() => sut.Unsubscribe<TestSampleEvent>(null!));
    }

    /// <summary>
    /// PublishAsync呼び出し時に渡されたCancellationTokenが、ハンドラーへ正しく伝播することを検証します。
    /// </summary>
    [Fact]
    public async Task PublishAsync_CancellationToken指定_ハンドラーへキャンセレーショントークンが伝播すること()
    {
        // Arrange
        var sut = new InMemoryEventBus();
        using var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        sut.Subscribe<TestSampleEvent>((_, token) =>
        {
            receivedToken = token;
            return Task.CompletedTask;
        });

        // Act
        await sut.PublishAsync(new TestSampleEvent("TokenTest"), cts.Token);

        // Assert
        Assert.Equal(cts.Token, receivedToken);
    }
}
