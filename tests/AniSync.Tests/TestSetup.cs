using System.IO;
using System.Runtime.CompilerServices;

namespace AniSync.Tests;

static class TestSetup
{
    /// <summary>Les tests écrivent leurs réglages et leur journal dans un dossier temporaire, jamais dans %APPDATA%\AniSync.</summary>
    [ModuleInitializer]
    internal static void UseTemporaryDataDir() =>
        Environment.SetEnvironmentVariable("ANISYNC_DATA_DIR",
            Path.Combine(Path.GetTempPath(), "AniSync.Tests", Environment.ProcessId.ToString()));
}
