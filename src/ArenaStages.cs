using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;

namespace ArenaPvP
{
    internal static class ArenaStages
    {
        internal static bool Playable(SceneDef scene)
        { return scene && !scene.isOfflineScene && !scene.suppressPlayerEntry &&
            (scene.sceneType == SceneType.Stage || scene.sceneType == SceneType.Intermission ||
             scene.sceneType == SceneType.TimedIntermission || scene.sceneType == SceneType.UntimedStage); }
        internal static SceneDef[] All()
        {
            var stages = new List<SceneDef>();
            for (int i = 0; i < SceneCatalog.sceneDefCount; i++)
            { var scene = SceneCatalog.GetSceneDef((SceneIndex)i); if (Playable(scene)) stages.Add(scene); }
            return stages.OrderBy(s => Language.GetString(s.nameToken)).ThenBy(s => s.cachedName).ToArray();
        }
        internal static bool Matches(SceneDef scene, string name)
        { return string.Equals(name.Trim(), scene.cachedName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name.Trim(), scene.baseSceneName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name.Trim(), Language.GetString(scene.nameToken), StringComparison.OrdinalIgnoreCase); }
    }
}
