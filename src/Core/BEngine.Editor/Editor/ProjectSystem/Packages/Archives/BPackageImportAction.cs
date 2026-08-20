namespace BEngine.Editor;

internal enum BPackageImportAction
{
    CreateDirectory,
    WriteFile,
    DeleteFile,
    DeleteDirectory,
    Unchanged,
    Skipped,
    PreserveModified
}
