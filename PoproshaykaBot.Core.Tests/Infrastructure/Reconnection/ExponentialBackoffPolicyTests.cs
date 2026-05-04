using PoproshaykaBot.Core.Infrastructure.Reconnection;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Reconnection;

[TestFixture]
public sealed class ExponentialBackoffPolicyTests
{
    [Test]
    public void Constructor_RejectsZeroOrNegativeMaxAttempts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffPolicy(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffPolicy(-1));
    }

    [Test]
    public void TryNextAttempt_ProducesExponentialDelays()
    {
        var policy = new ExponentialBackoffPolicy(5, TimeSpan.FromSeconds(1));

        var delays = new List<TimeSpan>();

        while (policy.TryNextAttempt(out var delay))
        {
            delays.Add(delay);
        }

        Assert.That(delays, Has.Count.EqualTo(5));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(delays[0], Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(delays[1], Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(delays[2], Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(delays[3], Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(delays[4], Is.EqualTo(TimeSpan.FromSeconds(16)));
        }
    }

    [Test]
    public void TryNextAttempt_ReturnsFalse_WhenBudgetExhausted()
    {
        var policy = new ExponentialBackoffPolicy(maxAttempts: 2);

        Assert.That(policy.TryNextAttempt(out _), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.TryNextAttempt(out _), Is.True);
            Assert.That(policy.TryNextAttempt(out var lastDelay), Is.False);
            Assert.That(lastDelay, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public void HasAttemptsLeft_TracksRemaining()
    {
        var policy = new ExponentialBackoffPolicy(maxAttempts: 2);

        Assert.That(policy.HasAttemptsLeft, Is.True);
        policy.TryNextAttempt(out _);
        Assert.That(policy.HasAttemptsLeft, Is.True);
        policy.TryNextAttempt(out _);
        Assert.That(policy.HasAttemptsLeft, Is.False);
    }

    [Test]
    public void Reset_ResumesAttemptCounter()
    {
        var policy = new ExponentialBackoffPolicy(maxAttempts: 3);

        policy.TryNextAttempt(out _);
        policy.TryNextAttempt(out _);
        Assert.That(policy.CurrentAttempt, Is.EqualTo(2));

        policy.Reset();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.CurrentAttempt, Is.Zero);
            Assert.That(policy.HasAttemptsLeft, Is.True);
        }

        policy.TryNextAttempt(out var delay);
        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(1)),
            "After reset, next attempt should yield base delay (1s), not continued exponent.");
    }

    [Test]
    public void CustomBaseDelay_AppliesToFirstAttempt()
    {
        var policy = new ExponentialBackoffPolicy(3, TimeSpan.FromMilliseconds(500));

        policy.TryNextAttempt(out var first);
        policy.TryNextAttempt(out var second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(second, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }
    }
}
