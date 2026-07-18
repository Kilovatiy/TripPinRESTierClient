using Client.Core.Abstractions;
using Client.Core.Exceptions;
using Client.Core.Models;
using PanoramicData.OData.Client;
using PanoramicData.OData.Client.Exceptions;

namespace Client.Infrastructure;

/// <summary>
/// IClientGateway implementation backed by the TripPin OData v4 service via
/// PanoramicData.OData.Client. All server-side paging/filtering/expansion
/// lives here; callers never see raw OData query syntax, and OData-specific
/// failures are translated into <see cref="ClientGatewayException"/> so outer
/// layers never need to reference PanoramicData.OData.Client.Exceptions.
/// </summary>
public class ClientGateway : IClientGateway
{
    private const string PeopleEntitySet = "People";

    private readonly ODataClient _client;

    public ClientGateway(ODataClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<PeoplePage> GetPeopleAsync(int top, int skip, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            // Stable order is required: without it, $skip/$top can return duplicates
            // or gaps between pages because the server's default order is undefined.
            // $count=true rides along on the same request, so a page + its total
            // count is one round-trip, not two.
            var response = await _client.For<Person>(PeopleEntitySet)
                .OrderBy(p => p.UserName, false)
                .Skip(skip)
                .Top(top)
                .Count(true)
                .GetAsync(ct);

            return new PeoplePage(response.Value, response.Count ?? 0);
        }, "Failed to get a page of people.");

    public Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default) =>
        ExecuteAsync<IReadOnlyList<Person>>(async () =>
        {
            var query = _client.For<Person>(PeopleEntitySet).OrderBy(p => p.UserName, false);

            // Case-insensitive on both sides: OData's contains() is case-sensitive,
            // and a search box that only matches exact casing feels broken.
            var needle = term.ToLowerInvariant();

            // Expression-based filters, not string concatenation: the client library
            // builds the $filter=contains(tolower(...), ...) clause itself, so a
            // search term can never break out into arbitrary OData syntax.
            query = field switch
            {
                SearchField.FirstName => query.Filter(p => p.FirstName.ToLower().Contains(needle)),
                SearchField.LastName => query.Filter(p => p.LastName.ToLower().Contains(needle)),
                SearchField.UserName => query.Filter(p => p.UserName.ToLower().Contains(needle)),
                SearchField.AnyName => query.Filter(p => p.FirstName.ToLower().Contains(needle) || p.LastName.ToLower().Contains(needle)),
                _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown search field.")
            };

            var response = await query.GetAsync(ct);
            return response.Value;
        }, "Failed to search people.");

    public async Task<Person?> GetPersonAsync(string userName, bool includeTrips, CancellationToken ct = default)
    {
        var query = _client.For<Person>(PeopleEntitySet);
        if (includeTrips)
            query = query.Expand(p => p.Trips);

        try
        {
            return await _client.GetByKeyOrDefaultAsync<Person, string>(userName, query, ct);
        }
        catch (ODataNotFoundException)
        {
            return null;
        }
        catch (ODataClientException ex)
        {
            throw new ClientGatewayException($"Failed to get person '{userName}'.", ex);
        }
    }

    // ODataNotFoundException is deliberately NOT caught here: GetPersonAsync is the
    // only place "not found" is a legitimate, expected outcome (-> null); everywhere
    // else a 404 is as unexpected as any other server failure.
    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string failureMessage)
    {
        try
        {
            return await operation();
        }
        catch (ODataClientException ex)
        {
            throw new ClientGatewayException(failureMessage, ex);
        }
    }
}
