using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

// Performance report helpers (Development builds on the Quest, Unity Profiler over USB). Editor only; nothing ships.
//   Connect:  adb forward to the player's profiler socket, connect the Profiler window to it and start recording.
//   Record:   every frame that arrives is summarised at once (so the Profiler's limited frame history doesn't matter):
//             main thread and render thread WORK = the thread's samples minus the time spent in wait markers
//             (anything named *Wait* / *Idle*, e.g. XR frame pacing, Gfx.WaitForPresentOnGfxThread), plus the draw call,
//             SetPass and batch counters and the GPU frame time if the device reports one.
//   Stop:     writes PerfReport/data/<run>/profiler-frames.csv, saves the capture (.data) and screenshots the Profiler window.
public static class PerfProfilerTools
{
    const string Package = "com.cmkl.hcd545.sliceandsell";
    const int Port = 34999;
    static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../PerfReport/data"));

    static bool recording;
    static int lastFrame = -1;
    static string run = "A-current";
    static readonly StringBuilder rows = new StringBuilder();
    static int frames;

    [MenuItem("Slice & Sell/Perf/Connect Profiler to Quest (USB)")]
    public static void Connect()
    {
        string adb = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
        var p = Process.Start(new ProcessStartInfo(adb, $"forward tcp:{Port} localabstract:Unity-{Package}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
        p.WaitForExit(10000);
        EditorWindow.GetWindow<ProfilerWindow>().Show();
        ProfilerDriver.DirectURLConnect($"127.0.0.1:{Port}");
        ProfilerDriver.enabled = true;
        Debug.Log($"[Perf] Profiler connected to {ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler)}");
    }

    public static void StartRun(string runName)
    {
        run = runName;
        rows.Clear();
        rows.Append("frame,frame_ms,main_ms,main_wait_ms,main_work_ms,render_ms,render_wait_ms,render_work_ms,gpu_ms,draw_calls,setpass,batches,shadow_casters\n");
        frames = 0;
        lastFrame = ProfilerDriver.lastFrameIndex;
        recording = true;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Debug.Log($"[Perf] recording {run} from frame {lastFrame + 1}");
    }

    // Command line (Editor with UI): -executeMethod PerfProfilerTools.AutoRun -perfRun A-current -perfSeconds 60
    // Connects, waits for frames from the Quest, records that many seconds of them, saves, screenshots, writes done.txt.
    // The Profiler window's target dropdown is what actually connects to the Quest ("Oculus_Quest 3@ADB:..."); this only
    // forwards the port once and then waits. It never reconnects by itself (a reconnect falls back to the Editor target).
    // The run survives script reloads (SessionState), so editing this file mid-session doesn't lose it.
    public static void AutoRun()
    {
        var args = System.Environment.GetCommandLineArgs();
        string Arg(string key, string def) { int i = System.Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        SessionState.SetString("perf.run", Arg("-perfRun", "A-current"));
        SessionState.SetFloat("perf.seconds", float.Parse(Arg("-perfSeconds", "60"), System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetBool("perf.active", true);
        File.Delete(Path.Combine(Root, SessionState.GetString("perf.run", "A-current"), "done.txt"));
        Connect();
        Arm();
    }

    // Also armed by PerfReport/data/arm.txt ("<run> <seconds>"), read once on a script reload, then deleted.
    [InitializeOnLoadMethod]
    static void Resume()
    {
        string arm = Path.Combine(Root, "arm.txt");
        if (File.Exists(arm))
        {
            var parts = File.ReadAllText(arm).Trim().Split(' ');
            File.Delete(arm);
            SessionState.SetString("perf.run", parts[0]);
            SessionState.SetFloat("perf.seconds", parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 60f);
            SessionState.SetBool("perf.active", true);
            File.Delete(Path.Combine(Root, parts[0], "done.txt"));
        }
        if (SessionState.GetBool("perf.active", false)) Arm();
        // PerfReport/data/analyze.txt ("<run>"): summarise that run's saved profiler-capture.data offline.
        string analyze = Path.Combine(Root, "analyze.txt");
        if (File.Exists(analyze))
        {
            string runName = File.ReadAllText(analyze).Trim();
            File.Delete(analyze);
            if (runName.StartsWith("dump "))
            {
                var p = runName.Split(' ');
                EditorApplication.delayCall += () => Dump(p[1], int.Parse(p[2]));
            }
            else EditorApplication.delayCall += () => SummariseCapture(runName);
        }
    }

    // Every sample of one frame (main and render thread) with its depth, start and duration: what the wait rule must match.
    static void Dump(string runName, int frame)
    {
        string dir = Path.Combine(Root, runName);
        ProfilerDriver.LoadProfile(Path.Combine(dir, "profiler-capture.data"), false);
        var sb = new StringBuilder();
        for (int t = 0; t < 64; t++)
            using (var v = ProfilerDriver.GetRawFrameDataView(frame, t))
            {
                if (!v.valid) break;
                if (t != 0 && v.threadName != "Render Thread") continue;
                sb.Append("== ").Append(v.threadName).Append('\n');
                var ends = new List<ulong>();
                ulong t0 = v.sampleCount > 0 ? v.GetSampleStartTimeNs(0) : 0;
                for (int i = 0; i < v.sampleCount; i++)
                {
                    ulong s = v.GetSampleStartTimeNs(i); double ms = v.GetSampleTimeMs(i);
                    while (ends.Count > 0 && s >= ends[ends.Count - 1]) ends.RemoveAt(ends.Count - 1);
                    if (ms >= 0.2 || ends.Count < 3)
                        sb.Append(new string(' ', ends.Count * 2)).Append(v.GetSampleName(i) ?? v.GetMarkerName(v.GetSampleMarkerId(i)))
                          .Append("  @").Append(((s - t0) / 1e6).ToString("0.00")).Append("  ").Append(ms.ToString("0.00")).Append(" ms\n");
                    ends.Add(s + (ulong)(ms * 1e6));
                }
            }
        File.WriteAllText(Path.Combine(dir, $"profiler-dump-{frame}.txt"), sb.ToString());
        Debug.Log($"[Perf] dumped frame {frame}");
    }

    // Loads a saved capture and writes profiler-frames-capture.csv with the same columns as the live summary.
    public static void SummariseCapture(string runName)
    {
        string dir = Path.Combine(Root, runName);
        bool ok = ProfilerDriver.LoadProfile(Path.Combine(dir, "profiler-capture.data"), false);
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
        run = runName;
        rows.Clear();
        rows.Append("frame,frame_ms,main_ms,main_wait_ms,main_work_ms,render_ms,render_wait_ms,render_work_ms,gpu_ms,draw_calls,setpass,batches,shadow_casters\n");
        frames = 0; int invalid = 0;
        for (int f = first; f <= last && f >= 0; f++)
        {
            using (var v = ProfilerDriver.GetRawFrameDataView(f, 0)) if (!v.valid) { invalid++; continue; }
            Summarise(f);
        }
        File.WriteAllText(Path.Combine(dir, "profiler-frames-capture.csv"), rows.ToString());
        Debug.Log($"[Perf] capture {runName}: loaded={ok} frames {first}..{last}, summarised {frames}, invalid {invalid}");
    }

    // Only what the report reads (CPU timeline, Rendering counters, GPU): less data per frame over USB, so the Editor keeps up
    // and the player never fills its 128 MB profiler buffer (which ends the connection).
    static void LeanAreas()
    {
        foreach (ProfilerArea a in System.Enum.GetValues(typeof(ProfilerArea)))
            ProfilerDriver.SetAreaEnabled(a, a == ProfilerArea.CPU || a == ProfilerArea.GPU || a == ProfilerArea.Rendering);
    }

    static void Arm()
    {
        LeanAreas();
        string name = SessionState.GetString("perf.run", "A-current");
        float seconds = SessionState.GetFloat("perf.seconds", 60f);
        double started = -1;
        void Tick()
        {
            if (started < 0)
            {
                // Recording starts when frames arrive AND go.txt exists (written when the route starts on the headset).
                // Nothing is read while frames stream in (reading live stalled the USB stream): just note the frame.
                if (ProfilerDriver.lastFrameIndex >= 0 && File.Exists(Path.Combine(Root, name, "go.txt")))
                {
                    started = EditorApplication.timeSinceStartup;
                    SessionState.SetInt("perf.goFrame", ProfilerDriver.lastFrameIndex);
                    Debug.Log($"[Perf] route started at frame {ProfilerDriver.lastFrameIndex}, recording {seconds} s");
                }
                return;
            }
            if (EditorApplication.timeSinceStartup - started < seconds) return;
            EditorApplication.update -= Tick;
            SessionState.SetBool("perf.active", false);
            // Save what the Profiler holds (its history: the last up to 2000 frames, all inside the route), then summarise
            // that file offline and screenshot the window.
            string dir = Path.Combine(Root, name);
            Directory.CreateDirectory(dir);
            int lastLive = ProfilerDriver.lastFrameIndex, goFrame = SessionState.GetInt("perf.goFrame", -1);
            ProfilerDriver.SaveProfile(Path.Combine(dir, "profiler-capture.data"));
            Screenshot(Path.Combine(dir, "profiler-window.png"));
            File.WriteAllText(Path.Combine(dir, "profiler-run.txt"), $"go frame {goFrame}, last frame {lastLive}, history {ProfilerDriver.firstFrameIndex}..{lastLive}\n");
            Debug.Log($"[Perf] {name}: saved, go frame {goFrame}, last {lastLive}, first in history {ProfilerDriver.firstFrameIndex}");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                SummariseCapture(name);
                File.Copy(Path.Combine(dir, "profiler-frames-capture.csv"), Path.Combine(dir, "profiler-frames.csv"), true);
                File.WriteAllText(Path.Combine(dir, "done.txt"), $"{frames} frames\n");
            };
        }
        EditorApplication.update += Tick;
        Debug.Log($"[Perf] armed: {name}, {seconds} s after go.txt");
    }

    [MenuItem("Slice & Sell/Perf/Start recording A-current")] static void RecA() => StartRun("A-current");
    [MenuItem("Slice & Sell/Perf/Start recording B-before-opt1")] static void RecB() => StartRun("B-before-opt1");
    [MenuItem("Slice & Sell/Perf/Start recording C-before-opt2")] static void RecC() => StartRun("C-before-opt2");

    [MenuItem("Slice & Sell/Perf/Stop recording and save")]
    public static void Stop()
    {
        Poll();
        recording = false;
        EditorApplication.update -= Poll;
        string dir = Path.Combine(Root, run);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "profiler-frames.csv"), rows.ToString());
        ProfilerDriver.SaveProfile(Path.Combine(dir, "profiler-capture.data"));
        Debug.Log($"[Perf] {run}: {frames} profiler frames written to {dir}");
        Screenshot(Path.Combine(dir, "profiler-window.png"));
    }

    [MenuItem("Slice & Sell/Perf/Screenshot Profiler window")]
    static void ScreenshotMenu() => Screenshot(Path.Combine(Root, run, $"profiler-window-{System.DateTime.Now:HHmmss}.png"));

    // Reads the Profiler window's pixels off the screen (what you see, so pick the view first: Timeline, a frame, ...).
    static void Screenshot(string path)
    {
        var w = EditorWindow.GetWindow<ProfilerWindow>();
        w.Repaint();
        EditorApplication.delayCall += () =>
        {
            float s = EditorGUIUtility.pixelsPerPoint;
            var r = w.position;
            int width = Mathf.RoundToInt(r.width * s), height = Mathf.RoundToInt(r.height * s);
            var px = InternalEditorUtility.ReadScreenPixel(new Vector2(r.x * s, r.y * s), width, height);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels(px); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[Perf] screenshot " + path);
        };
    }

    static void Poll()
    {
        if (!recording) return;
        int last = ProfilerDriver.lastFrameIndex;
        int first = Mathf.Max(lastFrame + 1, ProfilerDriver.firstFrameIndex);
        for (int f = first; f <= last; f++) Summarise(f);
        lastFrame = last;
    }

    static void Summarise(int frame)
    {
        using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
        {
            if (!main.valid) return;
            var (mainTotal, mainWait) = WorkAndWait(main);
            double renderTotal = -1, renderWait = -1;
            for (int t = 1; t < 64; t++)
            {
                using (var v = ProfilerDriver.GetRawFrameDataView(frame, t))
                {
                    if (!v.valid) break;
                    if (v.threadName != "Render Thread") continue;
                    (renderTotal, renderWait) = WorkAndWait(v);
                    break;
                }
            }
            rows.Append(frame).Append(',').Append(main.frameTimeMs.ToString("0.###")).Append(',')
                .Append(mainTotal.ToString("0.###")).Append(',').Append(mainWait.ToString("0.###")).Append(',').Append((mainTotal - mainWait).ToString("0.###")).Append(',')
                .Append(renderTotal.ToString("0.###")).Append(',').Append(renderWait.ToString("0.###")).Append(',')
                .Append(renderTotal < 0 ? "-1" : (renderTotal - renderWait).ToString("0.###")).Append(',')
                .Append(main.frameGpuTimeMs.ToString("0.###")).Append(',')
                .Append(Counter(main, "Draw Calls Count")).Append(',').Append(Counter(main, "SetPass Calls Count")).Append(',')
                .Append(Counter(main, "Batches Count")).Append(',').Append(Counter(main, "Shadow Casters Count")).Append('\n');
            frames++;
        }
    }

    static long Counter(RawFrameDataView v, string name)
    {
        int id = v.GetMarkerId(name);
        return id == FrameDataView.invalidMarkerId ? -1 : v.GetCounterValueAsLong(id);
    }

    // Total time of the thread's root samples, and the part of it spent in wait markers (the outermost one only).
    // Samples come depth-first in start order, so a sample that starts before the end of an earlier one is inside it.
    static (double total, double wait) WorkAndWait(RawFrameDataView v)
    {
        double total = 0, wait = 0;
        ulong rootEnd = 0, waitEnd = 0;
        for (int i = 0; i < v.sampleCount; i++)
        {
            ulong start = v.GetSampleStartTimeNs(i);
            double ms = v.GetSampleTimeMs(i);
            ulong end = start + (ulong)(ms * 1e6);
            if (start >= rootEnd) { total += ms; rootEnd = end; }
            if (start >= waitEnd && IsWait(v.GetSampleName(i) ?? v.GetMarkerName(v.GetSampleMarkerId(i)))) { wait += ms; waitEnd = end; }
        }
        return (total, wait);
    }

    // Waits, not work: the Gfx/semaphore waits, and EarlyUpdate.XRUpdate, where the OpenXR runtime holds the main thread
    // until the next display frame (xrWaitFrame, frame pacing; 5-7 ms of a 13.9 ms frame on the Quest at 72 Hz).
    static bool IsWait(string name) => name != null && (name == "EarlyUpdate.XRUpdate" || name.IndexOf("Wait", System.StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Idle", System.StringComparison.OrdinalIgnoreCase) >= 0);
}
