using System.Text;
using CobolAssistant.Core.Generation;
using CobolAssistant.Core.Llm;
using CobolAssistant.Core.Parsing;

namespace CobolAssistant.Core;

public sealed record AssistantResult(
    ProgramAnalysis Analysis,
    string Explanation,
    bool ExplanationByLlm,
    string Documentation,
    string CallGraph,
    IReadOnlyList<TestCase> TestCases);

/// <summary>
/// Kombiniert die deterministische Analyse mit einer Erklärung in Alltagssprache.
/// Das Sprachmodell bekommt die Analyse als Kontext, damit seine Erklärung auf Fakten aus dem Code beruht.
/// </summary>
public sealed class CobolAssistantService
{
    public const int MaxSourceLength = 200_000;

    private readonly CobolParser _parser;
    private readonly ILlmClient _llm;

    public CobolAssistantService(CobolParser parser, ILlmClient llm)
    {
        _parser = parser;
        _llm = llm;
    }

    public async Task<AssistantResult> AnalyzeAsync(string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Der Quelltext ist leer.", nameof(source));
        }
        if (source.Length > MaxSourceLength)
        {
            throw new ArgumentException($"Der Quelltext ist zu groß (höchstens {MaxSourceLength} Zeichen).", nameof(source));
        }

        var analysis = _parser.Parse(source);
        var (explanation, byLlm) = await ExplainAsync(source, analysis, ct);

        return new AssistantResult(
            analysis,
            explanation,
            byLlm,
            DocumentationGenerator.Markdown(analysis),
            DocumentationGenerator.Mermaid(analysis),
            TestCaseGenerator.Generate(analysis));
    }

    private async Task<(string Text, bool ByLlm)> ExplainAsync(string source, ProgramAnalysis analysis, CancellationToken ct)
    {
        if (_llm.IsConfigured)
        {
            try
            {
                var prompt = $"Strukturanalyse:\n{Summary(analysis)}\n\nQuelltext:\n{Truncate(source, 12_000)}";
                var text = await _llm.CompleteAsync(SystemPrompt, prompt, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return (text, true);
                }
            }
            catch (HttpRequestException)
            {
                // Rückfall auf die regelbasierte Erklärung.
            }
        }

        return (RuleBasedExplanation(analysis), false);
    }

    private const string SystemPrompt =
        "Du erklärst COBOL-Programme für Entwicklerinnen und Entwickler, die COBOL nicht kennen. " +
        "Antworte auf Deutsch in drei Abschnitten: Zweck, Ablauf, Risiken bei einer Modernisierung. " +
        "Stütze dich nur auf Quelltext und Strukturanalyse und kennzeichne Vermutungen ausdrücklich.";

    public static string Summary(ProgramAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Programm: {a.ProgramId}, Komplexität {a.Complexity}");
        foreach (var f in a.Files) sb.AppendLine($"Datei: {f.Name} ({f.Assignment})");
        foreach (var p in a.Paragraphs)
            sb.AppendLine($"Absatz {p.Name}: ruft {string.Join(", ", p.Performs)} | ändert {string.Join(", ", p.FieldsWritten)}");
        return sb.ToString();
    }

    /// <summary>Verständliche Erklärung ohne Sprachmodell, aus den Analysedaten zusammengesetzt.</summary>
    public static string RuleBasedExplanation(ProgramAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Das Programm {a.ProgramId} besteht aus {a.Paragraphs.Count} Absätzen und hat eine zyklomatische Komplexität von {a.Complexity}.");

        if (a.Files.Count > 0)
        {
            sb.AppendLine($"Es arbeitet mit den Dateien {string.Join(", ", a.Files.Select(f => $"{f.Name} ({f.Assignment})"))}.");
        }

        var entry = a.Paragraphs.FirstOrDefault();
        if (entry is not null && entry.Performs.Count > 0)
        {
            sb.AppendLine($"Der Einstieg {entry.Name} ruft nacheinander {string.Join(", ", entry.Performs)} auf.");
        }

        var calculations = a.Paragraphs.Where(p => p.FieldsWritten.Count > 0).ToList();
        foreach (var p in calculations)
        {
            sb.AppendLine($"Im Absatz {p.Name} werden {string.Join(", ", p.FieldsWritten)} gesetzt.");
        }

        if (a.Warnings.Count > 0)
        {
            sb.AppendLine($"Auffälligkeiten: {string.Join(" ", a.Warnings)}");
        }

        sb.Append("Für eine ausführlichere Erklärung in Alltagssprache kann ein Sprachmodell angebunden werden.");
        return sb.ToString();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "\n[gekürzt]";
}
