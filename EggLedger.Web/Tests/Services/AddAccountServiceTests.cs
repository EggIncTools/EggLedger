using EggLedger.Domain.Api;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.Tests.Data;
using Ei;
using ProtoBuf;

namespace EggLedger.Web.Tests.Services;

public sealed class AddAccountServiceTests {
    private const string Eid = "EI1234567890123456";

    private static AddAccountService Make(FakeIndexedDb db, HttpMessageHandler handler, out IndexedDbAccountStore accounts) {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var api = new ApiClient(http);
        accounts = new IndexedDbAccountStore(new IndexedDbSettings(db));
        return new AddAccountService(api, accounts, new LocalApiPayloadDecoder(api));
    }

    private static string ToApiBody<T>(T msg) {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string ValidFirstContactBody() {
        var fc = new EggIncFirstContactResponse {
            Backup = new Backup {
                UserName = "Alice",
                game = new Backup.Game { SoulEggsD = 1_500_000_000, EggsOfProphecy = 7 },
                settings = new Backup.Settings { LastBackupTime = 1000 },
                ArtifactsDb = new ArtifactsDB(),
            },
        };
        return ToApiBody(fc);
    }

    private sealed class FirstContactHandler(string body, bool fail = false) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(fail
                ? new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("") }
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task AddsAndPersistsShapedAccount() {
        var db = new FakeIndexedDb();
        var sut = Make(db, new FirstContactHandler(ValidFirstContactBody()), out var accounts);

        var account = await sut.AddAccountAsync(Eid);

        Assert.Equal(Eid, account.Id);
        Assert.Equal("Alice", account.Nickname);
        Assert.Equal("1.50B", account.SeString);
        Assert.Equal(7, account.PeCount);

        var stored = await accounts.GetKnownAccountsAsync();
        Assert.Single(stored);
        Assert.Equal(Eid, stored[0].Id);
    }

    [Fact]
    public async Task InvalidBackupThrowsDoubleCheckMessage() {
        var db = new FakeIndexedDb();

        var sut = Make(db, new FirstContactHandler(ToApiBody(new EggIncFirstContactResponse())), out var accounts);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.AddAccountAsync(Eid));
        Assert.Contains("double check your ID", ex.Message, StringComparison.Ordinal);

        Assert.Empty(await accounts.GetKnownAccountsAsync());
    }
}
