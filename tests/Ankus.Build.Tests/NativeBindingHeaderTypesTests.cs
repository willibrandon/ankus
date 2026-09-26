namespace Ankus.Build.Tests;

/// <summary>
/// Verifies C declarator precedence and explicit boundaries of the selected-header type model.
/// </summary>
[TestClass]
public sealed class NativeBindingHeaderTypesTests
{
    /// <summary>
    /// Qualifiers bind to their exact pointer or scalar level, including through typedefs and arrays.
    /// </summary>
    [TestMethod]
    public void DeclaratorsPreserveQualifierLevelsAndArrayPrecedence()
    {
        var integer = new NativeHeaderScalar("int");
        var readOnlyValue = new NativeHeaderQualified(integer, NativeHeaderQualifiers.Const);
        var volatileAddress = new NativeHeaderQualified(new NativeHeaderPointer(readOnlyValue), NativeHeaderQualifiers.Volatile);
        Assert.AreEqual("const int *volatile value", volatileAddress.Declare("value"));
        Assert.AreEqual("const int *volatile *address", new NativeHeaderPointer(volatileAddress).Declare("address"));
        var row = new NativeHeaderArray(readOnlyValue, 5);
        Assert.AreEqual("const int (*values)[5]", new NativeHeaderPointer(row).Declare("values"));
        Assert.AreEqual("const int *rows[3]", new NativeHeaderArray(new NativeHeaderPointer(readOnlyValue), 3).Declare("rows"));
        Assert.AreEqual("int values[]", new NativeHeaderArray(integer, null).Declare("values"));
        Assert.AreEqual("int values[0]", new NativeHeaderArray(integer, 0).Declare("values"));
        Assert.AreEqual("int values[18446744073709551615]", new NativeHeaderArray(integer, ulong.MaxValue).Declare("values"));
        var alias = new NativeHeaderAlias("NativePointer", new NativeHeaderPointer(integer));
        Assert.AreEqual("const NativePointer value", new NativeHeaderQualified(alias, NativeHeaderQualifiers.Const).Declare("value"));
        Assert.AreEqual("const int (*values)[5]", new NativeHeaderPointer(new NativeHeaderQualified(new NativeHeaderArray(integer, 5), NativeHeaderQualifiers.Const)).Declare("values"));
    }

    /// <summary>
    /// Nested function returns keep C parentheses, while unprototyped and zero-parameter functions stay distinct.
    /// </summary>
    [TestMethod]
    public void CallbackReturnsAndPrototypeStatesRemainDistinct()
    {
        var callback = new NativeHeaderFunction(new NativeHeaderScalar("int"), [new NativeHeaderScalar("long")], false, true, false);
        var factory = new NativeHeaderFunction(new NativeHeaderPointer(callback), [], false, true, false);
        Assert.AreEqual("int (*factory(void))(long ankus_arg0)", factory.Declare("factory"));
        Assert.AreEqual("int (*(*invoke)(void))(long ankus_arg0)", new NativeHeaderPointer(factory).Declare("invoke"));
        var noPrototype = new NativeHeaderFunction(new NativeHeaderScalar("void"), [], false, false, false);
        Assert.AreEqual("void invoke()", noPrototype.Declare("invoke"));
        Assert.AreEqual("void invoke(void)", (noPrototype with { HasPrototype = true }).Declare("invoke"));
        Assert.AreEqual("void invoke(void) __attribute__((noreturn))", (noPrototype with { HasPrototype = true, DoesNotReturn = true }).Declare("invoke"));
        Assert.ThrowsExactly<InvalidOperationException>(() => (noPrototype with { HasPrototype = true, IsVariadic = true }).Declare("invoke"));
    }

    /// <summary>
    /// Anonymous native types require their actual typedef and invalid names cannot become C source.
    /// </summary>
    [TestMethod]
    public void UnspellableTypesAndInvalidNamesFailExplicitly()
    {
        var record = new NativeHeaderRecord("", false);
        var enumeration = new NativeHeaderEnum("");
        Assert.ThrowsExactly<InvalidOperationException>(() => record.Declare("value"));
        Assert.ThrowsExactly<InvalidOperationException>(() => enumeration.Declare("value"));
        Assert.AreEqual("NativeRecord value", new NativeHeaderAlias("NativeRecord", record).Declare("value"));
        Assert.AreEqual("NativeMode value", new NativeHeaderAlias("NativeMode", enumeration).Declare("value"));
        Assert.AreEqual("struct Tagged value", new NativeHeaderRecord("Tagged", false).Declare("value"));
        Assert.AreEqual("union Variant value", new NativeHeaderRecord("Variant", true).Declare("value"));
        Assert.AreEqual("enum Choice value", new NativeHeaderEnum("Choice").Declare("value"));
        Assert.ThrowsExactly<FormatException>(() => new NativeHeaderScalar("int").Declare("value;"));
        var function = new NativeHeaderFunction(new NativeHeaderScalar("void"), [], false, true, false);
        Assert.ThrowsExactly<InvalidOperationException>(() => new NativeHeaderQualified(function, NativeHeaderQualifiers.Const).Declare("invoke"));
    }
}
