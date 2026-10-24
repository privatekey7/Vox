using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Joins the texts of the pieces a long recording was cut into (D030). The model hears each piece alone: it starts
/// it with a capital and may end it with a full stop where the speaker only paused ("чтобы ‖ Вот, например").
/// </summary>
/// <remarks>
/// After a piece with no sentence end the next one goes on in lower case, unless its first word is a name
/// ("Кирилл"), an abbreviation, mixed case, a term (<see cref="ContextJoiner.KeepsCase"/>) or Latin. A full stop or "..."
/// after a word no sentence ends with ("чтобы.", "через...") is the model's and goes too. A full stop after any other
/// word stays: the text cannot tell it from a real one, which is why GigaAM's pieces are long.
/// </remarks>
public static partial class PieceJoiner
{
    private const string Continuers = ",;:—–-";

    public static string Join(IEnumerable<string> pieces)
    {
        var text = "";
        foreach (var raw in pieces)
        {
            var piece = raw.Trim();
            if (text.Length == 0)
            {
                text = piece;
                continue;
            }

            piece = piece.TrimStart('.', '…', ' '); // "...хаб": the model's own ellipsis at a cut
            if (piece.Length == 0)
            {
                continue;
            }

            text = FalseStop().Replace(text, "");
            var last = text[^1];
            text += " " + (char.IsLetterOrDigit(last) || Continuers.Contains(last, StringComparison.Ordinal) ? LowerFirstWord(piece) : piece);
        }

        return text;
    }

    private static string LowerFirstWord(string piece)
    {
        var end = 0;
        while (end < piece.Length && (char.IsLetter(piece[end]) || piece[end] == '\''))
        {
            end++;
        }

        var word = piece[..end];
        // Only Cyrillic: an English word with a capital may be a name ("John") the Russian dictionary does not know.
        if (word.Length == 0 || !char.IsUpper(word[0]) || !IsCyrillic(word[0]) || ContextJoiner.KeepsCase(word) || RussianWords.IsName(word))
        {
            return piece;
        }

        return char.ToLowerInvariant(piece[0]) + piece[1..];
    }

    private static bool IsCyrillic(char c) => c is >= 'А' and <= 'я' or 'Ё' or 'ё';

    // Prepositions, conjunctions and "не": a sentence never ends on them. Not after a number: "5 с." is seconds.
    [GeneratedRegex(@"(?<=(?<![\p{L}\p{Nd}-])(?<!\d\s*)(?:в|во|на|с|со|к|ко|у|о|об|обо|от|до|по|за|из|изо|без|для|через|про|при|над|под|перед|между|среди|и|а|но|или|либо|чтобы|если|потому|не|ни))\s*(?:\.{1,3}|…)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FalseStop();
}
