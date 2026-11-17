using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    [Serializable]
    internal sealed class HillView
    {
        public bool Ready, Contested;
        public Vector3 From, To;
        public float MoveRemaining, MoveDuration, Radius, RedCharge, BlueCharge, RedProgress, BlueProgress;
        public int RedCaptures, BlueCaptures, Target;
    }

    // Only the host advances capture/movement. Clients receive presentation data.
    internal sealed class ArenaHill
    {
        internal HillScore Score { get; private set; } = new HillScore();
        internal int Target;
        private readonly List<Vector3> locations = new List<Vector3>();
        private Vector3 from, to;
        private float moving, retryAt;
        private bool ready, contested;
        private SceneInfo scene;
        private System.Random random;

        internal void Reset()
        { Score = new HillScore(); Target = 0; ready = contested = false; scene = null; moving = retryAt = 0; locations.Clear(); random = null; }

        internal void Tick(ArenaMatch match, float delta, bool canCharge)
        {
            if (!NetworkServer.active || !Plugin.Active || match.Rules.Mode != ArenaMode.KOTH) return;
            if (Target == 0) Target = match.Rules.FocusCaptures;
            if (!SceneInfo.instance) return;
            if (scene != SceneInfo.instance) { scene = SceneInfo.instance; ready = false; locations.Clear(); retryAt = 0; }
            if (!ready)
            {
                if (match.Clock < retryAt) return;
                retryAt = match.Clock + 1;
                if (random == null) random = new System.Random(unchecked((int)Run.instance.seed ^ 0x48494C4C));
                locations.Clear();
                foreach (Vector3 point in match.SpawnPositions)
                {
                    RaycastHit hit;
                    if (!Physics.Raycast(point + Vector3.up * 5, Vector3.down, out hit, 14, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.65f || Physics.CheckCapsule(hit.point + Vector3.up * 2.5f, hit.point + Vector3.up * 6, 2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    if (locations.All(p => (p - hit.point).sqrMagnitude > 100)) locations.Add(hit.point);
                }
                if (locations.Count == 0) return; // Retry as map/spawn nodes become available.
                from = to = locations[random.Next(locations.Count)]; moving = 0; ready = true;
                Plugin.Log("KOTH focus placed; " + locations.Count + " clear ground locations available.");
            }
            if (!canCharge) return;
            if (moving > 0) { moving = Mathf.Max(0, moving - delta); contested = false; return; }
            int red = 0, blue = 0;
            foreach (var player in match.AllPlayers)
            {
                if (!player.Connected || player.Dead || player.Side < 0 || !player.Master) continue;
                var body = player.Master.GetBody(); if (!body || !body.healthComponent || !body.healthComponent.alive) continue;
                var offset = body.footPosition - to;
                if (Mathf.Abs(offset.y) > 8 || new Vector2(offset.x, offset.z).sqrMagnitude > match.Rules.FocusRadius * match.Rules.FocusRadius
                    || Physics.Linecast(to + Vector3.up * 2, body.corePosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                if (player.Side == 0) red++; else blue++;
            }
            contested = red > 0 && blue > 0;
            int winner = Score.Tick(red, blue, delta, match.Rules.FocusChargeSeconds);
            if (winner < 0) return;
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = (winner == 0 ? "Red" : "Blue") + " team charged the Void Focus!" });
            from = to;
            var distant = locations.Where(p => (p - from).sqrMagnitude >= 4 * match.Rules.FocusRadius * match.Rules.FocusRadius).ToArray();
            if (distant.Length > 0) to = distant[random.Next(distant.Length)];
            else to = locations.OrderByDescending(p => (p - from).sqrMagnitude).First();
            moving = match.Rules.FocusMoveSeconds; contested = false;
            if (Score.Captures[winner] >= Target) match.Finish();
            ArenaNetwork.SendSnapshots();
        }

        internal HillView View(MatchSettings rules)
        {
            int target = Math.Max(1, Target == 0 ? rules.FocusCaptures : Target);
            return new HillView { Ready = ready, Contested = contested, From = from, To = to,
                MoveRemaining = moving, MoveDuration = rules.FocusMoveSeconds, Radius = rules.FocusRadius,
                RedCharge = Score.Charge[0], BlueCharge = Score.Charge[1], RedCaptures = Score.Captures[0], BlueCaptures = Score.Captures[1], Target = target,
                RedProgress = Score.Progress(0, target), BlueProgress = Score.Progress(1, target) };
        }
    }
}
