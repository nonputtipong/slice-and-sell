using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace HCD545
{
    // Lab 6: measures every frame and shows FPS, frame time, 1 % low, draw calls and triangles. Also writes fps-log.csv.
    //   FPS             frames per second over the last half second
    //   Frame time (ms) the average time of one frame. Quest 3 at 72 Hz needs 13.9 ms or less (90 Hz: 11.1 ms)
    //   1 % low         the FPS of the slowest 1 % of frames since the run started: the stutters you feel
    //   Average FPS     the average FPS since the run started
    //   Start route     (FrameHud) starts a new run: the average and the 1 % low start again, and the CSV label
    //                   becomes "route 1" (then "route 2", ...) from that frame on, so a spreadsheet can keep only those rows
    //   Draw calls / Triangles  only in the Editor and in a Development Build (Build Profiles > Development Build)
    // The CSV is in Application.persistentDataPath. On the Quest: /sdcard/Android/data/<package id>/files/fps-log.csv
    public class FpsLogger : MonoBehaviour
    {
        public TMP_Text fpsText, frameTimeText, onePercentLowText, drawCallsText, trianglesText;
        public TMP_Text averageText;             // optional: the average FPS since the run started
        public Button startRouteButton;          // optional: starts a new run labeled "route 1", "route 2", ...
        public TMP_Text routeText;               // optional: shows the route label and how long it has run
        public string label = "run";            // written in the CSV, for example "level 1" or "my scene"
        public bool writeCsv = true;

        public float Fps { get; private set; }
        public float FrameMs { get; private set; }
        public float OnePercentLow { get; private set; }
        public float AverageFps => runFrames.Count > 0 ? runFrames.Count / runSum : 0f;
        public int RouteCount { get; private set; }

        readonly List<float> runFrames = new List<float>();    // every frame time (seconds) since the run started
        readonly StringBuilder csv = new StringBuilder();
        ProfilerRecorder drawCalls, triangles;
        float windowTime, runTime, runSum; int windowFrames;
        float routeStart = -1f;
        string path;

        void OnEnable()
        {
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            path = Path.Combine(Application.persistentDataPath, "fps-log.csv");
            if (writeCsv)
            {
                File.WriteAllText(path, "time_s,frame_ms,label\n");
                Debug.Log("[HCD545] fps-log.csv: " + path);
            }
            if (startRouteButton) startRouteButton.onClick.AddListener(StartRoute);
        }

        void OnDisable()
        {
            if (startRouteButton) startRouteButton.onClick.RemoveListener(StartRoute);
            Flush();
            drawCalls.Dispose(); triangles.Dispose();
        }

        // Start a new run (the average and the 1 % low start again). PerfLevels calls this when you change level.
        public void NewRun(string newLabel)
        {
            label = newLabel;
            runFrames.Clear();
            runSum = 0f;
            OnePercentLow = 0f;
            if (onePercentLowText) onePercentLowText.text = "--";
            if (averageText) averageText.text = "--";
        }

        // The Start route button: play a short warm-up first, then press it and play your fixed route.
        public void StartRoute()
        {
            RouteCount++;
            NewRun("route " + RouteCount);
            routeStart = runTime;
            if (routeText) routeText.text = label + ": 0:00";
            Debug.Log("[HCD545] " + label + " started at " + runTime.ToString("0.0") + " s");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            runFrames.Add(dt); runSum += dt; runTime += dt;
            windowFrames++; windowTime += dt;
            if (writeCsv) csv.Append(runTime.ToString("0.000")).Append(',').Append((dt * 1000f).ToString("0.00")).Append(',').Append(label).Append('\n');

            if (windowTime < 0.5f) return;      // update the numbers twice a second
            Fps = windowFrames / windowTime;
            FrameMs = windowTime / windowFrames * 1000f;
            OnePercentLow = ComputeOnePercentLow();
            windowFrames = 0; windowTime = 0f;
            Flush();

            if (fpsText) fpsText.text = Fps.ToString("0");
            if (frameTimeText) frameTimeText.text = FrameMs.ToString("0.0");
            if (onePercentLowText) onePercentLowText.text = OnePercentLow.ToString("0.0");
            if (averageText) averageText.text = AverageFps.ToString("0.0");
            if (routeText && routeStart >= 0f)
            {
                int sec = (int)(runTime - routeStart);
                routeText.text = label + ": " + sec / 60 + ":" + (sec % 60).ToString("00");
            }
            if (drawCallsText) drawCallsText.text = drawCalls.Valid && drawCalls.LastValue > 0 ? drawCalls.LastValue.ToString() : "n/a";
            if (trianglesText) trianglesText.text = triangles.Valid && triangles.LastValue > 0 ? (triangles.LastValue / 1000f).ToString("0") + " k" : "n/a";
        }

        // Sort the frame times, take the slowest 1 %, and turn their average time back into FPS.
        float ComputeOnePercentLow()
        {
            if (runFrames.Count < 100) return Fps;
            var sorted = new List<float>(runFrames);
            sorted.Sort();
            int n = Mathf.Max(1, sorted.Count / 100);
            float sum = 0f;
            for (int i = sorted.Count - n; i < sorted.Count; i++) sum += sorted[i];
            return n / sum;
        }

        void Flush()
        {
            if (!writeCsv || csv.Length == 0) return;
            File.AppendAllText(path, csv.ToString());
            csv.Clear();
        }
    }
}
