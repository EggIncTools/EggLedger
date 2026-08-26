using EggLedger.Web.Server.Sync.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace EggLedger.Web.Server.Tests.Sync.Auth;

public class LoginCallbackMiddlewareTests {
    private static IQueryCollection Query(string queryString) =>
        new QueryCollection(QueryHelpers.ParseQuery(queryString));

    [Fact]
    public void BuildRedirectTarget_strips_code_keeps_other_params() {
        var target = LoginCallbackMiddleware.BuildRedirectTarget("/reports", Query("?code=abc123&tab=missions"));
        Assert.Equal("/reports?tab=missions", target);
    }

    [Fact]
    public void BuildRedirectTarget_strips_error_keeps_other_params() {
        var target = LoginCallbackMiddleware.BuildRedirectTarget("/", Query("?error=login_failed"));
        Assert.Equal("/", target);
    }

}
