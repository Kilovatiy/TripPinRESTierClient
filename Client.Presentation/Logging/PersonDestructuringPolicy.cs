using Client.Core.Models;
using Serilog.Core;
using Serilog.Events;

namespace Client.Presentation.Logging;

/// <summary>
/// Controls how a <see cref="Person"/> is rendered whenever it's logged as a
/// structured object (e.g. "{@Person}" in LoggingClientGateway).
///
/// Fail-closed by construction: this builds the logged projection from named
/// properties, so a new field added to <see cref="Person"/> is simply absent
/// from the log until someone deliberately adds a line for it below. A by-name
/// mask list is the more common way to do this, but it fails open instead: it
/// can silently miss a field nobody thought to add. This also makes renaming a
/// masked property a compile error here, not a silently-stale string.
/// </summary>
public sealed class PersonDestructuringPolicy : IDestructuringPolicy
{
    private const string Masked = "******";

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        if (value is not Person person)
        {
            result = null!;
            return false;
        }

        result = propertyValueFactory.CreatePropertyValue(new
        {
            person.UserName,
            person.Gender,
            person.Age,
            person.FavoriteFeature,
            person.Features,
            person.AddressInfo,
            person.HomeAddress,
            person.Trips,
            FirstName = Masked,
            LastName = Masked,
            MiddleName = Masked,
            FullName = Masked,
            Emails = Masked
        }, destructureObjects: true);

        return true;
    }
}
