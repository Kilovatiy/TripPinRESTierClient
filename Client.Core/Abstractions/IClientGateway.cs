using Client.Core.Exceptions;
using Client.Core.Models;

namespace Client.Core.Abstractions;

/// <summary>
/// Data access port for people. Implementations may throw
/// <see cref="ClientGatewayException"/> when the backing data source fails for
/// any reason other than "not found" - callers should not need to know or care
/// which concrete data source (or client library) is behind this interface.
/// </summary>
public interface IClientGateway
{
    /// <summary>
    /// One page of people, server-side paged ($top/$skip), stable order, plus the
    /// total count across all pages ($count=true on the same request - not a
    /// separate one).
    /// </summary>
    /// <exception cref="ClientGatewayException">The data source failed.</exception>
    Task<PeoplePage> GetPeopleAsync(int top, int skip, CancellationToken ct = default);

    /// <summary>Server-side filtered search ($filter) on the given field.</summary>
    /// <exception cref="ClientGatewayException">The data source failed.</exception>
    Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default);

    /// <summary>Single person by key; null when not found. Optionally expands Trips.</summary>
    /// <exception cref="ClientGatewayException">The data source failed for a reason other than "not found".</exception>
    Task<Person?> GetPersonAsync(string userName, bool includeTrips, CancellationToken ct = default);
}