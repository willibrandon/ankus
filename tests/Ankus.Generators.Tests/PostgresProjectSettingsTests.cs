using Ankus.PgConfig;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies PostgreSQL selection uses evaluated build properties without compiling an extension.
/// </summary>
/// <param name="context">The active test's cancellation context.</param>
[TestClass]
public sealed class PostgresProjectSettingsTests(TestContext context)
{
    /// <summary>
    /// Imported properties, configurations and project-relative paths participate in selection.
    /// </summary>
    [TestMethod]
    public async Task ProjectSelectionUsesImportsAndConfiguration()
    {
        string directory = Directory.CreateTempSubdirectory("ankus selection ").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, "<Import Project=\"selection.props\" />");
            await File.WriteAllTextAsync(Path.Combine(directory, "selection.props"), """
                <Project>
                  <PropertyGroup>
                    <AnkusPostgresMajor>17</AnkusPostgresMajor>
                    <AnkusPostgresMajor Condition="'$(Configuration)' == 'Shipping'">19</AnkusPostgresMajor>
                    <AnkusPgConfigPath>postgres/$(AnkusPostgresMajor)/pg_config</AnkusPgConfigPath>
                  </PropertyGroup>
                </Project>
                """, context.CancellationToken);

            PostgresProjectSettings debug = await PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: context.CancellationToken);
            PostgresProjectSettings shipping = await PostgresProjectSettings.ReadAsync(project, "Shipping", cancellationToken: context.CancellationToken);

            Assert.AreEqual(17, debug.PostgresMajor);
            Assert.AreEqual(Path.Combine(directory, "postgres", "17", "pg_config"), debug.PgConfigPath);
            Assert.AreEqual(19, shipping.PostgresMajor);
            Assert.AreEqual(Path.Combine(directory, "postgres", "19", "pg_config"), shipping.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Explicit build properties override project defaults and affect dependent path expressions.
    /// </summary>
    [TestMethod]
    public async Task ExplicitBuildMajorOverridesProject()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, """
                <PropertyGroup>
                  <AnkusPostgresMajor>18</AnkusPostgresMajor>
                  <AnkusPgConfigPath>postgres/$(AnkusPostgresMajor)/pg_config</AnkusPgConfigPath>
                </PropertyGroup>
                """);
            PostgresProjectSettings selection = await PostgresProjectSettings.ReadAsync(project, "Debug", 17, context.CancellationToken);

            Assert.AreEqual(17, selection.PostgresMajor);
            Assert.AreEqual(Path.Combine(directory, "postgres", "17", "pg_config"), selection.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// An undeclared selection retains the documented fallback while a bare executable retains PATH lookup.
    /// </summary>
    /// <param name="path">The optional project executable setting.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("pg_config")]
    public async Task UnspecifiedProjectSelectionUsesDefault(string path)
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, $"<PropertyGroup><AnkusPgConfigPath>{path}</AnkusPgConfigPath></PropertyGroup>");
            PostgresProjectSettings selection = await PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: context.CancellationToken);

            Assert.AreEqual(18, selection.PostgresMajor);
            Assert.AreEqual(path.Length == 0 ? null : path, selection.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Every supported boundary is accepted and invalid majors cannot silently fall back to 18.
    /// </summary>
    /// <param name="value">The evaluated property text.</param>
    /// <param name="expected">The accepted major, or null when rejected.</param>
    [TestMethod]
    [DataRow("13", 13)]
    [DataRow("19", 19)]
    [DataRow("12", null)]
    [DataRow("20", null)]
    [DataRow("17.1", null)]
    [DataRow("invalid", null)]
    public async Task ProjectMajorRequiresSupportedVersion(string value, int? expected)
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, $"<PropertyGroup><AnkusPostgresMajor>{value}</AnkusPostgresMajor></PropertyGroup>");
            if (expected is int major)
            {
                PostgresProjectSettings selection = await PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: context.CancellationToken);
                Assert.AreEqual(major, selection.PostgresMajor);
            }
            else
            {
                FormatException error = await Assert.ThrowsExactlyAsync<FormatException>(() =>
                    PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: context.CancellationToken));
                Assert.Contains("AnkusPostgresMajor", error.Message);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// MSBuild property separators and expressions remain literal configuration text.
    /// </summary>
    [TestMethod]
    public async Task ConfigurationIsOneLiteralProperty()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, """
                <PropertyGroup>
                  <AnkusPostgresMajor>17</AnkusPostgresMajor>
                  <AnkusPgConfigPath>$(Configuration)</AnkusPgConfigPath>
                </PropertyGroup>
                """);
            const string configuration = "Shipping;AnkusPostgresMajor=19,$(AnkusPostgresMajor)%27";
            PostgresProjectSettings selection = await PostgresProjectSettings.ReadAsync(project, configuration, cancellationToken: context.CancellationToken);

            Assert.AreEqual(17, selection.PostgresMajor);
            Assert.AreEqual(configuration, selection.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Evaluation errors retain the failing import instead of reporting only an empty stderr stream.
    /// </summary>
    [TestMethod]
    public async Task BrokenImportRetainsMsBuildDiagnostic()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, "<Import Project=\"missing-selection.props\" />");
            InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: context.CancellationToken));

            Assert.Contains("missing-selection.props", error.Message);
            Assert.Contains("MSB4019", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Invalid API inputs are rejected before starting evaluation.
    /// </summary>
    [TestMethod]
    public async Task InvalidProjectSelectionIsRejected()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, "");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => PostgresProjectSettings.ReadAsync(" ", "Debug", cancellationToken: context.CancellationToken));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => PostgresProjectSettings.ReadAsync(project, " ", cancellationToken: context.CancellationToken));
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => PostgresProjectSettings.ReadAsync(Path.Combine(directory, "missing.csproj"), "Debug", cancellationToken: context.CancellationToken));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => PostgresProjectSettings.ReadAsync(project, "Debug", 12, context.CancellationToken));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => PostgresProjectSettings.ReadAsync(project, "Debug", 20, context.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A canceled caller cannot start a new project evaluation.
    /// </summary>
    [TestMethod]
    public async Task CanceledProjectEvaluationStops()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, "");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A test project inherits the selected version from its evaluated extension reference.
    /// </summary>
    /// <param name="metadata">The reference's explicit global-property behavior.</param>
    /// <param name="expected">The selected extension major.</param>
    [TestMethod]
    [DataRow("", 17)]
    [DataRow("AdditionalProperties=\"Configuration=Special;AnkusPostgresMajor=19\"", 19)]
    [DataRow("SetConfiguration=\"Configuration=Special\"", 19)]
    [DataRow("SetConfiguration=\"Configuration=Shipping\" Properties=\"Configuration=Special\"", 19)]
    [DataRow("GlobalPropertiesToRemove=\"Configuration\"", 13)]
    public async Task TestProjectInheritsEvaluatedReferenceSelection(string metadata, int expected)
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string extensionDirectory = Directory.CreateDirectory(Path.Combine(directory, "extension")).FullName;
            string extension = await WriteProjectAsync(extensionDirectory, """
                <PropertyGroup>
                  <AnkusPostgresMajor>13</AnkusPostgresMajor>
                  <AnkusPostgresMajor Condition="'$(Configuration)' == 'Shipping'">17</AnkusPostgresMajor>
                  <AnkusPostgresMajor Condition="'$(Configuration)' == 'Special'">19</AnkusPostgresMajor>
                  <AnkusPgConfigPath>postgres/$(AnkusPostgresMajor)/pg_config</AnkusPgConfigPath>
                </PropertyGroup>
                """);
            string project = await WriteProjectAsync(directory, $"""
                <ItemGroup><ProjectReference Include="{System.Security.SecurityElement.Escape(extension)}" {metadata} /></ItemGroup>
                """);

            PostgresProjectSettings selection = await PostgresProjectSettings.ReadAsync(project, "Shipping", cancellationToken: context.CancellationToken);

            Assert.AreEqual(expected, selection.PostgresMajor);
            Assert.AreEqual(Path.Combine(extensionDirectory, "postgres", expected.ToString(System.Globalization.CultureInfo.InvariantCulture), "pg_config"), selection.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Conditional references participate and unrelated libraries do not invent a PostgreSQL 18 selection.
    /// </summary>
    [TestMethod]
    public async Task ConditionalReferencesIgnoreUnselectedAndUnrelatedProjects()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            await WriteProjectAsync(directory, "<PropertyGroup><AnkusPostgresMajor>17</AnkusPostgresMajor></PropertyGroup>", "Selected.csproj");
            await WriteProjectAsync(directory, "", "Library.csproj");
            string project = await WriteProjectAsync(directory, """
                <ItemGroup>
                  <ProjectReference Include="Selected.csproj" Condition="'$(Configuration)' == 'Shipping'" />
                  <ProjectReference Include="missing.csproj" Condition="'$(Configuration)' != 'Shipping'" />
                  <ProjectReference Include="Library.csproj" />
                </ItemGroup>
                """);

            PostgresProjectSettings selection = await PostgresProjectSettings.ReadAsync(project, "Shipping", cancellationToken: context.CancellationToken);

            Assert.AreEqual(17, selection.PostgresMajor);
            Assert.IsNull(selection.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Conflicting referenced installations require an explicit selection instead of depending on reference order.
    /// </summary>
    /// <param name="sameMajor">Whether paths conflict within one major.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConflictingProjectReferencesRequireExplicitSelection(bool sameMajor)
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            await WriteProjectAsync(directory, "<PropertyGroup><AnkusPostgresMajor>17</AnkusPostgresMajor></PropertyGroup>", "First.csproj");
            await WriteProjectAsync(directory, sameMajor
                ? "<PropertyGroup><AnkusPostgresMajor>17</AnkusPostgresMajor><AnkusPgConfigPath>other-pg-config</AnkusPgConfigPath></PropertyGroup>"
                : "<PropertyGroup><AnkusPostgresMajor>18</AnkusPostgresMajor></PropertyGroup>", "Second.csproj");
            string project = await WriteProjectAsync(directory, "<ItemGroup><ProjectReference Include=\"First.csproj\" /><ProjectReference Include=\"Second.csproj\" /></ItemGroup>");

            InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PostgresProjectSettings.ReadAsync(project, "Shipping", cancellationToken: context.CancellationToken));
            Assert.Contains("different PostgreSQL installations", error.Message);
            PostgresProjectSettings explicitSelection = await PostgresProjectSettings.ReadAsync(project, "Shipping", 19, context.CancellationToken);
            Assert.AreEqual(19, explicitSelection.PostgresMajor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Circular references fail promptly rather than recursively launching evaluation forever.
    /// </summary>
    [TestMethod]
    public async Task CircularProjectReferencesAreRejected()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-selection-").FullName;
        try
        {
            string project = await WriteProjectAsync(directory, "<ItemGroup><ProjectReference Include=\"Selection.csproj\" /></ItemGroup>");
            InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PostgresProjectSettings.ReadAsync(project, "Debug", cancellationToken: context.CancellationToken));
            Assert.Contains("circular project reference", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Creates an evaluation-only project whose default target fails if selection accidentally builds it.
    /// </summary>
    /// <param name="directory">The test's owned project directory.</param>
    /// <param name="content">The properties or imports under test.</param>
    /// <param name="fileName">The project file name within the owned directory.</param>
    /// <returns>The project path.</returns>
    private async Task<string> WriteProjectAsync(string directory, string content, string fileName = "Selection.csproj")
    {
        string path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, $"""
            <Project DefaultTargets="UnexpectedBuild">
              <PropertyGroup>
                <AnkusPostgresMajor />
                <AnkusPgConfigPath />
              </PropertyGroup>
              {content}
              <Target Name="UnexpectedBuild"><Error Text="Selection must not execute targets." /></Target>
            </Project>
            """, context.CancellationToken);
        return path;
    }
}
