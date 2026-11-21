using GTA;
using LemonUI.Scaleform;

namespace StreetRiders
{
    /// <summary>
    /// Affichage du mode édition, à la manière des éditeurs de GTA : en haut à gauche,
    /// l'outil actif et une consigne courte ; en bas à droite, la barre de touches native.
    /// Si la barre ne se charge pas, les touches passent en texte sous la consigne.
    /// </summary>
    public partial class StreetRidersScript
    {
        private const int EditButtonsLoadTimeoutMs = 1500;

        private InstructionalButtons _editButtons;
        private string _editButtonsSignature = "";
        private int _editButtonsRequestedAt;
        private bool _editButtonsLoadLogged;
        private bool _editButtonsFallback;

        private void DrawEditHud()
        {
            if (_editButtons == null)
            {
                _editButtons = new InstructionalButtons { Visible = true };
                _editButtonsSignature = "";
                _editButtonsRequestedAt = Game.GameTime;
                _editButtonsLoadLogged = false;
                _editButtonsFallback = false;
            }

            string signature = $"{_editTool}|{_editLineStart.HasValue}|{_editVenue.Lines.Count > 0}|{_langEn}";
            if (signature != _editButtonsSignature)
            {
                FillEditButtons(_editButtons);
                _editButtonsSignature = signature;
            }

            UpdateEditButtonsLoadState();
            GTA.UI.Screen.ShowHelpTextThisFrame(EditHelpText(), false);
            if (!_editButtonsFallback) _editButtons.Process();
        }

        /// <summary>
        /// Bascule une seule fois par session d'édition : barre chargée, ou secours texte
        /// si le scaleform n'est toujours pas prêt après le délai.
        /// </summary>
        private void UpdateEditButtonsLoadState()
        {
            if (_editButtonsLoadLogged) return;
            if (_editButtons.IsLoaded)
            {
                _editButtonsLoadLogged = true;
                LogMeeting("Edit HUD: key bar loaded");
            }
            else if (Game.GameTime - _editButtonsRequestedAt > EditButtonsLoadTimeoutMs)
            {
                _editButtonsLoadLogged = true;
                _editButtonsFallback = true;
                LogMeeting($"Edit HUD: key bar not loaded after {EditButtonsLoadTimeoutMs} ms, keys shown in help text");
            }
        }

        private void ReleaseEditHud()
        {
            if (_editButtons == null) return;
            _editButtons.Dispose();
            _editButtons = null;
        }

        /// <summary>
        /// Les boutons s'affichent de droite à gauche : l'action principale (Poser) en premier.
        /// Les icônes viennent des contrôles GTA dont la touche clavier par défaut est celle
        /// écoutée par l'éditeur ; F8 (touche du menu, configurable) n'a pas de contrôle GTA.
        /// </summary>
        private void FillEditButtons(InstructionalButtons buttons)
        {
            buttons.Clear();
            buttons.Add(new InstructionalButton(T("Poser", "Place"), Control.FrontendAccept));
            buttons.Add(new InstructionalButton(T("Outil suivant", "Next tool"), Control.Context));
            if (ShowsSurfaceToggle())
                buttons.Add(new InstructionalButton(T("Route / hors route", "Road / off-road"), Control.Reload));
            buttons.Add(new InstructionalButton(DeleteLabel(), Control.FrontendCancel));
            buttons.Add(new InstructionalButton(T("Terminer", "Finish"), "t_" + _meetingMenuKey));
        }

        private bool ShowsSurfaceToggle()
        {
            return _editTool == MeetingEditTool.Line && _editVenue.Lines.Count > 0;
        }

        private string DeleteLabel()
        {
            return _editLineStart.HasValue ? T("Annuler la ligne", "Cancel line") : T("Supprimer", "Delete");
        }

        /// <summary>Secours texte : mêmes touches que la barre, sur une ligne.</summary>
        private string EditKeysText()
        {
            string keys = T("Entrée", "Enter") + ": " + T("Poser", "Place")
                + " · E: " + T("Outil suivant", "Next tool");
            if (ShowsSurfaceToggle())
                keys += " · R: " + T("Route / hors route", "Road / off-road");
            keys += " · " + T("Retour", "Backspace") + ": " + DeleteLabel()
                + " · " + _meetingMenuKey + ": " + T("Terminer", "Finish");
            return keys;
        }

        private string EditHelpText()
        {
            string tool;
            string instruction;
            switch (_editTool)
            {
                case MeetingEditTool.Parking:
                    tool = T("PARKING", "PARKING");
                    instruction = T("Gare-toi où la moto doit aller.", "Park where the bike should go.");
                    break;
                case MeetingEditTool.Circle:
                    tool = T("CERCLE", "CIRCLE");
                    instruction = T("Place-toi au centre du cercle à pied.", "Stand at the center of the on-foot circle.");
                    break;
                default:
                    tool = T("LIGNE", "LINE");
                    instruction = _editLineStart.HasValue
                        ? T("Va à la fin de la ligne.", "Go to the end of the line.")
                        : T("Va au début de la ligne.", "Go to the start of the line.");
                    break;
            }
            string text = $"~y~{tool}~s~ ({(int)_editTool + 1}/3) · {_editVenue.Name}~n~{instruction}";
            return _editButtonsFallback ? text + "~n~" + EditKeysText() : text;
        }
    }
}
