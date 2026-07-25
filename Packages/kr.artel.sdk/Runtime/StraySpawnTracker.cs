using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// Records every root object alive before a scene is visited, so the ones that visit leaves
    /// behind can be handed to the scene that is about to be unloaded and die with it.
    /// </summary>
    /// <remarks>
    /// A visited scene escapes its own unload two ways: <c>DontDestroyOnLoad</c> moves objects to
    /// a scene of their own, and anything its <c>Awake</c> or <c>Start</c> instantiates lands in
    /// whatever scene is active at the time — the game's, since a scene cannot be made active
    /// until it has finished loading. Both end up as new roots somewhere, which is what this
    /// tracks.
    /// </remarks>
    internal sealed class StraySpawnTracker
    {
        private readonly HashSet<int> preexisting = new HashSet<int>();

        public void Capture()
        {
            preexisting.Clear();
            foreach (var scene in LoadedScenes())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    preexisting.Add(root.GetInstanceID());
                }
            }
        }

        /// <summary>
        /// Moves every root that appeared since <see cref="Capture"/> into <paramref name="doomed"/>,
        /// skipping that scene's own contents. Returns how many were moved. Unloading
        /// <paramref name="doomed"/> then destroys them along with it, running their
        /// <c>OnDestroy</c> as a normal unload would.
        /// </summary>
        public int MoveInto(Scene doomed)
        {
            var moved = 0;
            foreach (var scene in LoadedScenes())
            {
                if (scene == doomed)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (preexisting.Contains(root.GetInstanceID()))
                    {
                        continue;
                    }

                    // Only roots can be moved between scenes. An object the visited scene parented
                    // under something the game owns is unreachable from here, and stays.
                    SceneManager.MoveGameObjectToScene(root, doomed);
                    moved++;
                }
            }

            return moved;
        }

        private static IEnumerable<Scene> LoadedScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    yield return scene;
                }
            }

            yield return ResolveDontDestroyOnLoadScene();
        }

        private static Scene ResolveDontDestroyOnLoadScene()
        {
            // Unity hands out no reference to the DontDestroyOnLoad scene, and SceneManager does
            // not count it. Moving a throwaway object into it is the only way to read the handle
            // back, and it doubles as a way to make the scene exist at all — it does not until
            // something is put there.
            var probe = new GameObject("Artel DontDestroyOnLoad Probe");
            Object.DontDestroyOnLoad(probe);
            var resolved = probe.scene;
            Object.DestroyImmediate(probe);
            return resolved;
        }
    }
}
