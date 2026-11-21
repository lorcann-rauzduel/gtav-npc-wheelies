using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Rôles à pied d'un motard en meeting : dans le cercle autour du centre authoré,
    /// sur un spot authoré (un motard par spot) ou devant sa moto.
    /// Le cercle est recalculé dès que ses membres changent : places également réparties,
    /// rayon selon l'effectif, toutes au niveau du centre (bord de trottoir évité).
    /// </summary>
    public partial class StreetRidersScript
    {
        private const int MeetingCircleMaxMembers = 10;
        private const float MeetingCircleSpacing = 1.3f;     // écart visé entre deux voisins
        private const float MeetingCircleMinRadius = 0.9f;
        private const float MeetingCircleLevelTolerance = 0.25f;
        private const float MeetingHangoutMoveThreshold = 0.3f;

        // Poids du tirage de rôle à pied (seuls les rôles disponibles participent).
        private const int MeetingCircleWeight = 55;
        private const int MeetingStandWeight = 35;
        private const int MeetingAtBikeWeight = 25;

        private void EnterOnFoot(MeetingVenue venue, ActiveGroup group, MeetingRiderState ms, Ped ped, RiderState st, int now, string reason)
        {
            SetMeetingPhase(ms, ped, MeetingRiderPhase.OnFoot, reason);
            ResetMeetingWheelie(st);
            ms.LineIndex = -1;
            ms.CircleSlot = -1;
            ms.StandSpotIndex = -1;
            ms.HangoutDirty = false;
            ms.NextTaskTime = now + 300;
            ms.ChangeTime = now + _rnd.Next(_meetingRoleChangeMinMs, _meetingRoleChangeMaxMs + 1);
            ms.LastTalkTime = now;

            List<string> pool = _idleScenarios.Count > 0 ? _idleScenarios : new List<string> { "WORLD_HUMAN_SMOKING" };
            ms.ScenarioName = pool[_rnd.Next(pool.Count)];

            int standSpot = FindFreeStandSpot(venue, group, ms);
            bool circleOpen = venue.CircleCenter.HasValue && CircleMembers(group, ms).Count < MeetingCircleMaxMembers;
            ms.OnFootMode = PickOnFootMode(circleOpen, standSpot >= 0);

            switch (ms.OnFootMode)
            {
                case MeetingOnFootMode.Circle:
                    // Point provisoire : le placement du cercle le corrige au tick suivant.
                    ms.HangoutPoint = StandPoint(venue.CircleCenter.Value);
                    ms.HangoutHeading = ped.Heading;
                    ms.NextTaskTime = now + 600;
                    break;
                case MeetingOnFootMode.Stand:
                    ms.StandSpotIndex = standSpot;
                    MeetingPoint spot = venue.StandSpots[standSpot];
                    ms.HangoutPoint = StandPoint(spot.Position);
                    ms.HangoutHeading = spot.Heading;
                    break;
                default:
                    SetAtBikeHangout(ms, st);
                    break;
            }

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            string where = ms.OnFootMode == MeetingOnFootMode.Stand ? " S" + (standSpot + 1) : "";
            LogMeeting($"Rider {ped.Handle}: on foot {ms.OnFootMode}{where} scenario={ms.ScenarioName}");
            if (_debug) DrawDebugText(ped, "MEET: " + ms.OnFootMode, Color.LightBlue);
        }

        private MeetingOnFootMode PickOnFootMode(bool circleOpen, bool standFree)
        {
            int circle = circleOpen ? MeetingCircleWeight : 0;
            int stand = standFree ? MeetingStandWeight : 0;
            int roll = _rnd.Next(circle + stand + MeetingAtBikeWeight);
            if (roll < circle) return MeetingOnFootMode.Circle;
            if (roll < circle + stand) return MeetingOnFootMode.Stand;
            return MeetingOnFootMode.AtBike;
        }

        private List<MeetingRiderState> CircleMembers(ActiveGroup group, MeetingRiderState exclude)
        {
            var members = new List<MeetingRiderState>();
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState other;
                if (!_riders.TryGetValue(pedHandle, out other)) continue;
                MeetingRiderState oms = other.MeetingState;
                if (oms == null || oms == exclude) continue;
                if (oms.Phase == MeetingRiderPhase.OnFoot && oms.OnFootMode == MeetingOnFootMode.Circle)
                    members.Add(oms);
            }
            return members;
        }

        private int FindFreeStandSpot(MeetingVenue venue, ActiveGroup group, MeetingRiderState self)
        {
            if (venue.StandSpots.Count == 0) return -1;
            var used = new HashSet<int>();
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState other;
                if (!_riders.TryGetValue(pedHandle, out other)) continue;
                MeetingRiderState oms = other.MeetingState;
                if (oms == null || oms == self || oms.Phase != MeetingRiderPhase.OnFoot) continue;
                if (oms.OnFootMode == MeetingOnFootMode.Stand && oms.StandSpotIndex >= 0) used.Add(oms.StandSpotIndex);
            }

            var free = new List<int>();
            for (int i = 0; i < venue.StandSpots.Count; i++)
                if (!used.Contains(i)) free.Add(i);
            return free.Count == 0 ? -1 : free[_rnd.Next(free.Count)];
        }

        /// <summary>
        /// Replace les membres du cercle quand sa composition change : un rang par membre
        /// (ordre stable), places également réparties, rayon selon l'effectif.
        /// </summary>
        private void UpdateCircleLayout(MeetingVenue venue, ActiveGroup group)
        {
            if (!venue.CircleCenter.HasValue) return;
            List<MeetingRiderState> members = CircleMembers(group, null);
            var handles = new List<int>();
            var byHandle = new Dictionary<int, MeetingRiderState>();
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState other;
                if (_riders.TryGetValue(pedHandle, out other) && members.Contains(other.MeetingState))
                {
                    handles.Add(pedHandle);
                    byHandle[pedHandle] = other.MeetingState;
                }
            }
            handles.Sort();
            string key = string.Join(",", handles);
            if (key == venue.CircleLayoutKey) return;
            venue.CircleLayoutKey = key;
            if (handles.Count == 0) return;

            Vector3 center = GroundAt(venue.CircleCenter.Value);
            int n = handles.Count;
            float radius = Math.Max(MeetingCircleMinRadius, n * MeetingCircleSpacing / (float)(Math.PI * 2.0));
            int leveled = 0;
            for (int i = 0; i < n; i++)
            {
                MeetingRiderState ms = byHandle[handles[i]];
                double angle = i * Math.PI * 2.0 / n;
                Vector3 offset = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f) * radius;
                Vector3 ground = LevelCircleSlot(center, offset, out bool pulledIn);
                if (pulledIn) leveled++;

                Vector3 point = StandPoint(ground);
                ms.CircleSlot = i;
                ms.HangoutHeading = HeadingTowards(center - ground);
                if (point.DistanceTo(ms.HangoutPoint) > MeetingHangoutMoveThreshold)
                {
                    ms.HangoutPoint = point;
                    ms.HangoutDirty = true;
                }
            }
            LogMeeting($"Meeting {venue.Name}: circle layout {n} members radius={radius:0.0}m{(leveled > 0 ? $" ({leveled} pulled in to stay level)" : "")}");
        }

        /// <summary>
        /// Place au sol à la même hauteur que le centre : une place qui tombe d'un bord de
        /// trottoir est rapprochée du centre jusqu'à retrouver le même niveau.
        /// </summary>
        private static Vector3 LevelCircleSlot(Vector3 center, Vector3 offset, out bool pulledIn)
        {
            pulledIn = false;
            for (float t = 1f; t >= 0.3f; t -= 0.15f)
            {
                Vector3 ground = GroundAt(center + offset * t);
                if (Math.Abs(ground.Z - center.Z) <= MeetingCircleLevelTolerance)
                {
                    pulledIn = t < 1f;
                    return ground;
                }
            }
            pulledIn = true;
            return center + offset * 0.3f;
        }

        /// <summary>
        /// Posé devant sa moto, dos à elle. Sans moto, le point est calculé depuis sa place de parking.
        /// </summary>
        private void SetAtBikeHangout(MeetingRiderState ms, RiderState st)
        {
            Vehicle bike = GetMyBike(st);
            Vector3 origin = bike != null ? bike.Position : ms.ParkingOffset;
            float heading = bike != null ? bike.Heading : ms.ParkingHeading;
            ms.HangoutPoint = StandPoint(origin + GetHeadingDir(heading) * 1.4f);
            ms.HangoutHeading = heading;
        }

        /// <summary>Hauteur attendue par TASK_START_SCENARIO_AT_POSITION : sol + décalage racine du ped.</summary>
        private static Vector3 StandPoint(Vector3 position)
        {
            Vector3 ground = GroundAt(position);
            return ground == position ? position : ground + new Vector3(0f, 0f, 1f);
        }

        private static float HeadingTowards(Vector3 direction)
        {
            // Inverse de GetHeadingDir : dir = (-sin h, cos h).
            return (float)(Math.Atan2(-direction.X, direction.Y) * 180.0 / Math.PI);
        }

        private void UpdateOnFootRole(MeetingVenue venue, ActiveGroup group, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            UpdateHangout(ped, ms, now);
            if (ms.OnFootMode == MeetingOnFootMode.Circle) UpdateCircleChatter(ped, ms, now);

            if (now < ms.ChangeTime) return;

            // Rotation libre : un passage sur une ligne libre, sinon un nouveau rôle à pied.
            int line = venue.Lines.Count > 0 && _rnd.Next(100) < 50 ? PickFreeLine(venue, group) : -1;
            Vehicle myBike = GetMyBike(st);
            if (line >= 0 && myBike != null && ped.Position.DistanceTo(myBike.Position) < 60f)
            {
                ms.LineIndex = line;
                SetMeetingPhase(ms, ped, MeetingRiderPhase.Mounting, $"heading to line L{line + 1}");
                ms.HoldUntil = now + MeetingMountTimeoutMs;
                ms.NextTaskTime = 0;
                TryMountBike(ms, ped, st, now);
                return;
            }
            EnterOnFoot(venue, group, ms, ped, st, now, line >= 0 ? "bike out of reach" : "role rotation");
        }

        /// <summary>
        /// Le motard rejoint son point à pied puis y joue son scénario, orienté par le cap voulu.
        /// Réémis quand le scénario n'est pas actif, ou tout de suite si son point a bougé.
        /// </summary>
        private void UpdateHangout(Ped ped, MeetingRiderState ms, int now)
        {
            if (ms.ScenarioName.Length == 0) return;
            if (ms.HangoutDirty && now >= ms.NextTaskTime - 300)
            {
                ms.HangoutDirty = false;
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                StartHangoutScenario(ped, ms);
                ms.NextTaskTime = now + 6000;
                return;
            }
            if (now < ms.NextTaskTime) return;
            ms.NextTaskTime = now + 6000;
            if (Function.Call<bool>(Hash.IS_PED_ACTIVE_IN_SCENARIO, ped.Handle)) return;
            StartHangoutScenario(ped, ms);
        }

        private static void StartHangoutScenario(Ped ped, MeetingRiderState ms)
        {
            Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, ped.Handle, ms.ScenarioName,
                ms.HangoutPoint.X, ms.HangoutPoint.Y, ms.HangoutPoint.Z, ms.HangoutHeading, -1, true, false);
        }

        /// <summary>
        /// Parlote ambiante du cercle : CHAT_STATE / CHAT_RESP à un rythme personnel
        /// pour que tout le monde ne parle pas en même temps.
        /// </summary>
        private void UpdateCircleChatter(Ped ped, MeetingRiderState ms, int now)
        {
            if (now - ms.LastTalkTime <= _rnd.Next(8000, 15000)) return;
            if (_rnd.Next(100) < 45)
            {
                string speech = _rnd.Next(100) < 50 ? "CHAT_STATE" : "CHAT_RESP";
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, speech, "SPEECH_PARAMS_FORCE_NORMAL");
                if (_debug) DrawDebugText(ped, "MEET: talk " + speech, Color.LightGoldenrodYellow);
            }
            ms.LastTalkTime = now;
        }
    }
}
