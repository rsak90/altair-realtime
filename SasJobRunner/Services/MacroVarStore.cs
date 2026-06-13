using System.Collections.Concurrent;

namespace SasJobRunner.Services;

public sealed class MacroVarStore : IMacroVarStore
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _store = new();

    public Task<IReadOnlyDictionary<string, string>> GetAsync(string sessionId)
    {
        var vars = _store.TryGetValue(sessionId, out var v)
            ? (IReadOnlyDictionary<string, string>)v
            : new Dictionary<string, string>();
        return Task.FromResult(vars);
    }

    public Task SetAsync(string sessionId, IReadOnlyDictionary<string, string> vars)
    {
        _store[sessionId] = new Dictionary<string, string>(vars);
        return Task.CompletedTask;
    }

    public Task SetVarAsync(string sessionId, string name, string value)
    {
        _store.AddOrUpdate(sessionId,
            _ => new Dictionary<string, string> { [name] = value },
            (_, existing) => { existing[name] = value; return existing; });
        return Task.CompletedTask;
    }
}
