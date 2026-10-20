using System;
using System.Diagnostics;
using UnityEngine;

namespace StarVadersUltrawide;

// Opt-in frame timing (STARVADERS_UW_PROFILE=1). Logs average/worst frame time
// and the CPU time spent in each of this mod's per-frame hooks every 10 seconds.
internal static class Profile
{
    internal enum Hook { HudUpdate, HudLateUpdate, Menu, Scaler, MainMenu, Pointer, Rack, Selection }
    internal static readonly bool On = Environment.GetEnvironmentVariable("STARVADERS_UW_PROFILE") == "1";
    static readonly double[] _hookMs = new double[Enum.GetValues(typeof(Hook)).Length];
    static double _frameMs, _worstMs;
    static int _frames, _gcStart = -1;
    static float _nextLog = 30f;

    internal static long Start() => On ? Stopwatch.GetTimestamp() : 0;

    internal static void Stop(long start, Hook hook)
    {
        if (On) _hookMs[(int)hook] += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    // Call once per frame.
    internal static void Frame()
    {
        if (!On) return;
        if (_gcStart < 0) _gcStart = GC.CollectionCount(0);
        double dt = Time.unscaledDeltaTime * 1000.0;
        _frames++;
        _frameMs += dt;
        if (dt > _worstMs) _worstMs = dt;
        if (Time.unscaledTime < _nextLog) return;
        int gc = GC.CollectionCount(0);
        double total = 0;
        var parts = new System.Text.StringBuilder();
        for (int i = 0; i < _hookMs.Length; i++)
        {
            total += _hookMs[i];
            parts.Append($" {(Hook)i}={_hookMs[i] / _frames:0.000}");
            _hookMs[i] = 0;
        }
        Plugin.Instance.Log.LogInfo($"PROFILE: frames={_frames} avg={_frameMs / _frames:0.00}ms worst={_worstMs:0.00}ms mod={total / _frames:0.000}ms/frame ({parts.ToString().Trim()}) managedGC={gc - _gcStart}");
        _frameMs = _worstMs = 0;
        _frames = 0;
        _gcStart = gc;
        _nextLog = Time.unscaledTime + 10f;
    }
}
