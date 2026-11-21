using System.Windows.Forms;
using LemonUI.Scaleform;

namespace StreetRiders
{
    /// <summary>
    /// Affichage du mode édition, à la manière des éditeurs de GTA : en haut à gauche,
    /// l'outil actif et une consigne courte ; en bas à droite, la barre de touches native.
    /// La barre n'est reconstruite que lorsque l'outil ou l'état de la ligne change.
    /// </summary>
    public partial class StreetRidersScript
    {
        private InstructionalButtons _editButtons;
        private string _editButtonsSignature = "";

        private void DrawEditHud()
        {
            GTA.UI.Screen.ShowHelpTextThisFrame(EditHelpText(), false);

            string signature = $"{_editTool}|{_editLineStart.HasValue}|{_editVenue.Lines.Count > 0}|{_langEn}";
            if (_editButtons == null || signature != _editButtonsSignature)
            {
                _editButtons = BuildEditButtons();
                _editButtonsSignature = signature;
            }
            _editButtons.Visible = true;
            _editButtons.Process();
        }

        /// <summary>Les boutons s'affichent de droite à gauche : l'action principale (Poser) en premier.</summary>
        private InstructionalButtons BuildEditButtons()
        {
            var buttons = new InstructionalButtons();
            buttons.Add(new InstructionalButton(T("Poser", "Place"), KeyGlyph(Keys.Return)));
            buttons.Add(new InstructionalButton(T("Outil suivant", "Next tool"), KeyGlyph(Keys.E)));
            if (_editTool == MeetingEditTool.Line && _editVenue.Lines.Count > 0)
                buttons.Add(new InstructionalButton(T("Route / hors route", "Road / off-road"), KeyGlyph(Keys.R)));
            buttons.Add(new InstructionalButton(_editLineStart.HasValue ? T("Annuler la ligne", "Cancel line") : T("Supprimer", "Delete"),
                KeyGlyph(Keys.Back)));
            buttons.Add(new InstructionalButton(T("Terminer", "Finish"), KeyGlyph(_meetingMenuKey)));
            return buttons;
        }

        /// <summary>
        /// Touche clavier affichée par le scaleform INSTRUCTIONAL_BUTTONS : préfixe "t_" + texte.
        /// Les touches de l'éditeur sont des touches Windows, pas des contrôles GTA.
        /// </summary>
        private string KeyGlyph(Keys key)
        {
            switch (key)
            {
                case Keys.Return: return T("t_Entrée", "t_Enter");
                case Keys.Back: return T("t_Retour", "t_Backspace");
                default: return "t_" + key;
            }
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
            return $"~y~{tool}~s~ ({(int)_editTool + 1}/3) · {_editVenue.Name}~n~{instruction}";
        }
    }
}
