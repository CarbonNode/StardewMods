using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public class SkipVoteTests
{
    private static FarmerSnapshot F(long id, FarmerStatus status = FarmerStatus.Pending, bool online = false) => new(id, "F" + id, status, online, false);

    [Fact]
    public void It_takes_more_than_half_of_the_other_farmers()
    {
        Assert.Equal(1, SkipVoteBook.Needed(0));
        Assert.Equal(1, SkipVoteBook.Needed(1));
        Assert.Equal(2, SkipVoteBook.Needed(2));
        Assert.Equal(2, SkipVoteBook.Needed(3));
        Assert.Equal(3, SkipVoteBook.Needed(4));
        Assert.Equal(3, SkipVoteBook.Needed(5));
    }

    [Fact]
    public void On_a_farm_of_two_the_one_who_is_here_decides()
    {
        var roster = new[] { F(1, FarmerStatus.Completed, online: true), F(2) };
        var book = new SkipVoteBook();
        Assert.Equal(SkipVoteResult.Passed, book.Cast(10, voter: 1, target: 2, roster));
        Assert.Equal((1, 1), (book.Tally(2, roster).Votes, book.Tally(2, roster).Needed));
    }

    [Fact]
    public void On_a_farm_of_four_two_of_the_other_three_carry_it_and_a_vote_is_counted_once()
    {
        var roster = new[] { F(1, online: true), F(2, FarmerStatus.Completed), F(3, online: true), F(4) };
        var book = new SkipVoteBook();
        Assert.Equal(SkipVoteResult.Counted, book.Cast(10, 1, 4, roster));
        Assert.Equal(SkipVoteResult.Counted, book.Cast(10, 1, 4, roster));
        Assert.Equal(1, book.Tally(4, roster).Votes);
        Assert.Equal(2, book.Tally(4, roster).Needed);
        // The second voter may have played and left: a farmer who is not in the farm still has a say.
        Assert.Equal(SkipVoteResult.Passed, book.Cast(10, 2, 4, roster));
        Assert.Equal(new long[] { 1, 2 }, book.Tally(4, roster).Voters);
    }

    [Fact]
    public void Only_an_absent_farmer_who_has_not_played_can_be_voted_on_and_nobody_votes_on_themselves()
    {
        var roster = new[] { F(1, online: true), F(2, online: true), F(3, FarmerStatus.Completed), F(4, FarmerStatus.Skipped), F(5) };
        var book = new SkipVoteBook();
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 1, 2, roster));   // in the farm right now
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 1, 3, roster));   // has played
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 1, 4, roster));   // already sitting out
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 5, 5, roster));   // themselves
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 99, 5, roster));  // not on the roster
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 4, 5, roster));   // sitting out: no say today
        Assert.Equal(SkipVoteResult.Refused, book.Cast(10, 1, 77, roster));  // nobody
        // The farmer sitting out is not counted among those who must agree: 1, 2 and 3 are, so two.
        Assert.Equal(3, SkipVoteBook.Electorate(roster, 5).Count);
        Assert.Equal(SkipVoteResult.Counted, book.Cast(10, 1, 5, roster));
        Assert.Equal(SkipVoteResult.Passed, book.Cast(10, 3, 5, roster));
    }

    [Fact]
    public void A_vote_is_dropped_when_its_farmer_comes_back_and_when_the_day_ends()
    {
        var away = new[] { F(1, online: true), F(2, online: true), F(3) };
        var book = new SkipVoteBook();
        Assert.Equal(SkipVoteResult.Counted, book.Cast(10, 1, 3, away));
        // Farmer 3 joins: the vote about them is gone, and stays gone if they leave again.
        var back = new[] { F(1, online: true), F(2, online: true), F(3, online: true) };
        book.Prune(10, back);
        Assert.Equal(0, book.Tally(3, away).Votes);
        Assert.Equal(SkipVoteResult.Counted, book.Cast(10, 2, 3, away));
        Assert.Equal(1, book.Tally(3, away).Votes);
        // A new day starts clean.
        book.Prune(11, away);
        Assert.Equal(0, book.Tally(3, away).Votes);
        Assert.Equal(11, book.Day);
    }

    [Fact]
    public void Votes_survive_the_game_being_closed_and_woken_but_not_another_day_or_another_farm()
    {
        var roster = new[] { F(1, FarmerStatus.Completed), F(2, online: true), F(3, FarmerStatus.Completed), F(4) };
        var book = new SkipVoteBook();
        book.Cast(10, 1, 4, roster);
        var saved = book.Export("Sunny_1");
        Assert.Equal(new long[] { 1 }, saved.Votes[4]);

        var woken = SkipVoteBook.Restore(saved, "Sunny_1", 10);
        Assert.Equal(1, woken.Tally(4, roster).Votes);
        Assert.Equal(SkipVoteResult.Passed, woken.Cast(10, 2, 4, roster));

        Assert.Equal(0, SkipVoteBook.Restore(saved, "Sunny_1", 11).Tally(4, roster).Votes);
        Assert.Equal(0, SkipVoteBook.Restore(saved, "Other_2", 10).Tally(4, roster).Votes);
        Assert.Equal(0, SkipVoteBook.Restore(null, "Sunny_1", 10).Tally(4, roster).Votes);
    }
}
