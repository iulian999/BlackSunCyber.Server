using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BlackSunCyber.Server.Services;

/// <summary>
/// Wrapper subțire peste Supabase REST API (PostgREST), folosind doar
/// HttpClient din .NET standard — nicio dependență externă, deci nicio
/// problemă de compatibilitate cu formatul nou de chei (sb_secret_...).
/// Toate request-urile folosesc Secret key, deci au acces complet
/// (echivalentul service_role), ocolind RLS — de asta acest client
/// există DOAR pe server, niciodată în browser/client.
/// </summary>
public class SupabaseRestClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SupabaseRestClient(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        var url = config["Supabase:Url"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Lipsește Supabase:Url din appsettings.json");
        var secretKey = config["Supabase:SecretKey"]
            ?? throw new InvalidOperationException("Lipsește Supabase:SecretKey din appsettings.json");

        _http = httpClientFactory.CreateClient();
        _http.BaseAddress = new Uri($"{url}/rest/v1/");
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.Add("apikey", secretKey);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        _http.DefaultRequestHeaders.Add("Prefer", "return=representation");
    }

    public async Task<List<T>> GetAsync<T>(string table, string? query = null)
    {
        var url = string.IsNullOrEmpty(query) ? table : $"{table}?{query}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
    }

    public async Task<List<T>> PostAsync<T>(string table, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(table, content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
    }

    /// <summary>DELETE = sterge randul. `filterQuery` ex: "id=eq.3"</summary>
    public async Task DeleteAsync(string table, string filterQuery)
    {
        var response = await _http.DeleteAsync($"{table}?{filterQuery}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>PATCH = actualizare parțială. `filterQuery` ex: "id=eq.3"</summary>
    public async Task<List<T>> PatchAsync<T>(string table, string filterQuery, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _http.PatchAsync($"{table}?{filterQuery}", content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
    }
}