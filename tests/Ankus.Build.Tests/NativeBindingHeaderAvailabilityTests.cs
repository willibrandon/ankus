using System.Text.Json;

namespace Ankus.Build.Tests;

/// <summary>
/// Verifies explicit selected-header availability without accepting corrupt or nested declarations.
/// </summary>
[TestClass]
public sealed class NativeBindingHeaderAvailabilityTests
{
    private const string Inventory = """
        extern "C" {
            #[link_name = "native_run"]
            pub fn run(value: i32) -> i32;
            #[link_name = "allocate__pgrx_cshim"]
            pub fn allocate() -> i32;
            pub fn absent() -> i32;
            #[link_name = "native_state"]
            pub static mut state: i32;
            pub static hidden: i32;
        }
        """;

    /// <summary>
    /// Native names and pgrx shim mappings remain exact; nested variables cannot satisfy a missing global.
    /// </summary>
    [TestMethod]
    public void AvailabilityRetainsCompleteOrderedPartition()
    {
        const string Ast = """
            {"kind":"TranslationUnitDecl","inner":[
              {"kind":"FunctionDecl","name":"native_run","inner":[{"kind":"VarDecl","name":"hidden"}]},
              {"kind":"VarDecl","name":"native_state"},
              {"kind":"FunctionDecl","name":"allocate"},
              {"kind":"FunctionDecl","name":"native_run"},
              {"kind":"RecordDecl","name":"absent"}
            ]}
            """;
        using JsonDocument document = JsonDocument.Parse(Ast);
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse(Inventory, 18);
        NativeBindingAvailability result = NativeBindingHeaderAvailability.Read(document.RootElement, inventory);
        Assert.AreSequenceEqual<NativeHeaderRequest>([new("allocate", "allocate", true), new("run", "native_run", true), new("state", "native_state", false)], result.Available);
        Assert.AreSequenceEqual<NativeHeaderRequest>([new("absent", "absent", true), new("hidden", "hidden", false)], result.Absent);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NativeHeaderRequest>)result.Available).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NativeHeaderRequest>)result.Absent).Clear());
        NativeBindingAvailability reordered = NativeBindingHeaderAvailability.Read(document.RootElement,
            inventory with
            {
                Functions = inventory.Functions.Reverse().ToDictionary(),
                Globals = inventory.Globals.Reverse().ToDictionary()
            });
        Assert.AreSequenceEqual(result.Available, reordered.Available);
        Assert.AreSequenceEqual(result.Absent, reordered.Absent);
    }

    /// <summary>
    /// A successfully empty translation unit records all missing requests instead of inventing available declarations.
    /// </summary>
    [TestMethod]
    public void EmptyHeadersRetainAbsentInventory()
    {
        using JsonDocument document = JsonDocument.Parse("{\"kind\":\"TranslationUnitDecl\"}");
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse(Inventory, 18);
        NativeBindingAvailability result = NativeBindingHeaderAvailability.Read(document.RootElement, inventory);
        Assert.IsEmpty(result.Available);
        Assert.AreSequenceEqual<string>(["absent", "allocate", "hidden", "run", "state"], result.Absent.Select(static value => value.Name));
        NativeBindingAvailability empty = NativeBindingHeaderAvailability.Read(document.RootElement, NativeBindingRawParser.Parse("", 18));
        Assert.IsEmpty(empty.Available);
        Assert.IsEmpty(empty.Absent);
    }

    /// <summary>
    /// Selected headers determine declaration kinds independently of the reference inventory's platform.
    /// </summary>
    [TestMethod]
    public void AvailabilityUsesSelectedHeaderDeclarationKinds()
    {
        const string Ast = """
            {"kind":"TranslationUnitDecl","inner":[
              {"kind":"VarDecl","name":"native_run"},
              {"kind":"FunctionDecl","name":"native_state"}
            ]}
            """;
        using JsonDocument document = JsonDocument.Parse(Ast);
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse(Inventory, 18);
        NativeBindingAvailability result = NativeBindingHeaderAvailability.Read(document.RootElement, inventory);
        Assert.AreSequenceEqual<NativeHeaderRequest>([new("run", "native_run", false), new("state", "native_state", true)], result.Available);
        Assert.AreSequenceEqual<NativeHeaderRequest>([new("absent", "absent", true), new("allocate", "allocate", true), new("hidden", "hidden", false)], result.Absent);
        Assert.Contains("run", inventory.Functions.Keys);
        Assert.Contains("state", inventory.Globals.Keys);
    }

    /// <summary>
    /// Required helpers retain their native identities and join the ordered partition without hiding absent inventory.
    /// </summary>
    [TestMethod]
    public void RequiredHelpersRetainNativeIdentityAndOrderedPartition()
    {
        const string Ast = """
            {"kind":"TranslationUnitDecl","inner":[
              {"kind":"FunctionDecl","name":"native_zeta"},
              {"kind":"FunctionDecl","name":"native_run"},
              {"kind":"FunctionDecl","name":"native_alpha"}
            ]}
            """;
        using JsonDocument document = JsonDocument.Parse(Ast);
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse(Inventory, 18);
        NativeHeaderRequest[] helpers = [new("zeta", "native_zeta", true), new("alpha", "native_alpha", true)];
        NativeBindingAvailability result = NativeBindingHeaderAvailability.Read(document.RootElement, inventory, helpers);
        Assert.AreSequenceEqual<NativeHeaderRequest>([new("alpha", "native_alpha", true), new("run", "native_run", true), new("zeta", "native_zeta", true)], result.Available);
        Assert.AreSequenceEqual<string>(["absent", "allocate", "hidden", "state"], result.Absent.Select(static request => request.Name));
        Assert.AreSequenceEqual<NativeHeaderRequest>([new("zeta", "native_zeta", true), new("alpha", "native_alpha", true)], helpers);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NativeHeaderRequest>)result.Available).Clear());
    }

    /// <summary>
    /// A required helper cannot become an absent, nested, or variable declaration.
    /// </summary>
    /// <param name="ast">A complete observation without the required top-level function.</param>
    [TestMethod]
    [DataRow("{\"kind\":\"TranslationUnitDecl\"}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":[{\"kind\":\"VarDecl\",\"name\":\"native_helper\"}]}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":[{\"kind\":\"FunctionDecl\",\"name\":\"outer\",\"inner\":[{\"kind\":\"FunctionDecl\",\"name\":\"native_helper\"}]}]}")]
    public void RequiredHelpersRejectMissingFunctionDeclarations(string ast)
    {
        using JsonDocument document = JsonDocument.Parse(ast);
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse("", 18);
        FormatException error = Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, inventory,
            [new("helper", "native_helper", true)]));
        Assert.Contains("Required native helper 'helper'", error.Message);
        Assert.Contains("native_helper", error.Message);
    }

    /// <summary>
    /// Required helper metadata must contain distinct valid function identities.
    /// </summary>
    [TestMethod]
    public void RequiredHelpersRejectInvalidAndConflictingRequests()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"kind":"TranslationUnitDecl","inner":[{"kind":"FunctionDecl","name":"native_helper"}]}
            """);
        NativeBindingRawCatalog empty = NativeBindingRawParser.Parse("", 18);
        NativeHeaderRequest helper = new("helper", "native_helper", true);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, empty, [helper, helper]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, empty,
            [helper with { IsFunction = false }]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, empty,
            [helper with { Name = "invalid;" }]));
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, empty,
            [helper with { NativeName = "invalid;" }]));
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse("extern \"C\" { pub fn helper(); }", 18);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, inventory, [helper]));
    }

    /// <summary>
    /// Malformed compiler observations and conflicting declaration kinds never become ordinary absence.
    /// </summary>
    /// <param name="ast">An independently invalid compiler observation.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("{\"kind\":1}")]
    [DataRow("{\"kind\":\"FunctionDecl\"}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":{}}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":[{}]}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":[{\"kind\":false}]}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":[{\"kind\":\"FunctionDecl\"}]}")]
    [DataRow("{\"kind\":\"TranslationUnitDecl\",\"inner\":[{\"kind\":\"FunctionDecl\",\"name\":\"allocate\"},{\"kind\":\"VarDecl\",\"name\":\"allocate\"}]}")]
    public void InvalidAvailabilityCannotHideAsAbsence(string ast)
    {
        using JsonDocument document = JsonDocument.Parse(ast);
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse(Inventory, 18);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, inventory));
    }

    /// <summary>
    /// Corrupt inventory names and duplicate function/global identities cannot disappear into the absent partition.
    /// </summary>
    [TestMethod]
    public void InvalidInventoryCannotHideAsAbsence()
    {
        using JsonDocument document = JsonDocument.Parse("{\"kind\":\"TranslationUnitDecl\"}");
        NativeBindingRawCatalog inventory = NativeBindingRawParser.Parse(Inventory, 18);
        Dictionary<string, NativeBindingFunction> functions = inventory.Functions.ToDictionary();
        functions["run"] = functions["run"] with
        {
            NativeSymbol = "invalid;"
        };
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, inventory with { Functions = functions }));
        Dictionary<string, NativeBindingGlobal> globals = inventory.Globals.ToDictionary();
        globals.Add("run", globals["state"]);
        Assert.ThrowsExactly<FormatException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, inventory with { Globals = globals }));
        Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingHeaderAvailability.Read(document.RootElement, null!));
    }
}
