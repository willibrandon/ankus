using System.Text.Json;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class SerializedTypeTests
{
    private const string ImportedJson = """{"Member":{"é\u0000":7},"Mode":"ready\u0000","Variant":{"kind\u0000":"child\u0000","Value":9}}""";
    private const string ImportedCbor = "A3664D656D626572A163C3A90007644D6F6465667265616479006756617269616E74A2656B696E6400666368696C64006556616C756509";

    /// <summary>
    /// Exact imported strings survive published Native AOT JSON I/O, every ownership path and SQL NULL.
    /// </summary>
    /// <param name="mode">The direct or SPI ownership path.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task ImportedSerializationStringsCrossOwnershipPaths(int mode)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        string json = await Scalar<string>(connection, $"SELECT serialized_values.imported_contract($json${ImportedJson}$json$, {mode})::text");
        AssertImportedJson(json);
        Assert.IsTrue(await Scalar<bool>(connection, $"SELECT serialized_values.imported_contract(NULL, {mode}) IS NULL"));
        Assert.AreEqual("serialized_values.imported_contract", await Scalar<string>(connection,
            $"SELECT pg_typeof(serialized_values.imported_contract($json${ImportedJson}$json$, {mode}))::text"));
    }

    /// <summary>
    /// Independent binary COPY bytes preserve exact imported keys, enum names and discriminator identities.
    /// </summary>
    [TestMethod]
    public async Task ImportedSerializationBinaryMatchesIndependentFixture()
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        await Scalar<object>(connection, "CREATE TEMP TABLE serialized_binary(value serialized_values.imported_contract); SELECT 1");
        byte[] input = CopyPayload(Convert.FromHexString(ImportedCbor));
        await Import(connection, input);
        AssertImportedJson(await Scalar<string>(connection, "SELECT value::text FROM serialized_binary"));
        await using var output = new MemoryStream();
        await using (Stream copy = await connection.BeginRawBinaryCopyAsync("COPY serialized_binary TO STDOUT (FORMAT BINARY)", context.CancellationToken))
        {
            await copy.CopyToAsync(output, context.CancellationToken);
        }

        Assert.AreSequenceEqual(input, output.ToArray());
    }

    /// <summary>
    /// Truncated persisted identities are rejected without inserting rows or changing the backend session.
    /// </summary>
    /// <param name="exact">The exact identity in the independent CBOR fixture.</param>
    /// <param name="truncated">The same identity after the forbidden zero-character loss.</param>
    [TestMethod]
    [DataRow("63C3A900", "62C3A9")]
    [DataRow("66726561647900", "657265616479")]
    [DataRow("656B696E6400", "646B696E64")]
    [DataRow("666368696C6400", "656368696C64")]
    public async Task ImportedSerializationBinaryErrorsRecoverSameSession(string exact, string truncated)
    {
        await using NpgsqlConnection connection = await PostgresFixture.Cluster.OpenConnectionAsync(context.CancellationToken);
        int process = connection.ProcessID;
        await Scalar<object>(connection, "CREATE TEMP TABLE serialized_binary(value serialized_values.imported_contract); SELECT 1");
        byte[] invalid = CopyPayload(Convert.FromHexString(ImportedCbor.Replace(exact, truncated, StringComparison.Ordinal)));
        PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Import(connection, invalid));
        Assert.AreEqual("22P03", error.SqlState);
        Assert.AreEqual(0L, await Scalar<long>(connection, "SELECT count(*) FROM serialized_binary"));
        await Import(connection, CopyPayload(Convert.FromHexString(ImportedCbor)));
        Assert.AreEqual(1L, await Scalar<long>(connection, "SELECT count(*) FROM serialized_binary"));
        AssertImportedJson(await Scalar<string>(connection, "SELECT value::text FROM serialized_binary"));
        Assert.AreEqual(process, await Scalar<int>(connection, "SELECT pg_backend_pid()"));
    }

    /// <summary>
    /// Checks exact independent JSON identities without PostgreSQL's JSON zero-character restriction.
    /// </summary>
    private static void AssertImportedJson(string value)
    {
        using JsonDocument json = JsonDocument.Parse(value);
        JsonElement root = json.RootElement;
        Assert.HasCount(3, root.EnumerateObject());
        JsonElement member = root.GetProperty("Member");
        JsonProperty field = Assert.ContainsSingle(member.EnumerateObject());
        Assert.AreEqual("é\0", field.Name);
        Assert.AreEqual(7, field.Value.GetInt32());
        Assert.AreEqual("ready\0", root.GetProperty("Mode").GetString());
        JsonElement variant = root.GetProperty("Variant");
        Assert.HasCount(2, variant.EnumerateObject());
        Assert.AreEqual("child\0", variant.GetProperty("kind\0").GetString());
        Assert.AreEqual(9, variant.GetProperty("Value").GetInt32());
    }
}
