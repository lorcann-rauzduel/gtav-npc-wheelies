using System;
using System.IO;
using GTA;

namespace StreetRiders
{
    /// <summary>
    /// Journal technique des meetings (scripts\StreetRiders\meetings.log). Seuls les
    /// transitions, décisions, refus et actions d'édition y sont écrits, jamais un état
    /// stable répété à chaque tick. Le fichier est remis à zéro à chaque chargement du script.
    /// </summary>
    public partial class StreetRidersScript
    {
        private static readonly string MEETINGS_LOG_PATH = "scripts\\StreetRiders\\meetings.log";
        private bool _meetingLogReset;

        private void LogMeeting(string message)
        {
            try
            {
                string file = Path.GetFullPath(MEETINGS_LOG_PATH);
                string line = $"{DateTime.Now:HH:mm:ss.fff} [t={Game.GameTime}] {message}{Environment.NewLine}";
                if (!_meetingLogReset)
                {
                    _meetingLogReset = true;
                    File.WriteAllText(file, line);
                    return;
                }
                File.AppendAllText(file, line);
            }
            catch (Exception)
            {
                // Le journal ne doit jamais casser le tick du script (fichier verrouillé, droits...).
            }
        }
    }
}
