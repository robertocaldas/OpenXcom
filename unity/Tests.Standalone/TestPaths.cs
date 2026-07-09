using System;
using System.IO;

namespace OpenXcom.Core.Tests
{
    /// <summary>
    /// Resolves paths relative to this test assembly's own build output
    /// directory (<see cref="AppContext.BaseDirectory"/>), not the test
    /// host process's current working directory. `dotnet test` does not
    /// always run with CWD set to the assembly's bin/ folder (observed:
    /// some invocations use a temp data-collector directory instead),
    /// which made path-dependent tests intermittently fail depending on
    /// how the suite was invoked.
    /// </summary>
    public static class TestPaths
    {
        public static readonly string RawDataDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "RawData", "Resources", "UFO");

        public static readonly string RulesDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "bin", "standard", "xcom1");
    }
}
