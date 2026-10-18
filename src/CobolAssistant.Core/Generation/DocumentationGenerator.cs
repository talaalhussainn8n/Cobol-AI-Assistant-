using System.Text;
using CobolAssistant.Core.Parsing;

namespace CobolAssistant.Core.Generation;

/// <summary>
/// Erzeugt aus der Analyse eine technische Dokumentation (Markdown) und einen Aufrufgraphen (Mermaid).
/// </summary>
public static class DocumentationGenerator
{
    public static string Markdown(ProgramAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Programm {a.ProgramId}");
        sb.AppendLine();
        sb.AppendLine("| Kennzahl | Wert |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Codezeilen | {a.CodeLines} |");
        sb.AppendLine($"| Absätze | {a.Paragraphs.Count} |");
        sb.AppendLine($"| Datenfelder | {a.Fields.Count} |");
        sb.AppendLine($"| Zyklomatische Komplexität | {a.Complexity} |");
        sb.AppendLine();

        if (a.Files.Count > 0)
        {
            sb.AppendLine("## Dateien");
            sb.AppendLine();
            sb.AppendLine("| Datei | Zuordnung | Organisation |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var f in a.Files)
            {
                sb.AppendLine($"| {f.Name} | {f.Assignment} | {f.Organization ?? "nicht angegeben"} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Datenfelder");
        sb.AppendLine();
        sb.AppendLine("| Stufe | Name | PIC | Bedeutung | Bereich |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var f in a.Fields)
        {
            var meaning = f.IsCondition
                ? $"Bedingung, wahr bei {f.Value}"
                : f.IsGroup ? "Gruppe" : PictureClause.TryParse(f.Picture)?.Describe() ?? "";
            sb.AppendLine($"| {f.Level:D2} | {f.Name} | {f.Picture ?? ""} | {meaning} | {f.Section} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Ablauf");
        sb.AppendLine();
        foreach (var p in a.Paragraphs)
        {
            var parts = new List<string>();
            if (p.Performs.Count > 0) parts.Add($"ruft {string.Join(", ", p.Performs)} auf");
            if (p.Calls.Count > 0) parts.Add($"ruft Unterprogramm(e) {string.Join(", ", p.Calls)} auf");
            if (p.FieldsWritten.Count > 0) parts.Add($"ändert {string.Join(", ", p.FieldsWritten)}");
            if (p.Decisions > 0) parts.Add($"{p.Decisions} Entscheidung(en)");
            var detail = parts.Count > 0 ? string.Join("; ", parts) : "keine Aufrufe oder Änderungen erkannt";
            sb.AppendLine($"- **{p.Name}** (Zeilen {p.StartLine} bis {p.EndLine}): {detail}");
        }
        sb.AppendLine();

        sb.AppendLine("## Aufrufgraph");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.Append(Mermaid(a));
        sb.AppendLine("```");

        if (a.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Hinweise");
            sb.AppendLine();
            foreach (var w in a.Warnings)
            {
                sb.AppendLine($"- {w}");
            }
        }

        return sb.ToString();
    }

    public static string Mermaid(ProgramAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        foreach (var p in a.Paragraphs)
        {
            sb.AppendLine($"    {Id(p.Name)}[\"{p.Name}\"]");
        }
        foreach (var p in a.Paragraphs)
        {
            foreach (var target in p.Performs)
            {
                sb.AppendLine($"    {Id(p.Name)} --> {Id(target)}");
            }
            foreach (var call in p.Calls)
            {
                sb.AppendLine($"    {Id(p.Name)} -.-> {Id("CALL_" + call)}[[\"{call}\"]]");
            }
        }
        return sb.ToString();
    }

    private static string Id(string name) => new(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
