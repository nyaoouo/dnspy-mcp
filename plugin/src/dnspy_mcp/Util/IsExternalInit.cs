// Polyfill required for `init` property accessors on .NET Framework 4.8
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/init

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit { }
