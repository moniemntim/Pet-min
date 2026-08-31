using FluentAssertions;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class TwitchFeedQueueConcurrencyTests
{
    [Fact]
    public async Task CallbackCanEnqueueFromAnotherThreadWithoutDeadlocking()
    {
        var queue = new TwitchFeedQueue();
        queue.EnqueuePellet().Should().BeTrue();
        var callbackStarted = new ManualResetEventSlim();
        Task<bool>? enqueueTask = null;

        var consumer = Task.Run(() => queue.TryStartFeeding(_ =>
        {
            callbackStarted.Set();
            enqueueTask = Task.Run(queue.EnqueuePellet);
            return enqueueTask.Wait(TimeSpan.FromSeconds(2)) && enqueueTask.Result;
        }));

        (await Task.Run(() => callbackStarted.Wait(TimeSpan.FromSeconds(2)))).Should().BeTrue();
        (await consumer.WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
        queue.Count.Should().Be(1);
    }

    [Fact]
    public void FalseResultKeepsTheItemQueued()
    {
        var queue = new TwitchFeedQueue();
        queue.EnqueuePellet().Should().BeTrue();

        queue.TryStartFeeding(_ => false).Should().BeFalse();
        queue.Count.Should().Be(1);
        queue.TryStartFeeding(_ => true).Should().BeTrue();
        queue.Count.Should().Be(0);
    }

    [Fact]
    public void ExceptionKeepsTheItemQueued()
    {
        var queue = new TwitchFeedQueue();
        queue.EnqueuePellet().Should().BeTrue();

        Action attempt = () => queue.TryStartFeeding(_ => throw new InvalidOperationException("callback failed"));
        attempt.Should().Throw<InvalidOperationException>();
        queue.Count.Should().Be(1);
        queue.TryStartFeeding(_ => true).Should().BeTrue();
    }

    [Fact]
    public async Task SuccessAfterClearDoesNotConsumeAnItemEnqueuedAfterClear()
    {
        var queue = new TwitchFeedQueue();
        queue.EnqueuePellet().Should().BeTrue();
        var callbackStarted = new ManualResetEventSlim();
        var releaseCallback = new ManualResetEventSlim();

        var consumer = Task.Run(() => queue.TryStartFeeding(_ =>
        {
            callbackStarted.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(2));
            return true;
        }));

        (await Task.Run(() => callbackStarted.Wait(TimeSpan.FromSeconds(2)))).Should().BeTrue();
        queue.Clear();
        queue.EnqueuePellet().Should().BeTrue();
        queue.Count.Should().Be(2);
        releaseCallback.Set();

        (await consumer.WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
        queue.Count.Should().Be(1);
    }
}
