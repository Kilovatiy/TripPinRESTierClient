namespace Client.Core.Models;

public class Person
{
    public string UserName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? MiddleName { get; set; }
    public PersonGender Gender { get; set; }
    public long? Age { get; set; }
    public IList<string> Emails { get; set; } = new List<string>();
    public string? FavoriteFeature { get; set; }
    public IList<string> Features { get; set; } = new List<string>();
    public IList<Location> AddressInfo { get; set; } = new List<Location>();
    public Location? HomeAddress { get; set; }

    /// <summary>Navigation property; populated only when $expand=Trips is requested.</summary>
    public IList<Trip> Trips { get; set; } = new List<Trip>();

    public string FullName =>
        string.IsNullOrWhiteSpace(MiddleName)
            ? $"{FirstName} {LastName}"
            : $"{FirstName} {MiddleName} {LastName}";
}

public enum PersonGender { Male, Female, Unknown }