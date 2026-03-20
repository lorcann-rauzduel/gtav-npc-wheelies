using GTA;
using GTA.Native;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Bodyguard system: threat detection, LockPedForScript pattern for relationship
    /// management, combat tasking, and combat cancellation with remount logic.
    /// </summary>
    public partial class StreetRidersScript
    {
        /// <summary>
        /// Finds the ped that attacked the player within bodyguard radius.
        /// Does not modify relationships — purely task-based detection.
        /// </summary>
        private Ped FindPlayerAttacker(Vector3 playerPos)
        {
            int playerHandle = Game.Player.Character.Handle;
            Ped[] nearby = World.GetNearbyPeds(Game.Player.Character, _bodyguardRadius);

            foreach (Ped p in nearby)
            {
                if (p == null || !p.Exists() || !p.IsAlive) continue;
                if (p.Handle == playerHandle) continue;
                if (_riders.ContainsKey(p.Handle)) continue; // skip our own riders

                if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, playerHandle, p.Handle, true))
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Applies LockPedForScript pattern to all riders in a group:
        /// Re-locks relationship identity and ensures same-group + player companion status.
        /// Call this before/during/after combat to prevent relationship corruption.
        /// </summary>
        private void LockGroupRiders(ActiveGroup group)
        {
            GroupConfig grpCfg = FindGroupConfig(group.ConfigName);
            int playerRelGroup = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH,
                Game.Player.Character.Handle);

            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                RiderState st;
                _riders.TryGetValue(ped.Handle, out st);

                // 1. Get current identity
                int currentRelHash = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);

                // 2. Set mission entity (may reset rel group internally)
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

                // 3. Re-apply original group (prefer spawn-time original if available)
                int targetGroup = (st != null && st.OriginalRelGroup != 0)
                    ? (int)st.OriginalRelGroup
                    : currentRelHash;
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, targetGroup);

                // 4. Same-group = Companion (prevents friendly fire between riders)
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, targetGroup, targetGroup);

                // 5. Rider <-> Player relationship from config (default Companion)
                int playerRel = (grpCfg != null && grpCfg.PlayerRelationship >= 0)
                    ? grpCfg.PlayerRelationship : 0;
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, playerRel, targetGroup, playerRelGroup);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, playerRel, playerRelGroup, targetGroup);
            }
        }

        /// <summary>
        /// Releases all riders so native AI handles combat via relationship groups.
        /// Does NOT re-lock relationships — they are already set at spawn.
        /// Only called once when combat starts — after this, don't touch them.
        /// </summary>
        private void TaskGroupCombat(ActiveGroup group, Ped threat)
        {
            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                RiderState st;
                _riders.TryGetValue(ped.Handle, out st);

                if (st != null)
                {
                    st.ScenarioPlaying = false;
                    st.IdleArrivalTime = 0;
                    st.InIdleScenario = false;
                }

                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
            }
        }

        /// <summary>
        /// Returns true if any rider in the group is still in combat.
        /// </summary>
        private bool IsGroupInCombat(ActiveGroup group)
        {
            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, 0))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Cancels bodyguard combat and forces riders to remount and follow the player.
        /// Riders on bikes get retasked immediately; riders on foot sprint to their bike
        /// or run toward the player if no bike is available.
        /// </summary>
        private void CancelBodyguardAndFollow(ActiveGroup group, Vehicle playerVeh)
        {
            // Re-lock all riders' relationships to clean state after combat
            LockGroupRiders(group);

            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                RiderState st;
                _riders.TryGetValue(ped.Handle, out st);

                WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, cfg.BlockNonTempEvents);

                if (st != null)
                {
                    st.InIdleScenario = false;
                    st.ScenarioPlaying = false;
                    st.IdleArrivalTime = 0;
                }

                if (ped.IsInVehicle())
                {
                    // Already on bike — will be retasked below
                    continue;
                }

                // On foot — sprint back to bike or run to player
                if (st != null)
                {
                    Vehicle bike = (Vehicle)Entity.FromHandle(st.LastBikeHandle);
                    if (bike != null && bike.Exists())
                    {
                        // Sprint to bike and enter (moveSpeed 2.0 = run)
                        Function.Call(Hash.TASK_ENTER_VEHICLE,
                            ped.Handle, bike.Handle, -1, -1, 2.0f, 1, 0);
                        st.RemountActive = true;
                        st.RemountTimeout = Game.GameTime + 15000;
                        st.RemountLastTaskTime = Game.GameTime;
                    }
                    else if (playerVeh != null && playerVeh.Exists())
                    {
                        // No bike — run toward player's vehicle
                        Function.Call(Hash.TASK_GO_TO_ENTITY,
                            ped.Handle, playerVeh.Handle, -1, 5.0f, 2.0f, 0, 0);
                    }
                }
            }

            // Retask riders already on bikes to escort the player
            RetaskGroupFollowPlayer(group, playerVeh);
        }
    }
}
