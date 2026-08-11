using BEngine.Serialization.Documents;

namespace BEngine.Serialization;

public sealed class YamlProjectSerializer
{
    public ProjectDocument Load(string path)
    {
        var document = YamlUtility.Load<ProjectDocument>(path);
        Validate(document);
        return document;
    }

    public void Save(ProjectDocument document, string path)
    {
        Validate(document);
        YamlUtility.Save(document, path);
    }

    private static void Validate(ProjectDocument document)
    {
        if (document.Format != "BEngine.Project" || document.Version != 1)
        {
            throw new InvalidDataException($"Unsupported project document '{document.Format}' v{document.Version}.");
        }

        if (document.Window.Width < 320 || document.Window.Height < 200)
        {
            throw new InvalidDataException("Project window size is too small.");
        }

        _ = BEngine.Fix64.Parse(document.FixedDeltaTime);
    }

}
