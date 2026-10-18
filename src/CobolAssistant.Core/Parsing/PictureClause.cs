using System.Text;
using System.Text.RegularExpressions;

namespace CobolAssistant.Core.Parsing;

public enum PictureKind
{
    Numeric,
    Alphanumeric,
    Alphabetic
}

/// <summary>
/// Auswertung einer PIC-Klausel, z. B. "S9(5)V99" = vorzeichenbehaftet, 5 Vorkomma- und 2 Nachkommastellen.
/// </summary>
public sealed partial record PictureClause(PictureKind Kind, int IntegerDigits, int DecimalDigits, int Length, bool Signed)
{
    [GeneratedRegex(@"(.)\((\d+)\)")]
    private static partial Regex RepeatRegex();

    public static PictureClause? TryParse(string? picture)
    {
        if (string.IsNullOrWhiteSpace(picture))
        {
            return null;
        }

        // "9(3)V99" -> "999V99"
        var expanded = RepeatRegex().Replace(picture.ToUpperInvariant().TrimEnd('.'),
            m => new string(m.Groups[1].Value[0], int.Parse(m.Groups[2].Value)));

        var signed = expanded.StartsWith('S');
        var body = signed ? expanded[1..] : expanded;

        if (body.Length > 0 && body.All(c => c is '9' or 'V' or 'P' or 'Z' or '.' or ',' or '-' or '+'))
        {
            var parts = body.Split('V', '.');
            var integer = parts[0].Count(c => c is '9' or 'Z');
            var decimals = parts.Length > 1 ? parts[1].Count(c => c is '9' or 'Z') : 0;
            return new PictureClause(PictureKind.Numeric, integer, decimals, integer + decimals, signed);
        }

        if (body.Length > 0 && body.All(c => c == 'A'))
        {
            return new PictureClause(PictureKind.Alphabetic, 0, 0, body.Length, false);
        }

        return new PictureClause(PictureKind.Alphanumeric, 0, 0, body.Count(c => c is 'X' or 'A' or '9'), false);
    }

    /// <summary>Größter darstellbarer Wert, z. B. 999.99 für 9(3)V99.</summary>
    public decimal MaxValue
    {
        get
        {
            if (Kind != PictureKind.Numeric)
            {
                return 0;
            }

            var digits = new StringBuilder();
            digits.Append('9', Math.Max(IntegerDigits, 1));
            if (DecimalDigits > 0)
            {
                digits.Append('.').Append('9', DecimalDigits);
            }

            return IntegerDigits == 0 && DecimalDigits > 0
                ? decimal.Parse("0." + new string('9', DecimalDigits), System.Globalization.CultureInfo.InvariantCulture)
                : decimal.Parse(digits.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public string Describe() => Kind switch
    {
        PictureKind.Numeric when DecimalDigits > 0 =>
            $"numerisch{(Signed ? " mit Vorzeichen" : "")}, {IntegerDigits} Vorkomma- und {DecimalDigits} Nachkommastellen",
        PictureKind.Numeric => $"numerisch{(Signed ? " mit Vorzeichen" : "")}, {IntegerDigits} Stellen",
        PictureKind.Alphabetic => $"alphabetisch, {Length} Zeichen",
        _ => $"alphanumerisch, {Length} Zeichen"
    };
}
