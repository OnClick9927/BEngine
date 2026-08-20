namespace BEngine.Editor;

internal enum GameObjectCommand
{
    CreateEmpty,
    CreateEmptyChild,
    CreateEmptyParent,
    CreateSprite,
    CreateParticleSystem,
    CreateCamera2D,
    SetActive,
    SetInactive,
    ResetTransform,
    ResetPosition,
    ResetRotation,
    ResetScale,
    SelectParent,
    SelectChildren,
    MoveToRoot,
    MoveUp,
    MoveDown,
    SetAsFirstSibling,
    SetAsLastSibling,
    CenterOnChildren,
    FrameSelected,
    CopyHierarchyPath,
    Rename,
    Duplicate,
    Delete
}
