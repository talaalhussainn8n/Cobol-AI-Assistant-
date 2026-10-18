using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CobolAssistant.Core;
using CobolAssistant.Core.Generation;
using CobolAssistant.Core.Llm;
using CobolAssistant.Core.Parsing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CobolAssistant.Tests;

public class GenerationTests
{
    private static readonly string Sample =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "RENTBER.cbl"));

    private readonly ProgramAnalysis _analysis = new CobolParser().Parse(Sample);

    [Fact]
    public void Aufrufgraph_enthaelt_Kanten()
    {
        var graph = DocumentationGenerator.Mermaid(_analysis);

        Assert.StartsWith("flowchart TD", graph);
        Assert.Contains("HAUPTPROGRAMM --> INITIALISIEREN", graph);
        Assert.Contains("VERARBEITEN --> BERECHNEN", graph);
    }

    [Fact]
    public void Dokumentation_beschreibt_Felder_verstaendlich()
    {
        var doc = DocumentationGenerator.Markdown(_analysis);

        Assert.Contains("# Programm RENTBER", doc);
        Assert.Contains("numerisch, 3 Vorkomma- und 2 Nachkommastellen", doc);
        Assert.Contains("Bedingung, wahr bei 'J'", doc);
        Assert.Contains("```mermaid", doc);
    }

    [Fact]
    public void Testfaelle_aus_Grenzwerten()
    {
        var cases = TestCaseGenerator.Generate(_analysis);

        Assert.Contains(cases, c => c.Field == "VERS-ENTGELTPUNKTE" && c.Input == "999.99");
        Assert.Contains(cases, c => c.Field == "VERS-ZUGANGSFAKTOR" && c.Input == "0");
        Assert.Contains(cases, c => c.Field == "VERSICHERTE" && c.Input == "leere Datei");
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task Ohne_Sprachmodell_regelbasierte_Erklaerung()
    {
        var service = new CobolAssistantService(new CobolParser(), new FakeLlm(null));
        var result = await service.AnalyzeAsync(Sample);

        Assert.False(result.ExplanationByLlm);
        Assert.Contains("RENTBER", result.Explanation);
        Assert.Contains("INITIALISIEREN", result.Explanation);
    }

    [Fact]
    public async Task Sprachmodell_bekommt_Analyse_als_Kontext()
    {
        var llm = new FakeLlm("Zweck: Berechnet Monatsrenten.");
        var service = new CobolAssistantService(new CobolParser(), llm);

        var result = await service.AnalyzeAsync(Sample);

        Assert.True(result.ExplanationByLlm);
        Assert.Equal("Zweck: Berechnet Monatsrenten.", result.Explanation);
        Assert.Contains("Strukturanalyse", llm.LastPrompt);
        Assert.Contains("Absatz BERECHNEN", llm.LastPrompt);
    }

    [Fact]
    public async Task Leerer_Quelltext_wird_abgelehnt()
    {
        var service = new CobolAssistantService(new CobolParser(), new FakeLlm(null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync("  "));
    }

    private sealed class FakeLlm : ILlmClient
    {
        private readonly string? _answer;
        public FakeLlm(string? answer) => _answer = answer;
        public bool IsConfigured => _answer is not null;
        public string? LastPrompt { get; private set; }

        public Task<string> CompleteAsync(string system, string user, CancellationToken ct = default)
        {
            LastPrompt = user;
            return Task.FromResult(_answer ?? string.Empty);
        }
    }
}

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Beispiel_laden_und_analysieren()
    {
        var source = await _client.GetStringAsync("/api/sample");
        var response = await _client.PostAsJsonAsync("/api/analyze", new { source });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RENTBER", result.GetProperty("programId").GetString());
        Assert.Equal(7, result.GetProperty("paragraphs").GetArrayLength());
        Assert.True(result.GetProperty("testCases").GetArrayLength() > 5);
    }

    [Fact]
    public async Task Leerer_Quelltext_liefert_400()
    {
        var response = await _client.PostAsJsonAsync("/api/analyze", new { source = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
