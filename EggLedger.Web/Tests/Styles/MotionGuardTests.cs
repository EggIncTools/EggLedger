using EggIdentity.Styles.Css;

namespace EggLedger.Web.Tests.Styles;

public class MotionGuardTests {
    [Fact]
    public void App_and_scoped_sheets_pass_the_motion_guard() {
        var sheets = StylePaths.ScopedCssFiles()
            .Select(f => (Path.GetFileName(f), CssSheet.Load(f)))
            .Prepend(("app.css", StylePaths.AppSheet));
        var violations = sheets
            .SelectMany(x => MotionGuard.Check(x.Item2).Select(v => $"{x.Item1}: {v.Selector} {v.Property}: {v.Value} ({v.Reason})"))
            .ToList();
        Assert.Empty(violations);
    }
}
