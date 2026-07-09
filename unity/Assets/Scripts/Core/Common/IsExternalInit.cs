#if UNITY_2021_1_OR_NEWER
// Unity's scripting backend targets an API profile that predates C# 9's
// init-only setters (RuleItem.cs uses `init;`); the .NET SDK build (Tests.Standalone,
// net8.0) already ships this type, so it's only defined here under Unity.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
