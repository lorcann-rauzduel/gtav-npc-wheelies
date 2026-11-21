using GTA;

namespace StreetRiders
{
    /// <summary>
    /// Commandes manette du mode édition (le clavier passe par HandleMeetingEditorKey).
    /// Lues via les contrôles GTA, ce qui garde les icônes de la barre cohérentes :
    ///   A poser · B supprimer / annuler · RB / LB outil suivant / précédent · X route / hors route · croix bas terminer.
    /// Y (Enter / VehicleExit) reste libre pour monter et descendre de la moto.
    /// </summary>
    public partial class StreetRidersScript
    {
        private const Control PadPlace = Control.FrontendAccept;
        private const Control PadDelete = Control.FrontendCancel;
        private const Control PadNextTool = Control.FrontendRb;
        private const Control PadPrevTool = Control.FrontendLb;
        private const Control PadSurface = Control.FrontendX;
        private const Control PadFinish = Control.FrontendDown;

        /// <summary>Actions de jeu partageant ces boutons à la manette, bloquées pendant l'édition.</summary>
        private static readonly Control[] PadBlockedGameControls =
        {
            Control.Sprint, Control.Jump, Control.Cover, Control.MeleeAttackLight,
            Control.MeleeAttackAlternate, Control.Reload, Control.Detonate, Control.CharacterWheel,
            Control.SelectWeapon, Control.VehicleHandbrake, Control.VehicleDuck, Control.VehicleHeadlight,
            Control.VehicleCinCam, Control.VehicleRadioWheel
        };

        private static bool IsUsingGamepad
        {
            get { return Game.LastInputMethod == InputMethod.GamePad; }
        }

        private void PollMeetingEditorPad()
        {
            // Au clavier, ces contrôles frontend doublonneraient les touches déjà traitées.
            if (!IsUsingGamepad) return;
            if (_meetingMenu != null && _meetingMenu.Visible) return;

            foreach (Control control in PadBlockedGameControls)
                Game.DisableControlThisFrame(control);

            // Le A qui a validé « Nouveau lieu » / « Modifier » dans le menu ne doit pas poser de point.
            if (Game.FrameCount == _editStartFrame) return;

            if (Game.IsControlJustPressed(PadPlace)) PlaceWithActiveTool();
            else if (Game.IsControlJustPressed(PadDelete)) DeleteNearestEditPoint();
            else if (Game.IsControlJustPressed(PadNextTool)) CycleEditTool(1);
            else if (Game.IsControlJustPressed(PadPrevTool)) CycleEditTool(-1);
            else if (Game.IsControlJustPressed(PadSurface)) ToggleNearestLineSurface();
            else if (Game.IsControlJustPressed(PadFinish)) EndMeetingEdit("pad finish");
        }
    }
}
