using GTA.Math;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private enum MeetingRiderPhase
        {
            Arriving,        // entre dans le meeting, roule vers son emplacement de parking
            ParkedOnBike,    // assis sur la moto à son offset, s'apprête à descendre
            Dismounting,     // quitte la moto
            OnFoot,          // à pied : cercle idle (_idleScenarios) ou leaner collé à sa moto
            Mounting,        // retourne à la moto pour enchaîner un burst de freestyle
            FreestyleRiding, // freestyle (roues arrière) dans la zone autour du parking
            Paused           // pause brève garé à son offset avant de descendre à nouveau
        }

        private enum MeetingOnFootMode
        {
            Idle,  // en cercle avec le restant du groupe : fume / téléphone / boit (_idleScenarios)
            Lean   // un seul motard, WORLD_HUMAN_LEANING collé au côté de sa moto
        }

        /// <summary>
        /// État runtime d'un motard engagé dans un meeting. Chaque motard reçoit un point
        /// de parking authoré (astuce : l'offset est déjà capturé dans le point), un point
        /// de cercle authoré ou un rôle à pied (Idle/Lean), et une cible de freestyle.
        /// Le changement de rôle est piloté par ChangeTime.
        /// </summary>
        private class MeetingRiderState
        {
            public int VenueIndex = -1;
            public MeetingRiderPhase Phase = MeetingRiderPhase.Arriving;

            public int ParkingPointIndex = -1;   // index du point de parking authoré assigné
            public Vector3 ParkingOffset;        // position du point de parking (cible du TASK_VEHICLE_PARK)
            public float ParkingHeading;         // cap à respecter au parking

            public int CircleSlot = -1;          // index du point de cercle authoré assigné (ou -1 = leaner)
            public Vector3 HangoutPoint;         // point de rassemblement (cercle) ou côté de la moto (lean)
            public Vector3 FreestylePoint;       // cible de rappel de freestyle (zone)
            public MeetingOnFootMode OnFootMode = MeetingOnFootMode.Idle;
            public string ScenarioName = "";     // scénario idle en cours, ou WORLD_HUMAN_LEANING
            public int LeanSideSign = 1;         // +1 = côté droit de la moto, -1 = côté gauche

            public int NextTaskTime = 0;
            public int ChangeTime = 0;           // prochain changement de rôle/phase planifié
            public int HoldUntil = 0;            // instant où la phase suivante commence
            public int LastTalkTime = 0;         // dernière parole CHAT_STATE/CHAT_RESP (parlote en cercle)
        }
    }
}