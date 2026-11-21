using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Machine à états d'un motard engagé dans un meeting : arrivée et stationnement
    /// (MeetingParkingBehavior), rôles à pied (MeetingOnFootBehavior) et passages en wheelie sur une ligne
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

                    UpdateApproach(group, ms, ped, st, bike, now);
                    break;

                case MeetingRiderPhase.WaitingToPark:
                case MeetingRiderPhase.Parking:
                    if (bike == null || !ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now, "off the bike while parking");
                        break;
                    }
                    if (ms.Phase == MeetingRiderPhase.WaitingToPark)
                        UpdateWaitingToPark(group, ms, ped, bike, now);
                    else
                        UpdateParking(ms, ped, bike, now);
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
                    // Retour à sa place par la même file que l'arrivée, puis vie à pied.
                    UpdateApproach(group, ms, ped, st, bike, now);
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
        //  Freestyle lines (A <-> B wheelie runs on an authored segment)
        // =========================================================================

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
            ReleaseMeetingSpeedCap(ms, bike, st);
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
            ms.NearSpotSince = 0;
            SetMeetingPhase(ms, ped, MeetingRiderPhase.Paused, reason);
            if (ped.IsInVehicle()) DriveToApproach(ms, ped, st, now);
            if (_debug) DrawDebugText(ped, "MEET: Paused", Color.Gray);
        }

    }
}
