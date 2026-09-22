using Npgsql;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies geometric binary identity, collection ownership and backend validation under Native AOT.
/// </summary>
/// <param name="context">The current test context.</param>
[TestClass]
public sealed class GeometryDatumTests(TestContext context)
{
    /// <summary>
    /// Every scalar and array ownership path preserves the exact PostgreSQL binary representation.
    /// </summary>
    [TestMethod]
    [DataRow("point", "point", "point_send", "'(1.2345678901234567,-0)'")]
    [DataRow("point", "point", "point_send", "'(NaN,Infinity)'")]
    [DataRow("point", "point", "point_send", "NULL")]
    [DataRow("line", "line", "line_send", "'{1,-2,3}'")]
    [DataRow("line", "line", "line_send", "NULL")]
    [DataRow("lseg", "lseg", "lseg_send", "'[(4,-2),(-3,7)]'")]
    [DataRow("lseg", "lseg", "lseg_send", "NULL")]
    [DataRow("box", "box", "box_send", "'(1,8),(7,-2)'")]
    [DataRow("box", "box", "box_send", "'(NaN,-0),(Infinity,0)'")]
    [DataRow("box", "box", "box_send", "NULL")]
    [DataRow("circle", "circle", "circle_send", "'<(1.5,-2.5),3.25>'")]
    [DataRow("circle", "circle", "circle_send", "'<(0,0),NaN>'")]
    [DataRow("circle", "circle", "circle_send", "NULL")]
    [DataRow("path", "path", "path_send", "'[(1,2),(3,4),(-0,NaN)]'")]
    [DataRow("path", "path", "path_send", "'((1,2))'")]
    [DataRow("path", "path", "path_send", "NULL")]
    [DataRow("polygon", "polygon", "poly_send", "'((3,-2),(4,5),(-7,1))'")]
    [DataRow("polygon", "polygon", "poly_send", "'((1,2))'")]
    [DataRow("polygon", "polygon", "poly_send", "NULL")]
    [DataRow("points", "point[]", "array_send", "'[0:1][-1:0]={{\"(1,2)\",NULL},{\"(-0,NaN)\",\"(Infinity,-Infinity)\"}}'")]
    [DataRow("points", "point[]", "array_send", "'{}'")]
    [DataRow("points", "point[]", "array_send", "NULL")]
    [DataRow("lines", "line[]", "array_send", "ARRAY['{1,2,3}'::line,NULL,'{4,5,6}'::line]")]
    [DataRow("lsegs", "lseg[]", "array_send", "ARRAY['[(1,2),(3,4)]'::lseg,NULL]")]
    [DataRow("boxes", "box[]", "array_send", "ARRAY['(1,2),(3,4)'::box,NULL]")]
    [DataRow("circles", "circle[]", "array_send", "ARRAY['<(1,2),3>'::circle,NULL,'<(0,0),Infinity>'::circle]")]
    [DataRow("paths", "path[]", "array_send", "ARRAY['[(1,2),(3,4)]'::path,NULL,'((5,6))'::path]")]
    [DataRow("polygons", "polygon[]", "array_send", "ARRAY['((1,2),(3,4),(5,6))'::polygon,NULL]")]
    public Task GeometryOwnershipPathsPreserveBinaryValues(string function, string type, string send, string literal)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeometryOwnershipPathsPreserveBinaryValues), async (connection, transaction, token) =>
        {
            for (int mode = 0; mode <= 7; mode++)
            {
                await using var command = new NpgsqlCommand($"SELECT {send}(datatype.geometry_{function}(({literal})::{type}, {mode})) IS NOT DISTINCT FROM {send}(({literal})::{type})", connection, transaction);
                Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)), $"{function}, mode {mode}");
            }
        }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL text input and detached formatting preserve geometric values, including alternate syntax and precision.
    /// </summary>
    [TestMethod]
    [DataRow("point", "point_send", "1.2345678901234567,-0")]
    [DataRow("line", "line_send", "[(0,0),(1,1)]")]
    [DataRow("lseg", "lseg_send", "((1,2),(3,4))")]
    [DataRow("box", "box_send", "(1,9),(7,-2)")]
    [DataRow("circle", "circle_send", "<(1,2),Infinity>")]
    [DataRow("path", "path_send", "[(1,2),(3,4)]")]
    [DataRow("path", "path_send", "((1,2),(3,4))")]
    [DataRow("polygon", "poly_send", "((NaN,2),(-3,Infinity),(4,-5))")]
    public Task GeometryParsingMatchesPostgres(string type, string send, string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeometryParsingMatchesPostgres), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"SELECT {send}(datatype.geometry_parse($1,$2)::{type}) = {send}($2::{type})", connection, transaction);
            command.Parameters.AddWithValue(type);
            command.Parameters.AddWithValue(text);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Independently constructed coordinates retain their signed-zero and NaN payload bits in PostgreSQL wire output.
    /// </summary>
    [TestMethod]
    public Task GeometryConstructionUsesExactCoordinateBits()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeometryConstructionUsesExactCoordinateBits), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT encode(point_send(datatype.geometry_point_bits($1,$2)),'hex'),
                    encode(path_send(datatype.geometry_path_from_points(ARRAY[point(1,2),point(3,4)],true)),'hex'),
                    encode(poly_send(datatype.geometry_polygon_from_points(ARRAY[point(1,2),point(3,4)])),'hex'),
                    datatype.geometry_defaults()
                """, connection, transaction);
            command.Parameters.AddWithValue(0x7ff8000000001234L);
            command.Parameters.AddWithValue(long.MinValue);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            Assert.AreEqual("7ff80000000012348000000000000000", reader.GetString(0));
            Assert.AreEqual("01000000023ff0000000000000400000000000000040080000000000004010000000000000", reader.GetString(1));
            Assert.AreEqual("000000023ff0000000000000400000000000000040080000000000004010000000000000", reader.GetString(2));
            Assert.IsTrue(reader.GetBoolean(3));
        }, context.CancellationToken);

    /// <summary>
    /// Empty pgrx-compatible collections survive construction, SPI exchange and array transport with their closure flags.
    /// </summary>
    [TestMethod]
    public Task EmptyGeometricCollectionsRemainRepresentable()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(EmptyGeometricCollectionsRemainRepresentable), async (connection, transaction, token) =>
        {
            for (int mode = 0; mode <= 7; mode++)
            {
                await using var command = new NpgsqlCommand($"""
                    SELECT encode(path_send(datatype.geometry_path(datatype.geometry_path_from_points(ARRAY[]::point[],false),{mode})),'hex'),
                        encode(path_send(datatype.geometry_path(datatype.geometry_path_from_points(ARRAY[]::point[],true),{mode})),'hex'),
                        encode(poly_send(datatype.geometry_polygon(datatype.geometry_polygon_from_points(ARRAY[]::point[]),{mode})),'hex'),
                        npoints((datatype.geometry_polygons(ARRAY[datatype.geometry_polygon_from_points(ARRAY[]::point[])],{mode}))[1])
                    """, connection, transaction);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                Assert.IsTrue(await reader.ReadAsync(token));
                Assert.AreEqual("0000000000", reader.GetString(0));
                Assert.AreEqual("0100000000", reader.GetString(1));
                Assert.AreEqual("00000000", reader.GetString(2));
                Assert.AreEqual(0, reader.GetInt32(3));
            }
        }, context.CancellationToken);

    /// <summary>
    /// Managed polygon bounds match the backend, including NaN ordering, infinity and a single vertex.
    /// </summary>
    [TestMethod]
    [DataRow("((3,-2),(4,5),(-7,1))")]
    [DataRow("((NaN,Infinity),(-3,-Infinity),(4,0))")]
    [DataRow("((1,2))")]
    [DataRow("((-0,0),(0,-0))")]
    public Task GeometryBoundsMatchPostgres(string text)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeometryBoundsMatchPostgres), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT box_send(datatype.geometry_bounds($1::polygon)) = box_send(($1::polygon)::box)", connection, transaction);
            command.Parameters.AddWithValue(text);
            Assert.IsTrue(Assert.IsInstanceOfType<bool>(await command.ExecuteScalarAsync(token)));
        }, context.CancellationToken);

    /// <summary>
    /// Domain and compressed/external variable-length storage survives detoasting, cursors and collection copying.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task GeometryToastedStorageAndDomains(bool external)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeometryToastedStorageAndDomains), async (connection, transaction, token) =>
        {
            string storage = external ? "EXTERNAL" : "EXTENDED";
            await using var command = new NpgsqlCommand($"""
                CREATE DOMAIN pg_temp.geometry_polygon AS polygon;
                CREATE TEMP TABLE geometry_storage(p path, g pg_temp.geometry_polygon);
                ALTER TABLE geometry_storage ALTER COLUMN p SET STORAGE {storage};
                ALTER TABLE geometry_storage ALTER COLUMN g SET STORAGE {storage};
                INSERT INTO geometry_storage SELECT ('['||s||']')::path, ('('||s||')')::polygon
                    FROM (SELECT string_agg('(1,2)',',') s FROM generate_series(1,10000)) x;
                SELECT pg_column_size(p), pg_column_size(g),
                    path_send(datatype.geometry_path(p,4)) = path_send(p),
                    poly_send(datatype.geometry_polygon(g,5)) = poly_send(g),
                    array_send(datatype.geometry_paths(ARRAY[p,NULL],6)) = array_send(ARRAY[p,NULL])
                FROM geometry_storage
                """, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            Assert.IsTrue(await reader.ReadAsync(token));
            if (external)
            {
                Assert.IsGreaterThan(100000, reader.GetInt32(0));
                Assert.IsGreaterThan(100000, reader.GetInt32(1));
            }
            else
            {
                Assert.IsLessThan(10000, reader.GetInt32(0));
                Assert.IsLessThan(10000, reader.GetInt32(1));
            }

            Assert.IsTrue(reader.GetBoolean(2));
            Assert.IsTrue(reader.GetBoolean(3));
            Assert.IsTrue(reader.GetBoolean(4));
        }, context.CancellationToken);

    /// <summary>
    /// PostgreSQL validates raw fixed-size geometry after managed callbacks return.
    /// </summary>
    [TestMethod]
    [DataRow("line", "invalid line specification")]
    [DataRow("circle", "invalid radius")]
    public Task InvalidGeometryOutputRaisesNativeError(string type, string message)
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(InvalidGeometryOutputRaisesNativeError), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand($"SELECT datatype.geometry_invalid_{type}()", connection, transaction);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteScalarAsync(token));
            Assert.AreEqual("22P03", error.SqlState);
            Assert.Contains(message, error.MessageText);
        }, context.CancellationToken);

    /// <summary>
    /// Managed catches and finally blocks run after native input/conversion failures without losing successful session work.
    /// </summary>
    [TestMethod]
    public Task GeometryFailureRecoveryPreservesSession()
        => PostgresFixture.Cluster.RunInTransactionAsync(nameof(GeometryFailureRecoveryPreservesSession), async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("SELECT datatype.geometry_recovery()", connection, transaction);
            Assert.AreEqual("150:50:2:0", await command.ExecuteScalarAsync(token));
        }, context.CancellationToken);
}
