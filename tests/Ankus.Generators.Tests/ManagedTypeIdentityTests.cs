using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Detached provider keys preserve the compiler's distinctions while ignoring reference nullable annotations.
    /// </summary>
    /// <param name="left">The first resolved managed type.</param>
    /// <param name="right">The second resolved managed type.</param>
    [TestMethod]
    [DataRow("Box<string>", "Box<string?>")]
    [DataRow("Box<string>", "Box<string>?")]
    [DataRow("Box<(int A, int B)>", "Box<(int C, int B)>")]
    [DataRow("Box<(int A, int B)>", "Box<(int A, int B)>")]
    [DataRow("Box<dynamic>", "Box<object>")]
    [DataRow("Box<nint>", "Box<System.IntPtr>")]
    [DataRow("Box<int[]>", "Box<int[,]>")]
    [DataRow("Box<int>", "Box<long>")]
    [DataRow("Box<(int?, int)>", "Box<(int, int)>")]
    public void ManagedProviderKeysPreserveCompilerEquality(string left, string right)
    {
        CSharpCompilation compilation = ModuleCompilation($$"""
            #nullable enable
            public sealed class Box<T>;
            public sealed class Owner
            {
                public {{left}} Left { get; } = null!;
                public {{right}} Right { get; } = null!;
            }
            """);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        INamedTypeSymbol owner = compilation.GetTypeByMetadataName("Owner") ?? throw new InvalidOperationException("Missing authored type.");
        var first = (IPropertySymbol)Assert.ContainsSingle(owner.GetMembers("Left"));
        var second = (IPropertySymbol)Assert.ContainsSingle(owner.GetMembers("Right"));
        bool expected = SymbolEqualityComparer.Default.Equals(first.Type, second.Type);
        ManagedTypeIdentity firstKey = ManagedTypeIdentity.Create(first.Type);
        ManagedTypeIdentity secondKey = ManagedTypeIdentity.Create(second.Type);

        Assert.AreEqual(expected, firstKey.Equals(secondKey));
        if (expected)
        {
            Assert.AreEqual(firstKey.GetHashCode(), secondKey.GetHashCode());
        }
    }
}
