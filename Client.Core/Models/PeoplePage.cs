namespace Client.Core.Models;

/// <summary>
/// One server-side page of people, plus the total count across all pages -
/// returned together from a single request (OData $count=true alongside
/// $top/$skip), rather than as two separate round-trips.
/// </summary>
public sealed record PeoplePage(IReadOnlyList<Person> Items, long TotalCount);
