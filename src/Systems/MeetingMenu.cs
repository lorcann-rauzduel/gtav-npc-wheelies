using System.Linq;
using GTA;
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

        // Plages d'effectif proposées par lieu ; la première entrée = défaut de [Meetings].
        private static readonly int[][] MeetingRiderRanges =
        {
            null, new[] { 2, 4 }, new[] { 4, 6 }, new[] { 6, 8 }, new[] { 8, 10 }, new[] { 10, 14 }
        };

        private void BuildMeetingRoot(NativeMenu menu)
        {
            var createItem = new NativeItem(
                T("+ Nouveau lieu ici", "+ New venue here"),
                T("Crée un lieu vide ici puis ouvre la modification au sol.",
                  "Creates an empty venue here, then opens on-ground editing."));
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
            entry.AltTitle = venue.ForceActive
                ? T("EN COURS", "RUNNING")
                : $"{venue.GroupName} · {Plural(venue.ParkingPoints.Count, T("place", "spot"), T("places", "spots"))} · {Plural(venue.Lines.Count, T("ligne", "line"), T("lignes", "lines"))}";
            entry.Description = T("Lancer, modifier ou régler ce lieu.", "Start, edit or tune this venue.");

            AddStartStopItem(sub, venue);
            AddEditItem(sub, venue);
            AddGroupItem(sub, venue);
            AddRidersItem(sub, venue);
            AddExtraRidersItem(sub, venue);
            AddStateItems(sub, venue);

            var deleteItem = new NativeItem(
                T("Supprimer ce lieu", "Delete this venue"),
                T("Retire le lieu de meetings.ini.", "Removes the venue from meetings.ini."));
            deleteItem.Activated += (s, e) => DeleteVenue(venue);
            sub.Add(deleteItem);
        }

        private void AddStartStopItem(NativeMenu sub, MeetingVenue venue)
        {
            if (venue.ForceActive)
            {
                var stopItem = new NativeItem(
                    T("Arrêter le meetup", "Stop the meetup"),
                    T("Les motards repartent en formation.", "Riders leave in formation."));
                stopItem.Activated += (s, e) =>
                {
                    LogMeeting($"Menu: stop {venue.Name}");
                    DismantleVenueMeeting(venue, true);
                    RebuildMeetingMenus();
                };
                sub.Add(stopItem);
                return;
            }

            bool hasSpots = venue.ParkingPoints.Count > 0;
            var startItem = new NativeItem(
                T("Lancer le meetup maintenant", "Start the meetup now"),
                hasSpots
                    ? T("Le groupe arrive, se gare et s'installe, même hors horaires.",
                        "The group rides in, parks and settles, even off-schedule.")
                    : T("Indisponible : pose au moins une place via Modifier au sol.",
                        "Unavailable: place at least one spot with Edit on the ground."));
            startItem.Enabled = hasSpots;
            startItem.Activated += (s, e) => TryStartVenue(venue);
            sub.Add(startItem);
        }

        private void AddEditItem(NativeMenu sub, MeetingVenue venue)
        {
            var editItem = new NativeItem(
                T("Modifier au sol", "Edit on the ground"),
                venue.ForceActive
                    ? T("Indisponible : arrête d'abord le meetup.", "Unavailable: stop the meetup first.")
                    : T("Places de parking, cercle et lignes de freestyle.", "Parking spots, circle and freestyle lines."));
            editItem.Enabled = !venue.ForceActive;
            editItem.Activated += (s, e) => BeginMeetingEdit(venue);
            sub.Add(editItem);
        }

        private void AddGroupItem(NativeMenu sub, MeetingVenue venue)
        {
            if (_groupConfigs.Count == 0)
            {
                var noGroup = new NativeItem(
                    T("Groupe", "Group"),
                    T("Aucun groupe : ajoute un [Group_X] dans groups.ini.", "No group: add a [Group_X] in groups.ini."));
                noGroup.Enabled = false;
                sub.Add(noGroup);
                return;
            }

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

        private void AddRidersItem(NativeMenu sub, MeetingVenue venue)
        {
            string[] labels = MeetingRiderRanges
                .Select(r => r == null
                    ? T($"Défaut ({_meetingRidersMin}-{_meetingRidersMax})", $"Default ({_meetingRidersMin}-{_meetingRidersMax})")
                    : $"{r[0]}-{r[1]}")
                .ToArray();
            var item = new NativeListItem<string>(
                T("Nombre de motards", "Number of riders"),
                T("Tiré au hasard dans la plage à chaque meetup.", "Picked at random in the range for each meetup."),
                labels);

            int selected = 0;
            for (int i = 1; i < MeetingRiderRanges.Length; i++)
                if (venue.RidersMin == MeetingRiderRanges[i][0] && venue.RidersMax == MeetingRiderRanges[i][1]) selected = i;
            item.SelectedIndex = selected;

            item.ItemChanged += (sender, e) =>
            {
                int[] range = MeetingRiderRanges[e.Index];
                venue.RidersMin = range?[0];
                venue.RidersMax = range?[1];
                SaveVenueToIni(venue);
                LogMeeting($"Menu: {venue.Name} riders -> {(range == null ? "default" : range[0] + "-" + range[1])}");
            };
            sub.Add(item);
        }

        private void AddExtraRidersItem(NativeMenu sub, MeetingVenue venue)
        {
            string defaultLabel = _meetingExtraRiders == MeetingExtraRiders.AutoSpots ? T("places auto", "auto spots") : T("limiter", "limit");
            var item = new NativeListItem<string>(
                T("Motards en trop", "Extra riders"),
                T("Plus de motards que de places : limiter l'effectif, ou générer des places à la suite de la dernière.",
                  "More riders than spots: limit the group, or generate spots after the last one."),
                T($"Défaut ({defaultLabel})", $"Default ({defaultLabel})"),
                T("Limiter aux places", "Limit to spots"),
                T("Places auto", "Auto spots"));
            item.SelectedIndex = !venue.ExtraRiders.HasValue ? 0 : venue.ExtraRiders.Value == MeetingExtraRiders.Limit ? 1 : 2;
            item.ItemChanged += (sender, e) =>
            {
                venue.ExtraRiders = e.Index == 0 ? (MeetingExtraRiders?)null
                    : e.Index == 1 ? MeetingExtraRiders.Limit : MeetingExtraRiders.AutoSpots;
                SaveVenueToIni(venue);
                LogMeeting($"Menu: {venue.Name} extra riders -> {(venue.ExtraRiders.HasValue ? venue.ExtraRiders.Value.ToString() : "default")}");
            };
            sub.Add(item);
        }

        private void AddStateItems(NativeMenu sub, MeetingVenue venue)
        {
            sub.Add(new NativeSeparatorItem(T("État", "Status")));

            string circle = venue.CircleCenter.HasValue ? T("oui", "yes") : T("non", "no");
            string lines = venue.Lines.Count == 0
                ? T("0 (pas de freestyle)", "0 (no freestyle)")
                : venue.Lines.Count.ToString();
            var state = new NativeItem(
                T($"Places : {venue.ParkingPoints.Count}   Cercle : {circle}   Spots : {venue.StandSpots.Count}   Lignes : {lines}",
                  $"Parking: {venue.ParkingPoints.Count}   Circle: {circle}   Stand spots: {venue.StandSpots.Count}   Lines: {lines}"),
                StateHint(venue));
            state.Enabled = false;
            sub.Add(state);

            sub.Add(new NativeSeparatorItem(""));
        }

        private string StateHint(MeetingVenue venue)
        {
            if (venue.ParkingPoints.Count == 0)
                return T("Aucune place : pose-en via Modifier au sol (outil Parking).",
                         "No spot yet: place some with Edit on the ground (Parking tool).");
            if (!venue.CircleCenter.HasValue && venue.Lines.Count == 0)
                return T("Sans cercle ni ligne, ils restent devant leur moto. Ajoute-les via Modifier au sol.",
                         "No circle or line: riders stay by their bikes. Add them with Edit on the ground.");
            if (venue.Lines.Count == 0)
                return T("Aucune ligne : pas de freestyle. Ajoute-en via Modifier au sol.",
                         "No line: no freestyle. Add some with Edit on the ground.");
            if (!venue.CircleCenter.HasValue)
                return T("Pas de cercle : à pied, ils restent devant leur moto.",
                         "No circle: on foot, riders stay by their bikes.");
            return T("Lieu complet.", "Venue complete.");
        }

        private static string Plural(int count, string one, string many)
        {
            return $"{count} {(count > 1 ? many : one)}";
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
            venue.Anchor = Game.Player.Character.Position;
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
