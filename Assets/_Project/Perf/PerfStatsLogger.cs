using System.IO;
using System.Text;
using HCD545;
using Unity.Profiling;
using UnityEngine;

namespace SliceAndSell.Perf
{
    // Development builds only (DevelopmentOnly on the same object): every frame, the work each part of the frame did,
    // written next to FpsLogger's fps-log.csv as perf-log.csv, with the same route label ("route 1" after Start route).
    //   main_ms / render_ms / gpu_ms   FrameTimingManager: the main thread's, render thread's and GPU's work for the
    //                                   frame (main_ms excludes main_wait_ms, the time it waited for present / XR pacing)
    //   draw_calls, setpass, batches, shadow_casters, triangles   the Render profiler counters for the frame
    // FrameTimingManager reports a frame a few frames late; each row is that frame's own numbers.
    public class PerfStatsLogger : MonoBehaviour
    {
        public FpsLogger fps;               // its label marks the route rows

        readonly FrameTiming[] timings = new FrameTiming[1];
        readonly StringBuilder csv = new StringBuilder();
        ProfilerRecorder drawCalls, setPass, batches, shadowCasters, triangles;
        string path;
        float time, flushIn = 0.5f;

        void OnEnable()
        {
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            path = Path.Combine(Application.persistentDataPath, "perf-log.csv");
            File.WriteAllText(path, "time_s,label,main_ms,main_wait_ms,render_ms,gpu_ms,draw_calls,setpass,batches,shadow_casters,triangles\n");
            Debug.Log("[Perf] perf-log.csv: " + path);
        }

        void OnDisable()
        {
            Flush();
            drawCalls.Dispose(); setPass.Dispose(); batches.Dispose(); shadowCasters.Dispose(); triangles.Dispose();
        }

        void Update()
        {
            time += Time.unscaledDeltaTime;
            FrameTimingManager.CaptureFrameTimings();
            double main = -1, wait = -1, render = -1, gpu = -1;
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                var t = timings[0];
                main = t.cpuMainThreadFrameTime; wait = t.cpuMainThreadPresentWaitTime;
                render = t.cpuRenderThreadFrameTime; gpu = t.gpuFrameTime;
            }
            csv.Append(time.ToString("0.000")).Append(',').Append(fps ? fps.label : "run").Append(',')
               .Append(main.ToString("0.000")).Append(',').Append(wait.ToString("0.000")).Append(',')
               .Append(render.ToString("0.000")).Append(',').Append(gpu.ToString("0.000")).Append(',')
               .Append(drawCalls.LastValue).Append(',').Append(setPass.LastValue).Append(',').Append(batches.LastValue).Append(',')
               .Append(shadowCasters.LastValue).Append(',').Append(triangles.LastValue).Append('\n');
            if ((flushIn -= Time.unscaledDeltaTime) <= 0f) { flushIn = 0.5f; Flush(); }
        }

        void Flush()
        {
            if (csv.Length == 0) return;
            File.AppendAllText(path, csv.ToString());
            csv.Clear();
        }
    }
}
