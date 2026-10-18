namespace CobolAssistant.Core.Parsing;

/// <summary>Ein Datenfeld aus DATA DIVISION, z. B. "05 VERS-NR PIC X(12)".</summary>
public sealed record DataField(
    int Level,
    string Name,
    string? Picture,
    string? Value,
    string Section,
    int Line)
{
    public bool IsGroup => Picture is null && Level != 88;
    public bool IsCondition => Level == 88;
}

/// <summary>Ein Absatz in der PROCEDURE DIVISION mit seinen Aufrufen und Kennzahlen.</summary>
public sealed record Paragraph(
    string Name,
    int StartLine,
    int EndLine,
    IReadOnlyList<string> Performs,
    IReadOnlyList<string> Calls,
    IReadOnlyList<string> FieldsWritten,
    int Decisions);

public sealed record FileDefinition(string Name, string Assignment, string? Organization);

/// <summary>Strukturiertes Ergebnis der Analyse eines COBOL-Programms.</summary>
public sealed record ProgramAnalysis(
    string ProgramId,
    IReadOnlyList<DataField> Fields,
    IReadOnlyList<FileDefinition> Files,
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<string> Warnings,
    int CodeLines)
{
    /// <summary>Zyklomatische Komplexität über alle Absätze (Entscheidungen + 1).</summary>
    public int Complexity => Paragraphs.Sum(p => p.Decisions) + 1;

    /// <summary>Absätze, die von keinem anderen aufgerufen werden (außer dem Einstieg).</summary>
    public IReadOnlyList<string> UnreachableParagraphs =>
        Paragraphs.Skip(1)
            .Where(p => !Paragraphs.Any(other => other.Performs.Contains(p.Name)))
            .Select(p => p.Name)
            .ToList();
}
