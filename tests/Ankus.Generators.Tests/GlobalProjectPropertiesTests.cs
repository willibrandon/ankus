using Ankus.PgConfig;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies literal build properties participate in real MSBuild selection and reference evaluation.
/// </summary>
/// <param name="context">The active cancellation context.</param>
[TestClass]
public sealed class GlobalProjectPropertiesTests(TestContext context)
{
    /// <summary>
    /// A conditional custom property changes selection through all public evaluation entry points.
    /// </summary>
    [TestMethod]
    public async Task GlobalPropertiesSelectConditionalProjectAndReference()
    {
        string directory = Directory.CreateTempSubdirectory("ankus global selection ").FullName;
        try
        {
            string child = Path.Combine(directory, "extension.csproj");
            string parent = Path.Combine(directory, "tests.csproj");
            await File.WriteAllTextAsync(child, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AnkusPostgresMajor>18</AnkusPostgresMajor>
                    <AnkusPostgresMajor Condition="'$(ServerFlavor)' == 'alternate'">17</AnkusPostgresMajor>
                    <AnkusPgConfigPath>postgres/$(AnkusPostgresMajor)/pg_config</AnkusPgConfigPath>
                  </PropertyGroup>
                </Project>
                """, context.CancellationToken);
            await File.WriteAllTextAsync(parent, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AnkusPostgresMajor />
                    <AnkusPgConfigPath />
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="extension.csproj" /></ItemGroup>
                </Project>
                """, context.CancellationToken);
            var properties = new Dictionary<string, string> { ["ServerFlavor"] = "alternate" };
            PostgresProjectSettings direct = await PostgresProjectSettings.ReadAsync(child, "Release", properties,
                cancellationToken: context.CancellationToken);
            PostgresProjectSettings inherited = (await PostgresProjectSettings.TryReadAsync(parent, "Release", properties,
                cancellationToken: context.CancellationToken))!;
            PostgresProjectSettings solution = (await PostgresProjectSettings.TryReadAsync([child, parent], "Release", properties,
                cancellationToken: context.CancellationToken))!;
            foreach (PostgresProjectSettings selected in new[] { direct, inherited, solution })
            {
                Assert.IsNotNull(selected);
                Assert.AreEqual(17, selected.PostgresMajor);
                Assert.AreEqual(Path.Combine(directory, "postgres", "17", "pg_config"), selected.PgConfigPath);
            }

            Assert.AreEqual("alternate", properties["ServerFlavor"]);
            Assert.HasCount(1, properties);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// MSBuild separators, percent escapes and expansion syntax remain literal, including empty values.
    /// </summary>
    /// <param name="value">The literal global value.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("literal;comma,%3B,$(Other),@(Items),'quoted'=tail")]
    public async Task GlobalPropertyValuesRemainLiteral(string value)
    {
        string directory = Directory.CreateTempSubdirectory("ankus global literals ").FullName;
        try
        {
            string project = Path.Combine(directory, "literal.csproj");
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Other>expanded</Other>
                    <Literal>default</Literal>
                    <AnkusPostgresMajor>18</AnkusPostgresMajor>
                    <AnkusPgConfigPath>$(Literal)</AnkusPgConfigPath>
                  </PropertyGroup>
                </Project>
                """, context.CancellationToken);
            PostgresProjectSettings selected = await PostgresProjectSettings.ReadAsync(project, "Release",
                new Dictionary<string, string> { ["Literal"] = value }, cancellationToken: context.CancellationToken);
            Assert.AreEqual(18, selected.PostgresMajor);
            Assert.AreEqual(value.Length == 0 ? null : value, selected.PgConfigPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Contradictory canonical settings fail before evaluating imports or running a query process.
    /// </summary>
    /// <param name="property">The conflicting canonical property.</param>
    /// <param name="value">The forwarded value.</param>
    [TestMethod]
    [DataRow("Configuration", "Debug")]
    [DataRow("AnkusPostgresMajor", "17")]
    public async Task GlobalPropertiesRejectContradictorySelection(string property, string value)
    {
        string directory = Directory.CreateTempSubdirectory("ankus global conflicts ").FullName;
        try
        {
            string project = Path.Combine(directory, "conflict.csproj");
            await File.WriteAllTextAsync(project, "<Project><Import Project=\"must-not-evaluate.props\" /></Project>", context.CancellationToken);
            ArgumentException error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                PostgresProjectSettings.ReadAsync(project, "Release", new Dictionary<string, string> { [property] = value },
                    18, context.CancellationToken));
            Assert.Contains("Select the same", error.Message);
            Assert.DoesNotContain("must-not-evaluate", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
