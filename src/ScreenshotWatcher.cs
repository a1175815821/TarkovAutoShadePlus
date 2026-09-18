using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TarkovAutoShadePlus
{
    internal sealed class ScreenshotWatcher : IDisposable
    {
        private readonly object sync = new object();
        private readonly Dictionary<string, DateTime> emitted =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly List<string> folders = new List<string>();
        private Timer debounceTimer;
        private readonly List<string> pendingPaths = new List<string>();
        private bool enabled;

        public event Action<string> ScreenshotReady;
        public event Action<string> WatcherFaulted;

        public bool IsActive
        {
            get
            {
                lock (sync)
                {
                    try
                    {
                        foreach (FileSystemWatcher watcher in watchers)
                        {
                            if (watcher != null && watcher.EnableRaisingEvents)
                                return true;
                        }
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                lock (sync)
                {
                    foreach (FileSystemWatcher watcher in watchers)
                    {
                        try { watcher.EnableRaisingEvents = enabled; }
                        catch { }
                    }
                }
            }
        }

        public string Folder
        {
            get
            {
                lock (sync) { return folders.Count == 0 ? null : folders[0]; }
            }
        }

        public IList<string> Folders
        {
            get
            {
                lock (sync) { return new List<string>(folders); }
            }
        }

        public void SetFolder(string folder)
        {
            var single = new List<string>();
            if (!string.IsNullOrWhiteSpace(folder)) single.Add(folder);
            SetFolders(single);
        }

        public void SetFolders(IList<string> newFolders)
        {
            lock (sync)
            {
                StopWatcher();
                folders.Clear();
                if (newFolders != null)
                {
                    foreach (string folder in newFolders)
                    {
                        if (string.IsNullOrWhiteSpace(folder)) continue;
                        bool dup = false;
                        foreach (string existing in folders)
                        {
                            if (string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase))
                            {
                                dup = true;
                                break;
                            }
                        }
                        if (!dup && Directory.Exists(folder)) folders.Add(folder);
                    }
                }

                foreach (string folder in folders)
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(folder, "*.png");
                        watcher.NotifyFilter = NotifyFilters.FileName |
                            NotifyFilters.LastWrite | NotifyFilters.Size |
                            NotifyFilters.CreationTime;
                        watcher.Created += OnChanged;
                        watcher.Changed += OnChanged;
                        watcher.Renamed += OnRenamed;
                        watcher.Error += OnError;
                        watcher.EnableRaisingEvents = enabled;
                        watchers.Add(watcher);
                    }
                    catch { }
                }
            }
        }

        public string FindLatest()
        {
            string[] searchFolders;
            lock (sync) { searchFolders = folders.ToArray(); }
            if (searchFolders.Length == 0) return null;

            string latest = null;
            DateTime latestTime = DateTime.MinValue;
            foreach (string folder in searchFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string[] files;
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    files = Directory.GetFiles(folder, "*.png");
                }
                catch { continue; }
                foreach (string file in files)
                {
                    DateTime time;
                    try { time = File.GetLastWriteTimeUtc(file); }
                    catch { continue; }
                    if (time > latestTime)
                    {
                        latest = file;
                        latestTime = time;
                    }
                }
            }
            return latest;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Queue(e.FullPath);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Queue(e.FullPath);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Action<string> handler = WatcherFaulted;
            if (handler == null) return;
            Exception error = e.GetException();
            handler(error == null ? "未知监听错误" : error.Message);
        }

        private void Queue(string filePath)
        {
            if (!enabled || !string.Equals(
                Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase))
                return;

            lock (sync)
            {
                bool exists = false;
                foreach (string pending in pendingPaths)
                {
                    if (string.Equals(pending, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) pendingPaths.Add(filePath);
                if (debounceTimer == null)
                {
                    debounceTimer = new Timer(delegate {
                        EmitPending();
                    }, null, 750, Timeout.Infinite);
                }
                else
                {
                    debounceTimer.Change(750, Timeout.Infinite);
                }
            }
        }

        private void EmitPending()
        {
            string[] paths;
            lock (sync)
            {
                if (pendingPaths.Count == 0) return;
                paths = pendingPaths.ToArray();
                pendingPaths.Clear();
            }

            var readyPaths = new List<string>();
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                foreach (string path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    DateTime previous;
                    if (emitted.TryGetValue(path, out previous) &&
                        (now - previous).TotalSeconds < 3.0)
                        continue;
                    emitted[path] = now;
                    readyPaths.Add(path);
                }

                if (emitted.Count > 80)
                {
                    var stale = new List<string>();
                    foreach (KeyValuePair<string, DateTime> item in emitted)
                        if ((now - item.Value).TotalMinutes > 5.0) stale.Add(item.Key);
                    foreach (string item in stale) emitted.Remove(item);
                }
            }

            Action<string> handler = ScreenshotReady;
            if (handler == null) return;
            foreach (string path in readyPaths) handler(path);
        }

        private void StopWatcher()
        {
            foreach (FileSystemWatcher watcher in watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= OnChanged;
                    watcher.Changed -= OnChanged;
                    watcher.Renamed -= OnRenamed;
                    watcher.Error -= OnError;
                    watcher.Dispose();
                }
                catch { }
            }
            watchers.Clear();
        }

        public void Dispose()
        {
            lock (sync)
            {
                StopWatcher();
                if (debounceTimer != null)
                {
                    debounceTimer.Dispose();
                    debounceTimer = null;
                }
            }
        }
    }
}
