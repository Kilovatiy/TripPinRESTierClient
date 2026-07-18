using Client.Core.Abstractions;
using Client.Core.Models;

namespace Client.Application;

/// <summary>
/// Application service on top of <see cref="IClientGateway"/>: pagination and input
/// validation - the "use case" layer of the onion, sitting between the Domain
/// (Client.Core: models + ports) and the outer Infrastructure/Presentation rings.
/// Contains no I/O of its own, so it is fully testable against a fake gateway.
/// </summary>
public class ClientService : IClientService
{
    private readonly IClientGateway _gateway;
    private readonly int _pageSize;

    // The largest page number whose skip, (page - 1) * _pageSize, still fits in an
    // int. Bounding the page here means the multiplication below can never
    // overflow, instead of relying on checked math to catch it after the fact.
    private readonly int _maxRepresentablePage;

    public ClientService(IClientGateway gateway, ClientOptions options)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentNullException.ThrowIfNull(options);

        if (options.PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.PageSize, "Client:PageSize must be a positive integer.");

        _pageSize = options.PageSize;
        _maxRepresentablePage = (int)(int.MaxValue / (long)_pageSize) + 1;
    }

    public async Task<PagedResult<Person>> GetPageAsync(int page, CancellationToken ct = default)
    {
        // The lower bound (page >= 1) doesn't depend on the total count, but the
        // upper bound does - and the total count only comes back *with* a page,
        // not before it (one OData request, $count=true alongside $top/$skip).
        // So: ask for the requested page directly; if it turns out to be beyond
        // the last page, that response still told us the total, so clamp and
        // re-fetch. Every in-range request (which covers next/prev/first, i.e.
        // normal navigation) costs one gateway call; only an out-of-range request
        // (e.g. the user typing a page number past the end) costs two.
        //
        // A page number can also be far larger than the data could ever support
        // (e.g. a user typing 2000000000 at the prompt) - clamped to
        // _maxRepresentablePage first so (page - 1) * _pageSize can't overflow;
        // it then flows through the same out-of-range path as any other
        // past-the-end page and gets clamped again, down to the real last page.
        var firstAttemptPage = Math.Clamp(page, 1, _maxRepresentablePage);
        var firstAttemptSkip = checked((firstAttemptPage - 1) * _pageSize);

        var firstAttempt = await _gateway.GetPeopleAsync(_pageSize, firstAttemptSkip, ct);
        var totalPages = ComputeTotalPages(firstAttempt.TotalCount);

        if (firstAttemptPage <= totalPages)
            return new PagedResult<Person>(firstAttempt.Items, firstAttemptPage, totalPages, firstAttempt.TotalCount);

        var clampedPage = totalPages;
        var clampedSkip = checked((clampedPage - 1) * _pageSize);
        var corrected = await _gateway.GetPeopleAsync(_pageSize, clampedSkip, ct);

        // TotalPages is recomputed from `corrected`, not reused from `firstAttempt`,
        // so Items/TotalPages/TotalCount all describe the same snapshot even if the
        // backing data changed between the two calls. Page is left as the window we
        // actually requested (what Items came from) rather than re-clamped against
        // the new total - re-clamping it would make Page disagree with Items instead
        // of agreeing with the (possibly already stale again) total. In the rare case
        // the data shrank between calls, that can surface as e.g. "page 3 of 2" -
        // an honestly-labeled stale read, not a masked inconsistency, and next/prev
        // navigation in the caller still behaves correctly from there.
        var correctedTotalPages = ComputeTotalPages(corrected.TotalCount);
        return new PagedResult<Person>(corrected.Items, clampedPage, correctedTotalPages, corrected.TotalCount);
    }

    // An empty source is still "page 1 of 1", not "page 1 of 0" - there is always
    // at least one (empty) page to show. checked here means a total count too
    // large to represent as a page count throws instead of silently truncating.
    private int ComputeTotalPages(long totalCount) =>
        totalCount == 0 ? 1 : checked((int)Math.Ceiling(totalCount / (double)_pageSize));

    public async Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new ArgumentException("Search term must not be blank.", nameof(term));

        return await _gateway.SearchAsync(field, term.Trim(), ct);
    }

    public async Task<Person?> GetDetailsAsync(string userName, bool includeTrips = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Username must not be blank.", nameof(userName));

        return await _gateway.GetPersonAsync(userName.Trim(), includeTrips, ct);
    }
}
