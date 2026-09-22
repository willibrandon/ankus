using System.Runtime.InteropServices;

namespace Ankus.Examples.Hello;

/// <summary>
/// Represents the fixed header of PostgreSQL's <c>FunctionCallInfoBaseData</c>.
/// Its flexible <c>args</c> array begins immediately after this structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct FunctionCallInfo
{
    private readonly nint _functionInfo;
    private readonly nint _context;
    private readonly nint _resultInfo;
    private readonly uint _collation;
    private readonly byte _isNull;
    private readonly byte _padding;
    private readonly short _argumentCount;
}
