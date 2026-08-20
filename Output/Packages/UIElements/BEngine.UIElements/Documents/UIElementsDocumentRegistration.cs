using System.Runtime.CompilerServices;
using BEngine.Documents;

namespace BEngine.UIElements;

internal static class UIElementsDocumentRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        DocumentConversionRegistry.Register(new UIAssetDocumentConverter());
        DocumentValidationRegistry.Register<UIAssetDocument>(UIAssetDocumentConverter.Validate);
    }
}
