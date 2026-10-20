using System.Collections.Generic;
using GTA.Math;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        /// <summary>
        /// Point authoré en jeu dans l'éditeur Meetings : un point de parking ou un point
        /// de cercle. Position + cap sont capturés depuis le joueur.
        /// </summary>
        private class MeetingPoint
        {
            public Vector3 Position;
            public float Heading;
        }

        /// <summary>
        /// Lieu de rassemblement lié à un groupe existant. Le parking est authoré comme
        /// UNE LISTE de points (Position + Heading) afin que chaque motard gare la sienne
        /// exactement à son emplacement, orientée par le cap capturé. Le cercle à pied
        /// est authoré de la même façon (8 points max) : les motards s'y répartissent et
        /// regardent vers le centre du cercle. Les motards rouleront aussi en freestyle
        /// autour de la zone (croisière rapide + rafales de wheelies). Persisté dans
        /// meetings.ini sous [Venue_NOM] (Group + ParkingPoint{N} + CirclePoint{N}).
        /// </summary>
        private class MeetingVenue
        {
            public string Name = "";
            public string GroupName = "";
            public List<MeetingPoint> ParkingPoints = new List<MeetingPoint>();
            public List<MeetingPoint> CirclePoints = new List<MeetingPoint>();

            // État runtime du meeting (non persisté)
            public int ActiveGroupId = -1;
            public bool ForceActive = false;

            /// <summary>Centre utilisé pour la matérialisation, le freestyle et le départ : premier point de parking.</summary>
            public Vector3 Center
            {
                get { return ParkingPoints.Count > 0 ? ParkingPoints[0].Position : Vector3.Zero; }
            }
        }
    }
}