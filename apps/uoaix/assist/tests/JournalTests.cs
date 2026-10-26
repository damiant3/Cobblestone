#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// The journal's readers (AssistState.JournalSince, ServerSince, PlayerSpokeSince), driven by server packets.
public static class JournalTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("journal: the server's own lines are its answers; labels and speech are not", ServerLinesOnly),
        new TestCase("journal: the player's words are the player speaking; another's are not", PlayerSpoke),
        new TestCase("journal: 200 lines are all kept", KeepsTwoHundred),
        new TestCase("journal: the 201st line drops the first, and every speaker stays with its line",
        TrimKeepsSpeakers),
        new TestCase("journal: a mark older than the kept lines reads them all", OldMark),
        new TestCase("journal: a mark at the end reads nothing", MarkAtEnd),
        new TestCase("heard: the lines from the scope's start, none before; the server's and the player's apart",
        HeardFromStart),
        new TestCase("heard: the last line, null before any; a line found anywhere in it, any case", HeardLastAndFind),
        new TestCase("heard: disposed, it reads nothing said after; a second dispose keeps the first end",
        HeardDisposed),
        new TestCase("journal: a range ending before the kept lines is empty; one past the newest reads to it",
        BetweenEnds),
        new TestCase("journal: an answer is any line but a label and the player's words: the server's, a bark, an "
        + "NPC's, an item's", AnswersOnly),
        new TestCase("journal: the 201st line drops the first label with its line, and the answers stay their lines",
        TrimKeepsLabels),
        new TestCase("heard: last, find and has read answers: a label holding the word and the player's words are "
        + "never found", HeardAnswers));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static AssistState Player()
    {
        AssistState state = new();
        state.Server(Packets.LoginConfirm(1000, 1000, 0));
        return state;
    }

    static void ServerLinesOnly()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        state.Server(Packets.Speech(0xFFFFFFFF, "", "You must wait to perform another action.", 0));
        state.Server(Packets.Speech(0x464FABFC, "", "a bag of reagents", 6));
        state.Server(Packets.Speech(0x0000ABCD, "Morena", "Hello", 0));
        List<string> lines = state.ServerSince(mark);
        Check.Equal(1, lines.Count, "server lines");
        Check.Equal("You must wait to perform another action.", lines[0], "the line");
        Check.Equal(3, state.JournalSince(mark).Count, "journal lines");
    }

    static void PlayerSpoke()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        state.Server(Packets.Speech(0x0000ABCD, "Morena", "Kal Ort Por", 0));
        Check.False(state.PlayerSpokeSince(mark), "after another's words");
        state.Server(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
        Check.True(state.PlayerSpokeSince(mark), "after the player's words");
        Check.False(state.PlayerSpokeSince(state.JournalSeq), "from a mark after them");
    }

    static void Fill(AssistState state, int count, uint speaker)
    {
        for (int i = 0; i < count; i++)
            state.Server(Packets.Speech(speaker, speaker == 0xFFFFFFFF ? "" : "Someone", "line " + i, 0));
    }

    static void KeepsTwoHundred()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        Fill(state, AssistState.JournalKeep, 0xFFFFFFFF);
        Check.Equal(AssistState.JournalKeep, state.JournalSince(mark).Count, "lines kept");
        Check.Equal("line 0", state.JournalSince(mark)[0], "the first");
    }

    static void TrimKeepsSpeakers()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        // The first line is the server's; the 200 after it another speaker's: once it is trimmed, no server line is
        // left.
        state.Server(Packets.Speech(0xFFFFFFFF, "", "the first", 0));
        Fill(state, AssistState.JournalKeep, 0x0000ABCD);
        Check.Equal(AssistState.JournalKeep, state.Journal.Count, "lines kept");
        Check.Equal(0, state.ServerSince(mark).Count, "server lines after the trim");
        state.Server(Packets.Speech(0xFFFFFFFF, "", "the last", 0));
        List<string> server = state.ServerSince(mark);
        Check.Equal(1, server.Count, "server lines");
        Check.Equal("the last", server[0], "the server's line");
    }

    static void OldMark()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        Fill(state, AssistState.JournalKeep + 5, 0xFFFFFFFF);
        List<string> lines = state.JournalSince(mark);
        Check.Equal(AssistState.JournalKeep, lines.Count, "lines");
        Check.Equal("line 5", lines[0], "the oldest kept");
    }

    static void MarkAtEnd()
    {
        AssistState state = Player();
        Fill(state, 3, 0xFFFFFFFF);
        Check.Equal(0, state.JournalSince(state.JournalSeq).Count, "journal lines");
        Check.Equal(0, state.ServerSince(state.JournalSeq).Count, "server lines");
    }

    static void HeardFromStart()
    {
        AssistState state = Player();
        state.Server(Packets.Speech(0xFFFFFFFF, "", "before", 0));
        Heard heard = new(state);
        Check.Equal(0, heard.Lines().Count, "nothing yet");
        Check.False(heard.PlayerSpoke(), "no words yet");
        state.Server(Packets.Speech(0x0000ABCD, "Morena", "Hello", 0));
        state.Server(Packets.Speech(0xFFFFFFFF, "", "You must wait to perform another action.", 0));
        Check.Equal(2, heard.Lines().Count, "lines");
        Check.Equal(1, heard.Server().Count, "server lines");
        Check.Equal("You must wait to perform another action.", heard.Server()[0], "the server's line");
        Check.False(heard.PlayerSpoke(), "another's words");
        state.Server(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
        Check.True(heard.PlayerSpoke(), "the player's words");
    }

    static void HeardLastAndFind()
    {
        AssistState state = Player();
        Heard heard = new(state);
        Check.True(heard.Last() is null, "no last line");
        Check.True(heard.Find("wait") is null, "nothing found");
        state.Server(Packets.Speech(0xFFFFFFFF, "", "first", 0));
        state.Server(Packets.Speech(0xFFFFFFFF, "", "Kwik: You must WAIT", 0));
        Check.Equal("Kwik: You must WAIT", heard.Last(), "the last line");
        Check.Equal("Kwik: You must WAIT", heard.Find("you must wait"), "found inside the line, any case");
        Check.True(heard.Has("FIRST"), "has");
        Check.False(heard.Has("fizzle"), "has not");
    }

    static void HeardDisposed()
    {
        AssistState state = Player();
        Heard heard = new(state);
        state.Server(Packets.Speech(0xFFFFFFFF, "", "inside", 0));
        heard.Dispose();
        state.Server(Packets.Speech(0xFFFFFFFF, "", "after", 0));
        state.Server(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0));
        heard.Dispose();
        Check.Equal(1, heard.Lines().Count, "lines");
        Check.Equal("inside", heard.Last(), "the scope's line");
        Check.Equal(1, heard.Server().Count, "server lines");
        Check.False(heard.PlayerSpoke(), "the words came after");
    }

    static void BetweenEnds()
    {
        AssistState state = Player();
        long first = state.JournalSeq;
        Fill(state, 3, 0xFFFFFFFF);
        long third = state.JournalSeq - 1;
        Check.Equal(2, state.JournalBetween(first, third).Count, "two of three");
        Check.Equal(3, state.JournalBetween(first, long.MaxValue).Count, "an end past the newest");
        Fill(state, AssistState.JournalKeep, 0xFFFFFFFF);
        Check.Equal(0, state.JournalBetween(first, third).Count, "a range trimmed away");
        Check.Equal(0, state.ServerBetween(first, third).Count, "server lines trimmed away");
        Check.False(state.PlayerSpokeBetween(first, third), "no words in a trimmed range");
        Check.Equal(AssistState.JournalKeep, state.JournalSince(state.JournalSeq + 1).Count,
            "a mark past the newest reads every kept line");
        Check.Equal(0, state.AnswersBetween(first, third).Count, "no answers in a trimmed range");
    }

    const uint Bag = 0x464FABFC;

    static void AnswersOnly()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        state.Server(Packets.Speech(0xFFFFFFFF, "", "You must wait to perform another action.", 0));
        state.Server(Packets.Speech(Bag, "", "a bag of reagents", 6));
        state.Server(Packets.Speech(Packets.Player, "Kwik", "Kal Ort Por", 0, 0x5A));
        state.Server(Packets.Speech(Packets.Player, "Kwik", "Your hands must be free to cast spells.", 0,
            AssistState.BarkHue));
        state.Server(Packets.Speech(0x0000ABCD, "Morena", "The total of thy purchase is 36 gold.", 0));
        state.Server(Packets.Speech(Bag, "", "That is too far away.", 0));
        List<string> lines = state.AnswersBetween(mark, long.MaxValue);
        Check.Equal(4, lines.Count, "answers: " + string.Join(" | ", lines));
        Check.Equal("You must wait to perform another action.", lines[0], "the server's line");
        Check.Equal("Kwik: Your hands must be free to cast spells.", lines[1], "the bark over the player");
        Check.Equal("Morena: The total of thy purchase is 36 gold.", lines[2], "the NPC's line");
        Check.Equal("That is too far away.", lines[3], "a line over an item that is no label");
        Check.Equal(6, state.JournalSince(mark).Count, "journal lines");
    }

    static void TrimKeepsLabels()
    {
        AssistState state = Player();
        long mark = state.JournalSeq;
        state.Server(Packets.Speech(Bag, "", "a bag of reagents", 6));
        Fill(state, AssistState.JournalKeep - 1, 0xFFFFFFFF);
        Check.Equal(AssistState.JournalKeep - 1, state.AnswersBetween(mark, long.MaxValue).Count,
            "the label is no answer");
        Fill(state, 1, 0xFFFFFFFF);
        Check.Equal(AssistState.JournalKeep, state.AnswersBetween(mark, long.MaxValue).Count,
            "the label dropped, every kept line an answer");
        state.Server(Packets.Speech(Bag, "", "a bag of reagents", 6));
        Check.Equal(AssistState.JournalKeep - 1, state.AnswersBetween(mark, long.MaxValue).Count,
            "the new label is no answer");
    }

    static void HeardAnswers()
    {
        AssistState state = Player();
        Heard heard = new(state);
        state.Server(Packets.Speech(Bag, "", "a bag of reagents", 6));
        state.Server(Packets.Speech(Packets.Player, "Kwik", "fizzle", 0));
        Check.True(heard.Last() is null, "no answer yet");
        Check.False(heard.Has("reagents"), "the label not found");
        Check.False(heard.Has("fizzle"), "the player's words not found");
        Check.Equal(0, heard.Answers().Count, "no answers");
        Check.Equal(2, heard.Lines().Count, "two journal lines");
        state.Server(Packets.Speech(0xFFFFFFFF, "", "The spell fizzles.", 0));
        Check.Equal("The spell fizzles.", heard.Last(), "the answer");
        Check.Equal("The spell fizzles.", heard.Find("FIZZLE"), "found");
    }
}
