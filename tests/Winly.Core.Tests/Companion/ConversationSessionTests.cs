using Winly.Core.Companion;

namespace Winly.Core.Tests.Companion;

public class ConversationSessionTests
{
    [Fact]
    public void ANewSessionIsEmptyAsAfterARestart() => Assert.Empty(new ConversationSession().Exchanges);

    [Fact]
    public void ExchangesAccumulateInOrderWithinOneSession()
    {
        var session = new ConversationSession();
        var first = new Exchange("first", "answer one", null, DateTimeOffset.UtcNow);
        var second = new Exchange("second", "answer two", null, DateTimeOffset.UtcNow.AddSeconds(1));

        session.Append(first);
        session.Append(second);

        Assert.Equal([first, second], session.Exchanges);
    }

    [Fact]
    public void ExchangesSnapshotIsNotAffectedByLaterAppends()
    {
        var session = new ConversationSession();
        session.Append(new Exchange("first", "answer", null, DateTimeOffset.UtcNow));
        var snapshot = session.Exchanges;

        session.Append(new Exchange("second", "answer", null, DateTimeOffset.UtcNow));

        Assert.Single(snapshot);
        Assert.Equal(2, session.Exchanges.Count);
    }

    [Fact]
    public void RecentSendsOnlyTheLastFewTurnsWhileTheSessionKeepsThemAll()
    {
        var session = new ConversationSession();
        for (var turn = 1; turn <= 10; turn++)
        {
            session.Append(new Exchange($"question {turn}", "answer", null, DateTimeOffset.UtcNow));
        }

        var recent = session.Recent(3);

        Assert.Equal(["question 8", "question 9", "question 10"], recent.Select(exchange => exchange.Transcript));
        Assert.Equal(10, session.Exchanges.Count);
    }

    [Fact]
    public void RecentReturnsEverythingWhenTheSessionIsShorterThanTheCap()
    {
        var session = new ConversationSession();
        session.Append(new Exchange("only question", "answer", null, DateTimeOffset.UtcNow));

        Assert.Single(session.Recent(6));
    }
}
