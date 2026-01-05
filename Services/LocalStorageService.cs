using Microsoft.JSInterop;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GymTracker.Services
{
    public interface ILocalStorageService
    {
        Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
        Task SetAsync<T>(string key, T value, CancellationToken ct = default);
        Task RemoveAsync(string key, CancellationToken ct = default);
    }

    public sealed class LocalStorageService : ILocalStorageService, IAsyncDisposable
    {
        private readonly IJSRuntime _js;
        private IJSObjectReference? _module;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters =
        {
            new DateOnlyJsonConverter(),
            new TimeOnlyJsonConverter()
        }
        };

        public LocalStorageService(IJSRuntime js) => _js = js;

        private async ValueTask<IJSObjectReference> GetModuleAsync()
        {
            if (_module is not null) return _module;
            _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/localStorage.js");
            return _module;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            var module = await GetModuleAsync();
            var json = await module.InvokeAsync<string?>("getItem", ct, key);

            if (string.IsNullOrWhiteSpace(json))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch
            {
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
        {
            var module = await GetModuleAsync();
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await module.InvokeVoidAsync("setItem", ct, key, json);
        }

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("removeItem", ct, key);
        }

        public async ValueTask DisposeAsync()
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
                _module = null;
            }
        }

        private sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
        {
            private const string Format = "yyyy-MM-dd";

            public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return default;

                if (DateOnly.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d;

                return DateOnly.Parse(s, CultureInfo.InvariantCulture);
            }

            public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
        }

        private sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
        {
            private const string Format = "HH:mm:ss.fffffff";

            public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return default;

                if (TimeOnly.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    return t;

                return TimeOnly.Parse(s, CultureInfo.InvariantCulture);
            }

            public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
        }
    }
}
