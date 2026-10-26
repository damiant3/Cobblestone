#nullable enable
using System.Collections.Generic;

namespace Uoaix.Assist;

// Foliage among the statics, by the 1.25 TILEDATA name (Damian, 2026-10-15: "what about the trees and foliage"): 2 a
// canopy (trees, leaves, palms), 1 a small plant (vines, flowers, ferns, cacti, mushrooms), 3 water, 4 grass (drawn
// as the land texture beneath it), 0 anything else, and 0 for what is built of plants (a palm frond roof, a log wall,
// a potted tree).
public static class UoFoliage
{
    static byte[]? s_kind;
    static bool Any(string n, params string[] parts)
    {
        foreach (string p in parts)
            if (n.Contains(p))
                return true;
        return false;
    }
    public static int Kind(int id)
    {
        if (s_kind == null)
        {
            List<UoTiles.Tile> items = UoTiles.Items();
            if (items.Count == 0)
                return 0;
            byte[] k = new byte[items.Count];
            foreach (UoTiles.Tile t in items)
            {
                string n = t.Name.ToLowerInvariant();
                if (n.Length == 0 || t.Graphic >= k.Length)
                    continue;
                if (n == "water")
                {
                    k[t.Graphic] = 3;
                    continue;
                }
                if (n == "grass" || n == "grasses")
                {
                    k[t.Graphic] = 4;
                    continue;
                }
                if (Any(n, "roof", "wall", "table", "post", "pot", "grass", "wooden", "dried", "sunken"))
                    continue;
                if (n.EndsWith("tree") || n.EndsWith("leaves") || Any(n, "palm", "fronds"))
                    k[t.Graphic] = 2;
                else if (Any(n, "vine", "flower", "fern", "cactus", "mushroom", "plant", "lilypad", "stump", "reed",
                    "weed", "bush", "shrub", "seaweed"))
                    k[t.Graphic] = 1;
            }
            s_kind = k;
        }
        return id >= 0 && id < s_kind.Length ? s_kind[id] : 0;
    }
}
