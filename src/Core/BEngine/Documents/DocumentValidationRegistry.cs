using System.Collections.Concurrent;

namespace BEngine.Documents;

public static class DocumentValidationRegistry
{
    private static readonly ConcurrentDictionary<Type, Action<Document>> Validators = [];

    static DocumentValidationRegistry() => CoreDocumentRegistration.RegisterValidators();

    public static void Register<TDocument>(Action<TDocument> validator) where TDocument : Document
    {
        ArgumentNullException.ThrowIfNull(validator);
        Validators[typeof(TDocument)] = document => validator((TDocument)document);
    }

    public static void Validate(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (Validators.TryGetValue(document.GetType(), out var validator)) validator(document);
    }
}
