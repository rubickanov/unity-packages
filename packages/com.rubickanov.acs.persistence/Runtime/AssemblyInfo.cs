using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ACS.Tests.Persistence")]

// Unity 2022.3 targets .NET Standard 2.1 via C# 9, which recognises `init`-only setters
// syntactically but does not ship `System.Runtime.CompilerServices.IsExternalInit`. The
// compiler expects the type to exist in any referenced assembly — providing an internal
// stub here satisfies that requirement for this package without leaking out.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
