using GTA.Math;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private enum MeetingRiderPhase
        {
            Arriving,        // entre dans le meeting, roule vers sa place de parking
            ParkedOnBike,    // assis sur la moto garée, s'apprête à descendre
            Dismounting,     // quitte la moto
            OnFoot,          // à pied : dans le cercle ou devant sa moto
            Mounting,        // retourne à la moto pour un passage sur une ligne de freestyle
            FreestyleRiding, // allers-retours en wheelie sur sa ligne réservée
            Paused           // revient se garer à sa place avant de redescendre
        }

        private enum MeetingOnFootMode
        {
            Circle, // place générée autour du centre du cercle, tourné vers le centre
            AtBike  // posé devant sa moto, dos à elle
        }

        /// <summary>
        /// État runtime d'un motard engagé dans un meeting : sa place de parking, son rôle
        /// à pied, sa ligne de freestyle réservée et ses horloges de rotation.
        /// </summary>
        private class MeetingRiderState
        {
            public int VenueIndex = -1;
            public MeetingRiderPhase Phase = MeetingRiderPhase.Arriving;

            public int ParkingPointIndex = -1;   // index de la place de parking assignée
            public Vector3 ParkingOffset;        // position de la place (cible du TASK_VEHICLE_PARK)
            public float ParkingHeading;         // cap à respecter au parking
            public int NearSpotSince = 0;        // instant d'entrée près de sa place (0 = loin)

            public MeetingOnFootMode OnFootMode = MeetingOnFootMode.AtBike;
            public int CircleSlot = -1;          // place générée dans le cercle (-1 = hors cercle)
            public Vector3 HangoutPoint;         // point à pied (place du cercle ou devant la moto)
            public float HangoutHeading;         // cap à pied (vers le centre ou dos à la moto)
            public string ScenarioName = "";     // scénario idle en cours

            public int LineIndex = -1;           // ligne de freestyle réservée (-1 = aucune)
            public bool LineToB;                 // cible courante : true = B, false = A

            public int NextTaskTime = 0;
            public int ChangeTime = 0;           // prochain changement de rôle planifié
            public int HoldUntil = 0;            // instant où la phase suivante commence
            public int LastTalkTime = 0;         // dernière parole CHAT_STATE/CHAT_RESP
        }
    }
}
