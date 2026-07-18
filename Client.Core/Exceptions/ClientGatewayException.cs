namespace Client.Core.Exceptions;

/// <summary>
/// Thrown by an <see cref="Abstractions.IClientGateway"/> implementation when the
/// backing data source fails (auth, network, server error, etc.) for any reason
/// other than "not found" (which the gateway represents as a null return instead,
/// see <see cref="Abstractions.IClientGateway.GetPersonAsync"/>).
/// Keeps outer layers (Application, Presentation) decoupled from whatever
/// third-party client library or transport a given IClientGateway implementation
/// happens to use under the hood.
/// </summary>
public class ClientGatewayException : Exception
{
    public ClientGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
