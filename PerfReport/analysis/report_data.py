"""Builds PerfReport/analysis/report-data.json from the measured runs. Every number comes from a file in PerfReport/data:
  fps-log.csv   (FrameHud FpsLogger)  average FPS, 1% low, missed frames      - first WINDOW s of the route
  perf-log.csv  (PerfStatsLogger)     draw calls, SetPass, batches, shadow casters, triangles - same window
  vrapi.txt     (Quest compositor)    App = app GPU time per frame (1 line per second) - same window
  profiler-frames.csv (Unity Profiler over USB, last 2000 frames of the 60 s profiler run)
                                      main / render thread WORK = thread time minus waits (XR frame pacing, Gfx waits)
usage: python -I report_data.py
"""
import csv, json, re, statistics as st
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "data"
WINDOW = 180.0                     # s: every walkthrough compared over the same first 3:00 of its route (B's route was 3:00)
RUNS = {"A": "A-current", "B": "B-before-opt1", "C": "C-before-opt2"}


def route_label(d):
    p = d / "label.txt"
    return p.read_text().strip() if p.exists() else "route 1"


def walkthrough(d):
    label = route_label(d)
    rows = [r for r in csv.DictReader(open(d / "fps-log.csv", newline="")) if r["label"] == label]
    t0 = float(rows[0]["time_s"]) - float(rows[0]["frame_ms"]) / 1000.0
    rows = [r for r in rows if float(r["time_s"]) - t0 <= WINDOW]
    ft = [float(r["frame_ms"]) for r in rows]
    s = sorted(ft); n = max(1, len(s) // 100)
    out = {"label": label, "frames": len(ft), "seconds": round(sum(ft) / 1000, 1),
           "avg_fps": round(len(ft) / (sum(ft) / 1000), 1),
           "low1_fps": round(n / (sum(s[-n:]) / 1000), 1),
           "frame_ms_median": round(st.median(ft), 2),
           "missed": sum(1 for x in ft if x > 1000 / 72 * 1.5)}
    t_end = float(rows[-1]["time_s"])
    perf = [r for r in csv.DictReader(open(d / "perf-log.csv", newline="")) if r["label"] == label and float(r["time_s"]) <= t_end + 0.05]
    for k in ("draw_calls", "setpass", "batches", "shadow_casters", "triangles"):
        v = [float(r[k]) for r in perf if float(r[k]) > 0]
        out[k] = round(st.mean(v), 1) if v else None
    app = []
    for line in open(d / "vrapi.txt", errors="ignore"):
        m = re.search(r"App=([\d.]+)ms", line)
        if m: app.append(float(m.group(1)))
    app = app[: int(WINDOW)]                                   # one VrApi line per second from the route start
    out["gpu_app_ms"] = round(st.mean(app), 2) if app else None
    out["gpu_app_ms_p95"] = round(sorted(app)[int(len(app) * 0.95) - 1], 2) if app else None
    out["gpu_samples"] = len(app)
    return out


def profiler(d):
    p = d / "profiler-frames.csv"
    if not p.exists(): return None
    rows = list(csv.DictReader(open(p, newline="")))
    if not rows: return None
    out = {"frames": len(rows)}
    for k in ("frame_ms", "main_work_ms", "main_wait_ms", "render_work_ms", "render_wait_ms", "draw_calls", "setpass", "batches", "shadow_casters"):
        v = [float(r[k]) for r in rows if float(r[k]) >= 0]
        out[k] = {"mean": round(st.mean(v), 2), "p95": round(sorted(v)[int(len(v) * 0.95) - 1], 2)}
    return out


data = {"window_s": WINDOW}
for key, name in RUNS.items():
    d = ROOT / name
    if not (d / "fps-log.csv").exists(): continue
    data[key] = {"run": name, "walkthrough": walkthrough(d), "profiler": profiler(d)}
out = Path(__file__).resolve().parent / "report-data.json"
json.dump(data, open(out, "w"), indent=1)
print(json.dumps(data, indent=1))
