using Client.Application;
using Client.Core.Abstractions;
using Client.Core.Models;
using Xunit;

namespace Client.Tests;

public class ClientServiceTests
{
    private static Person MakePerson(string userName, string firstName = "First", string lastName = "Last") => new()
    {
        UserName = userName,
        FirstName = firstName,
        LastName = lastName,
    };

    private static ClientService CreateSut(IEnumerable<Person> people, int pageSize = 10) =>
        CreateSutWithGateway(people, pageSize).Sut;

    private static (ClientService Sut, FakeClientGateway Gateway) CreateSutWithGateway(IEnumerable<Person> people, int pageSize = 10)
    {
        var gateway = new FakeClientGateway(people);
        var sut = new ClientService(gateway, new ClientOptions { ServiceUrl = "https://example.test/", PageSize = pageSize });
        return (sut, gateway);
    }

    private static List<Person> MakePeople(int count) =>
        Enumerable.Range(1, count).Select(i => MakePerson($"user{i:D3}")).ToList();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_ForNonPositivePageSize(int pageSize)
    {
        var gateway = new FakeClientGateway([]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ClientService(gateway, new ClientOptions { ServiceUrl = "https://example.test/", PageSize = pageSize }));
    }

    [Fact]
    public async Task GetPageAsync_ComputesTotalPages_UsingCeilingDivision()
    {
        var sut = CreateSut(MakePeople(25), pageSize: 10);

        var result = await sut.GetPageAsync(1);

        Assert.Equal(25, result.TotalCount);
        Assert.Equal(3, result.TotalPages); // ceil(25/10) = 3
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task GetPageAsync_ClampsPageNumber_BelowValidRange()
    {
        var sut = CreateSut(MakePeople(25), pageSize: 10);

        var result = await sut.GetPageAsync(0);

        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task GetPageAsync_ClampsPageNumber_AboveValidRange()
    {
        var sut = CreateSut(MakePeople(25), pageSize: 10);

        var result = await sut.GetPageAsync(999);

        Assert.Equal(3, result.Page);
        Assert.Equal(5, result.Items.Count); // last page: 25 - 2*10 = 5 items
    }

    [Fact]
    public async Task GetPageAsync_ClampsPageNumber_WhenSkipComputationWouldOverflow()
    {
        var sut = CreateSut(MakePeople(25), pageSize: 10);

        var result = await sut.GetPageAsync(int.MaxValue);

        Assert.Equal(3, result.Page);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsSinglePageOfOne_WhenSourceIsEmpty()
    {
        var sut = CreateSut([], pageSize: 10);

        var result = await sut.GetPageAsync(1);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(1, result.Page);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetPageAsync_MakesOneGatewayCall_ForAnInRangePage()
    {
        var (sut, gateway) = CreateSutWithGateway(MakePeople(25), pageSize: 10);

        await sut.GetPageAsync(2);

        Assert.Equal(1, gateway.GetPeopleAsyncCallCount);
    }

    [Fact]
    public async Task GetPageAsync_MakesTwoGatewayCalls_OnlyWhenRequestedPageIsBeyondTheLastPage()
    {
        var (sut, gateway) = CreateSutWithGateway(MakePeople(25), pageSize: 10);

        await sut.GetPageAsync(999);

        Assert.Equal(2, gateway.GetPeopleAsyncCallCount);
    }

    // A stand-in for the backing data changing between the two gateway calls that
    // an out-of-range page triggers: the first call reports a total of 25 (stale),
    // the corrective second call reports 15 (as of "now"), simulating rows deleted
    // in between.
    private sealed class SequencedGateway(params PeoplePage[] responses) : IClientGateway
    {
        private readonly Queue<PeoplePage> _responses = new(responses);

        public Task<PeoplePage> GetPeopleAsync(int top, int skip, CancellationToken ct = default) =>
            Task.FromResult(_responses.Dequeue());

        public Task<IReadOnlyList<Person>> SearchAsync(SearchField field, string term, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Person?> GetPersonAsync(string userName, bool includeTrips, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task GetPageAsync_UsesTheCorrectiveResponsesTotals_NotTheStaleFirstResponses()
    {
        var stalePage = new PeoplePage(MakePeople(0), TotalCount: 25);
        var freshPage = new PeoplePage(MakePeople(0), TotalCount: 15);
        var gateway = new SequencedGateway(stalePage, freshPage);
        var sut = new ClientService(gateway, new ClientOptions { ServiceUrl = "https://example.test/", PageSize = 10 });

        var result = await sut.GetPageAsync(999);

        // TotalCount/TotalPages come from the corrective (second, "fresher") call,
        // not the stale total the first call saw.
        Assert.Equal(15, result.TotalCount);
        Assert.Equal(2, result.TotalPages); // ceil(15/10) = 2, not ceil(25/10) = 3
    }

    [Fact]
    public async Task GetDetailsAsync_ReturnsNull_WhenPersonNotFound()
    {
        var sut = CreateSut(MakePeople(3));

        var result = await sut.GetDetailsAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDetailsAsync_ReturnsPerson_WhenFound()
    {
        var sut = CreateSut(MakePeople(3));

        var result = await sut.GetDetailsAsync("user002");

        Assert.NotNull(result);
        Assert.Equal("user002", result!.UserName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetDetailsAsync_Throws_ForBlankUserName(string? userName)
    {
        var sut = CreateSut(MakePeople(1));

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetDetailsAsync(userName!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_Throws_ForBlankTerm(string? term)
    {
        var sut = CreateSut(MakePeople(1));

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SearchAsync(SearchField.AnyName, term!));
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatches_ForKnownField()
    {
        var people = new List<Person>
        {
            MakePerson("russellwhyte", "Russell", "Whyte"),
            MakePerson("scottketchum", "Scott", "Ketchum"),
        };
        var sut = CreateSut(people);

        var result = await sut.SearchAsync(SearchField.FirstName, "russ");

        Assert.Single(result);
        Assert.Equal("russellwhyte", result[0].UserName);
    }
}
