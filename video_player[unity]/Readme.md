# Husper Unity Video Player

This Unity project implements the real‑time VR playback client for Husper, a bio‑adaptive ABR framework. It loads network + biosignal‑aware bitrate policies (exported as ONNX) and streams DASH video chunks at runtime.

---



- **DASH Streaming**  
  Fetches and buffers video segments at multiple bitrates.

- **Bio‑Adaptive ABR**  
  Queries an ONNX policy every 400 ms, feeding in:  
  - Recent throughputs (last 8 samples)  
  - Current buffer level  
  - Real‑time eye, face & head signals  


- **Extensible**  
  Swap in alternate ABR controllers for comparison.

---

## Prerequisites

- Unity 2021.3.4 LTS (or newer)  
- .NET 4.x scripting runtime  
- [Unity Barracuda](https://docs.unity3d.com/Packages/com.unity.barracuda@latest) (for ONNX inference)  
- Windows / macOS / Linux (tested on all)

---

### To integrate trained model with unity, place the model in onnx format in Assets folder and attach the model to DQN game object inside unity.
### Build and run the application to device.

