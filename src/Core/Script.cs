using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Main entry point. Constructor, tick, input, debug utilities.
    /// Config          -> Core/Config.cs
    /// Models          -> Models/
    /// Spawning        -> Systems/SpawnSystem.cs
    /// Rider logic     -> Systems/RiderProcessor.cs
    /// Follow player   -> Systems/FollowPlayerSystem.cs
    /// Bodyguard       -> Systems/BodyguardSystem.cs
    /// Cleanup         -> Systems/CleanupSystem.cs
    /// Wheelie physics -> Physics/WheeliePhysics.cs
    /// </summary>
    public partial class StreetRidersScript : Script
    {
        public StreetRidersScript()
        {
            _bikeConfigs = new Dictionary<string, WheelieConfig>(StringComparer.OrdinalIgnoreCase);
            _groupConfigs = new List<GroupConfig>();
            LoadConfig();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            Notification.PostTicker($"~g~Street Riders v1.1.0: Loaded! {_groupConfigs.Count} groups, {_bikeConfigs.Count} bike configs", true);
        }

        // =========================================================================
        //  Main Tick
        // =========================================================================

        private void OnTick(object sender, EventArgs e)
        {
            if (!_enabled) return;

            int now = Game.GameTime;

            if (now - _lastSpawnCheck > _spawnIntervalMs)
            {
                _lastSpawnCheck = now;
                TrySpawnGroup();
            }

            if (now - _lastLogicCheck > LOGIC_INTERVAL_MS)
            {
                _lastLogicCheck = now;
                if (_applyToNatural)
                    ScanNaturalRiders();
                CleanupAll();
                RefreshDestinations();
            }

            ProcessFollowPlayer();
            ProcessAllRiders();
        }

        // =========================================================================
        //  Input
        // =========================================================================

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == _debugKey)
            {
                _debug = !_debug;
                RefreshAllBlips();
                string zone = GetZoneName(Game.Player.Character.Position);
                int hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
                Notification.PostTicker($"~y~Riders Debug: {(_debug ? "ON" : "OFF")} | Zone: {zone} | Hour: {hour}h | Riders: {_riders.Count} | Groups: {_activeGroups.Count}", true);
            }

            if (e.KeyCode == _reloadKey)
            {
                LoadConfig();
                RefreshAllBlips();
                Notification.PostTicker($"~g~Street Riders: Reloaded! ({_groupConfigs.Count} groups) {(_testMode ? "~y~[TEST MODE]" : "")}", true);
            }

            // ===== Follow Player Toggle =====
            if (_followEnabled && e.KeyCode == _followToggleKey)
            {
                ToggleFollowPlayer();
            }

            // ===== Test Mode Keys =====
            if (!_testMode) return;

            if (e.KeyCode == _forceSpawnKey)
            {
                ForceSpawnGroup();
            }

            if (e.KeyCode == _cleanAllKey)
            {
                CleanAllRiders();
            }
        }

        // =========================================================================
        //  Abort (clean all spawned entities)
        // =========================================================================

        private void OnAborted(object sender, EventArgs e)
        {
            // Remove all blips before deleting entities
            foreach (var kv in _riders)
                RemoveRiderBlip(kv.Value);

            foreach (var group in _activeGroups)
            {
                foreach (int ph in group.PedHandles)
                {
                    Ped p = (Ped)Entity.FromHandle(ph);
                    if (p != null && p.Exists()) p.Delete();
                }
                foreach (int bh in group.BikeHandles)
                {
                    Vehicle v = (Vehicle)Entity.FromHandle(bh);
                    if (v != null && v.Exists()) v.Delete();
                }
            }
            _activeGroups.Clear();
            _riders.Clear();
        }

        // =========================================================================
        //  Utilities
        // =========================================================================

        private static string GetZoneName(Vector3 pos)
        {
            return Function.Call<string>(Hash.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z) ?? "";
        }

        private static Vector3 GetHeadingDir(float heading)
        {
            float rad = heading * (float)(Math.PI / 180.0);
            return new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);
        }

        private void DrawDebugText(Ped p, string text, Color color)
        {
            if (!_debugShowOverhead || p == null || !p.Exists()) return;

            PointF pos = GTA.UI.Screen.WorldToScreen(p.Position + new Vector3(0, 0, 6f));
            if (pos.X == 0 && pos.Y == 0) return;

            var t = new TextElement(text, pos, 0.35f, color,
                GTA.UI.Font.ChaletComprimeCologne, Alignment.Center);
            t.Outline = true;
            t.Draw();
        }

        // =========================================================================
        //  Blip Management
        // =========================================================================

        /// <summary>
        /// Adds a minimap blip to a rider ped using the group's configured sprite, color, and scale.
        /// Falls back to General defaults if the group config is not found.
        /// </summary>
        private void AddRiderBlip(Ped ped, RiderState state)
        {
            if (ped == null || !ped.Exists()) return;
            if (state.BlipHandle != 0) return;

            // Resolve blip settings from group config
            int sprite = _defaultBlipSprite;
            int color = _defaultBlipColor;
            float scale = _defaultBlipScale;
            string name = "Rider";

            GroupConfig gcfg = FindGroupConfigByGroupId(state.GroupId);
            if (gcfg != null)
            {
                sprite = gcfg.BlipSprite;
                color = gcfg.BlipColor;
                scale = gcfg.BlipScale;
                name = gcfg.BlipName ?? gcfg.Name;
            }

            Blip blip = ped.AddBlip();
            if (blip != null)
            {
                blip.Sprite = (BlipSprite)sprite;
                blip.Color = (BlipColor)color;
                blip.Scale = scale;
                blip.Name = name;
                blip.IsShortRange = true;
                state.BlipHandle = blip.Handle;
            }
        }

        /// <summary>
        /// Finds the GroupConfig for a rider based on their group ID.
        /// </summary>
        private GroupConfig FindGroupConfigByGroupId(int groupId)
        {
            if (groupId < 0) return null;
            foreach (var ag in _activeGroups)
            {
                if (ag.Id == groupId)
                    return FindGroupConfig(ag.ConfigName);
            }
            return null;
        }

        /// <summary>
        /// Removes the minimap blip from a rider.
        /// </summary>
        private void RemoveRiderBlip(RiderState state)
        {
            if (state.BlipHandle == 0) return;

            Blip blip = new Blip(state.BlipHandle);
            if (blip != null && blip.Exists())
                blip.Delete();
            state.BlipHandle = 0;
        }

        /// <summary>
        /// Updates blips for all tracked riders based on _debugShowBlips.
        /// Called when config is reloaded or debug mode toggled.
        /// </summary>
        private void RefreshAllBlips()
        {
            foreach (var kv in _riders)
            {
                RiderState state = kv.Value;
                Ped ped = (Ped)Entity.FromHandle(kv.Key);

                if (_debugShowBlips)
                {
                    if (ped != null && ped.Exists() && state.BlipHandle == 0)
                        AddRiderBlip(ped, state);
                }
                else
                {
                    RemoveRiderBlip(state);
                }
            }
        }
    }
}
