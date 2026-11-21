using System;
using System.Drawing;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Mode édition au sol d'un lieu de meeting. Le menu est fermé pendant l'édition :
    /// les points du lieu sont dessinés dans le monde et un bandeau d'aide rappelle
    /// l'outil actif. Entrée pose, Retour arrière supprime le point le plus proche,
    /// E change d'outil, R bascule Route/Hors route, la touche du menu quitte.
    /// Chaque action est sauvegardée immédiatement dans meetings.ini.
    /// Aucune entité n'est créée : les marqueurs sont redessinés à chaque frame.
    /// </summary>
    public partial class StreetRidersScript
    {
        private enum MeetingEditTool { Parking, Circle, Line }

        private const float MeetingEditDrawDistance = 150f;
        private const float MeetingEditDeleteRadius = 5f;
        private const float MeetingLineMinLength = 20f;
        private const float MeetingCircleRadius = 2.5f;

        private static readonly Color EditParkingColor = Color.FromArgb(160, 60, 140, 255);
        private static readonly Color EditCircleColor = Color.FromArgb(90, 255, 210, 40);
        private static readonly Color EditRoadLineColor = Color.FromArgb(220, 60, 220, 90);
        private static readonly Color EditOffRoadLineColor = Color.FromArgb(220, 255, 140, 30);
        private static readonly Color EditPendingColor = Color.FromArgb(200, 255, 255, 255);

        private MeetingVenue _editVenue;
        private MeetingEditTool _editTool = MeetingEditTool.Parking;
        private Vector3? _editLineStart;

        private bool IsEditingMeeting
        {
            get { return _editVenue != null; }
        }

        // =========================================================================
        //  Lifecycle
        // =========================================================================

        private void BeginMeetingEdit(MeetingVenue venue)
        {
            if (venue.ForceActive)
            {
                Notification.PostTicker(T("~o~Meetings : arrête le meetup pour éditer ce lieu.",
                    "~o~Meetings: stop the meetup to edit this venue."), true);
                LogMeeting($"Edit: refused on {venue.Name} (meetup active)");
                return;
            }
            HideMeetingMenu();
            _editVenue = venue;
            _editTool = MeetingEditTool.Parking;
            _editLineStart = null;
            LogMeeting($"Edit: begin {venue.Name}");
        }

        private void EndMeetingEdit(string reason)
        {
            if (_editVenue == null) return;
            LogMeeting($"Edit: end {_editVenue.Name} ({reason})");
            Notification.PostTicker(T($"~b~Meetings : édition de {_editVenue.Name} terminée",
                $"~b~Meetings: finished editing {_editVenue.Name}"), true);
            _editVenue = null;
            _editLineStart = null;
        }

        /// <summary>Traite une touche du mode édition. Retourne true si la touche est consommée.</summary>
        private bool HandleMeetingEditorKey(Keys key)
        {
            if (!IsEditingMeeting) return false;
            if (_meetingMenu != null && _meetingMenu.Visible) return false;

            switch (key)
            {
                case Keys.Return: PlaceWithActiveTool(); return true;
                case Keys.Back: DeleteNearestEditPoint(); return true;
                case Keys.E: CycleEditTool(); return true;
                case Keys.R: ToggleNearestLineSurface(); return true;
                default: return false;
            }
        }

        private void TickMeetingEditor()
        {
            if (!IsEditingMeeting) return;
            if (!_meetingVenues.Contains(_editVenue))
            {
                EndMeetingEdit("venue removed");
                return;
            }
            if (_editVenue.ForceActive)
            {
                EndMeetingEdit("meetup started");
                return;
            }

            DrawEditVenue(_editVenue);
            GTA.UI.Screen.ShowHelpTextThisFrame(EditHelpText(), false);
        }

        // =========================================================================
        //  Actions
        // =========================================================================

        private void CycleEditTool()
        {
            _editTool = (MeetingEditTool)(((int)_editTool + 1) % 3);
            _editLineStart = null;
            LogMeeting($"Edit: tool -> {_editTool}");
        }

        private void PlaceWithActiveTool()
        {
            MeetingVenue venue = _editVenue;
            MeetingPoint here = CapturePoint();

            switch (_editTool)
            {
                case MeetingEditTool.Parking:
                    if (venue.ParkingPoints.Count >= MeetingVenue.MaxParkingPoints)
                    {
                        EditFeedback(T($"~r~{MeetingVenue.MaxParkingPoints} places max", $"~r~{MeetingVenue.MaxParkingPoints} spots max"));
                        return;
                    }
                    venue.ParkingPoints.Add(here);
                    SaveEdit(T($"~g~Place P{venue.ParkingPoints.Count} posée", $"~g~Spot P{venue.ParkingPoints.Count} placed"),
                        $"parking +1 at {here.Position} h={here.Heading:0}");
                    break;

                case MeetingEditTool.Circle:
                    bool moved = venue.CircleCenter.HasValue;
                    venue.CircleCenter = here.Position;
                    SaveEdit(moved ? T("~g~Cercle déplacé", "~g~Circle moved") : T("~g~Cercle posé", "~g~Circle placed"),
                        $"circle center at {here.Position}");
                    break;

                case MeetingEditTool.Line:
                    PlaceLinePoint(venue, here.Position);
                    break;
            }
        }

        private void PlaceLinePoint(MeetingVenue venue, Vector3 position)
        {
            if (!_editLineStart.HasValue)
            {
                if (venue.Lines.Count >= MeetingVenue.MaxLines)
                {
                    EditFeedback(T($"~r~{MeetingVenue.MaxLines} lignes max", $"~r~{MeetingVenue.MaxLines} lines max"));
                    return;
                }
                _editLineStart = position;
                EditFeedback(T("~g~Début posé · va à la fin et Entrée", "~g~Start placed · go to the end and press Enter"));
                LogMeeting($"Edit: line start at {position}");
                return;
            }

            float length = _editLineStart.Value.DistanceTo(position);
            if (length < MeetingLineMinLength)
            {
                EditFeedback(T($"~o~Ligne trop courte ({length:0} m, {MeetingLineMinLength:0} m min)",
                    $"~o~Line too short ({length:0} m, {MeetingLineMinLength:0} m min)"));
                return;
            }

            var line = new MeetingLine { A = _editLineStart.Value, B = position, OffRoad = false };
            venue.Lines.Add(line);
            _editLineStart = null;
            SaveEdit(T($"~g~Ligne L{venue.Lines.Count} posée ({length:0} m, route · R pour changer)",
                    $"~g~Line L{venue.Lines.Count} placed ({length:0} m, road · R to change)"),
                $"line +1 {line.A} -> {line.B} len={length:0}");
        }

        private void DeleteNearestEditPoint()
        {
            MeetingVenue venue = _editVenue;
            Vector3 player = Game.Player.Character.Position;

            if (_editLineStart.HasValue)
            {
                _editLineStart = null;
                EditFeedback(T("~o~Début de ligne annulé", "~o~Line start cancelled"));
                LogMeeting("Edit: pending line start cancelled");
                return;
            }

            float best = MeetingEditDeleteRadius;
            int parkingIndex = -1, lineIndex = -1;
            bool circle = false;

            for (int i = 0; i < venue.ParkingPoints.Count; i++)
            {
                float d = player.DistanceTo(venue.ParkingPoints[i].Position);
                if (d < best) { best = d; parkingIndex = i; lineIndex = -1; circle = false; }
            }
            if (venue.CircleCenter.HasValue)
            {
                float d = player.DistanceTo(venue.CircleCenter.Value);
                if (d < best) { best = d; parkingIndex = -1; lineIndex = -1; circle = true; }
            }
            for (int i = 0; i < venue.Lines.Count; i++)
            {
                float d = DistanceToSegment(player, venue.Lines[i].A, venue.Lines[i].B);
                if (d < best) { best = d; parkingIndex = -1; lineIndex = i; circle = false; }
            }

            if (parkingIndex >= 0)
            {
                if (venue.ParkingPoints.Count <= 1)
                {
                    EditFeedback(T("~o~Un lieu garde au moins une place · supprime le lieu via le menu",
                        "~o~A venue keeps at least one spot · delete the venue from the menu"));
                    return;
                }
                venue.ParkingPoints.RemoveAt(parkingIndex);
                SaveEdit(T($"~o~Place P{parkingIndex + 1} supprimée", $"~o~Spot P{parkingIndex + 1} deleted"),
                    $"parking P{parkingIndex + 1} deleted");
            }
            else if (circle)
            {
                venue.CircleCenter = null;
                SaveEdit(T("~o~Cercle supprimé", "~o~Circle deleted"), "circle deleted");
            }
            else if (lineIndex >= 0)
            {
                venue.Lines.RemoveAt(lineIndex);
                SaveEdit(T($"~o~Ligne L{lineIndex + 1} supprimée", $"~o~Line L{lineIndex + 1} deleted"),
                    $"line L{lineIndex + 1} deleted");
            }
            else
            {
                EditFeedback(T($"~o~Aucun point à moins de {MeetingEditDeleteRadius:0} m",
                    $"~o~No point within {MeetingEditDeleteRadius:0} m"));
            }
        }

        private void ToggleNearestLineSurface()
        {
            MeetingVenue venue = _editVenue;
            Vector3 player = Game.Player.Character.Position;
            int index = -1;
            float best = float.MaxValue;
            for (int i = 0; i < venue.Lines.Count; i++)
            {
                float d = DistanceToSegment(player, venue.Lines[i].A, venue.Lines[i].B);
                if (d < best) { best = d; index = i; }
            }
            if (index < 0)
            {
                EditFeedback(T("~o~Aucune ligne · outil Ligne (E) puis Entrée deux fois",
                    "~o~No line · Line tool (E) then Enter twice"));
                return;
            }

            MeetingLine line = venue.Lines[index];
            line.OffRoad = !line.OffRoad;
            SaveEdit(line.OffRoad ? T($"~g~L{index + 1} : hors route", $"~g~L{index + 1}: off-road")
                                  : T($"~g~L{index + 1} : route", $"~g~L{index + 1}: road"),
                $"line L{index + 1} offroad={line.OffRoad}");
        }

        private void SaveEdit(string feedback, string logDetail)
        {
            SaveVenueToIni(_editVenue);
            EditFeedback(feedback);
            LogMeeting($"Edit: {_editVenue.Name} {logDetail}");
        }

        private static void EditFeedback(string message)
        {
            Notification.PostTicker("Meetings : " + message, true);
        }

        /// <summary>
        /// Capture la position et le cap du joueur, ou de sa moto s'il roule dessus :
        /// garer sa propre moto puis valider donne exactement l'emplacement voulu.
        /// </summary>
        private static MeetingPoint CapturePoint()
        {
            Ped player = Game.Player.Character;
            Vehicle vehicle = player.CurrentVehicle;
            Entity source = vehicle != null && vehicle.Exists() ? (Entity)vehicle : player;
            return new MeetingPoint { Position = source.Position, Heading = source.Heading };
        }

        // =========================================================================
        //  Rendering
        // =========================================================================

        private void DrawEditVenue(MeetingVenue venue)
        {
            Vector3 player = Game.Player.Character.Position;

            foreach (MeetingPoint p in venue.ParkingPoints)
            {
                if (player.DistanceTo(p.Position) > MeetingEditDrawDistance) continue;
                Vector3 ground = GroundAt(p.Position);
                World.DrawMarker(MarkerType.Cylinder, ground, Vector3.Zero, Vector3.Zero,
                    new Vector3(0.8f, 0.8f, 0.4f), EditParkingColor);
                // Le trait indique le cap de stationnement (avant de la moto).
                Vector3 tip = ground + GetHeadingDir(p.Heading) * 1.8f + new Vector3(0f, 0f, 0.3f);
                World.DrawLine(ground + new Vector3(0f, 0f, 0.3f), tip, EditParkingColor);
            }

            if (venue.CircleCenter.HasValue && player.DistanceTo(venue.CircleCenter.Value) < MeetingEditDrawDistance)
            {
                float diameter = MeetingCircleRadius * 2f;
                World.DrawMarker(MarkerType.Cylinder, GroundAt(venue.CircleCenter.Value), Vector3.Zero, Vector3.Zero,
                    new Vector3(diameter, diameter, 0.3f), EditCircleColor);
            }

            foreach (MeetingLine line in venue.Lines)
            {
                if (player.DistanceTo(line.Middle) > MeetingEditDrawDistance + line.A.DistanceTo(line.B) * 0.5f) continue;
                DrawEditSegment(line.A, line.B, line.OffRoad ? EditOffRoadLineColor : EditRoadLineColor);
            }

            if (_editLineStart.HasValue)
                DrawEditSegment(_editLineStart.Value, player, EditPendingColor);
        }

        private static void DrawEditSegment(Vector3 a, Vector3 b, Color color)
        {
            Vector3 lift = new Vector3(0f, 0f, 0.4f);
            Vector3 ga = GroundAt(a), gb = GroundAt(b);
            World.DrawLine(ga + lift, gb + lift, color);
            World.DrawMarker(MarkerType.Cylinder, ga, Vector3.Zero, Vector3.Zero, new Vector3(0.6f, 0.6f, 1.2f), color);
            World.DrawMarker(MarkerType.Cylinder, gb, Vector3.Zero, Vector3.Zero, new Vector3(0.6f, 0.6f, 1.2f), color);
        }

        /// <summary>Projette un point au sol ; sol non résolu (zone non streamée) = position brute.</summary>
        private static Vector3 GroundAt(Vector3 position)
        {
            float ground;
            if (!World.GetGroundHeight(position + new Vector3(0f, 0f, 1.5f), out ground, GetGroundHeightMode.Normal))
                return position;
            return new Vector3(position.X, position.Y, ground);
        }

        private string EditHelpText()
        {
            string tool;
            switch (_editTool)
            {
                case MeetingEditTool.Parking:
                    tool = T("PARKING : Entrée = place ici (gare ta moto pour l'emplacement exact)",
                             "PARKING: Enter = spot here (park your bike for the exact spot)");
                    break;
                case MeetingEditTool.Circle:
                    tool = T("CERCLE : Entrée = centre du cercle à pied", "CIRCLE: Enter = on-foot circle center");
                    break;
                default:
                    tool = _editLineStart.HasValue
                        ? T("LIGNE : Entrée = fin de la ligne", "LINE: Enter = line end")
                        : T("LIGNE : Entrée = début de la ligne · R = route/hors route", "LINE: Enter = line start · R = road/off-road");
                    break;
            }
            MeetingVenue v = _editVenue;
            string counts = $"P {v.ParkingPoints.Count} · {T("Cercle", "Circle")} {(v.CircleCenter.HasValue ? T("oui", "yes") : T("non", "no"))} · {T("Lignes", "Lines")} {v.Lines.Count}";
            return $"{v.Name} · {counts}~n~{tool}~n~E = {T("outil suivant", "next tool")} · {T("Retour = suppr. proche", "Backspace = delete nearest")} · {_meetingMenuKey} = {T("quitter", "exit")}";
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.LengthSquared();
            if (lengthSq < 0.0001f) return p.DistanceTo(a);
            float t = Math.Max(0f, Math.Min(1f, Vector3.Dot(p - a, ab) / lengthSq));
            return p.DistanceTo(a + ab * t);
        }
    }
}
