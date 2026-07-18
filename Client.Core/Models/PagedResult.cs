namespace Client.Core.Models;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int TotalPages,
    long TotalCount);