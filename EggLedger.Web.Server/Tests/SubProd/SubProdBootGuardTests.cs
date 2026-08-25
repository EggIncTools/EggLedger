using EggLedger.Web.Server.SubProd;
using Xunit;

namespace EggLedger.Web.Server.Tests.SubProd;

public class SubProdBootGuardTests {
    [Fact]
    public void ThrowsWhenDatabaseDoesNotMatch() {
        var connectionString = "Host=localhost;Username=x;Password=y;Database=eggledger";
        Assert.Throws<InvalidOperationException>(() => SubProdBootGuard.EnsureSubProdDatabase(connectionString));
    }

    [Fact]
    public void PassesWhenDatabaseMatches() {
        var connectionString = "Host=localhost;Username=x;Password=y;Database=eggledger_subprod";
        SubProdBootGuard.EnsureSubProdDatabase(connectionString);
    }

    [Fact]
    public void ThrowsWhenDatabaseIsMissingEntirely() {
        var connectionString = "Host=localhost;Username=x;Password=y";
        Assert.Throws<InvalidOperationException>(() => SubProdBootGuard.EnsureSubProdDatabase(connectionString));
    }
}
