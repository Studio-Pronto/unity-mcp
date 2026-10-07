---
title: Tool reference
sidebar_label: Tools
sidebar_class_name: sidebar-hidden
slug: /reference/tools
description: Auto-generated catalog of every MCP for Unity tool, grouped by domain.
---

# Tool reference

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

Every tool MCP for Unity exposes, generated directly from the Python `@mcp_for_unity_tool` registry under `Server/src/services/tools/`.

## `animation` &nbsp; (1 tool)
Animator control & AnimationClip creation
- **[`manage_animation`](./animation/manage_animation.md)** — Manage Unity animation: Animator control, AnimatorController editing, and AnimationClip creation.

## `asset_gen` &nbsp; (6 tools)
AI asset generation – 3D model gen/import, 2D image gen & audio gen (bring-your-own-key)
- **[`blender_bridge`](./asset_gen/blender_bridge.md)** — Bridge to a running Blender that has the BlenderMCP addon connected (socket, default 127.0.0.1:9876; configured in Window > MCP for Unity > Generative > Blender Bridge).
- **[`generate_audio`](./asset_gen/generate_audio.md)** — Generate audio (sound effects and background music) with fal.ai models and import them as AudioClips into the Unity project.
- **[`generate_image`](./asset_gen/generate_image.md)** — Generate 2D images with AI providers (fal.ai, OpenRouter) and import them as textures/sprites into the Unity project.
- **[`generate_model`](./asset_gen/generate_model.md)** — Generate 3D models with AI providers (Tripo, Meshy, fal) and import them into the Unity project.
- **[`import_model`](./asset_gen/import_model.md)** — Import 3D models from the Sketchfab marketplace into the Unity project.
- **[`import_model_file`](./asset_gen/import_model_file.md)** — Import a local 3D model file that already exists on disk (e.g. an FBX/OBJ/glTF exported from Blender or another DCC tool) into the Unity project.

## `auditor` &nbsp; (1 tool)
Project Auditor static analysis – code, assets, shaders, settings (Unity 6.4+)
- **[`manage_project_auditor`](./auditor/manage_project_auditor.md)** — Unity Project Auditor: static analysis of code, assets, shaders, settings.

## `core` &nbsp; (31 tools)
Essential scene, script, asset & editor tools (always on by default)
- **[`apply_text_edits`](./core/apply_text_edits.md)** — Apply small text edits to a C# script identified by URI.
- **[`batch_execute`](./core/batch_execute.md)** — Executes multiple MCP commands in a single batch for dramatically better performance.
- **[`create_script`](./core/create_script.md)** — Create a new C# script at the given project path.
- **[`debug_request_context`](./core/debug_request_context.md)** — Return the current FastMCP request context details (client_id, session_id, and meta dump).
- **[`execute_custom_tool`](./core/execute_custom_tool.md)** — Execute a project-scoped custom tool registered by Unity.
- **[`execute_menu_item`](./core/execute_menu_item.md)** — Execute a Unity menu item by path.
- **[`find_gameobjects`](./core/find_gameobjects.md)** — Search for GameObjects in the scene by name, tag, layer, component type, or path.
- **[`find_in_file`](./core/find_in_file.md)** — Searches a file with a regex pattern and returns line numbers and excerpts.
- **[`get_sha`](./core/get_sha.md)** — Get SHA256 and basic metadata for a Unity C# script without returning file contents.
- **[`manage_asset`](./core/manage_asset.md)** — Performs asset operations (import, create, modify, delete, etc.) in Unity.
- **[`manage_build`](./core/manage_build.md)** — Manage Unity player builds — trigger builds, switch platforms, configure settings, manage build scenes and profiles, run batch builds across platforms. settings accepts any PlayerSettings property (e.g. bakeCollisionMeshes, allowUnsafeCo…
- **[`manage_camera`](./core/manage_camera.md)** — Manage cameras (Unity Camera + Cinemachine).
- **[`manage_components`](./core/manage_components.md)** — Add, remove, or set properties on components attached to GameObjects.
- **[`manage_editor`](./core/manage_editor.md)** — Controls and queries the Unity editor's state and settings.
- **[`manage_gameobject`](./core/manage_gameobject.md)** — Performs CRUD operations on GameObjects.
- **[`manage_graphics`](./core/manage_graphics.md)** — Manage rendering graphics: volumes, post-processing, light baking, rendering stats, pipeline settings, and URP renderer features.
- **[`manage_material`](./core/manage_material.md)** — Manages Unity materials (set properties, colors, shaders, etc).
- **[`manage_packages`](./core/manage_packages.md)** — Manage Unity packages: query, install, remove, embed, and configure registries.
- **[`manage_physics`](./core/manage_physics.md)** — Manage physics settings, collision matrix, materials, joints, queries, and validation.
- **[`manage_prefabs`](./core/manage_prefabs.md)** — Manages Unity Prefab assets.
- **[`manage_profiler`](./core/manage_profiler.md)** — Unity Profiler: CPU hotspots, memory, counters, frame time, captures.
- **[`manage_project_settings`](./core/manage_project_settings.md)** — Read, write, and discover Unity project settings across categories: quality (QualitySettings), physics, physics2d, time, editor (EditorSettings).
- **[`manage_scene`](./core/manage_scene.md)** — Performs CRUD operations on Unity scenes.
- **[`manage_script`](./core/manage_script.md)** — Compatibility router for legacy script operations.
- **[`manage_script_capabilities`](./core/manage_script_capabilities.md)** — Get manage_script capabilities (supported ops, limits, and guards).
- **[`manage_tools`](./core/manage_tools.md)** — Manage which tool groups are visible in this session.
- **[`read_console`](./core/read_console.md)** — Gets messages from or clears the Unity Editor console.
- **[`refresh_unity`](./core/refresh_unity.md)** — Request a Unity asset database refresh and optionally a script compilation.
- **[`script_apply_edits`](./core/script_apply_edits.md)** — Structured C# edits (methods/classes) with safer boundaries - prefer this over raw text.
- **[`set_active_instance`](./core/set_active_instance.md)** — Set the active Unity instance for this client/session.
- **[`validate_script`](./core/validate_script.md)** — Validate a C# script and return diagnostics.

## `docs` &nbsp; (2 tools)
Unity API reflection and documentation lookup
- **[`unity_docs`](./docs/unity_docs.md)** — Fetch official Unity documentation from docs.unity3d.com.
- **[`unity_reflect`](./docs/unity_reflect.md)** — Inspect Unity's live C# API via reflection.

## `probuilder` &nbsp; (1 tool)
ProBuilder 3D modeling – requires com.unity.probuilder package
- **[`manage_probuilder`](./probuilder/manage_probuilder.md)** — Manage ProBuilder meshes for in-editor 3D modeling.

## `scripting_ext` &nbsp; (2 tools)
ScriptableObject create/modify — serialized property patching, GUID & path object references, arrays, dry-run validation
- **[`execute_code`](./scripting_ext/execute_code.md)** — Execute arbitrary C# code inside the Unity Editor.
- **[`manage_scriptable_object`](./scripting_ext/manage_scriptable_object.md)** — Creates and modifies ScriptableObject assets via serialized property patching.

## `testing` &nbsp; (2 tools)
Test runner & async test jobs
- **[`get_test_job`](./testing/get_test_job.md)** — Polls an async Unity test job by job_id.
- **[`run_tests`](./testing/run_tests.md)** — Starts a Unity test run asynchronously and returns a job_id immediately.

## `ui` &nbsp; (1 tool)
UI Toolkit (UXML, USS, UIDocument)
- **[`manage_ui`](./ui/manage_ui.md)** — Manages Unity UI Toolkit elements (UXML documents, USS stylesheets, UIDocument components).

## `vfx` &nbsp; (3 tools)
Visual effects – VFX Graph, shaders, procedural textures
- **[`manage_shader`](./vfx/manage_shader.md)** — Manages shader scripts in Unity (create, read, update).
- **[`manage_texture`](./vfx/manage_texture.md)** — Procedural texture generation for Unity.
- **[`manage_vfx`](./vfx/manage_vfx.md)** — Manage Unity VFX: ParticleSystem, VisualEffect (VFX Graph), LineRenderer, TrailRenderer.

