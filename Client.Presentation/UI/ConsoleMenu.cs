using Client.Core.Abstractions;
using Client.Core.Exceptions;

namespace Client.Presentation.UI;

public class ConsoleMenu
{
    private readonly IClientService _clientService;

    public ConsoleMenu(IClientService client) =>
        _clientService = client ?? throw new ArgumentNullException(nameof(client));

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("=== People Explorer (TripPin OData v4) ===");

        while (!ct.IsCancellationRequested)
        {
            Console.WriteLine();
            Console.WriteLine("1) List people (paged)");
            Console.WriteLine("2) Search people");
            Console.WriteLine("3) Person details");
            Console.WriteLine("0) Exit");
            Console.Write("> ");

            var choice = Console.ReadLine()?.Trim();
            try
            {
                switch (choice)
                {
                    case "1": await ListPeopleAsync(ct); break;
                    case "2": await SearchAsync(ct); break;
                    case "3": await DetailsAsync(ct); break;
                    case "0": return;
                    default: Console.WriteLine("Unknown option."); break;
                }
            }
            catch (ClientGatewayException ex)
            {
                // Everything the gateway can fail with (auth, server errors, transient
                // network failures the client library's own retries didn't recover
                // from, ...) arrives here as one type - the menu doesn't need to know
                // which third-party client or transport is behind IClientGateway.
                Console.WriteLine($"Service error: {ex.Message}");
                Console.WriteLine("Try again, or check the service status.");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Network error: {ex.Message}");
                Console.WriteLine("Check your connection and try again.");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("The request timed out. Try again.");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid input: {ex.Message}");
            }
        }
    }

    private async Task ListPeopleAsync(CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await _clientService.GetPageAsync(page, ct);
            Console.WriteLine();
            Console.WriteLine($"--- People (page {result.Page}/{result.TotalPages}, total {result.TotalCount}) ---");

            if (result.Items.Count == 0)
            {
                Console.WriteLine("No people found.");
                return;
            }

            foreach (var p in result.Items)
                Console.WriteLine($"  {p.UserName,-18} {p.FullName}");

            Console.Write("[n]ext, [p]revious, page number, or [q] back: ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (input == "q" || string.IsNullOrEmpty(input)) return;
            if (input == "n")
            {
                if (result.Page < result.TotalPages) page = result.Page + 1;
                continue;
            }

            if (input == "p")
            {
                if (result.Page > 1) page = result.Page - 1;
                continue;
            }

            if (int.TryParse(input, out var requested))
            {
                page = requested;
                continue;
            }

            Console.WriteLine("Unrecognized command.");
        }
    }

    private async Task SearchAsync(CancellationToken ct)
    {
        Console.WriteLine("Search in: 1) First or last name  2) First name  3) Last name  4) Username");
        Console.Write("> ");
        var fieldChoice = Console.ReadLine()?.Trim();
        var field = fieldChoice switch
        {
            "2" => SearchField.FirstName,
            "3" => SearchField.LastName,
            "4" => SearchField.UserName,
            _ => SearchField.AnyName
        };

        Console.Write("Search term: ");
        var term = Console.ReadLine() ?? "";

        var results = await _clientService.SearchAsync(field, term, ct);

        Console.WriteLine();
        if (results.Count == 0)
        {
            Console.WriteLine("No matches.");
            return;
        }

        Console.WriteLine($"--- {results.Count} match(es) ---");
        foreach (var p in results)
            Console.WriteLine($"  {p.UserName,-18} {p.FullName}");
    }

    private async Task DetailsAsync(CancellationToken ct)
    {
        Console.Write("Username (e.g. russellwhyte): ");
        var userName = Console.ReadLine() ?? "";

        var person = await _clientService.GetDetailsAsync(userName, includeTrips: true, ct);
        if (person is null)
        {
            Console.WriteLine($"Person '{userName.Trim()}' was not found.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"--- {person.FullName} ---");
        Console.WriteLine($"  Username : {person.UserName}");
        Console.WriteLine($"  Gender   : {person.Gender}");
        Console.WriteLine($"  Age      : {(person.Age.HasValue ? person.Age.Value.ToString() : "n/a")}");
        Console.WriteLine($"  Emails   : {(person.Emails.Count > 0 ? string.Join(", ", person.Emails) : "none")}");
        Console.WriteLine($"  Favorite : {(person.FavoriteFeature ?? "n/a")}");
        Console.WriteLine($"  Features : {(person.Features.Count > 0 ? string.Join(", ", person.Features) : "none")}");

        if (person.AddressInfo.Count > 0)
        {
            Console.WriteLine("  Addresses:");
            foreach (var a in person.AddressInfo)
            {
                var city = a.City is null ? "" : $" ({a.City.Name}, {a.City.Region}, {a.City.CountryRegion})";
                Console.WriteLine($"    - {a.Address}{city}");
            }
        }

        if (person.Trips.Count > 0)
        {
            Console.WriteLine("  Trips:");
            foreach (var t in person.Trips)
                Console.WriteLine(
                    $"    - [{t.TripId}] {t.Name} ({t.StartsAt:yyyy-MM-dd} -> {t.EndsAt:yyyy-MM-dd}, budget {t.Budget?.ToString("0.##") ?? "n/a"})");
        }
        else
        {
            Console.WriteLine("  Trips    : none");
        }
    }
}