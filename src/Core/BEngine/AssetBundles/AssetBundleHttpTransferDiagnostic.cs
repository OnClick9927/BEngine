using BEngine.Networking;

namespace BEngine.AssetBundles;

public readonly record struct AssetBundleHttpTransferDiagnostic(
    AssetBundleHttpResourceKind ResourceKind,
    string Endpoint,
    int Attempt,
    int? StatusCode,
    long ReceivedBytes,
    NetworkRequestResult Result);
