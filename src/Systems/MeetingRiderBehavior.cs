using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Comportement d'un motard engagé dans un meeting : arrivée et parking, rôle à pied
    /// (cercle autour du centre ou devant sa moto) et passages en wheelie sur une ligne
    /// de freestyle réservée (2 motards max par ligne). La rotation est libre : à chaque
    /// échéance de rôle, le motard tire au hasard un passage sur ligne ou un nouveau rôle à pied.
    /// </summary>
    public partial class StreetRidersScript
    {
        // =========================================================================
        //  Per-rider finite state machine
        // =========================================================================

        private const int MeetingMaxRidersPerLine = 2;
        private const float MeetingLineEndReach = 6f;
        private const int MeetingMountTimeoutMs = 15000;

        // DF_SwerveAroundAllCars | DF_SteerAroundPeds | DF_SteerAroundObjects | DF_ForceStraightLine :
        // trajet tout droit d'un bout à l'autre de la ligne, sans suivre le réseau routier.
        private const int MeetingOffRoadDrivingStyle = 4 | 16 | 32 | 16777216;

        private void SetMeetingPhase(MeetingRiderState ms, Ped ped, MeetingRiderPhase phase, string reason)
        {
            if (ms.Phase == phase) return;
            LogMeeting($"Rider {ped.Handle}: {ms.Phase} -> {phase} ({reason})");
            ms.Phase = phase;
        }

        private void ProcessMeetingRider(MeetingVenue venue, ActiveGroup group, Ped ped, RiderState st, int now)
        {
            MeetingRiderState ms = st.MeetingState;
            if (ms == null || ms.VenueIndex < 0) return;
            if (!ped.IsAlive || ped.IsRagdoll) return;

            Vehicle bike = ped.CurrentVehicle;

            switch (ms.Phase)
            {
                case MeetingRiderPhase.Arriving:
                    if (bike == null || bike.IsDead || bike.IsInWater)
                    {
                        if (TryMountBike(ms, ped, st, now)) break;
                        EnterOnFoot(venue, group, ms, ped, st, now, "bike lost while arriving");
                        break;
                    }

                    if (bike.Position.DistanceTo(ms.ParkingOffset) < 8f && bike.Speed < 2f)
                    {
                        // Place atteinte : moteur au ralenti, frein à main, le motard se prépare.
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);
                        SetMeetingPhase(ms, ped, MeetingRiderPhase.ParkedOnBike, "reached parking spot");
                        ms.HoldUntil = now + _rnd.Next(2000, 6000);
                        if (_debug) DrawDebugText(ped, "MEET: ParkedOnBike", Color.SkyBlue);
                    }
                    else if (now > ms.NextTaskTime)
                    {
                        DriveToParkingOffset(ms, ped, now);
                    }
                    break;

                case MeetingRiderPhase.ParkedOnBike:
                    if (bike == null || !ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now, "already off the bike");
                        break;
                    }
                    if (now >= ms.HoldUntil)
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                        SetMeetingPhase(ms, ped, MeetingRiderPhase.Dismounting, "parked hold over");
                        ms.HoldUntil = now + 5000;
                    }
                    break;

                case MeetingRiderPhase.Dismounting:
                    if (!ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now, "dismounted");
                    }
                    else if (now > ms.HoldUntil)
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                        ms.HoldUntil = now + 5000;
                    }
                    break;

                case MeetingRiderPhase.OnFoot:
                    if (ped.IsInVehicle())
                    {
                        if (now > ms.NextTaskTime)
                        {
                            Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                            ms.NextTaskTime = now + 3000;
                        }
                        break;
                    }
                    UpdateOnFootRole(venue, group, ms, ped, st, now);
                    break;

                case MeetingRiderPhase.Mounting:
                    if (ped.IsInVehicle())
                    {
                        StartFreestyleRiding(venue, ped, st, ms, now);
                    }
                    else if (now >= ms.HoldUntil || !TryMountBike(ms, ped, st, now))
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now, "could not mount");
                    }
                    break;

                case MeetingRiderPhase.FreestyleRiding:
                    if (!ped.IsInVehicle())
                    {
                        // Chute en plein passage : on remonte si la moto est proche, sinon à pied.
                        if (!TryMountBike(ms, ped, st, now))
                            EnterOnFoot(venue, group, ms, ped, st, now, "fell during freestyle");
                        break;
                    }
                    UpdateFreestyleRiding(venue, ms, ped, st, now);
                    break;

                case MeetingRiderPhase.Paused:
                    if (!ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now, "off the bike while returning");
                        break;
                    }
                    // Retour à sa place : il descend et rejoint la vie à pied.
                    if (bike.Position.DistanceTo(ms.ParkingOffset) < 8f && bike.Speed < 2f)
                    {
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                        SetMeetingPhase(ms, ped, MeetingRiderPhase.Dismounting, "back at parking spot");
                        ms.HoldUntil = now + 5000;
                    }
                    else if (now > ms.NextTaskTime)
                    {
                        DriveToParkingOffset(ms, ped, now);
                    }
                    break;
            }
        }

        private Vehicle GetMyBike(RiderState st)
        {
            Vehicle bike = (Vehicle)Entity.FromHandle(st.LastBikeHandle);
            if (bike != null && bike.Exists() && !bike.IsDead && !bike.IsInWater)
                return bike;
            return null;
        }

        /// <summary>
        /// Renvoie le motard vers sa moto (tâche d'entrée réémise toutes les 3 s).
        /// Retourne false quand la moto n'existe plus ou est trop loin.
        /// </summary>
        private bool TryMountBike(MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle myBike = GetMyBike(st);
            if (myBike == null) return false;
            if (ped.Position.DistanceTo(myBike.Position) > 60f) return false;

            if (now > ms.NextTaskTime)
            {
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, myBike.Handle, -1, -1, 2.0f, 1, 0);
                ms.NextTaskTime = now + 3000;
            }
            return true;
        }

        private static void ResetMeetingWheelie(RiderState st)
        {
            st.Phase = Phase.Idle;
            st.WheelieAnimPlayed = false;
            st.WheelieAnimTriggerTime = 0;
            st.WheelieNeedsRetask = false;
        }

        // =========================================================================
        //  On-foot roles (circle around the authored center, or in front of the bike)
        // =========================================================================

        private void EnterOnFoot(MeetingVenue venue, ActiveGroup group, MeetingRiderState ms, Ped ped, RiderState st, int now, string reason)
        {
            SetMeetingPhase(ms, ped, MeetingRiderPhase.OnFoot, reason);
            ResetMeetingWheelie(st);
            ms.LineIndex = -1;
            ms.CircleSlot = -1;
            ms.NextTaskTime = now + 300;
            ms.ChangeTime = now + _rnd.Next(_meetingRoleChangeMinMs, _meetingRoleChangeMaxMs + 1);
            ms.LastTalkTime = now;

            List<string> pool = _idleScenarios.Count > 0 ? _idleScenarios : new List<string> { "WORLD_HUMAN_SMOKING" };
            ms.ScenarioName = pool[_rnd.Next(pool.Count)];

            // Rotation libre : cercle si le lieu en a un et qu'une place est libre, sinon devant la moto.
            int slot = venue.CircleCenter.HasValue && _rnd.Next(100) < 60 ? FindFreeCircleSlot(group, ms) : -1;
            if (slot >= 0)
            {
                ms.OnFootMode = MeetingOnFootMode.Circle;
                ms.CircleSlot = slot;
                SetCircleHangout(venue.CircleCenter.Value, CircleSlotCount(group), ms);
            }
            else
            {
                ms.OnFootMode = MeetingOnFootMode.AtBike;
                SetAtBikeHangout(ms, st);
            }

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            LogMeeting($"Rider {ped.Handle}: on foot {ms.OnFootMode}{(slot >= 0 ? " slot " + slot : "")} scenario={ms.ScenarioName}");
            if (_debug) DrawDebugText(ped, "MEET: " + ms.OnFootMode, Color.LightBlue);
        }

        private static int CircleSlotCount(ActiveGroup group)
        {
            return Math.Max(4, Math.Min(10, group.PedHandles.Count));
        }

        private int FindFreeCircleSlot(ActiveGroup group, MeetingRiderState self)
        {
            int slotCount = CircleSlotCount(group);
            var used = new HashSet<int>();
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState other;
                if (!_riders.TryGetValue(pedHandle, out other)) continue;
                MeetingRiderState oms = other.MeetingState;
                if (oms == null || oms == self || oms.Phase != MeetingRiderPhase.OnFoot) continue;
                if (oms.OnFootMode == MeetingOnFootMode.Circle && oms.CircleSlot >= 0) used.Add(oms.CircleSlot);
            }

            // Départ aléatoire : le cercle se remplit sans toujours commencer au même endroit.
            int start = _rnd.Next(slotCount);
            for (int k = 0; k < slotCount; k++)
            {
                int slot = (start + k) % slotCount;
                if (!used.Contains(slot)) return slot;
            }
            return -1;
        }

        /// <summary>Place générée sur le cercle autour du centre, tournée vers le centre.</summary>
        private static void SetCircleHangout(Vector3 center, int slotCount, MeetingRiderState ms)
        {
            double angle = ms.CircleSlot * Math.PI * 2.0 / slotCount;
            Vector3 offset = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f) * MeetingCircleRadius;
            ms.HangoutPoint = StandPoint(center + offset);
            ms.HangoutHeading = HeadingTowards(-offset);
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
        /// Réémis seulement quand le scénario n'est pas actif.
        /// </summary>
        private void UpdateHangout(Ped ped, MeetingRiderState ms, int now)
        {
            if (now < ms.NextTaskTime || ms.ScenarioName.Length == 0) return;
            ms.NextTaskTime = now + 6000;
            if (Function.Call<bool>(Hash.IS_PED_ACTIVE_IN_SCENARIO, ped.Handle)) return;

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

        // =========================================================================
        //  Freestyle lines (A <-> B wheelie runs on an authored segment)
        // =========================================================================

        private void DriveToParkingOffset(MeetingRiderState ms, Ped ped, int now)
        {
            // TASK_VEHICLE_PARK aligne la moto sur le cap capturé (mode 1 = garé en marche avant).
            Function.Call(Hash.TASK_VEHICLE_PARK,
                ped.Handle, ped.CurrentVehicle.Handle,
                ms.ParkingOffset.X, ms.ParkingOffset.Y, ms.ParkingOffset.Z,
                ms.ParkingHeading, 1, 20f, false);
            ms.NextTaskTime = now + 4000;
        }

        private int CountLineRiders(ActiveGroup group, int lineIndex)
        {
            int count = 0;
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState st;
                if (!_riders.TryGetValue(pedHandle, out st) || st.MeetingState == null) continue;
                MeetingRiderState ms = st.MeetingState;
                if (ms.LineIndex == lineIndex
                    && (ms.Phase == MeetingRiderPhase.Mounting || ms.Phase == MeetingRiderPhase.FreestyleRiding))
                    count++;
            }
            return count;
        }

        private int PickFreeLine(MeetingVenue venue, ActiveGroup group)
        {
            var free = new List<int>();
            for (int i = 0; i < venue.Lines.Count; i++)
                if (CountLineRiders(group, i) < MeetingMaxRidersPerLine) free.Add(i);
            if (free.Count == 0)
            {
                LogMeeting($"Meeting {venue.Name}: all {venue.Lines.Count} lines full");
                return -1;
            }
            return free[_rnd.Next(free.Count)];
        }

        private void StartFreestyleRiding(MeetingVenue venue, Ped ped, RiderState st, MeetingRiderState ms, int now)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (ms.LineIndex < 0 || ms.LineIndex >= venue.Lines.Count || bike == null || !bike.Exists())
            {
                ReturnToParking(ms, ped, st, now, "line no longer available");
                return;
            }

            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);
            ResetMeetingWheelie(st);
            MeetingLine line = venue.Lines[ms.LineIndex];
            // Il rejoint d'abord le bout le plus proche, puis enchaîne les allers-retours.
            ms.LineToB = bike.Position.DistanceTo2D(line.B) < bike.Position.DistanceTo2D(line.A);
            ms.ChangeTime = now + _rnd.Next(_meetingFreestyleMinMs, _meetingFreestyleMaxMs + 1);
            SetMeetingPhase(ms, ped, MeetingRiderPhase.FreestyleRiding,
                $"line L{ms.LineIndex + 1} {(line.OffRoad ? "offroad" : "road")}");
            DriveLineTarget(venue, ms, ped, st, now);
            if (_debug) DrawDebugText(ped, "MEET: Freestyle L" + (ms.LineIndex + 1), Color.Orange);
        }

        private void DriveLineTarget(MeetingVenue venue, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (bike == null || !bike.Exists()) return;
            MeetingLine line = venue.Lines[ms.LineIndex];
            Vector3 target = ms.LineToB ? line.B : line.A;
            WheelieConfig cfg = GetWheelieConfig(st.BikeModel);
            int style = line.OffRoad ? MeetingOffRoadDrivingStyle : cfg.DrivingFlags;
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle, target.X, target.Y, target.Z, cfg.DriveSpeed, style, 4f);
            ms.NextTaskTime = now + 3500;
        }

        private void UpdateFreestyleRiding(MeetingVenue venue, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (bike == null || !bike.Model.IsBike) return;
            if (ms.LineIndex < 0 || ms.LineIndex >= venue.Lines.Count)
            {
                ReturnToParking(ms, ped, st, now, "line removed");
                return;
            }
            WheelieConfig cfg = GetWheelieConfig(st.BikeModel, st.WheelieProtected);

            // Réémet la conduite après une anim de stunt terminée (même règle que ProcessAllRiders).
            if (st.Phase == Phase.Sustained
                && st.WheelieNeedsRetask
                && !string.IsNullOrEmpty(st.LastWheelieAnimDict)
                && !string.IsNullOrEmpty(st.LastWheelieAnimClip)
                && now - st.WheelieAnimStartTime >= 500
                && !Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM,
                    ped.Handle, st.LastWheelieAnimDict, st.LastWheelieAnimClip, 3))
            {
                DriveLineTarget(venue, ms, ped, st, now);
                st.WheelieNeedsRetask = false;
                int nextCooldown = st.WheelieAnimUpperBodyOnly ? 2000 : cfg.StuntCooldown;
                st.WheelieAnimUpperBodyOnly = false;
                st.WheelieAnimPlayed = false;
                st.WheelieAnimTriggerTime = now + nextCooldown;
            }

            MeetingLine line = venue.Lines[ms.LineIndex];
            Vector3 target = ms.LineToB ? line.B : line.A;
            if (bike.Position.DistanceTo2D(target) < MeetingLineEndReach)
            {
                // Bout de ligne : demi-tour (le virage coupe le wheelie via la safety gate de braquage).
                ms.LineToB = !ms.LineToB;
                DriveLineTarget(venue, ms, ped, st, now);
            }
            else if (now > ms.NextTaskTime && st.Phase == Phase.Idle)
            {
                // Pas de réémission en plein wheelie pour ne pas casser la figure en cours.
                DriveLineTarget(venue, ms, ped, st, now);
            }

            if (now >= ms.ChangeTime)
                ReturnToParking(ms, ped, st, now, "freestyle burst over");
        }

        private void ReturnToParking(MeetingRiderState ms, Ped ped, RiderState st, int now, string reason)
        {
            ResetMeetingWheelie(st);
            ms.LineIndex = -1;
            SetMeetingPhase(ms, ped, MeetingRiderPhase.Paused, reason);
            if (ped.IsInVehicle()) DriveToParkingOffset(ms, ped, now);
            if (_debug) DrawDebugText(ped, "MEET: Paused", Color.Gray);
        }

    }
}
