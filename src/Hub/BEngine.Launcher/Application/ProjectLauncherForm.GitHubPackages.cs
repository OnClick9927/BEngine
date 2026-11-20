namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private void OpenGitHubPackages()
    {
        if (_needsUpdateRecovery || SelectedEngine is not { } engine) return;
        try
        {
            using var repository = new GitHubPackageRepository();
            var manager = new GitHubPackageManager(_packageCatalog, repository);
            using var dialog = new GitHubPackagesForm(manager, repository, _localization, _palette, engine.Version);
            dialog.ShowDialog(this);
            _packageCatalog.Refresh(_packageCatalog.ResolvePackagesRoot());
            ReloadPackages();
        }
        catch (Exception error) { MessageBox.Show(this, error.GetBaseException().Message, T("GitHub packages"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
