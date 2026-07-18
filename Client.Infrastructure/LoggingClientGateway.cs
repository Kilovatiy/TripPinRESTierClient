using Client.Core.Abstractions;
using Client.Core.Exceptions;
using Client.Core.Models;
using Microsoft.Extensions.Logging;

namespace Client.Infrastructure;

/// <summary>
/// Decorator over <see cref="IClientGateway"/> that adds structured logging,
/// including a Warning-level entry (visible on the console, unlike the
/// Information-level trail) whenever the inner gateway fails.
/// Kept separate from <see cref="ClientGateway"/> so the data-access code stays
/// free of cross-cutting concerns; composition happens in Program.cs.
/// Personal fields (Emails/FirstName/LastName/MiddleName/FullName - the last one
/// being a computed property that would otherwise leak the same PII under a
/// different name) are masked at the Serilog configuration level via
/// PersonDestructuringPolicy, so logging a full Person here via "{@Person}" is
/// safe.
/// </summary>
public class LoggingClientGateway : IClientGateway
{
    private readonly IClientGateway _inner;
    private readonly ILogger<LoggingClientGateway> _logger;

    public LoggingClientGateway(IClientGateway inner, ILogger<LoggingClientGateway> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PeoplePage> GetPeopleAsync(int top, int skip, CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting people page: top={Top}, skip={Skip}", top, skip);
        try
        {
            var page = await _inner.GetPeopleAsync(top, skip, ct);
            _logger.LogInformation("Received {Count} people (total {TotalCount})", page.Items.Count, page.TotalCount);
            return page;
        }
        catch (ClientGatewayException ex)
        {
            _logger.LogWarning(ex, "Failed to get people page: top={Top}, skip={Skip}", top, skip);
            throw;
        }
    }

    public async Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default)
    {
        _logger.LogInformation("Searching people by {Field}", field);
        try
        {
            var results = await _inner.SearchAsync(field, term, ct);
            _logger.LogInformation("Search by {Field} returned {Count} match(es)", field, results.Count);
            return results;
        }
        catch (ClientGatewayException ex)
        {
            _logger.LogWarning(ex, "Search by {Field} failed", field);
            throw;
        }
    }

    public async Task<Person?> GetPersonAsync(string userName, bool includeTrips, CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting person details: {UserName} (includeTrips={IncludeTrips})", userName, includeTrips);
        try
        {
            var person = await _inner.GetPersonAsync(userName, includeTrips, ct);

            if (person is null)
                _logger.LogInformation("Person not found: {UserName}", userName);
            else
                _logger.LogInformation("Person retrieved: {@Person}", person);

            return person;
        }
        catch (ClientGatewayException ex)
        {
            _logger.LogWarning(ex, "Failed to get person details: {UserName}", userName);
            throw;
        }
    }
}
