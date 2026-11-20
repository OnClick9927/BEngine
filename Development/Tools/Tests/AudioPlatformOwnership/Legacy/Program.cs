using System.Text.Json;
using BEngine.Audio;
var folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
var result = QueueProbe.Run(typeof(AudioClip).Assembly, "BEngine.Audio.BufferedStereoPcmStream");
File.WriteAllText(Path.Combine(folder, "Queue.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("LEGACY_PCM_FIXTURE_OK");
