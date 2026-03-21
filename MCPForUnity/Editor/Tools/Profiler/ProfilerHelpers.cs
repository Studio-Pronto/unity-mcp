using System;
using System.Threading.Tasks;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class ProfilerHelpers
    {
        internal static Task WaitForFrames(int frameCount)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int counted = 0;
            var start = DateTime.UtcNow;

            void Tick()
            {
                try
                {
                    if (tcs.Task.IsCompleted)
                    {
                        EditorApplication.update -= Tick;
                        return;
                    }

                    if ((DateTime.UtcNow - start).TotalSeconds > 25)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(true); // return what we have rather than error
                        return;
                    }

                    counted++;
                    if (counted >= frameCount)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(true);
                    }
                }
                catch (Exception ex)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetException(ex);
                }
            }

            EditorApplication.update += Tick;
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
            return tcs.Task;
        }

    }
}
