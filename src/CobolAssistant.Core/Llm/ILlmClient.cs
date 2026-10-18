namespace CobolAssistant.Core.Llm;

public interface ILlmClient
{
    bool IsConfigured { get; }
    Task<string> CompleteAsync(string system, string user, CancellationToken ct = default);
}