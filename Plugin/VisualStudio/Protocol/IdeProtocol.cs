using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BEngine.Plugin.VisualStudio.Protocol
{
    [DataContract]
    internal sealed class ProjectDescriptor
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "projectPath")] public string ProjectPath { get; set; } = "";
        [DataMember(Name = "projectName")] public string ProjectName { get; set; } = "";
        [DataMember(Name = "solutionPath")] public string SolutionPath { get; set; } = "";
        [DataMember(Name = "engineVersion")] public string EngineVersion { get; set; } = "";
    }

    [DataContract]
    internal sealed class DebugTarget
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "processId")] public int ProcessId { get; set; }
        [DataMember(Name = "processStartTimeUtc")] public string ProcessStartTimeUtc { get; set; } = "";
        [DataMember(Name = "projectPath")] public string ProjectPath { get; set; } = "";
        [DataMember(Name = "projectName")] public string ProjectName { get; set; } = "";
        [DataMember(Name = "kind")] public string Kind { get; set; } = "";
        [DataMember(Name = "displayName")] public string DisplayName { get; set; } = "";
        [DataMember(Name = "executablePath")] public string ExecutablePath { get; set; } = "";
        [DataMember(Name = "solutionPath")] public string SolutionPath { get; set; } = "";
        [DataMember(Name = "runtime")] public string Runtime { get; set; } = "";
        public override string ToString() => $"{(string.IsNullOrWhiteSpace(DisplayName) ? ProjectName : DisplayName)} — PID {ProcessId}";
    }

    [DataContract]
    internal sealed class SyncRequest
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [DataMember(Name = "requestId")] public string RequestId { get; set; } = "";
        [DataMember(Name = "command")] public string Command { get; set; } = "sync";
    }

    [DataContract]
    internal sealed class SyncResponse
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "requestId")] public string RequestId { get; set; } = "";
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "solutionPath")] public string SolutionPath { get; set; } = "";
        [DataMember(Name = "error")] public string Error { get; set; } = "";
    }

    internal static class JsonFile
    {
        internal static T Read<T>(string path)
        {
            if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("BEngine IDE metadata is too large.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ?? throw new InvalidDataException("Empty IDE metadata."));
        }

        internal static void WriteAtomic<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
