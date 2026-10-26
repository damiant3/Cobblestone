#nullable enable
using System.Collections.Immutable;

namespace Uoaix.Assist.Tests;

// Kinds by their graphics, never their tile names (AssistIdentity.md stage 4), over a client map of the test's own
// (TestMap): a door is an art TILEDATA flags a door; a pack bag is a container that is no book.
public static class KindTests
{
    static readonly ImmutableArray<TestCase> s_cases = ImmutableArray.Create(
        new TestCase("kinds: a door-flagged art is a door, a set art or not; a wall, a bag and an art past the data "
            + "are not", Doors),
        new TestCase("kinds: a bag is a pack bag; a bookcase, the spellbook and a door are not; the box past the "
        + "books is", PackBags));

    public static ImmutableArray<TestCase> Cases => s_cases;

    static void Doors()
    {
        new TestMap().Run(() =>
        {
            Check.True(AssistFeatures.IsDoor(TestMap.DoorShut), "the set's shut art");
            Check.True(AssistFeatures.IsDoor(TestMap.DoorOpen), "the set's open art");
            Check.True(AssistFeatures.IsDoor(TestMap.Door), "a flagged art in no set");
            Check.False(AssistFeatures.IsDoor(TestMap.Wall), "a wall named nothing like a door");
            Check.False(AssistFeatures.IsDoor(TestMap.Bag), "a bag");
            Check.False(AssistFeatures.IsDoor(-1), "art -1");
            Check.False(AssistFeatures.IsDoor(0x7FFF), "an art past the tile data");
        });
    }

    static void PackBags()
    {
        new TestMap().Run(() =>
        {
            Check.True(AssistFeatures.PackBag(TestMap.Bag), "a bag");
            Check.True(AssistFeatures.PackBag(TestMap.BoxPastBooks), "the box one past the bookcases");
            Check.False(AssistFeatures.PackBag(TestMap.Bookcase), "a bookcase");
            Check.False(AssistFeatures.PackBag(TestMap.Spellbook), "the spellbook");
            Check.False(AssistFeatures.PackBag(TestMap.Door), "a door, no container");
        });
    }
}
