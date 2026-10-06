using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Domain;

namespace Outline.Rag.Infrastructure.Outline;

/// <summary>
/// Reads documents through the Outline HTTP API rather than Outline's database, so Outline's own
/// permission model and schema changes (see the upgrade notes in README.md) stay behind a stable contract.
/// </summary>
internal sealed class OutlineApiClient(HttpClient http, IOptions<OutlineOptions> options) : IOutlineDocumentSource
{
    public async IAsyncEnumerable<SourceDocument> ListDocumentsAsync(
        DateTimeOffset? updatedSince,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pageSize = options.Value.PageSize;
        for (var offset = 0; ; offset += pageSize)
        {
            using var response = await http
                .PostAsJsonAsync("api/documents.list", new OutlineListRequest(offset, pageSize), cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync<OutlineResponse<List<OutlineDocumentDto>>>(cancellationToken)
                .ConfigureAwait(false);

            var documents = page?.Data ?? [];
            foreach (var dto in documents)
            {
                // Sorted by updatedAt DESC, so everything after this point is already indexed. Documents updated
                // exactly at the checkpoint are re-read: paging may have skipped some that share that timestamp.
                if (updatedSince is not null && dto.UpdatedAt < updatedSince)
                {
                    yield break;
                }

                if (ToDomain(dto) is { } document)
                {
                    yield return document;
                }
            }

            if (documents.Count < pageSize)
            {
                yield break;
            }
        }
    }

    public async Task<IReadOnlySet<Guid>> ListDocumentIdsAsync(CancellationToken cancellationToken)
    {
        var pageSize = options.Value.PageSize;
        var collections = new HashSet<Guid>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await PostAsync<List<OutlineCollectionDto>>(
                "api/collections.list", new OutlinePageRequest(offset, pageSize), cancellationToken).ConfigureAwait(false);
            collections.UnionWith(page.Data.Select(c => c.Id));

            if (page.Data.Count < pageSize)
            {
                // A missed collection would get all its documents removed from the index, so don't trust an
                // incomplete list (offset paging can skip rows when collections change during the listing).
                if (page.Pagination?.Total is { } total && collections.Count < total)
                {
                    throw new InvalidOperationException(
                        $"Outline listed {collections.Count} of {total} collections; not reconciling the index.");
                }

                break;
            }
        }

        var documents = new HashSet<Guid>();
        foreach (var collectionId in collections)
        {
            var tree = await PostAsync<List<OutlineNavigationNode>>(
                "api/collections.documents", new OutlineInfoRequest(collectionId), cancellationToken).ConfigureAwait(false);
            AddIds(tree.Data, documents);
        }

        return documents;

        static void AddIds(IEnumerable<OutlineNavigationNode> nodes, HashSet<Guid> ids)
        {
            foreach (var node in nodes)
            {
                ids.Add(node.Id);
                AddIds(node.Children ?? [], ids);
            }
        }
    }

    public async Task<SourceDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var response = await http
            .PostAsJsonAsync("api/documents.info", new OutlineInfoRequest(documentId), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content
            .ReadFromJsonAsync<OutlineResponse<OutlineDocumentDto>>(cancellationToken)
            .ConfigureAwait(false);

        return body is null ? null : ToDomain(body.Data);
    }

    private async Task<OutlineResponse<TData>> PostAsync<TData>(string path, object request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OutlineResponse<TData>>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Outline returned an empty body for {path}.");
    }

    /// <returns><see langword="null"/> for drafts, archived and deleted documents, which are not indexed.</returns>
    private SourceDocument? ToDomain(OutlineDocumentDto dto)
    {
        if (dto.PublishedAt is null || dto.ArchivedAt is not null || dto.DeletedAt is not null)
        {
            return null;
        }

        return new SourceDocument(
            dto.Id,
            dto.CollectionId,
            dto.Title,
            dto.Text ?? "",
            new Uri(options.Value.BaseUrl!, dto.Url),
            dto.UpdatedAt);
    }
}
