using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Profiling;
using UProfiler = UnityEngine.Profiling.Profiler;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class RenderingStatsOps
    {
        private static readonly (string counterName, string jsonKey)[] COUNTER_MAP = new[]
        {
            ("Draw Calls Count", "draw_calls"),
            ("Batches Count", "batches"),
            ("SetPass Calls Count", "set_pass_calls"),
            ("Triangles Count", "triangles"),
            ("Vertices Count", "vertices"),
            ("Dynamic Batches Count", "dynamic_batches"),
            ("Dynamic Batched Draw Calls Count", "dynamic_batched_draw_calls"),
            ("Static Batches Count", "static_batches"),
            ("Static Batched Draw Calls Count", "static_batched_draw_calls"),
            ("Instanced Batches Count", "instanced_batches"),
            ("Instanced Batched Draw Calls Count", "instanced_batched_draw_calls"),
            ("Shadow Casters Count", "shadow_casters"),
            ("Render Textures Count", "render_textures"),
            ("Render Textures Bytes", "render_textures_bytes"),
            ("Used Textures Count", "used_textures"),
            ("Used Textures Bytes", "used_textures_bytes"),
            ("Render Textures Changes Count", "render_target_changes"),
            ("Visible Skinned Meshes Count", "visible_skinned_meshes"),
        };

        // === stats_get ===
        internal static object GetStats(JObject @params)
        {
            var stats = new Dictionary<string, object>();

            foreach (var (counterName, jsonKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, counterName);
                stats[jsonKey] = recorder.Valid ? recorder.CurrentValue : 0;
            }

            return new
            {
                success = true,
                message = "Rendering stats captured.",
                data = stats
            };
        }

        // === stats_list_counters ===
        internal static object ListCounters(JObject @params)
        {
            var p = new ToolParams(@params);
            string categoryName = p.Get("category");

            // Default to "Render" category to avoid massive payloads (all categories = 300K+ chars)
            ProfilerCategory category = ProfilerCategory.Render;
            if (!string.IsNullOrEmpty(categoryName))
            {
                category = TryResolveCategory(categoryName);
            }

            var allHandles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(allHandles);
            var counters = allHandles
                .Select(h => ProfilerRecorderHandle.GetDescription(h))
                .Where(d => string.Equals(d.Category.Name, category.Name, StringComparison.OrdinalIgnoreCase))
                .Select(d => new
                {
                    name = d.Name,
                    category = d.Category.Name,
                    unit = d.UnitType.ToString()
                })
                .OrderBy(c => c.name).ToList();

            return new
            {
                success = true,
                message = $"Found {counters.Count} counters in category '{category.Name}'.",
                data = new { counters }
            };
        }

        // === stats_set_scene_debug_mode ===
        internal static object SetSceneDebugMode(JObject @params)
        {
            var p = new ToolParams(@params);
            string modeName = p.Get("mode");
            if (string.IsNullOrEmpty(modeName))
            {
                var validModes = string.Join(", ", Enum.GetNames(typeof(DrawCameraMode)).Take(20));
                return new ErrorResponse(
                    $"'mode' parameter required. Options: {validModes}");
            }

            if (!Enum.TryParse<DrawCameraMode>(modeName, true, out var drawMode))
            {
                var validModes = string.Join(", ", Enum.GetNames(typeof(DrawCameraMode)).Take(20));
                return new ErrorResponse($"Unknown mode '{modeName}'. Valid: {validModes}");
            }

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return new ErrorResponse("No active Scene View found.");

            sceneView.cameraMode = SceneView.GetBuiltinCameraMode(drawMode);
            sceneView.Repaint();

            if (!p.GetBool("capture"))
            {
                return new
                {
                    success = true,
                    message = $"Scene debug mode set to '{drawMode}'."
                };
            }

            // capture=true: render the Scene View in the new debug mode (e.g. Overdraw) and
            // return the image inline by composing manage_scene's scene-view screenshot.
            // Note: Repaint() is queued; if the captured frame lags the mode change, re-issue.
            var screenshotParams = new JObject
            {
                ["action"] = "screenshot",
                ["capture_source"] = "scene_view",
                ["include_image"] = true,
            };
            object screenshot = CommandRegistry.InvokeCommandAsync("manage_scene", screenshotParams)
                                               .GetAwaiter().GetResult();

            return new
            {
                success = true,
                message = $"Scene debug mode set to '{drawMode}' and Scene View captured.",
                data = new { mode = drawMode.ToString(), screenshot }
            };
        }

        // === stats_get_memory ===
        internal static object GetMemory(JObject @params)
        {
            var data = new Dictionary<string, object>
            {
                ["totalAllocatedMB"] = Math.Round(UProfiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 2),
                ["totalReservedMB"] = Math.Round(UProfiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0), 2),
                ["totalUnusedReservedMB"] = Math.Round(UProfiler.GetTotalUnusedReservedMemoryLong() / (1024.0 * 1024.0), 2),
                ["monoUsedMB"] = Math.Round(UProfiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 2),
                ["monoHeapMB"] = Math.Round(UProfiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0), 2),
                ["graphicsDriverMB"] = Math.Round(UProfiler.GetAllocatedMemoryForGraphicsDriver() / (1024.0 * 1024.0), 2),
            };

            return new
            {
                success = true,
                message = "Memory stats captured.",
                data
            };
        }

        // === stats_get_texture_streaming ===
        internal static object GetTextureStreaming(JObject @params)
        {
            const double MB = 1024.0 * 1024.0;
            var data = new Dictionary<string, object>
            {
                // Memory (bytes -> MB). desired > target indicates the budget is exceeded.
                ["totalTextureMemoryMB"] = Math.Round(Texture.totalTextureMemory / MB, 2),
                ["desiredTextureMemoryMB"] = Math.Round(Texture.desiredTextureMemory / MB, 2),
                ["targetTextureMemoryMB"] = Math.Round(Texture.targetTextureMemory / MB, 2),
                ["currentTextureMemoryMB"] = Math.Round(Texture.currentTextureMemory / MB, 2),
                ["nonStreamingTextureMemoryMB"] = Math.Round(Texture.nonStreamingTextureMemory / MB, 2),
                // Counts
                ["streamingTextureCount"] = Texture.streamingTextureCount,
                ["nonStreamingTextureCount"] = Texture.nonStreamingTextureCount,
                ["streamingMipmapUploadCount"] = Texture.streamingMipmapUploadCount,
                ["streamingRendererCount"] = Texture.streamingRendererCount,
                ["streamingTexturePendingLoadCount"] = Texture.streamingTexturePendingLoadCount,
                ["streamingTextureLoadingCount"] = Texture.streamingTextureLoadingCount,
                // Mipmap streaming quality settings
                ["mipmapStreamingActive"] = QualitySettings.streamingMipmapsActive,
                ["mipmapStreamingMemoryBudgetMB"] = QualitySettings.streamingMipmapsMemoryBudget,
                ["mipmapStreamingMaxLevelReduction"] = QualitySettings.streamingMipmapsMaxLevelReduction,
                ["mipmapStreamingRenderersPerFrame"] = QualitySettings.streamingMipmapsRenderersPerFrame,
            };

            return new
            {
                success = true,
                message = "Texture streaming stats captured.",
                data
            };
        }

        // --- Helper: Try to resolve a ProfilerCategory by name ---
        private static ProfilerCategory TryResolveCategory(string name)
        {
            // ProfilerCategory has static properties for well-known categories
            switch (name.ToLowerInvariant())
            {
                case "render": return ProfilerCategory.Render;
                case "scripts": return ProfilerCategory.Scripts;
                case "memory": return ProfilerCategory.Memory;
                case "physics": return ProfilerCategory.Physics;
                case "animation": return ProfilerCategory.Animation;
                case "audio": return ProfilerCategory.Audio;
                case "lighting": return ProfilerCategory.Lighting;
                case "network": return ProfilerCategory.Network;
                case "gui": return ProfilerCategory.Gui;
                case "ai": return ProfilerCategory.Ai;
                case "video": return ProfilerCategory.Video;
                case "loading": return ProfilerCategory.Loading;
                case "input": return ProfilerCategory.Input;
                case "vr": return ProfilerCategory.Vr;
                case "internal": return ProfilerCategory.Internal;
                default: return ProfilerCategory.Render;
            }
        }
    }
}
