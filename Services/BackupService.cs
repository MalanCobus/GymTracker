using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace GymTracker.Services;

public sealed class BackupService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public BackupService(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        if (_module is not null) return _module;
        _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/backup.js");
        return _module;
    }

    public async Task<bool> RequestPersistentStorageAsync(CancellationToken ct = default)
    {
        var m = await GetModuleAsync();
        return await m.InvokeAsync<bool>("requestPersistentStorage", ct);
    }

    public async Task<bool?> IsPersistentStorageAsync(CancellationToken ct = default)
    {
        var m = await GetModuleAsync();
        return await m.InvokeAsync<bool?>("isPersistentStorage", ct);
    }

    public async Task ExportAsync<T>(string filename, T data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var m = await GetModuleAsync();

        // Try share first (nice on iPhone). Fallback to download.
        var shared = false;
        try { shared = await m.InvokeAsync<bool>("shareJsonFile", ct, filename, json); }
        catch { /* ignore */ }

        if (!shared)
            await m.InvokeVoidAsync("downloadJson", ct, filename, json);
    }

    public async Task<T?> ImportAsync<T>(CancellationToken ct = default)
    {
        var m = await GetModuleAsync();
        var json = await m.InvokeAsync<string?>("pickJsonFileText", ct);
        if (string.IsNullOrWhiteSpace(json)) return default;

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}
