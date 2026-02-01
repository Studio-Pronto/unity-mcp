# Fork To-Do List

Feature requests and improvements planned for the Studio-Pronto/unity-mcp fork.

## Planned Features

### Profiler Tool

Add MCP access to Unity's Profiler for performance analysis.

**Potential capabilities:**
- Start/stop profiler recording
- Get CPU frame timings (main thread, render thread)
- Get GPU timings
- Memory allocation stats and GC collection counts
- Top N most expensive methods per frame
- Save/load profiler captures

**Use cases:**
- Automated performance regression testing
- AI-assisted performance optimization
- Profiling specific code paths on demand

### Frame Debugger Tool

Add MCP access to Unity's Frame Debugger for rendering analysis.

**Potential capabilities:**
- Enable/disable frame debugger
- List draw calls for current frame
- Get details per draw call (shader, material, render state, mesh)
- Filter draw calls by material/shader/pass
- Capture frame for analysis

**Use cases:**
- Debug rendering issues
- Analyze overdraw and batching
- Understand shader/material usage per object

---

## Completed

- [x] Component-type field references in `set_property` (9.2.0-fork.3)
- [x] Struct array support in `manage_components` (9.2.0-fork.2)
- [x] Prefab Stage dirty marking fix (9.0.8-fork.1)
- [x] `"find"` instruction for object references (9.0.8-fork.1)
