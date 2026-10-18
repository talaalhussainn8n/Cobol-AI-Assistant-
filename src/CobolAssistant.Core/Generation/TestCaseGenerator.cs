using System.Globalization;
using CobolAssistant.Core.Parsing;

namespace CobolAssistant.Core.Generation;

public sealed record TestCase(string Id, string Field, string Input, string Expectation, string Reason);

/// <summary>
/// Leitet Grenzwert-Testfälle aus den PIC-Klauseln der Eingabefelder ab.
/// Diese Testfälle sichern ab, dass sich das Verhalten bei einer Modernisierung nicht ändert.
/// </summary>
public static class TestCaseGenerator
{
    public static IReadOnlyList<TestCase> Generate(ProgramAnalysis analysis)
    {
        var cases = new List<TestCase>();
        var inputFields = analysis.Fields
            .Where(f => f.Section == "FILE" && !f.IsGroup && !f.IsCondition)
            .ToList();
        var candidates = inputFields.Count > 0
            ? inputFields
            : analysis.Fields.Where(f => !f.IsGroup && !f.IsCondition).ToList();

        foreach (var field in candidates)
        {
            var pic = PictureClause.TryParse(field.Picture);
            if (pic is null)
            {
                continue;
            }

            if (pic.Kind == PictureKind.Numeric)
            {
                var max = pic.MaxValue.ToString(CultureInfo.InvariantCulture);
                Add(cases, field.Name, "0", "Verarbeitung ohne Abbruch, Sonderfall Null wird behandelt", "untere Grenze");
                Add(cases, field.Name, max, "korrekte Berechnung ohne Überlauf", $"obere Grenze von PIC {field.Picture}");
                if (pic.DecimalDigits > 0)
                {
                    var smallest = "0." + new string('0', pic.DecimalDigits - 1) + "1";
                    Add(cases, field.Name, smallest, "Rundung bleibt nachvollziehbar", "kleinster Wert ungleich Null");
                }
                if (pic.Signed)
                {
                    Add(cases, field.Name, "-" + max, "negative Werte werden korrekt verarbeitet", "untere Grenze mit Vorzeichen");
                }
                else
                {
                    Add(cases, field.Name, "nicht numerischer Inhalt", "Fehler wird erkannt statt falsch gerechnet", "ungültige Eingabe (Datenqualität)");
                }
            }
            else
            {
                Add(cases, field.Name, "Leerzeichen", "Feld leer wird erkannt", "leere Eingabe");
                Add(cases, field.Name, $"{pic.Length} Zeichen", "Wert wird vollständig übernommen", "maximale Länge");
            }
        }

        foreach (var file in analysis.Files)
        {
            Add(cases, file.Name, "leere Datei", "Programm endet regulär, Zähler stehen auf 0", "Dateiende sofort erreicht");
        }

        return cases;
    }

    private static void Add(List<TestCase> cases, string field, string input, string expectation, string reason) =>
        cases.Add(new TestCase($"TC-{cases.Count + 1:D3}", field, input, expectation, reason));
}
