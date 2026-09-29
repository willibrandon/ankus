namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies persistent port selection and validation without creating PostgreSQL processes.
/// </summary>
[TestClass]
public sealed class PostgresRegistryPortTests
{
    /// <summary>
    /// Missing settings retain independent development and testing defaults for every supported major.
    /// </summary>
    /// <param name="major">The selected major.</param>
    /// <param name="development">The expected development port.</param>
    /// <param name="testing">The expected testing port.</param>
    [TestMethod]
    [DataRow(13, 28813, 32213)]
    [DataRow(14, 28814, 32214)]
    [DataRow(15, 28815, 32215)]
    [DataRow(16, 28816, 32216)]
    [DataRow(17, 28817, 32217)]
    [DataRow(18, 28818, 32218)]
    [DataRow(19, 28819, 32219)]
    public void MissingPortSettingsUseIndependentDefaults(int major, int development, int testing)
    {
        WithConfiguration(null, registry =>
        {
            Assert.AreEqual(development, registry.GetPort(major));
            Assert.AreEqual(testing, registry.GetTestPort(major));
            Assert.IsEmpty(Directory.GetFileSystemEntries(registry.HomeDirectory));
        });
    }

    /// <summary>
    /// Configured bases and their endpoints are read independently and never rewrite the configuration.
    /// </summary>
    /// <param name="configuration">The persisted settings.</param>
    /// <param name="major">The selected major.</param>
    /// <param name="development">The expected development port.</param>
    /// <param name="testing">The expected testing port.</param>
    [TestMethod]
    [DataRow("{\"basePort\":30000,\"baseTestingPort\":31000,\"custom\":true}", 18, 30018, 31018)]
    [DataRow("{\"basePort\":0,\"baseTestingPort\":65516}", 19, 19, 65535)]
    [DataRow("{\"basePort\":65516,\"baseTestingPort\":0}", 13, 65529, 13)]
    [DataRow("{\"basePort\":30000}", 18, 30018, 32218)]
    [DataRow("{\"baseTestingPort\":31000}", 18, 28818, 31018)]
    public void PersistedPortsPreserveValuesAndUnselectedDefaults(string configuration, int major, int development, int testing)
    {
        WithConfiguration(configuration, registry =>
        {
            Assert.AreEqual(development, registry.GetPort(major));
            Assert.AreEqual(testing, registry.GetTestPort(major));
            Assert.AreEqual(configuration, File.ReadAllText(registry.ConfigurationPath));
        });
    }

    /// <summary>
    /// Invalid persisted values fail rather than silently using defaults or overflowing the TCP port.
    /// </summary>
    /// <param name="value">A JSON value that is not a valid port base.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("true")]
    [DataRow("\"30000\"")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("1.5")]
    [DataRow("-1")]
    [DataRow("65517")]
    [DataRow("2147483648")]
    public void MalformedPortSettingsFailWithoutChangingConfiguration(string value)
    {
        string configuration = "{\"basePort\":" + value + ",\"baseTestingPort\":" + value + "}";
        WithConfiguration(configuration, registry =>
        {
            FormatException development = Assert.ThrowsExactly<FormatException>(() => registry.GetPort(18));
            FormatException testing = Assert.ThrowsExactly<FormatException>(() => registry.GetTestPort(18));
            Assert.Contains("basePort", development.Message);
            Assert.Contains("baseTestingPort", testing.Message);
            Assert.AreEqual(configuration, File.ReadAllText(registry.ConfigurationPath));
        });
    }

    /// <summary>
    /// Unsupported majors cannot produce a seemingly usable port outside the supported version contract.
    /// </summary>
    /// <param name="major">The unsupported major.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(12)]
    [DataRow(20)]
    [DataRow(int.MaxValue)]
    public void UnsupportedMajorsCannotSelectPorts(int major)
    {
        WithConfiguration(null, registry =>
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => registry.GetPort(major));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => registry.GetTestPort(major));
            Assert.IsEmpty(Directory.GetFileSystemEntries(registry.HomeDirectory));
        });
    }

    /// <summary>
    /// A null selection preserves settings while both allowed base endpoints remain representable.
    /// </summary>
    [TestMethod]
    public void PortOptionsPreserveUnspecifiedValuesAndAcceptEndpoints()
    {
        var omitted = new PostgresPortOptions();
        Assert.IsNull(omitted.BasePort);
        Assert.IsNull(omitted.BaseTestingPort);
        var selected = new PostgresPortOptions(0, 65516);
        Assert.AreEqual<int?>(0, selected.BasePort);
        Assert.AreEqual<int?>(65516, selected.BaseTestingPort);
    }

    /// <summary>
    /// A change validates before registry or provisioning work can begin.
    /// </summary>
    /// <param name="value">The invalid base.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(65517)]
    [DataRow(int.MaxValue)]
    public void PortOptionsRejectInvalidBases(int value)
    {
        ArgumentOutOfRangeException development = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PostgresPortOptions(basePort: value));
        ArgumentOutOfRangeException testing = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PostgresPortOptions(baseTestingPort: value));
        Assert.AreEqual("basePort", development.ParamName);
        Assert.AreEqual("baseTestingPort", testing.ParamName);
    }

    private static void WithConfiguration(string? configuration, Action<PostgresRegistry> verify)
    {
        string home = Directory.CreateTempSubdirectory("ankus-port-settings-").FullName;
        try
        {
            var registry = new PostgresRegistry(home);
            if (configuration is not null)
            {
                File.WriteAllText(registry.ConfigurationPath, configuration);
            }

            verify(registry);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
