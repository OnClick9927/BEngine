using System;
using System.Diagnostics;
using Android.Util;
using BEngine.Rendering.Rhi;
using BEngine.Rendering;

namespace BEngine.Player.Hosts.Android;

// Explicit opt-in diagnostic; disabled builds perform no sampling or logging.
internal sealed class AndroidFrameMetrics
{
    private long _started, _lastRenderEnd;
    private int _frames;
    private double _tick, _render, _idle, _maximumTick, _maximumRender;
    private long _drawCalls;
    private long _lastDrawCalls;
    private double _collect, _sort, _geometry, _submit;
    private double _coreCollect, _extensionCollect;
    private long _submissions, _vertices;

    internal void Record(long begin, long tickEnd, long renderEnd, GraphicsDrawStatistics statistics, SceneRenderStatistics scene)
    {
        if (_started == 0) _started = begin;
        if (_lastRenderEnd != 0) _idle += Stopwatch.GetElapsedTime(_lastRenderEnd, begin).TotalMilliseconds;
        _lastRenderEnd = renderEnd;
        var tick = Stopwatch.GetElapsedTime(begin, tickEnd).TotalMilliseconds;
        var render = Stopwatch.GetElapsedTime(tickEnd, renderEnd).TotalMilliseconds;
        _tick += tick; _render += render; _frames++;
        _maximumTick = Math.Max(_maximumTick, tick);
        _maximumRender = Math.Max(_maximumRender, render);
        _drawCalls += Math.Max(0, statistics.DrawCallCount - _lastDrawCalls);
        _lastDrawCalls = statistics.DrawCallCount;
        _collect += scene.CollectMilliseconds; _sort += scene.SortMilliseconds;
        _coreCollect += scene.CoreCollectMilliseconds; _extensionCollect += scene.ExtensionCollectMilliseconds;
        _geometry += scene.GeometryMilliseconds; _submit += scene.SubmitMilliseconds;
        _submissions += scene.VisibleSubmissionCount; _vertices += scene.VertexCount;
        var seconds = Stopwatch.GetElapsedTime(_started, renderEnd).TotalSeconds;
        if (seconds < 2) return;
        Log.Info("BEngine", FormattableString.Invariant(
            $"BENGINE_ANDROID_FRAME_METRICS|fps={_frames / seconds:F1}|tickMs={_tick / _frames:F2}|renderMs={_render / _frames:F2}|maxTickMs={_maximumTick:F2}|maxRenderMs={_maximumRender:F2}|draws={_drawCalls / _frames}|idleMs={_idle / _frames:F2}|collectMs={_collect / _frames:F2}|coreCollectMs={_coreCollect / _frames:F2}|extensionCollectMs={_extensionCollect / _frames:F2}|sortMs={_sort / _frames:F2}|geometryMs={_geometry / _frames:F2}|submitMs={_submit / _frames:F2}|submissions={_submissions / _frames}|vertices={_vertices / _frames}|targetFps={BEngine.Application.targetFrameRate}"));
        _started = renderEnd; _frames = 0; _tick = _render = _idle = _maximumTick = _maximumRender = 0; _drawCalls = 0;
        _collect = _sort = _geometry = _submit = 0; _submissions = _vertices = 0;
        _coreCollect = _extensionCollect = 0;
    }
}
