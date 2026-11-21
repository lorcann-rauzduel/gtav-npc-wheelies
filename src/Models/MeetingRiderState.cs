using GTA.Math;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private enum MeetingRiderPhase
        {
            Arriving,        // entre dans le meeting, roule vers le point d'approche de sa place
            WaitingToPark,   // arrêté à son point d'approche, attend que la manœuvre en cours se termine
            Parking,         // avance lentement et en ligne droite jusqu'à sa place (un seul à la fois)
            ParkedOnBike,    // assis sur la moto garée, s'apprête à descendre
            Dismounting,     // quitte la moto
            OnFoot,          // à pied : cercle, spot ou devant sa moto
            Mounting,        // retourne à la moto pour un passage sur une ligne de freestyle
            FreestyleRiding, // allers-retours en wheelie sur sa ligne réservée
            Paused           // revient vers le point d'approche de sa place après un passage
        }

        private enum MeetingOnFootMode
        {
            Circle, // place générée autour du centre du cercle, tourné vers le centre
            Stand,  // spot authoré, orienté par le cap capturé
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
            public Vector3 ParkingOffset;        // position de la place
            public float ParkingHeading;         // cap à respecter au parking
            public Vector3 ApproachPoint;        // point aligné derrière la place, d'où part la manœuvre
            public int NearSpotSince = 0;        // instant d'entrée près de la cible courante (0 = loin)
            public int WaitSince = 0;            // instant d'entrée dans la file de stationnement
            public int ParkStartTime = 0;        // début de la manœuvre finale
            public float SpeedCap = 0f;          // vitesse max imposée à l'approche (0 = aucune)

            public MeetingOnFootMode OnFootMode = MeetingOnFootMode.AtBike;
            public int CircleSlot = -1;          // rang dans le cercle (-1 = hors cercle)
            public int StandSpotIndex = -1;      // spot authoré occupé (-1 = aucun)
            public Vector3 HangoutPoint;         // point à pied
            public float HangoutHeading;         // cap à pied
            public bool HangoutDirty;            // point déplacé : la tâche de scénario doit être réémise
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
