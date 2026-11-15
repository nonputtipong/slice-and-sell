# Setup notes – Quest 3 build settings (draft for Assignment 2)

Values below were read from the project files (`ProjectSettings/`, `Assets/XR/Settings/`). "Why" is my understanding from the Week 4 lecture; check against the slides.

## OpenXR and features (Android tab)
- **OpenXR (XR Plug-in Management, Android)** – ticked. The standard API between Unity and the headset runtime; one app can run on many headsets. Off on Windows/Mac/Linux so Play uses the XR Interaction Simulator instead of starting Meta Horizon Link.
- **Meta Quest Support** – feature that adds Quest-specific parts of the Meta OpenXR runtime (Android manifest entries, Quest app setup). Without it the app is not recognised as a Quest app.
- **Oculus Touch Controller Profile** and **Meta Quest Touch Plus Controller Profile** – interaction profiles. They tell OpenXR which controller layouts (trigger, grip, thumbstick, A/B/X/Y) the app understands; Touch Plus is the Quest 3 controller. With no profile enabled the runtime gives the app no controller input.
- **Hand Tracking Subsystem** and **Meta Hand Tracking Aim** – hand joints and an aim pose (pinch ray) for Lab 5. Turned on now so the manifest/build is the same for Labs 4–6 and not rebuilt later.
- **Render Mode: Single Pass Instanced / Multi-view** – both eyes drawn in one pass; roughly halves CPU draw-call cost, needed to hold frame rate on mobile.

## Player settings (Android → Other Settings)
- **Color Space: Linear** – lighting maths is physically correct; URP default look.
- **Graphics API: Vulkan** – modern low-overhead API Quest supports; needed for current Meta features and better CPU cost than OpenGL ES.
- **Scripting Backend: IL2CPP** – C# compiled to native code; required for 64-bit ARM Android builds and faster than Mono.
- **Target Architecture: ARM64** – the Quest 3 CPU (Snapdragon XR2 Gen 2) is 64-bit ARM; Quest store apps must be ARM64.
- **Package Name: `com.cmkl.hcd545.puttipong`** – unique app ID; same ID on a shared headset overwrites the other app. Changed from `...quest` to our group name.
- **Product Name: `puttipong`** – the name shown in the headset's Library (Unknown Sources).
- Also set in the starter: minimum API level 32, ASTC texture compression, Input System (New) as active input handler.
