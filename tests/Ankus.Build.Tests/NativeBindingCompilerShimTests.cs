namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Native call bodies execute the selected headers' spin-delay operation exactly once for either implementation form.
    /// </summary>
    /// <param name="macro">Whether the native compiler sees a macro fallback instead of the collected inline function.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeCallBodiesPreserveCompilerMacroFallback(bool macro)
    {
        const string Measured = "extern void pg_spin_delay_impl(void);";
        string actual = "static int calls;\n" + (macro ? "#define pg_spin_delay_impl() ((void)++calls)"
            : "void pg_spin_delay_impl(void) { ++calls; }");
        const string Main = """
            #include <stdio.h>
            int main(void)
            {
                if (ankus_native_call_pg_spin_delay_impl(NULL, 0, NULL, 0) != ANKUS_CALL_OK) return 1;
                if (calls != 1) return 2;
                if (ankus_native_call_pg_spin_delay_impl(NULL, 1, NULL, 0) != ANKUS_CALL_COUNT) return 3;
                if (calls != 1) return 4;
                puts("native spin delay invoked once");
                return 0;
            }
            """;
        Assert.AreEqual("native spin delay invoked once\n", await ExecuteNativeCallsAsync(Measured,
            [new("pg_spin_delay_impl", "pg_spin_delay_impl", true)], null, Main, nativeCompiler: true, nativeHeaders: actual));
    }
}
