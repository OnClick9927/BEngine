namespace BEngine.Documents;

public readonly record struct DocumentConversionContext(
    string SourcePath = "",
    IServiceProvider? Services = null);
