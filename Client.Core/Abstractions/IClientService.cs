using Client.Core.Models;

namespace Client.Core.Abstractions;

public interface IClientService
{
    /// <summary>Returns the requested page; page numbers are clamped into the valid range.</summary>
    Task<PagedResult<Person>> GetPageAsync(int page, CancellationToken ct = default);

    /// <summary>Searches people by the given field. Throws ArgumentException for a blank term.</summary>
    Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default);

    /// <summary>Person details by username, or null when the person does not exist.</summary>
    Task<Person?> GetDetailsAsync(string userName, bool includeTrips = true, CancellationToken ct = default);
}