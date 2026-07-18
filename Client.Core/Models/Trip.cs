namespace Client.Core.Models;

public class Trip
{
    public int TripId { get; set; }
    public string ShareId { get; set; } = "";
    public string Name { get; set; } = "";
    public float? Budget { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public IList<string> Tags { get; set; } = new List<string>();
}