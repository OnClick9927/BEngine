namespace BEngine.Editor;

/// <summary>Reflection-visible contract used to audit concrete EditorWindow MVC bindings.</summary>
public interface IEditorWindowMvcContract { }

/// <summary>Declares the concrete model and controller owned by an EditorWindow type.</summary>
public interface IEditorWindowMvcContract<TModel, TController> : IEditorWindowMvcContract
    where TModel : class, IEditorWindowModel
    where TController : class, IEditorWindowController
{ }

/// <summary>
/// Strongly typed MVC base for an EditorWindow. Existing EditorWindow subclasses remain source
/// compatible, while engine windows can expose their concrete model and controller without casts.
/// </summary>
public abstract class EditorWindow<TModel, TController> : EditorWindow,
    IEditorWindowMvcContract<TModel, TController>
    where TModel : class, IEditorWindowModel
    where TController : class, IEditorWindowController
{
    public new TModel model => (TModel)base.model;
    public TModel Model => model;
    public new TController controller => (TController)base.controller;
    public TController Controller => controller;

    protected sealed override IEditorWindowModel CreateModel(EditorWindowMvcContext context) =>
        CreateWindowModel(context);

    protected sealed override IEditorWindowController CreateController(
        EditorWindowMvcContext context,
        IEditorWindowModel windowModel) =>
        CreateWindowController(context, (TModel)windowModel);

    protected abstract TModel CreateWindowModel(EditorWindowMvcContext context);

    protected abstract TController CreateWindowController(
        EditorWindowMvcContext context,
        TModel windowModel);
}

/// <summary>
/// Lightweight, per-view MVC base for windows whose only state is presentation state. The closed
/// generic types still make each window's model, controller and view relationship explicit.
/// </summary>
public abstract class EditorWindow<TView> :
    EditorWindow<EditorWindowModel<TView>,
        EditorWindowController<TView, EditorWindowModel<TView>>>
    where TView : EditorWindow
{
    protected sealed override EditorWindowModel<TView> CreateWindowModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected sealed override EditorWindowController<TView, EditorWindowModel<TView>>
        CreateWindowController(
            EditorWindowMvcContext context,
            EditorWindowModel<TView> windowModel) => new(context, windowModel);
}

/// <summary>Per-instance model identity for a specific EditorWindow view type.</summary>
public class EditorWindowModel<TView>(string windowId) : EditorWindowModel(windowId)
    where TView : EditorWindow
{
    public Type ViewType => typeof(TView);
}

/// <summary>Strongly typed common controller for a specific EditorWindow view type.</summary>
public class EditorWindowController<TView, TModel>(
    EditorWindowMvcContext context,
    TModel model) : EditorWindowController(context, model)
    where TView : EditorWindow
    where TModel : class, IEditorWindowModel
{
    public new TModel Model { get; } = model;
    public new TView View { get; } = context.View as TView ??
        throw new ArgumentException(
            $"MVC context view is not {typeof(TView).FullName}.", nameof(context));
}
