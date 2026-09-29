using System.Runtime.InteropServices;
using Ankus.PgConfig;

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Reads exact embedded SQL from a real optimized, dead-stripped library with no metadata sidecars.
    /// </summary>
    [TestMethod]
    public async Task LinkedLibraryRetainsEmbeddedSchema()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ankus schema " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string rid = RuntimeInformation.RuntimeIdentifier;
            string filename = OperatingSystem.IsWindows() ? "schema.dll" : OperatingSystem.IsMacOS() ? "schema.dylib" : "schema.so";
            string library = Path.Combine(directory, filename);
            string source = Path.Combine(directory, "schema.c");
            const string Sql = "CREATE FUNCTION \"café 🐘\"() RETURNS integer AS 'MODULE_PATHNAME', 'ankus_fn_1' LANGUAGE c;\n";
            await File.WriteAllTextAsync(source, NativeSchemaEmitter.Emit("schema_probe", "0.1.0", filename, 18, rid, false, Sql), context.CancellationToken);
            string compiler = OperatingSystem.IsWindows() ? "clang-cl.exe" : "cc";
            string[] arguments = OperatingSystem.IsWindows()
                ? ["/nologo", "/W4", "/WX", "/O2", "/LD", "/MT", "/Fo" + Path.Combine(directory, "schema.obj"), source,
                    "/link", "/OPT:REF", "/OUT:" + library]
                : OperatingSystem.IsMacOS()
                    ? ["-Wall", "-Wextra", "-Werror", "-O2", "-dynamiclib", "-Wl,-dead_strip", source, "-o", library]
                    : ["-Wall", "-Wextra", "-Werror", "-O2", "-shared", "-fPIC", "-fdata-sections", "-Wl,--gc-sections", "-s", source, "-o", library];
            await RunAsync(compiler, arguments, directory);
            ExtensionSchema schema = ExtensionSchema.Read(library, rid);
            Assert.AreEqual("schema_probe", schema.Name);
            Assert.AreEqual("0.1.0", schema.Version);
            Assert.AreEqual(18, schema.Artifacts.PostgresMajor);
            Assert.AreEqual(rid, schema.Artifacts.RuntimeIdentifier);
            Assert.AreEqual(filename, schema.Artifacts.Library);
            Assert.AreEqual("schema_probe.control", schema.Artifacts.Control);
            Assert.AreEqual("schema_probe--0.1.0.sql", schema.Artifacts.Sql);
            Assert.IsFalse(schema.Relocatable);
            Assert.AreEqual(Sql, schema.Sql);
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }
}
