namespace Client.Application;

/// <summary>Configuration bound from the "Client" section of appsettings.json.</summary>
public sealed class ClientOptions
{
    public string ServiceUrl { get; set; } = "";
    public int PageSize { get; set; } = 10;
}
