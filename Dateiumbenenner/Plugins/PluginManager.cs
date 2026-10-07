using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Dateiumbenenner.Plugins
{
    public class LoadedPlugin
    {
        public required IPlugin Plugin { get; init; }
        public required string AssemblyPath { get; init; }
        public bool Enabled { get; set; }
    }

    public class PluginManager
    {
        private class PluginSettings { public List<string> Enabled { get; set; } = new(); }

        // Portabel: alle Daten im Unterordner "Daten" neben der EXE
        public static string BaseDirectory => Path.Combine(AppContext.BaseDirectory, "Daten");
        public static string PluginDirectory => Path.Combine(AppContext.BaseDirectory, "Plugins");
        private static string SettingsFile => Path.Combine(BaseDirectory, "plugins.json");

        public List<LoadedPlugin> Plugins { get; } = new();
        public List<string> Errors { get; } = new();

        public void LoadAll()
        {
            Plugins.Clear();
            Errors.Clear();
            if (!Directory.Exists(PluginDirectory)) return;
            ApplyPendingUpdates();
            var enabled = ReadSettings().Enabled;
            foreach (var dir in Directory.GetDirectories(PluginDirectory))
            {
                if (Path.GetFileName(dir).Equals(PendingFolder, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var dll in Directory.GetFiles(dir, "*.Plugin.dll"))
                    LoadAssembly(dll, enabled);
            }
        }

        private void LoadAssembly(string dll, List<string> enabled)
        {
            try
            {
                var ctx = new PluginLoadContext(dll);
                var asm = ctx.LoadFromAssemblyName(AssemblyName.GetAssemblyName(dll));
                foreach (var type in asm.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin && Plugins.All(p => p.Plugin.Id != plugin.Id))
                        Plugins.Add(new LoadedPlugin { Plugin = plugin, AssemblyPath = dll, Enabled = enabled.Contains(plugin.Id) });
                }
            }
            catch (Exception ex)
            {
                Errors.Add($"{Path.GetFileName(dll)}: {ex.Message}");
            }
        }

        private const string PendingFolder = "_pending";

        /// <summary>Verschiebt beim Start vorgemerkte Plugin-Updates an ihren Platz.</summary>
        private void ApplyPendingUpdates()
        {
            var pending = Path.Combine(PluginDirectory, PendingFolder);
            if (!Directory.Exists(pending)) return;
            try
            {
                foreach (var dir in Directory.GetDirectories(pending))
                    CopyDirectory(dir, Path.Combine(PluginDirectory, Path.GetFileName(dir)));
                Directory.Delete(pending, true);
            }
            catch (Exception ex)
            {
                Errors.Add($"Plugin-Update konnte nicht angewendet werden: {ex.Message}");
            }
        }

        /// <summary>Kopiert die Plugin-DLL samt aller DLLs im gleichen Ordner in einen eigenen Plugin-Unterordner.
        /// Ist das Plugin bereits geladen (Dateien gesperrt), wird es für den nächsten Start vorgemerkt und <paramref name="needsRestart"/> ist true.</summary>
        public string Import(string pluginDllPath, out bool needsRestart)
        {
            pluginDllPath = ResolveRuntimeDll(pluginDllPath);
            var srcDir = Path.GetDirectoryName(pluginDllPath)!;
            if (srcDir.Split(Path.DirectorySeparatorChar).Any(s => s.Equals("obj", StringComparison.OrdinalIgnoreCase))
                || !File.Exists(Path.ChangeExtension(pluginDllPath, ".deps.json")))
                throw new InvalidOperationException("Bitte die Plugin-DLL aus dem Build-Ausgabeordner (bin\\Debug oder bin\\Release) wählen, nicht aus obj. Dort fehlen die benötigten Abhängigkeiten des Plugins.");
            var name = Path.GetFileNameWithoutExtension(pluginDllPath);
            var finalDir = Path.Combine(PluginDirectory, name);
            needsRestart = Plugins.Any(p => string.Equals(Path.GetDirectoryName(p.AssemblyPath), finalDir, StringComparison.OrdinalIgnoreCase));
            var targetDir = needsRestart ? Path.Combine(PluginDirectory, PendingFolder, name) : finalDir;
            CopyPluginFiles(pluginDllPath, targetDir);
            return finalDir;
        }

        /// <summary>Lädt ein importiertes Plugin sofort; liefert die neu hinzugekommenen Plugins.</summary>
        public List<LoadedPlugin> LoadFrom(string pluginDir)
        {
            var before = Plugins.Count;
            Errors.Clear();
            var enabled = ReadSettings().Enabled;
            foreach (var dll in Directory.GetFiles(pluginDir, "*.Plugin.dll"))
                LoadAssembly(dll, enabled);
            return Plugins.Skip(before).ToList();
        }

        /// <summary>Wurde eine DLL aus obj\... (auch obj\...\ref) gewählt, wird die zugehörige Laufzeit-DLL aus bin\... verwendet.</summary>
        private static string ResolveRuntimeDll(string dllPath)
        {
            var parts = Path.GetFullPath(dllPath).Split(Path.DirectorySeparatorChar).ToList();
            var objIndex = parts.FindLastIndex(s => s.Equals("obj", StringComparison.OrdinalIgnoreCase));
            if (objIndex < 0) return dllPath;
            parts[objIndex] = "bin";
            var file = parts[^1];
            parts.RemoveAt(parts.Count - 1);
            var dir = string.Join(Path.DirectorySeparatorChar, parts);
            foreach (var candidate in new[] { dir, Path.GetDirectoryName(dir)! })
            {
                var path = Path.Combine(candidate, file);
                if (File.Exists(path)) return path;
            }
            return dllPath;
        }

        private static void CopyPluginFiles(string pluginDllPath, string targetDir)
        {
            var sourceDir = Path.GetDirectoryName(pluginDllPath)!;
            Directory.CreateDirectory(targetDir);
            var hostAssembly = Path.GetFileName(typeof(PluginManager).Assembly.Location);
            foreach (var file in Directory.GetFiles(sourceDir).Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                if (Path.GetFileName(file).Equals(hostAssembly, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, Path.GetFileName(file)))) continue;
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }
            var runtimes = Path.Combine(sourceDir, "runtimes");
            if (Directory.Exists(runtimes)) CopyDirectory(runtimes, Path.Combine(targetDir, "runtimes"));
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(source)) CopyDirectory(d, Path.Combine(target, Path.GetFileName(d)));
        }

        public void SaveSettings()
        {
            Directory.CreateDirectory(BaseDirectory);
            var settings = new PluginSettings { Enabled = Plugins.Where(p => p.Enabled).Select(p => p.Plugin.Id).ToList() };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static PluginSettings ReadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(SettingsFile)) ?? new PluginSettings();
            }
            catch { }
            return new PluginSettings();
        }

        private class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;
            public PluginLoadContext(string pluginPath) => _resolver = new AssemblyDependencyResolver(pluginPath);

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                // Hauptprogramm und bereits geladene Framework-Assemblies teilen, damit IPlugin-Typen übereinstimmen
                var shared = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                if (shared != null) return shared;
                // Vom Hauptprogramm mitgelieferte (noch nicht geladene) Assemblies ebenfalls gemeinsam nutzen
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll")))
                {
                    try { return Default.LoadFromAssemblyName(assemblyName); } catch { }
                }
                var path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path != null ? LoadFromAssemblyPath(path) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
            }
        }
    }
}
