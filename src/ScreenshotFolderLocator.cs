using System;
using System.Collections.Generic;
using System.IO;

namespace TarkovAutoShadePlus
{
    internal static class ScreenshotFolderLocator
    {
        public static string Find()
        {
            List<string> all = FindAll();
            return all.Count == 0 ? null : all[0];
        }

        public static List<string> FindAll()
        {
            var roots = new List<string>();
            AddRoot(roots, Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments));
            AddRoot(roots, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents"));
            AddRoot(roots, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OneDrive", "Documents"));

            var found = new List<string>();
            foreach (string gameFolderName in AppSettings.KnownGameFolderNames)
            {
                foreach (string root in roots)
                {
                    string standard = Path.Combine(root,
                        gameFolderName, "Screenshots");
                    AddFound(found, standard);

                    try
                    {
                        foreach (string gameFolder in Directory.GetDirectories(
                            root, gameFolderName, SearchOption.TopDirectoryOnly))
                        {
                            AddFound(found, Path.Combine(gameFolder, "Screenshots"));
                        }
                    }
                    catch
                    {
                        // An inaccessible Documents location is simply skipped.
                    }
                }
            }
            return found;
        }

        private static void AddFound(List<string> found, string screenshots)
        {
            if (string.IsNullOrWhiteSpace(screenshots)) return;
            try
            {
                if (!Directory.Exists(screenshots)) return;
            }
            catch { return; }
            foreach (string existing in found)
            {
                if (string.Equals(existing, screenshots, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            found.Add(screenshots);
        }

        private static void AddRoot(List<string> roots, string root)
        {
            if (string.IsNullOrWhiteSpace(root) ||
                !Directory.Exists(root) || roots.Contains(root)) return;
            roots.Add(root);
        }
    }
}
