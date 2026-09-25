namespace Ankus.Build;

/// <summary>
/// Contains deterministic managed binding declarations and their shared assembly identity.
/// </summary>
/// <param name="AssemblyName">The identity derived from the complete generated contract.</param>
/// <param name="AbiIdentity">The hash of the complete generated binding contract.</param>
/// <param name="Source">The C# declarations ready for ordinary compilation.</param>
internal sealed record NativeBindingSource(string AssemblyName, string AbiIdentity, string Source);
