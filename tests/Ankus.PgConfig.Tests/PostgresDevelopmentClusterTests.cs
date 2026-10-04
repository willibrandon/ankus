namespace Ankus.PgConfig.Tests;

/// <summary>
/// Verifies exact development-server configuration and validation before native operations.
/// </summary>
[TestClass]
public sealed class PostgresDevelopmentClusterTests
{
    /// <summary>
    /// Each supported major receives its own pgrx-compatible port and local-only connection routing.
    /// </summary>
    /// <param name="major">The selected major.</param>
    /// <param name="port">Its independent default port.</param>
    [TestMethod]
    [DataRow(13, 28813)]
    [DataRow(14, 28814)]
    [DataRow(15, 28815)]
    [DataRow(16, 28816)]
    [DataRow(17, 28817)]
    [DataRow(18, 28818)]
    [DataRow(19, 28819)]
    public void DefaultConfigurationUsesIndependentLoopbackPorts(int major, int port)
    {
        string expected = $"port = {port}\nlisten_addresses = '127.0.0.1'\nunix_socket_directories = ''\n" +
            "log_destination = 'stderr'\nlogging_collector = off\n";
        Assert.AreEqual(expected, PostgresDevelopmentCluster.CreateConfiguration(major, new PostgresDevelopmentOptions())
            .ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Literal quotes, backslashes, equals signs, whitespace, empty values, and Unicode retain their exact meaning.
    /// </summary>
    [TestMethod]
    public void SettingsAreQuotedWithoutShellOrConfigurationExpansion()
    {
        var options = new PostgresDevelopmentOptions
        {
            Port = 15432,
            Settings = new Dictionary<string, string>
            {
                ["probe.text"] = " café='\\value # ; $(echo test) ",
                ["probe.empty"] = "",
                ["work_mem"] = "16MB",
            },
        };
        string actual = PostgresDevelopmentCluster.CreateConfiguration(18, options);
        Assert.StartsWith("probe.text = ' café=''\\\\value # ; $(echo test) '" + Environment.NewLine, actual);
        Assert.Contains("probe.empty = ''" + Environment.NewLine, actual);
        Assert.Contains("work_mem = '16MB'" + Environment.NewLine, actual);
        Assert.Contains("port = 15432" + Environment.NewLine, actual);
    }

    /// <summary>
    /// Explicit port and startup-timeout endpoints remain accepted.
    /// </summary>
    /// <param name="port">An endpoint port.</param>
    /// <param name="timeout">An endpoint timeout.</param>
    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(65535, 600)]
    public void AcceptsOptionBoundaries(int port, int timeout)
    {
        string configuration = PostgresDevelopmentCluster.CreateConfiguration(18,
            new PostgresDevelopmentOptions { Port = port, TimeoutSeconds = timeout });
        Assert.StartsWith($"port = {port}" + Environment.NewLine, configuration);
    }

    /// <summary>
    /// Values immediately outside supported bounds are rejected.
    /// </summary>
    /// <param name="port">The port to validate.</param>
    /// <param name="timeout">The startup timeout to validate.</param>
    [TestMethod]
    [DataRow(0, 60)]
    [DataRow(-1, 60)]
    [DataRow(65536, 60)]
    [DataRow(28818, 0)]
    [DataRow(28818, -1)]
    [DataRow(28818, 601)]
    public void RejectsOptionValuesOutsideBounds(int port, int timeout)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PostgresDevelopmentCluster.CreateConfiguration(18,
            new PostgresDevelopmentOptions { Port = port, TimeoutSeconds = timeout }));
    }

    /// <summary>
    /// Routing or ownership overrides cannot redirect Ankus to another cluster or authentication file.
    /// </summary>
    /// <param name="name">A setting managed by Ankus.</param>
    [TestMethod]
    [DataRow("data_directory")]
    [DataRow("config_file")]
    [DataRow("hba_file")]
    [DataRow("ident_file")]
    [DataRow("external_pid_file")]
    [DataRow("port")]
    [DataRow("LISTEN_ADDRESSES")]
    [DataRow("unix_socket_directories")]
    [DataRow("log_destination")]
    [DataRow("logging_collector")]
    [DataRow("event_source")]
    [DataRow("include")]
    [DataRow("include_dir")]
    [DataRow("include_if_exists")]
    public void RejectsManagedSettingOverrides(string name)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => PostgresDevelopmentCluster.CreateConfiguration(18,
            new PostgresDevelopmentOptions { Settings = new Dictionary<string, string> { [name] = "anything" } }));
        Assert.Contains(name, error.Message);
    }

    /// <summary>
    /// Configuration names and values cannot inject extra statements or terminate native text.
    /// </summary>
    /// <param name="name">A candidate setting name.</param>
    /// <param name="value">A candidate literal value.</param>
    [TestMethod]
    [DataRow("bad name", "value")]
    [DataRow("", "value")]
    [DataRow(" ", "value")]
    [DataRow("1name", "value")]
    [DataRow("work_mem='1MB' #", "value")]
    [DataRow("work_mem", "16MB\nport=5432")]
    [DataRow("work_mem", "16MB\rport=5432")]
    [DataRow("work_mem", "16MB\0")]
    public void RejectsConfigurationInjection(string name, string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => PostgresDevelopmentCluster.CreateConfiguration(18,
            new PostgresDevelopmentOptions { Settings = new Dictionary<string, string> { [name] = value } }));
    }

    /// <summary>
    /// Null option collections and values fail explicitly instead of becoming omitted settings.
    /// </summary>
    [TestMethod]
    public void RejectsNullSettingsAndValues()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PostgresDevelopmentCluster.CreateConfiguration(18,
            new PostgresDevelopmentOptions { Settings = null! }));
        Assert.ThrowsExactly<ArgumentNullException>(() => PostgresDevelopmentCluster.CreateConfiguration(18,
            new PostgresDevelopmentOptions { Settings = new Dictionary<string, string> { ["probe.text"] = null! } }));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PostgresDevelopmentCluster(null!));
    }
}
