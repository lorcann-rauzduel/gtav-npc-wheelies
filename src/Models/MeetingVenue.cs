using System.Collections.Generic;
using GTA.Math;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        /// <summary>
        /// Place de parking authorée dans l'éditeur : position + cap capturés sur le joueur
        /// (ou sur sa moto s'il est dessus).
        /// </summary>
        private class MeetingPoint
        {
            public Vector3 Position;
            public float Heading;
        }

        /// <summary>
        /// Ligne de freestyle authorée : segment A→B parcouru en allers-retours.
        /// OffRoad = trajet en ligne droite sans suivre le réseau routier.
        /// </summary>
        private class MeetingLine
        {
            public Vector3 A;
            public Vector3 B;
            public bool OffRoad;

            public Vector3 Middle
            {
                get { return (A + B) * 0.5f; }
            }
        }

        /// <summary>Que faire des motards au-delà du nombre de places authorées.</summary>
        private enum MeetingExtraRiders
        {
            Limit,     // l'effectif est plafonné au nombre de places
            AutoSpots  // places supplémentaires générées dans le prolongement de la dernière rangée
        }

        /// <summary>
        /// Lieu de rassemblement lié à un groupe existant. Composé de places de parking
        /// (une par moto), d'un centre de cercle optionnel (les places à pied sont générées
        /// autour) et de lignes de freestyle. Persisté dans meetings.ini sous [Venue_NOM].
        /// </summary>
        private class MeetingVenue
        {
            public const int MaxParkingPoints = 24;
            public const int MaxLines = 8;

            public string Name = "";
            public string GroupName = "";
            public List<MeetingPoint> ParkingPoints = new List<MeetingPoint>();
            public Vector3? CircleCenter;
            public List<MeetingLine> Lines = new List<MeetingLine>();

            // Réglages propres au lieu ; null = valeur par défaut de [Meetings].
            public int? RidersMin;
            public int? RidersMax;
            public MeetingExtraRiders? ExtraRiders;

            // État runtime du meeting (non persisté)
            public int ActiveGroupId = -1;
            public bool ForceActive = false;
            public int NextScheduleAttempt = 0;

            /// <summary>
            /// Ancre du lieu, posée à la création : sert à la matérialisation à l'approche
            /// du joueur, au point d'arrivée du convoi et au départ en formation.
            /// </summary>
            public Vector3 Anchor;

            public Vector3 Center
            {
                get { return Anchor; }
            }
        }
    }
}
