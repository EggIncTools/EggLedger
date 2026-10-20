using EggIdentity.UI;

namespace EggLedger.Web.State;

public sealed class AdminFaultDetailGate(AdminState admin) : IFaultDetailGate {
    public ValueTask<bool> CanSeeDetailAsync() => ValueTask.FromResult(admin.IsAdmin);
}
