using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises the public event trigger sample's database-wide attachment and extension lifecycle.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class EventTriggerLifecycleTests(TestContext context)
{
    /// <summary>
    /// Event triggers retain function bindings across relocation and disappear with the owning extension.
    /// </summary>
    [TestMethod]
    public Task EventTriggerSampleRelocatesAndReinstalls()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EventTriggerSampleRelocatesAndReinstalls), async (connection, transaction, token) =>
        {
            var notices = new List<string>();
            connection.Notice += (_, args) => notices.Add(args.Notice.MessageText);
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA event_first;
                CREATE SCHEMA event_second;
                CREATE EXTENSION ankus_event_triggers WITH SCHEMA event_first;
                CREATE TABLE event_first.created_before_move (id integer);
                SELECT evtfoid FROM pg_event_trigger WHERE evtname='ankus_report_table_creation'
                """, connection, transaction);
            uint originalOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            string[] before = ["CREATE TABLE: event_first.created_before_move"];
            Assert.AreSequenceEqual(before, notices);
            notices.Clear();
            command.CommandText = """
                ALTER EXTENSION ankus_event_triggers SET SCHEMA event_second;
                CREATE TABLE event_second."café table" (id integer);
                SELECT evtfoid='event_second.report_table_creation()'::regprocedure::oid
                    FROM pg_event_trigger WHERE evtname='ankus_report_table_creation'
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            string[] after = ["CREATE TABLE: event_second.\"café table\""];
            Assert.AreSequenceEqual(after, notices);
            notices.Clear();
            command.CommandText = """
                DROP EXTENSION ankus_event_triggers;
                CREATE TABLE event_first.created_without_trigger (id integer);
                SELECT NOT EXISTS(SELECT FROM pg_event_trigger WHERE evtname='ankus_report_table_creation')
                    AND to_regprocedure('event_second.report_table_creation()') IS NULL
                """;
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
            Assert.IsEmpty(notices);
            command.CommandText = """
                CREATE EXTENSION ankus_event_triggers WITH SCHEMA event_first;
                CREATE TABLE event_first.created_after_reinstall (id integer);
                SELECT evtfoid FROM pg_event_trigger WHERE evtname='ankus_report_table_creation'
                """;
            uint newOid = Assert.IsInstanceOfType<uint>(await command.ExecuteScalarAsync(token));
            Assert.AreNotEqual(originalOid, newOid);
            string[] reinstalled = ["CREATE TABLE: event_first.created_after_reinstall"];
            Assert.AreSequenceEqual(reinstalled, notices);
        }, context.CancellationToken);
}
