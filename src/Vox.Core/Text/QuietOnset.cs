namespace Vox.Core.Text;

/// <summary>
/// Words whose first sound is said so softly that the model drops it: "вставь" with a soft "ф" ("фставь") comes out
/// as "Ставь" (owner, 01.11).
/// </summary>
public static partial class QuietOnset
{
    public static IReadOnlyList<string> Hotwords => throw new NotImplementedException();

    public static bool NeedsSecondPass(string text) => throw new NotImplementedException();

    public static string Merge(string first, string second) => throw new NotImplementedException();
}
