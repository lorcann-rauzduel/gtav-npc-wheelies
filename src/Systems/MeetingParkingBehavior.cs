using System;
using GTA;
using GTA.Native;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Stationnement discipliné : chaque motard rejoint d'abord un point d'approche aligné
    /// derrière sa place, attend son tour à l'arrêt, puis avance lentement en ligne droite
    /// jusqu'à sa place. Une seule manœuvre à la fois par meeting, dans l'ordre d'arrivée.
    /// Sert à l'arrivée du convoi comme au retour d'un passage sur une ligne.
    /// </summary>
    public partial class StreetRidersScript
    {
        private const float MeetingApproachDistance = 6f;
        private const float MeetingApproachReach = 3.5f;
        private const float MeetingApproachMaxSpeed = 5f;
        private const float MeetingParkCreepSpeed = 2.5f;
        private const float MeetingParkFinalReach = 1.8f;
        private const float MeetingBrakeZone = 45f;   // en deçà : allure réduite
        private const float MeetingBrakeSpeed = 8f;
        private const float MeetingSlowZone = 18f;    // en deçà : allure de manœuvre
        private const float MeetingSlowSpeed = 4f;
        private const int MeetingParkTimeoutMs = 15000;
        private const float MeetingParkNearZone = 12f;
        private const int MeetingParkStuckMs = 20000;

        private static Vector3 ApproachPointFor(Vector3 spot, float heading)
        {
            return spot - GetHeadingDir(heading) * MeetingApproachDistance;
        }

        private void DriveToApproach(MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (bike == null || !bike.Exists()) return;
            WheelieConfig cfg = GetWheelieConfig(st.BikeModel);
            float speed = cfg.DriveSpeed * 0.6f;
            if (ms.SpeedCap > 0f && ms.SpeedCap < speed) speed = ms.SpeedCap;
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle, ms.ApproachPoint.X, ms.ApproachPoint.Y, ms.ApproachPoint.Z,
                speed, cfg.DrivingFlags, 2f);
            ms.NextTaskTime = now + 4000;
        }

        /// <summary>
        /// Ralentissement progressif près du lieu : l'IA freine trop tard d'elle-même, on plafonne
        /// donc la vitesse de la moto (SET_ENTITY_MAX_SPEED) et celle de la tâche de conduite.
        /// </summary>
        private void ApplyMeetingSpeedCap(MeetingRiderState ms, Ped ped, Vehicle bike, float cap, string reason)
        {
            if (Math.Abs(cap - ms.SpeedCap) < 0.1f) return;
            ms.SpeedCap = cap;
            Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cap);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, ped.Handle, cap);
            LogMeeting($"Rider {ped.Handle}: speed cap {cap:0.0} m/s ({reason})");
        }

        /// <summary>Rend à la moto sa vitesse normale (départ en freestyle, fin du meeting).</summary>
        private void ReleaseMeetingSpeedCap(MeetingRiderState ms, Vehicle bike, RiderState st)
        {
            if (ms.SpeedCap <= 0f || bike == null || !bike.Exists()) return;
            ms.SpeedCap = 0f;
            Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, GetWheelieConfig(st.BikeModel).MaxSpeed);
        }

        private float ApproachSpeedCap(float distance)
        {
            if (distance < MeetingSlowZone) return MeetingSlowSpeed;
            if (distance < MeetingBrakeZone) return MeetingBrakeSpeed;
            return 0f;
        }

        /// <summary>Arriving / Paused : rouler jusqu'au point d'approche puis prendre la file.</summary>
        private void UpdateApproach(ActiveGroup group, MeetingRiderState ms, Ped ped, RiderState st, Vehicle bike, int now)
        {
            float approachDistance = bike.Position.DistanceTo2D(ms.ApproachPoint);
            float cap = ApproachSpeedCap(approachDistance);
            if (cap > 0f && (ms.SpeedCap <= 0f || cap < ms.SpeedCap))
                ApplyMeetingSpeedCap(ms, ped, bike, cap, $"approach dist={approachDistance:0}m");

            if (HasReachedApproach(ms, bike, now, out string detail))
            {
                ms.WaitSince = now;
                ms.NearSpotSince = 0;
                if (CanStartParking(group, ms))
                {
                    StartParking(ms, ped, bike, now, detail);
                }
                else
                {
                    // Frein à main : la moto reste immobile sans tâche de conduite concurrente.
                    Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);
                    SetMeetingPhase(ms, ped, MeetingRiderPhase.WaitingToPark, $"P{ms.ParkingPointIndex + 1} queued {detail}");
                }
            }
            else if (now > ms.NextTaskTime)
            {
                DriveToApproach(ms, ped, st, now);
            }
        }

        private void UpdateWaitingToPark(ActiveGroup group, MeetingRiderState ms, Ped ped, Vehicle bike, int now)
        {
            if (CanStartParking(group, ms))
                StartParking(ms, ped, bike, now, $"turn after {(now - ms.WaitSince) / 1000}s");
        }

        /// <summary>Personne en manœuvre, et aucun motard arrivé avant lui ne patiente encore.</summary>
        private bool CanStartParking(ActiveGroup group, MeetingRiderState self)
        {
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState other;
                if (!_riders.TryGetValue(pedHandle, out other)) continue;
                MeetingRiderState oms = other.MeetingState;
                if (oms == null || oms == self) continue;
                if (oms.Phase == MeetingRiderPhase.Parking) return false;
                if (oms.Phase == MeetingRiderPhase.WaitingToPark && oms.WaitSince < self.WaitSince) return false;
            }
            return true;
        }

        private void StartParking(MeetingRiderState ms, Ped ped, Vehicle bike, int now, string reason)
        {
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);
            ms.ParkStartTime = now;
            SetMeetingPhase(ms, ped, MeetingRiderPhase.Parking, $"P{ms.ParkingPointIndex + 1} {reason}");
            ApplyMeetingSpeedCap(ms, ped, bike, MeetingParkCreepSpeed, "final park");
            CreepToSpot(ms, ped, bike, now);
        }

        private void CreepToSpot(MeetingRiderState ms, Ped ped, Vehicle bike, int now)
        {
            // Ligne droite depuis le point d'approche aligné : la moto arrive dans l'axe de sa place.
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle, ms.ParkingOffset.X, ms.ParkingOffset.Y, ms.ParkingOffset.Z,
                MeetingParkCreepSpeed, MeetingOffRoadDrivingStyle, 0.5f);
            ms.NextTaskTime = now + 4000;
        }

        /// <summary>Parking : place atteinte (ou manœuvre trop longue acceptée) = moto garée.</summary>
        private void UpdateParking(MeetingRiderState ms, Ped ped, Vehicle bike, int now)
        {
            float distance = bike.Position.DistanceTo2D(ms.ParkingOffset);
            bool arrived = distance < MeetingParkFinalReach && bike.Speed < 1f;
            bool timedOut = now - ms.ParkStartTime > MeetingParkTimeoutMs;
            if (arrived || timedOut)
            {
                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);
                SetMeetingPhase(ms, ped, MeetingRiderPhase.ParkedOnBike,
                    $"P{ms.ParkingPointIndex + 1} dist={distance:0.0}m{(timedOut && !arrived ? " (timeout, accepted)" : "")}");
                ms.HoldUntil = now + _rnd.Next(2000, 6000);
                return;
            }
            if (now > ms.NextTaskTime && bike.Speed < 0.5f)
                CreepToSpot(ms, ped, bike, now);
        }

        /// <summary>
        /// Point d'approche atteint : proche et à allure réduite. Une moto bloquée près du point
        /// (bouchon, voisin) est acceptée après MeetingParkStuckMs pour ne pas tourner en rond.
        /// </summary>
        private bool HasReachedApproach(MeetingRiderState ms, Vehicle bike, int now, out string detail)
        {
            float distance = bike.Position.DistanceTo2D(ms.ApproachPoint);
            detail = $"approach dist={distance:0.0}m";
            if (distance < MeetingApproachReach && bike.Speed < MeetingApproachMaxSpeed) return true;

            if (distance > MeetingParkNearZone)
            {
                ms.NearSpotSince = 0;
                return false;
            }
            if (ms.NearSpotSince == 0) ms.NearSpotSince = now;
            if (now - ms.NearSpotSince < MeetingParkStuckMs) return false;
            detail += " (stuck near approach, accepted)";
            return true;
        }
    }
}
