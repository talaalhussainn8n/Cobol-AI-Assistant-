using System.Text.RegularExpressions;

namespace CobolAssistant.Core.Parsing;

/// <summary>
/// Leichtgewichtiger, fehlertoleranter Parser für COBOL-Quelltext (festes und freies Format).
/// Er ersetzt keinen Compiler, liefert aber die Struktur, die für Dokumentation,
/// Abhängigkeitsanalyse und Testfallableitung gebraucht wird.
/// </summary>
public sealed partial class CobolParser
{
    private static readonly HashSet<string> NotParagraphs = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXIT", "GOBACK", "CONTINUE", "STOP", "END", "ELSE", "DECLARATIVES"
    };

    private static readonly HashSet<string> PerformKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "UNTIL", "VARYING", "WITH", "TEST", "FOREVER"
    };

    [GeneratedRegex(@"^(IDENTIFICATION|ID|ENVIRONMENT|DATA|PROCEDURE)\s+DIVISION\b", RegexOptions.IgnoreCase)]
    private static partial Regex DivisionRegex();

    [GeneratedRegex(@"^([A-Z-]+)\s+SECTION\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"PROGRAM-ID\s*\.\s*([A-Z0-9][A-Z0-9-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ProgramIdRegex();

    [GeneratedRegex(@"\.(?=\s|$)")]
    private static partial Regex StatementEndRegex();

    [GeneratedRegex(@"SELECT\s+([A-Z0-9-]+)\s+ASSIGN\s+(?:TO\s+)?'?([^'\s]+)'?(?:[\s\S]*?ORGANIZATION\s+(?:IS\s+)?([A-Z]+(?:\s+SEQUENTIAL)?))?", RegexOptions.IgnoreCase)]
    private static partial Regex SelectRegex();

    [GeneratedRegex(@"^FD\s+([A-Z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FdRegex();

    [GeneratedRegex(@"^(\d{1,2})\s+([A-Z0-9][A-Z0-9-]*)(?:\s+PIC(?:TURE)?\s+(?:IS\s+)?(\S+))?(?:[\s\S]*?\bVALUES?\s+(?:IS\s+|ARE\s+)?([\s\S]+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"^([A-Z0-9][A-Z0-9-]*)\s*\.\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"\bPERFORM\s+([A-Z0-9][A-Z0-9-]*)(?:\s+(?:THRU|THROUGH)\s+([A-Z0-9][A-Z0-9-]*))?", RegexOptions.IgnoreCase)]
    private static partial Regex PerformRegex();

    [GeneratedRegex(@"\bCALL\s+'([^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex CallRegex();

    [GeneratedRegex(@"(?<![\w-])(IF|WHEN|UNTIL)(?![\w-])|AT\s+END(?![\w-])", RegexOptions.IgnoreCase)]
    private static partial Regex DecisionRegex();

    [GeneratedRegex(@"\bMOVE\s+[\s\S]+?\s+TO[ \t]+((?:[A-Z0-9][A-Z0-9-]*[ \t]*)+)", RegexOptions.IgnoreCase)]
    private static partial Regex MoveRegex();

    [GeneratedRegex(@"\b(?:ADD|SUBTRACT)\s+[\s\S]+?\s+(?:TO|FROM)\s+([A-Z0-9][A-Z0-9-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex AddRegex();

    [GeneratedRegex(@"\b(?:COMPUTE|INITIALIZE)\s+([A-Z0-9][A-Z0-9-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ComputeRegex();

    [GeneratedRegex(@"\bSET\s+([A-Z0-9][A-Z0-9-]*)\s+TO\b", RegexOptions.IgnoreCase)]
    private static partial Regex SetRegex();

    [GeneratedRegex(@"\bGO\s+TO\b", RegexOptions.IgnoreCase)]
    private static partial Regex GoToRegex();

    private sealed record SourceLine(int Number, string Code, int Indent);

    public ProgramAnalysis Parse(string source)
    {
        var lines = Normalize(source);
        var warnings = new List<string>();

        var division = string.Empty;
        var section = string.Empty;
        var envText = new List<string>();
        var dataLines = new List<(SourceLine Line, string Section)>();
        var procLines = new List<SourceLine>();
        var programId = "UNBEKANNT";

        foreach (var line in lines)
        {
            var text = line.Code.Trim();
            var idMatch = ProgramIdRegex().Match(text);
            if (idMatch.Success)
            {
                programId = idMatch.Groups[1].Value.ToUpperInvariant();
            }

            var div = DivisionRegex().Match(text);
            if (div.Success)
            {
                division = div.Groups[1].Value.ToUpperInvariant();
                section = string.Empty;
                continue;
            }

            if (division != "PROCEDURE")
            {
                var sec = SectionRegex().Match(text);
                if (sec.Success)
                {
                    section = sec.Groups[1].Value.ToUpperInvariant();
                    continue;
                }
            }

            switch (division)
            {
                case "ENVIRONMENT":
                    envText.Add(text);
                    break;
                case "DATA":
                    dataLines.Add((line, section));
                    break;
                case "PROCEDURE":
                    procLines.Add(line);
                    break;
            }
        }

        var files = ParseFiles(string.Join(' ', envText));
        var fields = ParseFields(dataLines);
        var fieldNames = fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paragraphs = ParseParagraphs(procLines, fieldNames);

        if (procLines.Count == 0)
        {
            warnings.Add("Keine PROCEDURE DIVISION gefunden.");
        }
        if (procLines.Any(l => GoToRegex().IsMatch(l.Code)))
        {
            warnings.Add("GO TO wird verwendet: der Kontrollfluss ist schwerer nachvollziehbar.");
        }
        if (!procLines.Any(l => Regex.IsMatch(l.Code, @"\b(STOP\s+RUN|GOBACK)\b", RegexOptions.IgnoreCase)))
        {
            warnings.Add("Kein STOP RUN oder GOBACK gefunden.");
        }

        var analysis = new ProgramAnalysis(programId, fields, files, paragraphs, warnings, lines.Count(l => l.Code.Trim().Length > 0));
        foreach (var name in analysis.UnreachableParagraphs)
        {
            warnings.Add($"Absatz {name} wird nirgends per PERFORM aufgerufen.");
        }

        var referenced = string.Join('\n', procLines.Select(l => l.Code));
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var hasConditions = i + 1 < fields.Count && fields[i + 1].IsCondition;
            if (field.IsGroup || field.Section != "WORKING-STORAGE" || hasConditions)
            {
                continue; // Felder mit 88-Stufen werden über ihre Bedingungsnamen genutzt.
            }

            if (!Regex.IsMatch(referenced, $@"(?<![\w-]){Regex.Escape(field.Name)}(?![\w-])", RegexOptions.IgnoreCase))
            {
                warnings.Add($"Feld {field.Name} wird in der PROCEDURE DIVISION nicht verwendet.");
            }
        }

        return analysis;
    }

    /// <summary>
    /// Entfernt Folgenummern (Spalte 1 bis 6), Kommentarzeilen und die Spalten ab 73 im festen Format.
    /// Freies Format (Kommentare mit *>) wird ebenfalls unterstützt.
    /// </summary>
    private static List<SourceLine> Normalize(string source)
    {
        var result = new List<SourceLine>();
        var raw = source.Replace("\r\n", "\n").Split('\n');
        var fixedFormat = raw.Where(l => l.Trim().Length > 0).Take(20)
            .Count(l => l.Length > 7 && l[..6].All(c => c == ' ' || char.IsDigit(c))) >= 3;

        for (var i = 0; i < raw.Length; i++)
        {
            var line = raw[i].TrimEnd();
            string code;
            if (fixedFormat && line.Length >= 7)
            {
                if (line[6] is '*' or '/')
                {
                    continue;
                }
                code = line.Length > 7 ? line[7..Math.Min(line.Length, 72)] : string.Empty;
            }
            else if (fixedFormat)
            {
                continue;
            }
            else
            {
                code = line;
            }

            var commentStart = code.IndexOf("*>", StringComparison.Ordinal);
            if (commentStart >= 0)
            {
                code = code[..commentStart];
            }

            if (code.Trim().Length == 0)
            {
                continue;
            }

            var indent = code.Length - code.TrimStart().Length;
            result.Add(new SourceLine(i + 1, code.TrimEnd(), indent));
        }

        return result;
    }

    private static List<FileDefinition> ParseFiles(string environment)
    {
        return SelectRegex().Matches(environment)
            .Select(m => new FileDefinition(
                m.Groups[1].Value.ToUpperInvariant(),
                m.Groups[2].Value.TrimEnd('.'),
                m.Groups[3].Success ? m.Groups[3].Value.Trim().ToUpperInvariant() : null))
            .ToList();
    }

    private static List<DataField> ParseFields(List<(SourceLine Line, string Section)> lines)
    {
        var fields = new List<DataField>();
        var buffer = new List<string>();
        var startLine = 0;
        var section = string.Empty;

        void Flush()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            var statement = string.Join(' ', buffer).Trim();
            buffer.Clear();

            if (FdRegex().IsMatch(statement))
            {
                return;
            }

            var m = FieldRegex().Match(statement);
            if (!m.Success)
            {
                return;
            }

            fields.Add(new DataField(
                int.Parse(m.Groups[1].Value),
                m.Groups[2].Value.ToUpperInvariant(),
                m.Groups[3].Success ? m.Groups[3].Value.ToUpperInvariant() : null,
                m.Groups[4].Success ? m.Groups[4].Value.Trim() : null,
                section,
                startLine));
        }

        foreach (var (line, sec) in lines)
        {
            if (buffer.Count == 0)
            {
                startLine = line.Number;
                section = sec;
            }

            // Ein Satz endet mit einem Punkt, dem ein Leerzeichen oder das Zeilenende folgt (nicht 39.32).
            var parts = StatementEndRegex().Split(line.Code);
            for (var i = 0; i < parts.Length; i++)
            {
                buffer.Add(parts[i]);
                if (i < parts.Length - 1)
                {
                    Flush();
                    startLine = line.Number;
                    section = sec;
                }
            }
        }

        Flush();
        return fields;
    }

    private static List<Paragraph> ParseParagraphs(List<SourceLine> lines, HashSet<string> fieldNames)
    {
        var paragraphs = new List<Paragraph>();
        string? current = null;
        var start = 0;
        var body = new List<SourceLine>();

        void Close(int endLine)
        {
            if (current is null)
            {
                return;
            }

            var text = string.Join('\n', body.Select(b => b.Code));
            var performs = new List<string>();
            foreach (Match m in PerformRegex().Matches(text))
            {
                var target = m.Groups[1].Value.ToUpperInvariant();
                if (PerformKeywords.Contains(target) || fieldNames.Contains(target) || char.IsDigit(target[0]))
                {
                    continue; // Inline-PERFORM (z. B. PERFORM UNTIL) oder PERFORM n TIMES
                }
                if (!performs.Contains(target))
                {
                    performs.Add(target);
                }
                if (m.Groups[2].Success && !performs.Contains(m.Groups[2].Value.ToUpperInvariant()))
                {
                    performs.Add(m.Groups[2].Value.ToUpperInvariant());
                }
            }

            var calls = CallRegex().Matches(text).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();

            var written = new List<string>();
            void AddWritten(string name)
            {
                var upper = name.ToUpperInvariant();
                if (fieldNames.Contains(upper) && !written.Contains(upper))
                {
                    written.Add(upper);
                }
            }

            foreach (Match m in MoveRegex().Matches(text))
            {
                foreach (var target in m.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    AddWritten(target);
                }
            }
            foreach (Match m in AddRegex().Matches(text)) AddWritten(m.Groups[1].Value);
            foreach (Match m in ComputeRegex().Matches(text)) AddWritten(m.Groups[1].Value);
            foreach (Match m in SetRegex().Matches(text)) AddWritten(m.Groups[1].Value);

            paragraphs.Add(new Paragraph(current, start, endLine, performs, calls, written, DecisionRegex().Matches(text).Count));
        }

        foreach (var line in lines)
        {
            var match = ParagraphRegex().Match(line.Code.Trim());
            var isAreaA = line.Indent < 4;
            if (match.Success && isAreaA
                && !NotParagraphs.Contains(match.Groups[1].Value)
                && !match.Groups[1].Value.StartsWith("END-", StringComparison.OrdinalIgnoreCase))
            {
                Close(body.Count > 0 ? body[^1].Number : line.Number);
                current = match.Groups[1].Value.ToUpperInvariant();
                start = line.Number;
                body.Clear();
                continue;
            }

            if (current is null)
            {
                // Anweisungen vor dem ersten Absatz gehören zu einem gedachten Einstieg.
                current = "(EINSTIEG)";
                start = line.Number;
            }
            body.Add(line);
        }

        Close(body.Count > 0 ? body[^1].Number : start);
        return paragraphs;
    }
}
