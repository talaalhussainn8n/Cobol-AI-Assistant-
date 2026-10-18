using System.Text.Json.Serialization;
using CobolAssistant.Core;
using CobolAssistant.Core.Llm;
using CobolAssistant.Core.Parsing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(builder.Configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions());
builder.Services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddSingleton<CobolParser>();
builder.Services.AddScoped<CobolAssistantService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/analyze", async (AnalyzeRequest request, CobolAssistantService assistant, CancellationToken ct) =>
{
    try
    {
        var result = await assistant.AnalyzeAsync(request.Source ?? string.Empty, ct);
        return Results.Ok(new
        {
            result.Analysis.ProgramId,
            result.Analysis.CodeLines,
            result.Analysis.Complexity,
            result.Analysis.Files,
            result.Analysis.Fields,
            result.Analysis.Paragraphs,
            result.Analysis.Warnings,
            result.Explanation,
            result.ExplanationByLlm,
            result.Documentation,
            result.CallGraph,
            result.TestCases
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/sample", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "samples", "RENTBER.cbl");
    return File.Exists(path) ? Results.Text(File.ReadAllText(path)) : Results.NotFound();
});

app.Run();

public sealed record AnalyzeRequest(string? Source);

// Für Integrationstests mit WebApplicationFactory.
public partial class Program;
