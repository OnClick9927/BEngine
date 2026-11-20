using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace BEngine.Plugin.VisualStudio.Protocol
{
    internal sealed class ProcessIdentity
    {
        internal int Id { get; set; }
        internal DateTime StartTimeUtc { get; set; }
        internal string ExecutablePath { get; set; } = "";
    }

    internal static class TargetDiscovery
    {
        internal static string? FindProject(string? location)
        {
            if (string.IsNullOrWhiteSpace(location)) return null;
            var path = Path.GetFullPath(location);
            if (!Directory.Exists(path)) path = Path.GetDirectoryName(path)!;
            for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "Project.yaml"))) return current.FullName;
            return null;
        }

        internal static string IdeDirectory(string project) => Path.Combine(project, "Library", "IDE");

        internal static bool SamePath(string left, string right) =>
            string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        internal static string ValidateSolution(string project, string solution)
        {
            var root = Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(solution);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException("The BEngine solution is missing or outside this project. Synchronize C# projects first.");
            return path;
        }

        internal static ProjectDescriptor ReadProject(string project)
        {
            var descriptor = JsonFile.Read<ProjectDescriptor>(Path.Combine(IdeDirectory(project), "project.json"));
            if (descriptor.SchemaVersion != 1 || !SamePath(project, descriptor.ProjectPath))
                throw new InvalidDataException("Unsupported or mismatched BEngine IDE project metadata.");
            return descriptor;
        }

        internal static IReadOnlyList<DebugTarget> Discover(string project, Func<int, ProcessIdentity?>? resolve = null)
        {
            resolve = resolve ?? ResolveProcess;
            var directory = Path.Combine(IdeDirectory(project), "Targets");
            if (!Directory.Exists(directory)) return Array.Empty<DebugTarget>();
            var targets = new List<DebugTarget>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Take(512))
            {
                try
                {
                    var target = JsonFile.Read<DebugTarget>(path);
                    if (!string.Equals(Path.GetFileNameWithoutExtension(path), target.ProcessId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) continue;
                    if (IsCurrent(project, target, resolve)) targets.Add(target);
                }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is SerializationException || error is ArgumentException || error is NotSupportedException) { }
            }
            return targets.OrderBy(t => t.ProjectName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.ProcessId).ToArray();
        }

        internal static bool IsCurrent(string project, DebugTarget target, Func<int, ProcessIdentity?>? resolve = null)
        {
            if (target.SchemaVersion != 1 || target.ProcessId <= 0 || target.Runtime != "coreclr" || target.Kind != "editor" ||
                string.IsNullOrWhiteSpace(target.ExecutablePath) || !SamePath(project, target.ProjectPath) ||
                !DateTimeOffset.TryParse(target.ProcessStartTimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)) return false;
            var actual = (resolve ?? ResolveProcess)(target.ProcessId);
            return actual is not null && actual.Id == target.ProcessId &&
                Math.Abs((actual.StartTimeUtc - started.UtcDateTime).TotalMilliseconds) < 10 && SamePath(actual.ExecutablePath, target.ExecutablePath);
        }

        private static ProcessIdentity? ResolveProcess(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return null;
                    return new ProcessIdentity { Id = process.Id, StartTimeUtc = process.StartTime.ToUniversalTime(), ExecutablePath = process.MainModule?.FileName ?? "" };
                }
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception) { return null; }
        }
    }
}
