namespace BEngine.Editor;

internal static class GameObjectMenuCommands
{
    [MenuItem("GameObject/Create Empty", false, 0)]
    private static void CreateEmpty(MenuCommand command) => Execute(GameObjectCommand.CreateEmpty, command);

    [MenuItem("GameObject/Create Empty", true)]
    private static bool ValidateCreateEmpty(MenuCommand command) => CanExecute(GameObjectCommand.CreateEmpty, command);

    [MenuItem("GameObject/Create Empty Child", false, 1)]
    private static void CreateEmptyChild(MenuCommand command) => Execute(GameObjectCommand.CreateEmptyChild, command);

    [MenuItem("GameObject/Create Empty Child", true)]
    private static bool ValidateCreateEmptyChild(MenuCommand command) =>
        CanExecute(GameObjectCommand.CreateEmptyChild, command);

    [MenuItem("GameObject/Create Empty Parent", false, 2)]
    private static void CreateEmptyParent(MenuCommand command) =>
        Execute(GameObjectCommand.CreateEmptyParent, command);

    [MenuItem("GameObject/Create Empty Parent", true)]
    private static bool ValidateCreateEmptyParent(MenuCommand command) =>
        CanExecute(GameObjectCommand.CreateEmptyParent, command);

    [MenuItem("GameObject/2D Object/Sprite", false, 20)]
    private static void CreateSprite(MenuCommand command) => Execute(GameObjectCommand.CreateSprite, command);

    [MenuItem("GameObject/2D Object/Sprite", true)]
    private static bool ValidateCreateSprite(MenuCommand command) =>
        CanExecute(GameObjectCommand.CreateSprite, command);

    [MenuItem("GameObject/Effects/Particle System 2D", false, 30)]
    private static void CreateParticleSystem(MenuCommand command) =>
        Execute(GameObjectCommand.CreateParticleSystem, command);

    [MenuItem("GameObject/Effects/Particle System 2D", true)]
    private static bool ValidateCreateParticleSystem(MenuCommand command) =>
        CanExecute(GameObjectCommand.CreateParticleSystem, command);

    [MenuItem("GameObject/Camera 2D", false, 40)]
    private static void CreateCamera2D(MenuCommand command) => Execute(GameObjectCommand.CreateCamera2D, command);

    [MenuItem("GameObject/Camera 2D", true)]
    private static bool ValidateCreateCamera2D(MenuCommand command) =>
        CanExecute(GameObjectCommand.CreateCamera2D, command);

    [MenuItem("GameObject/Set Active", false, 100)]
    private static void SetActive(MenuCommand command) => Execute(GameObjectCommand.SetActive, command);

    [MenuItem("GameObject/Set Active", true)]
    private static bool ValidateSetActive(MenuCommand command) => CanExecute(GameObjectCommand.SetActive, command);

    [MenuItem("GameObject/Set Inactive", false, 101)]
    private static void SetInactive(MenuCommand command) => Execute(GameObjectCommand.SetInactive, command);

    [MenuItem("GameObject/Set Inactive", true)]
    private static bool ValidateSetInactive(MenuCommand command) => CanExecute(GameObjectCommand.SetInactive, command);

    [MenuItem("GameObject/Transform/Reset", false, 120)]
    private static void ResetTransform(MenuCommand command) => Execute(GameObjectCommand.ResetTransform, command);

    [MenuItem("GameObject/Transform/Reset", true)]
    private static bool ValidateResetTransform(MenuCommand command) =>
        CanExecute(GameObjectCommand.ResetTransform, command);

    [MenuItem("GameObject/Transform/Reset Position", false, 121)]
    private static void ResetPosition(MenuCommand command) => Execute(GameObjectCommand.ResetPosition, command);

    [MenuItem("GameObject/Transform/Reset Position", true)]
    private static bool ValidateResetPosition(MenuCommand command) =>
        CanExecute(GameObjectCommand.ResetPosition, command);

    [MenuItem("GameObject/Transform/Reset Rotation", false, 122)]
    private static void ResetRotation(MenuCommand command) => Execute(GameObjectCommand.ResetRotation, command);

    [MenuItem("GameObject/Transform/Reset Rotation", true)]
    private static bool ValidateResetRotation(MenuCommand command) =>
        CanExecute(GameObjectCommand.ResetRotation, command);

    [MenuItem("GameObject/Transform/Reset Scale", false, 123)]
    private static void ResetScale(MenuCommand command) => Execute(GameObjectCommand.ResetScale, command);

    [MenuItem("GameObject/Transform/Reset Scale", true)]
    private static bool ValidateResetScale(MenuCommand command) => CanExecute(GameObjectCommand.ResetScale, command);

    [MenuItem("GameObject/Hierarchy/Select Parent", false, 140)]
    private static void SelectParent(MenuCommand command) => Execute(GameObjectCommand.SelectParent, command);

    [MenuItem("GameObject/Hierarchy/Select Parent", true)]
    private static bool ValidateSelectParent(MenuCommand command) =>
        CanExecute(GameObjectCommand.SelectParent, command);

    [MenuItem("GameObject/Hierarchy/Select Children", false, 141)]
    private static void SelectChildren(MenuCommand command) => Execute(GameObjectCommand.SelectChildren, command);

    [MenuItem("GameObject/Hierarchy/Select Children", true)]
    private static bool ValidateSelectChildren(MenuCommand command) =>
        CanExecute(GameObjectCommand.SelectChildren, command);

    [MenuItem("GameObject/Hierarchy/Move To Root", false, 150)]
    private static void MoveToRoot(MenuCommand command) => Execute(GameObjectCommand.MoveToRoot, command);

    [MenuItem("GameObject/Hierarchy/Move To Root", true)]
    private static bool ValidateMoveToRoot(MenuCommand command) => CanExecute(GameObjectCommand.MoveToRoot, command);

    [MenuItem("GameObject/Hierarchy/Move Up", false, 151)]
    private static void MoveUp(MenuCommand command) => Execute(GameObjectCommand.MoveUp, command);

    [MenuItem("GameObject/Hierarchy/Move Up", true)]
    private static bool ValidateMoveUp(MenuCommand command) => CanExecute(GameObjectCommand.MoveUp, command);

    [MenuItem("GameObject/Hierarchy/Move Down", false, 152)]
    private static void MoveDown(MenuCommand command) => Execute(GameObjectCommand.MoveDown, command);

    [MenuItem("GameObject/Hierarchy/Move Down", true)]
    private static bool ValidateMoveDown(MenuCommand command) => CanExecute(GameObjectCommand.MoveDown, command);

    [MenuItem("GameObject/Hierarchy/Set As First Sibling", false, 153)]
    private static void SetAsFirstSibling(MenuCommand command) =>
        Execute(GameObjectCommand.SetAsFirstSibling, command);

    [MenuItem("GameObject/Hierarchy/Set As First Sibling", true)]
    private static bool ValidateSetAsFirstSibling(MenuCommand command) =>
        CanExecute(GameObjectCommand.SetAsFirstSibling, command);

    [MenuItem("GameObject/Hierarchy/Set As Last Sibling", false, 154)]
    private static void SetAsLastSibling(MenuCommand command) =>
        Execute(GameObjectCommand.SetAsLastSibling, command);

    [MenuItem("GameObject/Hierarchy/Set As Last Sibling", true)]
    private static bool ValidateSetAsLastSibling(MenuCommand command) =>
        CanExecute(GameObjectCommand.SetAsLastSibling, command);

    [MenuItem("GameObject/Hierarchy/Center On Children", false, 160)]
    private static void CenterOnChildren(MenuCommand command) =>
        Execute(GameObjectCommand.CenterOnChildren, command);

    [MenuItem("GameObject/Hierarchy/Center On Children", true)]
    private static bool ValidateCenterOnChildren(MenuCommand command) =>
        CanExecute(GameObjectCommand.CenterOnChildren, command);

    [MenuItem("GameObject/Frame Selected", false, 300)]
    private static void FrameSelected(MenuCommand command) => Execute(GameObjectCommand.FrameSelected, command);

    [MenuItem("GameObject/Frame Selected", true)]
    private static bool ValidateFrameSelected(MenuCommand command) =>
        CanExecute(GameObjectCommand.FrameSelected, command);

    [MenuItem("GameObject/Copy Hierarchy Path", false, 301)]
    private static void CopyHierarchyPath(MenuCommand command) =>
        Execute(GameObjectCommand.CopyHierarchyPath, command);

    [MenuItem("GameObject/Copy Hierarchy Path", true)]
    private static bool ValidateCopyHierarchyPath(MenuCommand command) =>
        CanExecute(GameObjectCommand.CopyHierarchyPath, command);

    [MenuItem("GameObject/Rename", false, 500)]
    private static void Rename(MenuCommand command) => Execute(GameObjectCommand.Rename, command);

    [MenuItem("GameObject/Rename", true)]
    private static bool ValidateRename(MenuCommand command) => CanExecute(GameObjectCommand.Rename, command);

    [MenuItem("GameObject/Duplicate", false, 501)]
    private static void Duplicate(MenuCommand command) => Execute(GameObjectCommand.Duplicate, command);

    [MenuItem("GameObject/Duplicate", true)]
    private static bool ValidateDuplicate(MenuCommand command) => CanExecute(GameObjectCommand.Duplicate, command);

    [MenuItem("GameObject/Delete", false, 502)]
    private static void Delete(MenuCommand command) => Execute(GameObjectCommand.Delete, command);

    [MenuItem("GameObject/Delete", true)]
    private static bool ValidateDelete(MenuCommand command) => CanExecute(GameObjectCommand.Delete, command);

    private static GameObject? Target(MenuCommand command) => command.context switch
    {
        GameObject gameObject => gameObject,
        Component component => component.gameObject,
        _ => Selection.activeGameObject
    };

    private static bool CanExecute(GameObjectCommand command, MenuCommand menuCommand) =>
        EditorBridge.Host?.CanExecuteGameObjectCommand(command, Target(menuCommand)) == true;

    private static void Execute(GameObjectCommand command, MenuCommand menuCommand) =>
        EditorBridge.Host?.ExecuteGameObjectCommand(command, Target(menuCommand));
}
