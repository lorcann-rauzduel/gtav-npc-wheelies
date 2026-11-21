using System.Linq;
using GTA.UI;
using LemonUI;
using LemonUI.Menus;

namespace StreetRiders
{
    /// <summary>
    /// Menu MEETINGS (F8 par défaut) : créer un lieu, puis pour chaque lieu Éditer,
    /// Démarrer/Arrêter, Groupe et Supprimer. La pose des points se fait uniquement
    /// dans le mode édition au sol (MeetingEditor), jamais depuis ce menu.
    /// </summary>
    public partial class StreetRidersScript
    {
        private ObjectPool _meetingPool;
        private NativeMenu _meetingMenu;

        private string T(string fr, string en)
        {
            return _langEn ? en : fr;
        }

        // =========================================================================
        //  Menu lifecycle
        // =========================================================================

        private void ToggleMeetingMenu()
        {
            if (IsEditingMeeting)
            {
                EndMeetingEdit("menu key");
                return;
            }
            if (_meetingMenu != null && _meetingMenu.Visible)
            {
                HideMeetingMenu();
                return;
            }
            RebuildMeetingMenus();
        }

        private void HideMeetingMenu()
        {
            if (_meetingPool != null) _meetingPool.HideAll();
        }

        private void RebuildMeetingMenus()
        {
            // Un pool neuf écarte toute instance obsolète : une seule surface visible.
            HideMeetingMenu();
            _meetingPool = new ObjectPool();
            _meetingMenu = new NativeMenu("MEETINGS", "STREET RIDERS");
            _meetingPool.Add(_meetingMenu);
            BuildMeetingRoot(_meetingMenu);
            _meetingMenu.Visible = true;
        }

        // =========================================================================
        //  Root menu
        // =========================================================================

        private void BuildMeetingRoot(NativeMenu menu)
        {
            var createItem = new NativeItem(
                T("Créer un lieu ici", "Create venue here"),
                T("Pose une 1re place de parking ici puis ouvre l'édition.",
                  "Places a first parking spot here, then opens the editor."));
            createItem.Activated += (s, e) => CreateVenue();
            menu.Add(createItem);

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
            NativeSubmenuItem entry = parent.AddSubMenu(sub);
            entry.Description = VenueSummary(venue);

            var editItem = new NativeItem(
                T("Éditer", "Edit"),
                venue.ForceActive
                    ? T("Indisponible : arrête d'abord le meetup.", "Unavailable: stop the meetup first.")
                    : T("Places, cercle et lignes posés au sol.", "Spots, circle and lines placed on the ground."));
            editItem.Enabled = !venue.ForceActive;
            editItem.Activated += (s, e) => BeginMeetingEdit(venue);
            sub.Add(editItem);

            if (venue.ForceActive)
            {
                var stopItem = new NativeItem(
                    T("Arrêter", "Stop"),
                    T("Les motards repartent en formation.", "Riders leave in formation."));
                stopItem.Activated += (s, e) =>
                {
                    LogMeeting($"Menu: stop {venue.Name}");
                    DismantleVenueMeeting(venue, true);
                    RebuildMeetingMenus();
                };
                sub.Add(stopItem);
            }
            else
            {
                var startItem = new NativeItem(
                    T("Démarrer", "Start"),
                    T("Lance le meetup maintenant, même hors horaires.", "Starts the meetup now, even off-schedule."));
                startItem.Activated += (s, e) => TryStartVenue(venue);
                sub.Add(startItem);
            }

            if (_groupConfigs.Count > 0)
            {
                var groupItem = new NativeListItem<string>(
                    T("Groupe", "Group"),
                    T("Motards et motos utilisés (groups.ini).", "Riders and bikes used (groups.ini)."),
                    _groupConfigs.Select(g => g.Name).ToArray());
                int groupIndex = _groupConfigs.FindIndex(g => g.Name == venue.GroupName);
                if (groupIndex >= 0) groupItem.SelectedIndex = groupIndex;
                groupItem.ItemChanged += (sender, e) =>
                {
                    venue.GroupName = e.Object;
                    SaveVenueToIni(venue);
                    LogMeeting($"Menu: {venue.Name} group -> {venue.GroupName}");
                };
                sub.Add(groupItem);
            }
            else
            {
                var noGroup = new NativeItem(
                    T("Groupe", "Group"),
                    T("Aucun groupe : ajoute un [Group_X] dans groups.ini.", "No group: add a [Group_X] in groups.ini."));
                noGroup.Enabled = false;
                sub.Add(noGroup);
            }

            var deleteItem = new NativeItem(
                T("Supprimer le lieu", "Delete venue"),
                T("Retire le lieu de meetings.ini.", "Removes the venue from meetings.ini."));
            deleteItem.Activated += (s, e) => DeleteVenue(venue);
            sub.Add(deleteItem);
        }

        private string VenueSummary(MeetingVenue venue)
        {
            string circle = venue.CircleCenter.HasValue ? T("oui", "yes") : T("non", "no");
            string state = venue.ForceActive ? T(" · EN COURS", " · RUNNING") : "";
            return $"{venue.GroupName} · P {venue.ParkingPoints.Count} · {T("Cercle", "Circle")} {circle} · {T("Lignes", "Lines")} {venue.Lines.Count}{state}";
        }

        // =========================================================================
        //  Actions
        // =========================================================================

        private void CreateVenue()
        {
            var venue = new MeetingVenue
            {
                Name = NextVenueName(),
                GroupName = _groupConfigs.Count > 0 ? _groupConfigs[0].Name : ""
            };
            venue.ParkingPoints.Add(CapturePoint());
            _meetingVenues.Add(venue);
            SaveVenueToIni(venue);
            LogMeeting($"Menu: created {venue.Name} group={venue.GroupName} at {venue.Center}");
            Notification.PostTicker(T($"~g~Meetings : {venue.Name} créé", $"~g~Meetings: {venue.Name} created"), true);
            BeginMeetingEdit(venue);
        }

        private string NextVenueName()
        {
            // Évite de réutiliser le nom (donc la section INI) d'un lieu existant.
            for (int i = _meetingVenues.Count + 1; ; i++)
            {
                string name = "Venue_" + i;
                if (!_meetingVenues.Any(v => v.Name == name)) return name;
            }
        }

        private void DeleteVenue(MeetingVenue venue)
        {
            if (venue.ForceActive) DismantleVenueMeeting(venue, true);
            if (_editVenue == venue) EndMeetingEdit("venue deleted");
            RemoveVenueFromIni(venue);
            _meetingVenues.Remove(venue);
            LogMeeting($"Menu: deleted {venue.Name}");
            Notification.PostTicker(T($"~o~Meetings : {venue.Name} supprimé", $"~o~Meetings: {venue.Name} deleted"), true);
            RebuildMeetingMenus();
        }

        private void TryStartVenue(MeetingVenue venue)
        {
            if (venue.ForceActive) return;
            LogMeeting($"Menu: start {venue.Name}");
            StartVenueMeeting(venue, true);
            RebuildMeetingMenus();
        }
    }
}
