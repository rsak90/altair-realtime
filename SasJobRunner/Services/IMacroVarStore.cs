namespace SasJobRunner.Services;

public interface IMacroVarStore
{
    Task<IReadOnlyDictionary<string, string>> GetAsync(string sessionId);
    Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> vars);
    Task SetVarAsync(string sessionId, string name, string value);
}
