using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;

DotNetPlatformServices.Initialize();
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}
Check(typeof(BObject).Assembly == typeof(MonoBehaviourCoroutineExtensions).Assembly,
    "Coroutine API belongs to runtime Core");
Check(new CoroutineRuntimeSystem().packageId == string.Empty,
    "Built-in coroutine system is active without a package manifest");
using var scene = new Scene("invocation-contract");
var invocation = scene.CreateGameObject().AddComponent<InvocationProbe>();
foreach (var name in new string?[] { null, string.Empty, " ", "Missing" })
    Throws<MissingMethodException>(() => invocation.Invoke(name!, Fix64.Zero), "Delayed invocation retains missing-method failure: " + (name ?? "null"));
Throws<ArgumentOutOfRangeException>(() => invocation.InvokeRepeating("Delayed", Fix64.Zero, Fix64.Zero), "Repeating invocation rejects nonpositive interval");
invocation.Invoke("Delayed", Fix64.One);
Check(invocation.IsInvoking("Delayed"), "Private delayed message can still be scheduled");
invocation.CancelInvoke();
Check(!invocation.IsInvoking("Delayed"), "Delayed message can still be canceled");
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed=true, Checks=checks.Count, Tests=checks }, new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine($"PASS {checks.Count} coroutine compatibility checks");

public sealed class InvocationProbe : MonoBehaviour
{
    private void Delayed() { }
}
