using System.Text.Json;
using CurrencyConverter.Core;

namespace CurrencyConverter2.Services;

public sealed class JsonStateStore : IStateStore
{
    private readonly string path = Path.Combine(FileSystem.AppDataDirectory, "converter-state.json");
    public async Task<ConverterState?> LoadAsync()
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), ConverterJsonContext.Default.ConverterState);
    }
    public async Task SaveAsync(ConverterState state)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, ConverterJsonContext.Default.ConverterState));
        File.Move(temporary, path, overwrite: true);
    }
}
