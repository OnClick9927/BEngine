using BEngine.ProjectSystem;

namespace BEngine.Launcher;

// The Hub needs a validated description and template files, never a running
// Scene, graphics device, asset database or Editor service provider.
internal sealed class LauncherProjectWorkspace
{
    internal const string ProjectFileName = "Project.yaml";
    internal string RootPath { get; }
    internal string ProjectFilePath => Path.Combine(RootPath, ProjectFileName);
    internal ProjectData Project { get; }

    private LauncherProjectWorkspace(string root, ProjectData project)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Project = project;
        foreach (var relative in new[]
        {
            project.AssetsDirectory, project.ScenesDirectory, project.ScriptsDirectory, project.EditorScriptsDirectory,
            "ProjectSettings", "Packages", project.AssetsDirectory + "/3rd", "Library", "Library/Packages",
            "Library/ScriptAssemblies", "Library/Artifacts", "Library/ShaderCache", "Logs", "Temp"
        }) Directory.CreateDirectory(ResolveInside(relative));
    }

    internal static LauncherProjectWorkspace Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var projectPath = Directory.Exists(fullPath) ? Path.Combine(fullPath, ProjectFileName) : fullPath;
        if (!File.Exists(projectPath) || !Path.GetFileName(projectPath).Equals(ProjectFileName, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException($"The selected directory does not contain {ProjectFileName}.", projectPath);
        var project = YamlUtility.Load<ProjectData>(projectPath);
        if (project.Format != "BEngine.Project" || project.Version != 1)
            throw new InvalidDataException($"Unsupported project document '{project.Format}' v{project.Version}.");
        if (project.Window.Width < 320 || project.Window.Height < 200) throw new InvalidDataException("Project window size is too small.");
        _ = Fix64.Parse(project.FixedDeltaTime);
        if (string.IsNullOrWhiteSpace(project.EngineVersion))
        {
            project.EngineVersion = EngineVersion.Current;
            YamlUtility.Save(project, projectPath);
        }
        return new(Path.GetDirectoryName(projectPath)!, project);
    }

    internal string ResolveInside(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException($"Project path must be relative: {relative}");
        var resolved = Path.GetFullPath(Path.Combine(RootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Project path escapes the workspace: {relative}");
        return resolved;
    }

    internal static LauncherProjectWorkspace Create(string rootPath, string projectName)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (File.Exists(root)) throw new IOException($"The project path is a file: {root}.");
        var targetExisted = Directory.Exists(root);
        if (targetExisted && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException($"The project folder is not empty: {root}.");
        var parent = Path.GetDirectoryName(root) ?? throw new IOException($"The project folder must have a parent directory: {root}.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".bengine-project-{Guid.NewGuid():N}");
        try
        {
            var name = projectName.Trim();
            var workspace = new LauncherProjectWorkspace(staging, new ProjectData
            { Name = name, EngineVersion = EngineVersion.Current, Window = new WindowData { Title = name } });
            YamlUtility.Save(workspace.Project, workspace.ProjectFilePath);
            LauncherProjectTemplate.Write(workspace);
            if (File.Exists(root)) throw new IOException($"The project path became a file: {root}.");
            if (Directory.Exists(root))
            {
                if (!targetExisted || Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException($"The project folder changed while the project was being created: {root}.");
                Directory.Delete(root);
            }
            Directory.Move(staging, root);
            return Open(root);
        }
        finally
        {
            // staging is a fresh sibling with a fixed generated name, and is
            // never selected from an existing project or a user supplied child.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
