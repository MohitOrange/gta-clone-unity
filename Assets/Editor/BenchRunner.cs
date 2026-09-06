using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Enters Play mode from the command line so a benchmark can run with nobody at the
    /// keyboard.
    ///
    /// Deliberately does <b>not</b> pass <c>-quit</c>: the Editor has to stay alive and keep
    /// pumping its loop for Play mode to actually run frames. <see cref="CrowdBenchmark"/>
    /// takes over from there and calls <c>EditorApplication.Exit</c> when it has its sample,
    /// so the process still terminates on its own.
    ///
    /// Invoked as:
    /// <code>
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod MiniGTA.EditorTools.BenchRunner.Run -benchSeconds 30 -logFile bench.log
    /// </code>
    /// <c>-nographics</c> is deliberately omitted. The point of the measurement is the frame
    /// cost, and most of that is rendering; a run with no graphics device would produce a
    /// confident number that means nothing.
    /// </summary>
    public static class BenchRunner
    {
        public static void Run()
        {
            Debug.Log("[BENCH] BenchRunner entering Play mode "
                      + "(Editor batch mode, desktop -- NOT a device measurement)");

            if (EditorApplication.isPlaying)
            {
                Debug.Log("[BENCH] already playing");
                return;
            }

            EditorApplication.EnterPlaymode();
        }
    }
}
