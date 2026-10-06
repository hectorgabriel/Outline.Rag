using System.Net.Http.Json;

namespace Outline.Rag.Infrastructure.Outline;

internal static class OutlineHttp
{
    /// <summary>Calls an Outline RPC method and unwraps the <c>{ data, pagination }</c> envelope.</summary>
    public static async Task<OutlineResponse<TData>> PostOutlineAsync<TData>(
        this HttpClient http, string path, object request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OutlineResponse<TData>>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Outline returned an empty body for {path}.");
    }
}
