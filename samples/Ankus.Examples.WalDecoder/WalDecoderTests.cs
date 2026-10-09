namespace Ankus.Examples.WalDecoder;

/// <summary>
/// Ports pgrx's in-backend wal_decoder test and checks the remaining JSON shapes.
/// </summary>
/// <remarks>
/// Test publications install these functions; ordinary publications omit them. pgrx leaves its callback test empty
/// and ignored because a backend test runs inside an uncommitted transaction. The integration tests decode committed
/// changes through replication slots instead.
/// </remarks>
public static partial class WalDecoderTests
{
    /// <summary>
    /// A BEGIN action serializes only its type.
    /// </summary>
    [PgTest]
    public static void ActionBeginSerializesItsType() => Require(DecodedAction.Begin().ToJson(), """{"typ":"BEGIN"}""");

    /// <summary>
    /// COMMIT and row actions keep pgrx's member order and serde_json's escaping.
    /// </summary>
    [PgTest]
    public static void ActionsKeepMemberOrderAndEscaping()
    {
        Require(DecodedAction.Commit(779145498360779, 2).ToJson(), """{"typ":"COMMIT","committed":779145498360779,"change_count":2}""");
        var row = new DecodedRow([("name", "Bruce \"Batman\" Wayne\n<é>\u0001"), ("\"Age\"", 42)]);
        Require(new DecodedAction("UPDATE", Relation: "public.person", Old: DecodedRow.Empty, New: row).ToJson(),
            """{"typ":"UPDATE","rel":"public.person","old":{},"new":{"name":"Bruce \"Batman\" Wayne\n<é>\u0001","\"Age\"":42}}""");
        Require(new DecodedAction("DELETE", Relation: "\"My Schema\".t", Old: row).ToJson(),
            """{"typ":"DELETE","rel":"\"My Schema\".t","old":{"name":"Bruce \"Batman\" Wayne\n<é>\u0001","\"Age\"":42}}""");
    }

    /// <summary>
    /// Fails the backend test when the serialized text differs.
    /// </summary>
    private static void Require(string actual, string expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
        }
    }
}
