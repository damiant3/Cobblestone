#nullable enable

namespace Uoaix.Assist;

public sealed class AdviceRow
{
    public string Key = null!, Text = null!, Source = null!;
    public bool Strength; public int Skill = -1; public int At; public int[] Graphics = new int[0];
}
