using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

/// <summary>
/// Main entry point. Constructor, tick, input, debug utilities.
/// Config     -> RidersConfig.cs
/// Spawning   -> RidersSpawning.cs
/// Wheelie    -> RidersWheelie.cs   (tweak wheelie physics here!)
/// </summary>
public partial class RidersScript : Script
{
    public RidersScript()
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
            string zone = GetZoneName(Game.Player.Character.Position);
            int hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            Notification.PostTicker($"~y~Riders Debug: {(_debug ? "ON" : "OFF")} | Zone: {zone} | Hour: {hour}h | Riders: {_riders.Count} | Groups: {_activeGroups.Count}", true);
        }

        if (e.KeyCode == _reloadKey)
        {
            LoadConfig();
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
        if (!_debug || p == null || !p.Exists()) return;

        PointF pos = GTA.UI.Screen.WorldToScreen(p.Position + new Vector3(0, 0, 6f));
        if (pos.X == 0 && pos.Y == 0) return;

        var t = new TextElement(text, pos, 0.35f, color,
            GTA.UI.Font.ChaletComprimeCologne, Alignment.Center);
        t.Outline = true;
        t.Draw();
    }
}
