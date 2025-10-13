using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MapAirdrop", "Waynieoaks", "1.0.0")]
    [Description("Shows a rough map radius for random airdrops, so players can search for the drop.")]

    public class MapAirdrop : RustPlugin
    {
        // --- Settings ---
        private const bool debug = false; // toggle debug logs

        // --- Storage ---
        private readonly Dictionary<ulong, string> planeTypes = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, MapMarkerGenericRadius> markers = new Dictionary<ulong, MapMarkerGenericRadius>();

        // --- Hooks ---

        private void OnServerInitialized()
        {
            if (debug) Puts("[MapAirdrop] Server initialized. Tracking new supply drops from now on.");
        }

        private void Unload()
        {
            foreach (var marker in markers.Values)
                marker?.Kill();

            markers.Clear();
            planeTypes.Clear();

            if (debug) Puts("[MapAirdrop] Plugin unloaded. All markers cleared.");
        }

        // Fired when a supply signal spawns a plane
        private void OnCargoPlaneSignaled(CargoPlane plane, SupplySignal signal)
        {
            if (plane == null) return;

            if (signal != null)
            {
                planeTypes[plane.net.ID.Value] = "signal";
                if (debug) Puts($"[MapAirdrop] Plane {plane.net.ID} flagged as SUPPLY SIGNAL drop");
            }
            else
            {
                planeTypes[plane.net.ID.Value] = "random";
                if (debug) Puts($"[MapAirdrop] Plane {plane.net.ID} flagged as RANDOM drop");
            }
        }

        // Fired when Excavator calls in supplies
        private void OnExcavatorSuppliesRequested(ExcavatorSignalComputer computer, BasePlayer player, BaseEntity planeEntity)
        {
            var plane = planeEntity as CargoPlane;
            if (plane == null) return;

            planeTypes[plane.net.ID.Value] = "excav";
            if (debug) Puts($"[MapAirdrop] Plane {plane.net.ID} flagged as EXCAVATOR drop");
        }

        // Fired when a crate spawns in the world
        private void OnEntitySpawned(SupplyDrop drop)
        {
            if (drop == null || !drop.IsValid()) return;

            // Try to tag this crate with its origin type
            foreach (var kvp in planeTypes)
            {
                var plane = BaseNetworkable.serverEntities.Find(new NetworkableId(kvp.Key)) as CargoPlane;
                if (plane != null && Vector3.Distance(plane.transform.position, drop.transform.position) < 300f)
                {
                    var flag = drop.gameObject.AddComponent<DropFlag>();
                    flag.Type = kvp.Value;
                    if (debug) Puts($"[MapAirdrop] Drop {drop.net.ID} tagged as {kvp.Value.ToUpper()}");
                    return;
                }
            }

            if (debug) Puts($"[MapAirdrop] Drop {drop.net.ID} spawned but no plane match found → type UNKNOWN");
        }

        // Fired when the crate touches the ground
        private void OnSupplyDropLanded(SupplyDrop drop)
        {
            if (drop == null || !drop.IsValid()) return;

            var flag = drop.GetComponent<DropFlag>();
            string type = flag?.Type ?? "unknown";

            if (debug) Puts($"[MapAirdrop] SupplyDrop {drop.net.ID} LANDED at {drop.transform.position}. Type = {type}");

            switch (type)
			{
				case "signal":
					if (debug) Puts($"[MapAirdrop] Skipping marker for SUPPLY SIGNAL drop {drop.net.ID}");
					break;

				case "excav":
					if (debug) Puts($"[MapAirdrop] Skipping marker for EXCAVATOR drop {drop.net.ID}");
					break;

				default:
					// Treat anything else as random
					TrySpawnMarker(drop);
					break;
			}
        }

        private void OnEntityKill(SupplyDrop drop)
        {
            if (!drop.IsValid()) return;

            if (markers.TryGetValue(drop.net.ID.Value, out var marker))
            {
                marker?.Kill();
                markers.Remove(drop.net.ID.Value);

                if (debug) Puts($"[MapAirdrop] Marker removed for drop {drop.net.ID}");
            }
        }

        private void OnEntityKill(CargoPlane plane)
        {
            if (plane == null) return;

            if (planeTypes.Remove(plane.net.ID.Value))
            {
                if (debug) Puts($"[MapAirdrop] Plane {plane.net.ID} removed from tracking");
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected) return;

                if (debug) Puts($"[MapAirdrop] Player {player.displayName} connected, refreshing random drop markers...");

                foreach (var marker in markers.Values)
                {
                    marker.SendUpdate();
                    marker.SendNetworkUpdateImmediate();
                }
            });
        }

        // --- Core ---

        private void TrySpawnMarker(SupplyDrop drop)
        {
            if (markers.ContainsKey(drop.net.ID.Value)) return;

            float markerRadius = 1.4f;
            float maxOffset = markerRadius * 150f * 0.99f;

            Vector2 rand = UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(0f, maxOffset);
            Vector3 markerPos = drop.transform.position + new Vector3(rand.x, 0, rand.y);

            var markerEntity = GameManager.server.CreateEntity(
                "assets/prefabs/tools/map/genericradiusmarker.prefab",
                markerPos) as MapMarkerGenericRadius;

            if (markerEntity == null) return;

            markerEntity.alpha = 0.5f;
            markerEntity.color1 = Color.blue;
            markerEntity.radius = 1.4f;
            markerEntity.enableSaving = false;

            markerEntity.Spawn();
            markerEntity.SendUpdate();
            markerEntity.SendNetworkUpdateImmediate();

            markers.Add(drop.net.ID.Value, markerEntity);

            if (debug) Puts($"[MapAirdrop] Marker created for RANDOM drop {drop.net.ID} at {markerPos}, radius {markerEntity.radius}");
        }

        // --- Helper class for tagging ---
        private class DropFlag : FacepunchBehaviour
        {
            public string Type;
        }
    }
}
