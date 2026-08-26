using System;
using System.IO;

namespace Conduit.PlaywrightTests;

internal static class RepoPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Conduit.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate Conduit.slnx from the test output directory."
            );
        }
    }

    public static string E2eJsRaw => Path.Combine(Root, "artifacts", "coverage", "e2e-js", "raw");
}
