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

    [Fact]
    public void RecentDropsEverythingBeforeAPauseLongerThanTheFollowUpWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ConversationSession();
        session.Append(new Exchange("what is this error", "answer", null, now.AddHours(-1)));
        session.Append(new Exchange("and the one next to it", "answer", null, now.AddHours(-1).AddSeconds(20)));
        session.Append(new Exchange("what is the weather", "answer", null, now.AddSeconds(-10)));

        var recent = session.Recent(6, now);

        Assert.Equal(["what is the weather"], recent.Select(exchange => exchange.Transcript));
    }

    [Fact]
    public void RecentKeepsALongConversationWhoseTurnsNeverPause()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ConversationSession();
        for (var turn = 10; turn >= 1; turn--)
        {
            // Ten minutes end to end, but never more than a minute between turns.
            session.Append(new Exchange($"question {turn}", "answer", null, now.AddMinutes(-turn)));
        }

        var recent = session.Recent(6, now);

        Assert.Equal(6, recent.Count);
        Assert.Equal("question 1", recent[^1].Transcript);
    }

    [Fact]
    public void RecentSendsNothingWhenTheUserSimplyComesBackLater()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ConversationSession();
        session.Append(new Exchange("question", "answer", null, now.AddMinutes(-30)));

        Assert.Empty(session.Recent(6, now));
        Assert.Single(session.Exchanges);
    }
}
