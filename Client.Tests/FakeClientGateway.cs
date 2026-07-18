using Client.Core.Abstractions;
using Client.Core.Models;

namespace Client.Tests;

/// <summary>
/// In-memory <see cref="IClientGateway"/> used by unit tests so
/// <see cref="Client.Application.ClientService"/> can be tested without any network access.
/// Mirrors the server-side semantics (stable UserName order, contains-based search,
/// and a total count that reflects all matching people regardless of the current
/// page window) closely enough to exercise the business logic realistically.
/// </summary>
public class FakeClientGateway : IClientGateway
{
    private readonly List<Person> _people;

    public FakeClientGateway(IEnumerable<Person> people) => _people = people.ToList();

    /// <summary>Number of times <see cref="GetPeopleAsync"/> has been called - lets
    /// tests assert on round-trip count (e.g. one call for an in-range page,
    /// two for a page that needed clamping).</summary>
    public int GetPeopleAsyncCallCount { get; private set; }

    public Task<PeoplePage> GetPeopleAsync(int top, int skip, CancellationToken ct = default)
    {
        GetPeopleAsyncCallCount++;

        IReadOnlyList<Person> items = _people
            .OrderBy(p => p.UserName, StringComparer.Ordinal)
            .Skip(skip)
            .Take(top)
            .ToList();

        return Task.FromResult(new PeoplePage(items, _people.Count));
    }

    public Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default)
    {
        Func<Person, bool> matches = field switch
        {
            SearchField.FirstName => p => p.FirstName.Contains(term, StringComparison.OrdinalIgnoreCase),
            SearchField.LastName => p => p.LastName.Contains(term, StringComparison.OrdinalIgnoreCase),
            SearchField.UserName => p => p.UserName.Contains(term, StringComparison.OrdinalIgnoreCase),
            SearchField.AnyName => p => p.FirstName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                         || p.LastName.Contains(term, StringComparison.OrdinalIgnoreCase),
            _ => _ => false
        };

        IReadOnlyList<Person> results = _people
            .Where(matches)
            .OrderBy(p => p.UserName, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(results);
    }

    public Task<Person?> GetPersonAsync(string userName, bool includeTrips, CancellationToken ct = default)
    {
        var person = _people.FirstOrDefault(p => string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(person);
    }
}
