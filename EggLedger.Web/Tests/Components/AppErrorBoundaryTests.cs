using Bunit;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace EggLedger.Web.Tests.Components;

public class AppErrorBoundaryTests {
    [Fact]
    public void ChildLifecycleExceptionIsContainedNotPropagated() {
        using var ctx = new BunitContext();

        var cut = ctx.Render<AppErrorBoundary>(parameters => parameters
            .AddChildContent<ThrowingComponent>());

        cut.WaitForState(() => cut.Markup.Contains("Something went wrong."));

        Assert.Contains("Something went wrong.", cut.Markup);
    }

    [Fact]
    public void RecoverAllowsChildToRenderAgainAfterTransientFailure() {
        FlakyComponent.Attempts = 0;
        using var ctx = new BunitContext();

        var cut = ctx.Render<AppErrorBoundary>(parameters => parameters
            .AddChildContent<FlakyComponent>());

        cut.WaitForState(() => cut.Markup.Contains("Something went wrong."));

        cut.Find("button").Click();

        cut.WaitForState(() => cut.Markup.Contains("recovered"));

        Assert.DoesNotContain("Something went wrong.", cut.Markup);
    }

    private sealed class ThrowingComponent : ComponentBase {
        protected override async Task OnParametersSetAsync() {
            await Task.Yield();
            throw new InvalidOperationException("simulated uncaught component failure");
        }
    }

    private sealed class FlakyComponent : ComponentBase {
        public static int Attempts;

        protected override async Task OnParametersSetAsync() {
            Attempts++;
            await Task.Yield();
            if (Attempts == 1) {
                throw new InvalidOperationException("simulated transient failure");
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            builder.AddContent(0, "recovered");
        }
    }
}
