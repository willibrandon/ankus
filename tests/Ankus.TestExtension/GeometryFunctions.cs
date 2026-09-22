namespace Ankus.TestExtension;

/// <summary>
/// Exercises geometric values through each native ownership path and input/output error boundary.
/// </summary>
public static class GeometryFunctions
{
    /// <summary>
    /// Exchanges nullable point values.
    /// </summary>
    [PgFunction]
    public static PgPoint? GeometryPoint(PgPoint? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges nullable line coefficients.
    /// </summary>
    [PgFunction]
    public static PgLine? GeometryLine(PgLine? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges nullable line segments.
    /// </summary>
    [PgFunction]
    public static PgLineSegment? GeometryLseg(PgLineSegment? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges nullable boxes.
    /// </summary>
    [PgFunction]
    public static PgBox? GeometryBox(PgBox? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges nullable circles.
    /// </summary>
    [PgFunction]
    public static PgCircle? GeometryCircle(PgCircle? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges owned nullable paths.
    /// </summary>
    [PgFunction]
    public static PgPath? GeometryPath(PgPath? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges owned nullable polygons.
    /// </summary>
    [PgFunction]
    public static PgPolygon? GeometryPolygon(PgPolygon? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped point arrays with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgPoint?>? GeometryPoints(PgArray<PgPoint?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped line arrays with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgLine?>? GeometryLines(PgArray<PgLine?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges segment vectors with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgLineSegment?[]? GeometryLsegs(PgLineSegment?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped box arrays with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgBox?>? GeometryBoxes(PgArray<PgBox?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges circle vectors with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgCircle?[]? GeometryCircles(PgCircle?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges shaped path arrays with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgArray<PgPath?>? GeometryPaths(PgArray<PgPath?>? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Exchanges polygon vectors with nullable elements.
    /// </summary>
    [PgFunction]
    public static PgPolygon?[]? GeometryPolygons(PgPolygon?[]? value, int mode) => ArrayFunctions.Exchange(value, mode);

    /// <summary>
    /// Parses each geometric type through native input and formats its detached coordinates.
    /// </summary>
    [PgFunction]
    public static string GeometryParse(string type, string text) => type switch
    {
        "point" => PgPoint.Parse(text).ToString(),
        "line" => PgLine.Parse(text).ToString(),
        "lseg" => PgLineSegment.Parse(text).ToString(),
        "box" => PgBox.Parse(text).ToString(),
        "circle" => PgCircle.Parse(text).ToString(),
        "path" => PgPath.Parse(text).ToString(),
        "polygon" => PgPolygon.Parse(text).ToString(),
        _ => throw new ArgumentException("Unknown geometry.", nameof(type)),
    };

    /// <summary>
    /// Constructs exact signed-zero or nonfinite coordinate bit patterns independently of geometric input.
    /// </summary>
    [PgFunction]
    public static PgPoint GeometryPointBits(long x, long y) => new(BitConverter.Int64BitsToDouble(x), BitConverter.Int64BitsToDouble(y));

    /// <summary>
    /// Constructs a path from managed points, including empty collections and closure changes.
    /// </summary>
    [PgFunction]
    public static PgPath GeometryPathFromPoints(PgPoint[] points, bool closed) => new(points, closed);

    /// <summary>
    /// Constructs a polygon from managed points, including empty collections.
    /// </summary>
    [PgFunction]
    public static PgPolygon GeometryPolygonFromPoints(PgPoint[] points) => new(points);

    /// <summary>
    /// Returns the managed bounding-box calculation for comparison with PostgreSQL's polygon-to-box cast.
    /// </summary>
    [PgFunction]
    public static PgBox GeometryBounds(PgPolygon value) => value.BoundingBox;

    /// <summary>
    /// Produces an invalid line to test native validation after the managed callback returns.
    /// </summary>
    [PgFunction]
    public static PgLine GeometryInvalidLine() => default;

    /// <summary>
    /// Produces an invalid circle to test native validation after the managed callback returns.
    /// </summary>
    [PgFunction]
    public static PgCircle GeometryInvalidCircle() => new(default, -1);

    /// <summary>
    /// Verifies SQL defaults match the valid zero-initialized fixed-size geometric values.
    /// </summary>
    [PgFunction]
    public static bool GeometryDefaults(PgPoint point = default, PgLineSegment segment = default, PgBox box = default, PgCircle circle = default)
        => point == default && segment == default && box == default && circle == default;

    /// <summary>
    /// Recovers from parsing and binary conversion errors while keeping writes, plans and memory contexts intact.
    /// </summary>
    [PgFunction]
    public static string GeometryRecovery() => Spi.Connect(session =>
    {
        session.Execute("CREATE TEMP TABLE geometry_writes(value int); INSERT INTO geometry_writes VALUES (1)");
        using SpiPreparedStatement plan = session.Prepare("SELECT count(*) FROM geometry_writes");
        const string contexts = "SELECT count(*) FROM pg_backend_memory_contexts WHERE name IN ('Ankus SPI operation', 'Ankus error diagnostics', 'CurTransactionContext')";
        long before = session.ExecuteScalar<long>(contexts);
        int failures = 0;
        int finalized = 0;
        for (int i = 0; i < 50; i++)
        {
            if (!PgLine.TryParse("{0,0,0}", out _) && !PgCircle.TryParse("<(0,0),-1>", out _) && !PgPolygon.TryParse("()", out _))
            {
                failures++;
            }

            try
            {
                _ = session.ExecuteScalar<PgLine>("SELECT $1", SpiParameter.Create(default(PgLine)));
            }
            catch (PgException error) when (error.SqlState == "22P03")
            {
                failures++;
            }
            finally
            {
                finalized++;
            }

            try
            {
                _ = session.ExecuteScalar<PgCircle>("SELECT $1", SpiParameter.Create(new PgCircle(default, -1)));
            }
            catch (PgException error) when (error.SqlState == "22P03")
            {
                failures++;
            }
        }

        session.Execute("INSERT INTO geometry_writes VALUES (2)");
        return $"{failures}:{finalized}:{plan.ExecuteScalar<long>()}:{session.ExecuteScalar<long>(contexts) - before}";
    });
}
