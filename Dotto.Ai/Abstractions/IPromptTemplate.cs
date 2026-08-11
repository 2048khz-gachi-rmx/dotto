namespace Dotto.Ai.Abstractions;

public interface IPromptTemplate
{
    Task<string> GetAsync(CancellationToken cancellationToken = default);
}
