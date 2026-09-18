using EggIdentity.DbClone;
using EggLedger.Web.Server.SubProd;

return await CloneConsole.RunAsync(args, LedgerClonePlan.Plan, Environment.GetEnvironmentVariable);
