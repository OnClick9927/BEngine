using BEngine.Editor;

namespace BEngine.Audio.Editor;

[CustomEditor(typeof(AudioMixerGroup))]
public sealed class AudioMixerGroupEditor : BAssetEditor
{
    private AudioMixerEffectType _newEffect;
    private AudioMixerGroup? _sendTarget;
    private string? _error;
    private AudioMixerGroup Group => (AudioMixerGroup)target;
    public override void OnInspectorGUI()
    {
        DrawAssetInformationHeader(Group);
        using var disabled = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        EditorGUI.BeginChangeCheck();
        var parent = EditorGUILayout.ObjectField("Parent Bus", Group.parent, false);
        var volume = (Fix64)EditorGUILayout.Slider("Volume", (float)Group.volume, 0, 1);
        var mute = EditorGUILayout.Toggle("Mute", Group.mute);
        if (EditorGUI.EndChangeCheck()) Attempt(() => SetBus(parent, volume, mute));
        GUILayout.Label("Effects · processed from top to bottom", EditorStyles.boldLabel);
        for (var i = 0; i < Group.effects.Count; i++)
        {
            var index = i; var effect = Group.effects[i].Clone();
            EditorGUI.BeginChangeCheck();
            effect.enabled = EditorGUILayout.Toggle($"{i + 1}. {effect.type}", effect.enabled);
            switch (effect.type)
            {
                case AudioMixerEffectType.Gain: effect.gainDb = Slider("Gain (dB)", effect.gainDb, -80, 24); break;
                case AudioMixerEffectType.LowPass:
                case AudioMixerEffectType.HighPass:
                    effect.cutoffHz = Slider("Cutoff (Hz)", effect.cutoffHz, 10, 24000);
                    effect.resonance = Slider("Q", effect.resonance, .1f, 10); break;
                case AudioMixerEffectType.Delay:
                    effect.delaySeconds = Slider("Delay (seconds)", effect.delaySeconds, .001f, 2);
                    effect.feedback = Slider("Feedback", effect.feedback, 0, .95f);
                    effect.wet = Slider("Wet", effect.wet, 0, 1); break;
                case AudioMixerEffectType.Compressor:
                    effect.thresholdDb = Slider("Threshold (dB)", effect.thresholdDb, -80, 0);
                    effect.ratio = Slider("Ratio", effect.ratio, 1, 20);
                    effect.attackSeconds = Slider("Attack (seconds)", effect.attackSeconds, 0, 1);
                    effect.releaseSeconds = Slider("Release (seconds)", effect.releaseSeconds, 0, 2);
                    effect.gainDb = Slider("Makeup (dB)", effect.gainDb, -80, 24); break;
                case AudioMixerEffectType.ParametricEqualizer:
                    effect.cutoffHz = Slider("Center (Hz)", effect.cutoffHz, 10, 24000);
                    effect.resonance = Slider("Q", effect.resonance, .1f, 10);
                    effect.gainDb = Slider("Gain (dB)", effect.gainDb, -24, 24); break;
                case AudioMixerEffectType.Distortion:
                    effect.drive = Slider("Drive", effect.drive, 1, 50);
                    effect.wet = Slider("Wet", effect.wet, 0, 1); break;
                case AudioMixerEffectType.Chorus:
                    effect.delaySeconds = Slider("Delay (seconds)", effect.delaySeconds, .001f, .05f);
                    effect.modulationDepthSeconds = Slider("Depth (seconds)", effect.modulationDepthSeconds, 0, .05f);
                    effect.modulationRateHz = Slider("Rate (Hz)", effect.modulationRateHz, .01f, 20);
                    effect.feedback = Slider("Feedback", effect.feedback, 0, .95f);
                    effect.wet = Slider("Wet", effect.wet, 0, 1); break;
                case AudioMixerEffectType.ConvolutionReverb:
                    effect.wet = Slider("Wet", effect.wet, 0, 1);
                    EditorGUILayout.HelpBox($"{effect.impulseResponse.Count} mono impulse taps at {effect.impulseResponseSampleRate} Hz", MessageType.Info);
                    break;
                case AudioMixerEffectType.Plugin:
                    effect.pluginId = EditorGUILayout.TextField("Plugin ID", effect.pluginId);
                    var count = Math.Clamp(EditorGUILayout.IntField("Parameters", effect.pluginParameters.Count), 0, 256);
                    while (effect.pluginParameters.Count < count) effect.pluginParameters.Add(0);
                    if (effect.pluginParameters.Count > count) effect.pluginParameters.RemoveRange(count, effect.pluginParameters.Count - count);
                    for (var parameter = 0; parameter < count; parameter++)
                        effect.pluginParameters[parameter] = EditorGUILayout.FloatField("Parameter " + parameter, effect.pluginParameters[parameter]);
                    break;
            }
            if (EditorGUI.EndChangeCheck()) Attempt(() => SetEffect(index, effect));
            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(i == 0)) if (GUILayout.Button("Move Up")) Attempt(() => MoveEffect(index, index - 1));
            using (new EditorGUI.DisabledScope(i + 1 == Group.effects.Count)) if (GUILayout.Button("Move Down")) Attempt(() => MoveEffect(index, index + 1));
            var remove = GUILayout.Button("Remove Effect"); GUILayout.EndHorizontal();
            if (remove) { Attempt(() => RemoveEffect(index)); break; }
        }
        _newEffect = (AudioMixerEffectType)EditorGUILayout.EnumPopup("New Effect", _newEffect);
        if (GUILayout.Button("Add Effect")) Attempt(() => AddEffect(_newEffect));
        GUILayout.Label("Sends · after effects, before or after bus volume", EditorStyles.boldLabel);
        for (var i = 0; i < Group.sends.Count; i++)
        {
            var index = i; var send = Group.sends[i]; EditorGUI.BeginChangeCheck();
            var destination = EditorGUILayout.ObjectField($"Send {i + 1}", send.target, false);
            var level = Slider("Send Volume", send.volume, 0, 1); var pre = EditorGUILayout.Toggle("Pre Fader", send.preFader);
            if (EditorGUI.EndChangeCheck()) Attempt(() => SetSend(index, destination, level, pre));
            if (GUILayout.Button("Remove Send")) { Attempt(() => RemoveSend(index)); break; }
        }
        _sendTarget = EditorGUILayout.ObjectField("New Send Target", _sendTarget, false);
        using (new EditorGUI.DisabledScope(_sendTarget is null))
            if (GUILayout.Button("Add Send")) Attempt(() => AddSend(_sendTarget!));
        try { Group.Validate(); } catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        if (_error is not null) EditorGUILayout.HelpBox(_error, MessageType.Error);
        DrawApplyBar();
    }
    private static Fix64 Slider(string label, Fix64 value, float minimum, float maximum) => (Fix64)EditorGUILayout.Slider(label, (float)value, minimum, maximum);
    private void Attempt(Action action) { try { action(); _error = null; } catch (Exception error) { _error = error.Message; } }
    public void SetBus(AudioMixerGroup? parent, Fix64 volume, bool mute)
    {
        var oldParent = Group.parent; var oldVolume = Group.volume; var oldMute = Group.mute;
        Change("Edit mixer bus", () => { Group.parent = parent; Group.volume = Mathf.Clamp01(volume); Group.mute = mute; },
            () => { Group.parent = oldParent; Group.volume = oldVolume; Group.mute = oldMute; });
    }
    public void AddEffect(AudioMixerEffectType type) => ReplaceEffects([.. Group.effects, new AudioMixerEffect { type = type }]);
    public void RemoveEffect(int index) { var list = Group.effects.ToList(); list.RemoveAt(index); ReplaceEffects(list); }
    public void MoveEffect(int from, int to)
    { var list = Group.effects.ToList(); var effect = list[from]; list.RemoveAt(from); list.Insert(to, effect); ReplaceEffects(list); }
    public void SetEffect(int index, AudioMixerEffect effect)
    { var list = Group.effects.ToList(); list[index] = effect.Clone(); ReplaceEffects(list); }
    private void ReplaceEffects(List<AudioMixerEffect> effects)
    { var old = Group.effects; Change("Edit mixer effects", () => Group.effects = effects, () => Group.effects = old); }
    public void AddSend(AudioMixerGroup target) => ReplaceSends([.. Group.sends, new AudioMixerSend { target = target }]);
    public void SetSend(int index, AudioMixerGroup? target, Fix64 volume, bool preFader)
    { var list = Group.sends.ToList(); list[index] = new() { target = target, volume = volume, preFader = preFader }; ReplaceSends(list); }
    public void RemoveSend(int index) { var list = Group.sends.ToList(); list.RemoveAt(index); ReplaceSends(list); }
    private void ReplaceSends(List<AudioMixerSend> sends)
    { var old = Group.sends; Change("Edit mixer sends", () => Group.sends = sends, () => Group.sends = old); }
    private void Change(string label, Action apply, Action restore)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        try { apply(); Group.Validate(); } finally { restore(); }
        Undo.IncrementCurrentGroup(); Undo.RecordObject(Group, label); apply(); EditorUtility.SetDirty(Group); hasUnsavedChanges = true;
    }
}
