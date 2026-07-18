using Client.Core.Models;
using Client.Presentation.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Client.Tests;

/// <summary>
/// Locks in the guarantee <see cref="PersonDestructuringPolicy"/> exists for: no
/// personal field value ever reaches a rendered log line, regardless of which
/// property on <see cref="Person"/> it came from - including the computed
/// <see cref="Person.FullName"/>, which is easy to miss since it isn't itself
/// one of the "raw" PII fields, even though it's built from two that are.
/// </summary>
public class PersonDestructuringPolicyTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        public LogEvent? LastEvent { get; private set; }
        public void Emit(LogEvent logEvent) => LastEvent = logEvent;
    }

    [Fact]
    public void Person_LoggedAsStructuredObject_NeverExposesPersonalFieldValues()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Destructure.With<PersonDestructuringPolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var person = new Person
        {
            UserName = "russellwhyte",
            FirstName = "Russell",
            LastName = "Whyte",
            MiddleName = "Quinn",
            Emails = ["russell@example.com"]
        };

        logger.Information("Person retrieved: {@Person}", person);

        var rendered = sink.LastEvent!.RenderMessage(null);
        Assert.DoesNotContain("Russell", rendered);
        Assert.DoesNotContain("Whyte", rendered);
        Assert.DoesNotContain("Quinn", rendered);
        Assert.DoesNotContain("russell@example.com", rendered);
        Assert.Contains("russellwhyte", rendered); // UserName is not masked
    }
}
