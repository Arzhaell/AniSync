using System.IO;
using AniSync.Core;

namespace AniSync.Tests;

public class StartupManagerTests
{
    [Fact]
    public void Creates_a_shortcut_that_starts_minimized()
    {
        var dir = Directory.CreateTempSubdirectory("anisync-test-");
        try
        {
            var link = Path.Combine(dir.FullName, "AniSync.lnk");
            var target = Path.Combine(dir.FullName, "AniSync.exe");

            StartupManager.CreateShortcut(link, target, "--minimized");

            Assert.True(File.Exists(link));
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic read = shell.CreateShortcut(link);
            Assert.Equal(target, (string)read.TargetPath, ignoreCase: true);
            Assert.Equal("--minimized", (string)read.Arguments);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
