using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Core.Abstractions;
using Client.Core.Exceptions;
using Client.Core.Models;
using Client.Infrastructure;
using PanoramicData.OData.Client;
using Xunit;

namespace Client.Tests;

/// <summary>
/// Exercises <see cref="ClientGateway"/> against a fake HTTP transport (no real
/// network) to verify the actual $filter/$expand/$orderby/$top/$skip query shapes
/// it builds, its JSON deserialization, and its translation of OData failures
/// into <see cref="ClientGatewayException"/>. Mirrors the JsonSerializerOptions
/// Program.cs configures in production (case-insensitive + string enums) so
/// these tests catch the same deserialization issues production would hit.
/// </summary>
public class ClientGatewayTests
{
    private const string PersonJson =
        """{"UserName":"russellwhyte","FirstName":"Russell","LastName":"Whyte","Gender":"Male","Emails":["Russell@example.com"],"AddressInfo":[],"Features":[]}""";

    private static (ClientGateway Gateway, RecordingHttpMessageHandler Handler) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new RecordingHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var odataClient = new ODataClient(new ODataClientOptions
        {
            BaseUrl = "https://example.test/",
            HttpClient = httpClient,
            RetryCount = 0, // keep failure-path tests fast and deterministic
            JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            }
        });

        return (new ClientGateway(odataClient), handler);
    }

    [Fact]
    public async Task GetPeopleAsync_RequestsStableOrderAndCorrectPageWindow()
    {
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"value":[]}"""));

        await gateway.GetPeopleAsync(top: 10, skip: 20);

        var query = Assert.Single(handler.RequestUris).Query;
        Assert.Contains("$orderby=UserName", query);
        Assert.Contains("$skip=20", query);
        Assert.Contains("$top=10", query);
    }

    [Fact]
    public async Task GetPeopleAsync_RequestsCountOnTheSameRequest_NotASeparateOne()
    {
        // The whole point of returning PeoplePage instead of just the items: one
        // HTTP round-trip should carry both the page and the total count.
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"value":[]}"""));

        await gateway.GetPeopleAsync(top: 10, skip: 0);

        Assert.Single(handler.RequestUris);
        Assert.Contains("$count=true", Assert.Single(handler.RequestUris).Query);
    }

    [Fact]
    public async Task GetPeopleAsync_ParsesReturnedPeopleAndTotalCount()
    {
        var (gateway, _) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK,
                $$"""{"@odata.count":25,"value":[{{PersonJson}}]}"""));

        var page = await gateway.GetPeopleAsync(10, 0);

        var person = Assert.Single(page.Items);
        Assert.Equal("russellwhyte", person.UserName);
        Assert.Equal(PersonGender.Male, person.Gender);
        Assert.Equal(25, page.TotalCount);
    }

    [Theory]
    [InlineData(SearchField.FirstName, "$filter=(contains(tolower(FirstName),'russ'))")]
    [InlineData(SearchField.LastName, "$filter=(contains(tolower(LastName),'russ'))")]
    [InlineData(SearchField.UserName, "$filter=(contains(tolower(UserName),'russ'))")]
    [InlineData(SearchField.AnyName, "$filter=(contains(tolower(FirstName),'russ') or contains(tolower(LastName),'russ'))")]
    public async Task SearchAsync_BuildsExpectedFilter_PerField(SearchField field, string expectedFilter)
    {
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"value":[]}"""));

        await gateway.SearchAsync(field, "russ");

        var query = Uri.UnescapeDataString(Assert.Single(handler.RequestUris).Query);
        Assert.Contains(expectedFilter, query);
    }

    [Fact]
    public async Task SearchAsync_LowercasesTheSearchTerm_ForCaseInsensitiveMatching()
    {
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"value":[]}"""));

        // Mixed-case input: the gateway must lower-case both sides of contains(),
        // since OData's contains() is case-sensitive by default.
        await gateway.SearchAsync(SearchField.FirstName, "RuSs");

        var query = Uri.UnescapeDataString(Assert.Single(handler.RequestUris).Query);
        Assert.Contains("'russ'", query);
    }

    [Fact]
    public async Task GetPersonAsync_IncludeTripsTrue_AddsExpand()
    {
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PersonJson));

        await gateway.GetPersonAsync("russellwhyte", includeTrips: true);

        Assert.Contains("$expand=Trips", Assert.Single(handler.RequestUris).Query);
    }

    [Fact]
    public async Task GetPersonAsync_IncludeTripsFalse_OmitsExpand()
    {
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PersonJson));

        await gateway.GetPersonAsync("russellwhyte", includeTrips: false);

        Assert.DoesNotContain("$expand", Assert.Single(handler.RequestUris).Query);
    }

    [Fact]
    public async Task GetPersonAsync_RequestsByKey()
    {
        var (gateway, handler) = CreateSut(_ =>
            RecordingHttpMessageHandler.JsonResponse(HttpStatusCode.OK, PersonJson));

        await gateway.GetPersonAsync("russellwhyte", includeTrips: false);

        Assert.Contains("People('russellwhyte')", Assert.Single(handler.RequestUris).ToString());
    }

    [Fact]
    public async Task GetPersonAsync_ReturnsNull_On404()
    {
        var (gateway, _) = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var person = await gateway.GetPersonAsync("nosuchuser", includeTrips: true);

        Assert.Null(person);
    }

    [Fact]
    public async Task GetPersonAsync_ThrowsClientGatewayException_OnServerError()
    {
        var (gateway, _) = CreateSut(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });

        var ex = await Assert.ThrowsAsync<ClientGatewayException>(
            () => gateway.GetPersonAsync("russellwhyte", includeTrips: true));

        // The wrapped exception is a domain type: this call must not require the
        // caller to reference PanoramicData.OData.Client.Exceptions at all, only
        // that the InnerException (whatever it is) is preserved for diagnostics.
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task GetPeopleAsync_ThrowsClientGatewayException_OnServerError()
    {
        var (gateway, _) = CreateSut(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });

        await Assert.ThrowsAsync<ClientGatewayException>(() => gateway.GetPeopleAsync(10, 0));
    }

    [Fact]
    public async Task SearchAsync_ThrowsClientGatewayException_OnServerError()
    {
        var (gateway, _) = CreateSut(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });

        await Assert.ThrowsAsync<ClientGatewayException>(() => gateway.SearchAsync(SearchField.AnyName, "russ"));
    }
}
