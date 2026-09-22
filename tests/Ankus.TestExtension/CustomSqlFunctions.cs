using Ankus;

[assembly: PgSql("sql-last", "INSERT INTO sql_install_order(label) VALUES ('final');", Order = PgSqlOrder.Finalize)]
[assembly: PgSql("sql-view", """
    CREATE VIEW ankus_sql.summary AS SELECT ankus_sql.custom_sql_value() AS value, message FROM ankus_sql.messages;
    INSERT INTO sql_install_order(label) VALUES ('view');
    """, Requires = ["sql-function", "sql-file"])]
[assembly: PgSqlFile("sql-file", "Sql/install.sql", Requires = ["sql-support"])]
[assembly: PgSql("sql-support", """
    CREATE FUNCTION ankus_sql.server_default() RETURNS integer LANGUAGE sql IMMUTABLE AS 'SELECT 41';
    INSERT INTO sql_install_order(label) VALUES ('support');
    """, Requires = ["sql-schema"], Before = ["sql-function"])]
[assembly: PgSql("sql-first", """
    CREATE TABLE sql_install_order(position bigint GENERATED ALWAYS AS IDENTITY, label text);
    INSERT INTO sql_install_order(label) VALUES ('bootstrap');
    """, Order = PgSqlOrder.Bootstrap)]

namespace Ankus.TestExtension;

/// <summary>
/// Exercises custom SQL ordering around a generated schema and a function whose default needs a custom SQL routine.
/// </summary>
[PgSchema("ankus_sql", Id = "sql-schema")]
public static class CustomSqlFunctions
{
    /// <summary>
    /// Uses a SQL default created by an explicitly required installation block.
    /// </summary>
    [PgFunction(Id = "sql-function", Requires = ["sql-support"])]
    public static int CustomSqlValue([PgParameter(Default = "ankus_sql.server_default()")] int value) => value + 1;
}
