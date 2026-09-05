using System.Xml.Linq;
using EggIdentity.Resilience;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace EggLedger.Web.Server.Auth;

public sealed class PostgresXmlRepository(NpgsqlDataSource source) : IXmlRepository {

    private static readonly RetryOptions RetryOpts = new() {
        MaxAttempts = 3,
        BaseDelay = TimeSpan.FromMilliseconds(100),
        ShouldRetry = ex => ex is NpgsqlException npg && npg.IsTransient,
    };

    public IReadOnlyCollection<XElement> GetAllElements() {
        return Retry.RunAsync(_ => Task.FromResult(GetAllElementsOnce()), RetryOpts).GetAwaiter().GetResult();
    }

    private List<XElement> GetAllElementsOnce() {
        var elements = new List<XElement>();
        using var cmd = source.CreateCommand("SELECT xml FROM data_protection_keys");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            elements.Add(XElement.Parse(reader.GetString(0)));
        }
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName) {
        Retry.RunAsync(_ => {
            StoreElementOnce(element, friendlyName);
            return Task.FromResult(true);
        }, RetryOpts).GetAwaiter().GetResult();
    }

    private void StoreElementOnce(XElement element, string friendlyName) {
        using var cmd = source.CreateCommand(
            "INSERT INTO data_protection_keys (friendly_name, xml) VALUES ($1, $2)");
        cmd.Parameters.AddWithValue(string.IsNullOrEmpty(friendlyName) ? (object)DBNull.Value : friendlyName);
        cmd.Parameters.AddWithValue(element.ToString(SaveOptions.DisableFormatting));
        cmd.ExecuteNonQuery();
    }
}
