using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GTA;
using GTA.Math;
using GTA.UI;
using LemonUI;
using LemonUI.Menus;

namespace StreetRiders
{
    /// <summary>
    /// Meetings editor menu (default F8). Authoring tool: create venues with a single
    /// parking anchor at the player position, re-anchor it, bind the venue to a group,
    /// start or stop the meeting, delete the venue. All edits persist immediately in
    /// meetings.ini. Test-style editing surfaces are only reachable through this
    /// submenu, never on the main in-game flow.
    /// </summary>
    public partial class StreetRidersScript
    {
        private ObjectPool _meetingPool;
        private NativeMenu _meetingMenu;
        private int _meetingParkingSel;
        private int _meetingCircleSel;

        private string T(string fr, string en)
        {
            return _langEn ? en : fr;
        }

        // =========================================================================
        //  Menu lifecycle
        // =========================================================================

        private void ToggleMeetingMenu()
        {
            if (_meetingMenu != null && _meetingMenu.Visible)
            {
                _meetingMenu.Visible = false;
                return;
            }
            RebuildMeetingMenus();
        }

        private void HideMeetingMenu()
        {
            if (_meetingMenu != null) _meetingMenu.Visible = false;
        }

        private void RebuildMeetingMenus()
        {
            // A fresh pool drops any stale menu instance, keeping a single visible surface.
            _meetingPool = new ObjectPool();
            _meetingMenu = new NativeMenu(T("MEETINGS", "MEETINGS"), "STREET RIDERS");
            _meetingPool.Add(_meetingMenu);
            _meetingMenu.Visible = true;
            BuildMeetingRoot(_meetingMenu);
        }

        // =========================================================================
        //  Root menu
        // =========================================================================

        private void BuildMeetingRoot(NativeMenu menu)
        {
            var createItem = new NativeItem(
                T("Créer un lieu ici", "Create venue here"),
                T("Crée le lieu avec un premier point de parking à la position + cap du joueur.",
                  "Creates the venue with a first parking spot at the player position + heading."));
            createItem.Activated += (s, e) => CreateVenue();
            menu.Add(createItem);

            if (_meetingVenues.Count == 0)
            {
                var info = new NativeItem(
                    T("Aucun lieu configuré", "No venues yet"),
                    T("Créer un lieu : ajoute des points de parking (1 par moto), des points de cercle à pied (8 max), puis démarre.",
                      "Create a venue: add parking spots (1 per bike), on-foot circle points (8 max), then start."));
                menu.Add(info);
                return;
            }

            foreach (var venue in _meetingVenues)
                BuildVenueMenu(menu, venue);
        }

        // =========================================================================
        //  Venue submenu
        // =========================================================================

        private void BuildVenueMenu(NativeMenu parent, MeetingVenue venue)
        {
            var sub = new NativeMenu("MEETINGS", venue.Name);
            _meetingPool.Add(sub);
            parent.AddSubMenu(sub, venue.Name);

            // Start / stop
            if (venue.ForceActive)
            {
                var stopItem = new NativeItem(
                    T("Arrêter (départ en formation)", "Stop (formation departure)"),
                    T("Lance le départ en formation des motards.", "Sends the riders away in formation."));
                stopItem.Activated += (s, e) =>
                {
                    DismantleVenueMeeting(venue, true);
                    RebuildMeetingMenus();
                };
                sub.Add(stopItem);
            }
            else
            {
                var startItem = new NativeItem(
                    T("Démarrer maintenant", "Start now"),
                    T("Force le meeting ici, hors horaires si besoin.", "Forces the meeting here, off-schedule if needed."));
                startItem.Activated += (s, e) => TryStartVenue(venue);
                sub.Add(startItem);
            }

            // Group binding (persist on change)
            var groupItem = new NativeListItem<string>(
                T("Groupe", "Group"),
                T("Le groupe utilisé pour les motards et motos.", "Group used for riders and bikes."),
                _groupConfigs.Select(g => g.Name).ToArray());
            int groupIndex = _groupConfigs.FindIndex(g => g.Name == venue.GroupName);
            if (groupIndex >= 0) groupItem.SelectedIndex = groupIndex;
            groupItem.ItemChanged += (sender, e) =>
            {
                venue.GroupName = e.Object;
                SaveVenueToIni(venue);
            };
            sub.Add(groupItem);

            // Parking points: capture a new point at the player, or re-align an existing one.
            var addParking = new NativeItem(
                T("Ajouter un point de parking (joueur)", "Add parking point (player)"),
                T("Enregistre position + cap du joueur comme emplacement de parking.",
                  "Records player position + heading as a parking spot."));
            addParking.Activated += (s, e) =>
            {
                venue.ParkingPoints.Add(CapturePoint());
                SaveVenueToIni(venue);
                Notification.PostTicker($"~g~Meetings: {venue.Name} point parking +1", true);
                RebuildMeetingMenus();
            };
            sub.Add(addParking);

            var selParking = new NativeListItem<string>(
                T("Point parking", "Parking point"),
                T("Choisis le point de parking à remplacer ou supprimer.",
                  "Pick the parking spot to replace or delete."),
                Enumerable.Range(1, venue.ParkingPoints.Count)
                    .Select(i => i.ToString()).ToArray());
            int parkSel = _meetingParkingSel;
            if (parkSel >= venue.ParkingPoints.Count) parkSel = 0;
            selParking.SelectedIndex = parkSel;
            selParking.ItemChanged += (sender, e) => _meetingParkingSel = e.Index;
            sub.Add(selParking);

            var setParking = new NativeItem(
                T("Repositionner le point choisi (joueur)", "Overwrite picked point (player)"),
                T("Remplace le point choisi par la position + cap du joueur.",
                  "Replaces the picked spot with player position + heading."));
            setParking.Activated += (s, e) =>
            {
                int idx = Math.Min(_meetingParkingSel, venue.ParkingPoints.Count - 1);
                if (idx < 0) return;
                venue.ParkingPoints[idx] = CapturePoint();
                SaveVenueToIni(venue);
                Notification.PostTicker($"~g~Meetings: {venue.Name} point parking {idx + 1} mis à jour", true);
                RebuildMeetingMenus();
            };
            sub.Add(setParking);

            var delParking = new NativeItem(
                T("Supprimer le point choisi", "Delete picked point"),
                T("Retire le point de parking choisi. Le dernier point ne peut pas être supprimé.",
                  "Removes the picked parking spot. The last spot cannot be removed."));
            delParking.Activated += (s, e) =>
            {
                if (venue.ParkingPoints.Count <= 1) return;
                int idx = Math.Min(_meetingParkingSel, venue.ParkingPoints.Count - 1);
                venue.ParkingPoints.RemoveAt(Math.Max(0, idx));
                _meetingParkingSel = 0;
                SaveVenueToIni(venue);
                Notification.PostTicker($"~o~Meetings: {venue.Name} point parking {idx + 1} supprimé", true);
                RebuildMeetingMenus();
            };
            sub.Add(delParking);

            // Circle points: authored slots (8 max) where on-foot riders gather facing center.
            var addCircle = new NativeItem(
                T("Ajouter un point de cercle (joueur)", "Add circle point (player)"),
                T("Enregistre un point du cercle à pied (8 max) à la position du joueur.",
                  "Records an on-foot circle slot (8 max) at the player position."));
            addCircle.Activated += (s, e) =>
            {
                if (venue.CirclePoints.Count >= 8)
                {
                    Notification.PostTicker($"~r~Meetings: 8 points de cercle max", true);
                    return;
                }
                venue.CirclePoints.Add(CapturePoint());
                SaveVenueToIni(venue);
                Notification.PostTicker($"~g~Meetings: {venue.Name} point cercle +1", true);
                RebuildMeetingMenus();
            };
            sub.Add(addCircle);

            var selCircle = new NativeListItem<string>(
                T("Point cercle", "Circle point"),
                T("Choisis le point de cercle à remplacer ou supprimer.",
                  "Pick the circle slot to replace or delete."),
                Enumerable.Range(1, Math.Max(1, venue.CirclePoints.Count))
                    .Select(i => i.ToString()).ToArray());
            int circSel = _meetingCircleSel;
            if (circSel >= venue.CirclePoints.Count) circSel = 0;
            selCircle.SelectedIndex = circSel;
            selCircle.ItemChanged += (sender, e) => _meetingCircleSel = e.Index;
            sub.Add(selCircle);

            var setCircle = new NativeItem(
                T("Repositionner le point cercle choisi (joueur)", "Overwrite picked circle point (player)"),
                T("Remplace le point de cercle choisi par la position + cap du joueur.",
                  "Replaces the picked circle slot with player position + heading."));
            setCircle.Activated += (s, e) =>
            {
                if (venue.CirclePoints.Count == 0) return;
                int idx = Math.Min(_meetingCircleSel, venue.CirclePoints.Count - 1);
                venue.CirclePoints[idx] = CapturePoint();
                SaveVenueToIni(venue);
                Notification.PostTicker($"~g~Meetings: {venue.Name} point cercle {idx + 1} mis à jour", true);
                RebuildMeetingMenus();
            };
            sub.Add(setCircle);

            var delCircle = new NativeItem(
                T("Supprimer le point cercle choisi", "Delete picked circle point"),
                T("Retire le point de cercle choisi.", "Removes the picked circle slot."));
            delCircle.Activated += (s, e) =>
            {
                if (venue.CirclePoints.Count == 0) return;
                int idx = Math.Min(_meetingCircleSel, venue.CirclePoints.Count - 1);
                venue.CirclePoints.RemoveAt(Math.Max(0, idx));
                _meetingCircleSel = 0;
                SaveVenueToIni(venue);
                Notification.PostTicker($"~o~Meetings: {venue.Name} point cercle {idx + 1} supprimé", true);
                RebuildMeetingMenus();
            };
            sub.Add(delCircle);

            var deleteItem = new NativeItem(
                T("Supprimer ce lieu", "Delete venue"),
                T("Supprime le lieu de meetings.ini.", "Deletes the venue from meetings.ini."));
            deleteItem.Activated += (s, e) => DeleteVenue(venue);
            sub.Add(deleteItem);

            sub.Add(new NativeItem(
                T($"Active: {(venue.ForceActive ? "OUI" : "non")} · Parking {venue.ParkingPoints.Count} · Cercle {venue.CirclePoints.Count}",
                  $"Active: {(venue.ForceActive ? "YES" : "no")} · Parking {venue.ParkingPoints.Count} · Circle {venue.CirclePoints.Count}"),
                T("Info lieu — modifié via le menu ou meetings.ini.", "Venue info — editable in the menu or meetings.ini.")));
        }

        // =========================================================================
        //  Actions
        // =========================================================================

        private void CreateVenue()
        {
            var venue = new MeetingVenue
            {
                Name = "Venue_" + (_meetingVenues.Count + 1),
                GroupName = _groupConfigs.Count > 0 ? _groupConfigs[0].Name : ""
            };
            venue.ParkingPoints.Add(CapturePoint());
            _meetingVenues.Add(venue);
            SaveVenueToIni(venue);
            Notification.PostTicker($"~g~Meetings: {venue.Name} créé (1 point de parking)", true);
            RebuildMeetingMenus();
        }

        private void DeleteVenue(MeetingVenue venue)
        {
            if (venue.ForceActive)
            {
                DismantleVenueMeeting(venue, true);
            }
            RemoveVenueFromIni(venue);
            _meetingVenues.Remove(venue);
            Notification.PostTicker($"~o~Meetings: {venue.Name} supprimé", true);
            RebuildMeetingMenus();
        }

        private void TryStartVenue(MeetingVenue venue)
        {
            if (venue.ForceActive) return;
            if (FindGroupConfig(venue.GroupName) == null)
            {
                Notification.PostTicker($"~r~Meetings: {venue.Name} — groupe '{venue.GroupName}' introuvable dans groups.ini", true);
                return;
            }
            StartVenueMeeting(venue, true);
            RebuildMeetingMenus();
        }

        // =========================================================================
        //  Persistence (meetings.ini via Windows INI API)
        // =========================================================================

        private void SaveVenueToIni(MeetingVenue venue)
        {
            string file = Path.GetFullPath(MEETINGS_INI_PATH);
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string section = "Venue_" + SanitizeVenueName(venue.Name);
            WriteIniValue(file, section, "Group", venue.GroupName);
            // Supprime les clés de l'ancien schéma (point unique) et toute clé devenant obsolète.
            DeleteIniValue(file, section, "Position");
            for (int i = 1; i <= 24; i++)
            {
                if (i <= venue.ParkingPoints.Count)
                    WriteIniValue(file, section, "ParkingPoint" + i,
                        SerializeVenuePosition(venue.ParkingPoints[i - 1].Position, venue.ParkingPoints[i - 1].Heading));
                else
                    DeleteIniValue(file, section, "ParkingPoint" + i);
            }
            for (int i = 1; i <= 8; i++)
            {
                if (i <= venue.CirclePoints.Count)
                    WriteIniValue(file, section, "CirclePoint" + i,
                        SerializeVenuePosition(venue.CirclePoints[i - 1].Position, venue.CirclePoints[i - 1].Heading));
                else
                    DeleteIniValue(file, section, "CirclePoint" + i);
            }
        }

        private static MeetingPoint CapturePoint()
        {
            return new MeetingPoint
            {
                Position = Game.Player.Character.Position,
                Heading = Game.Player.Character.Heading
            };
        }

        private void RemoveVenueFromIni(MeetingVenue venue)
        {
            string file = Path.GetFullPath(MEETINGS_INI_PATH);
            string section = "Venue_" + SanitizeVenueName(venue.Name);
            DeleteIniSection(file, section);
        }

        private static string SanitizeVenueName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            if (sb.Length == 0) sb.Append("Venue");
            return sb.ToString();
        }
    }
}