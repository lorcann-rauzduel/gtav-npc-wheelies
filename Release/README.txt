===============================================================================
  Street Riders v1.1.0 - Realistic NPC Wheelies
  for Grand Theft Auto V
===============================================================================

REQUIREMENTS:
  - GTA V (latest version)
  - ScriptHookV            -> http://www.dev-c.com/gtav/scripthookv/
  - ScriptHookVDotNet v3   -> https://github.com/scripthookvdotnet/scripthookvdotnet/releases

INSTALLATION:
  1. Install ScriptHookV and ScriptHookVDotNet3 in your GTA V root folder.
  2. Copy the "scripts" folder from this archive into your GTA V root folder.
  3. Launch the game. You should see: "Street Riders v1.1.0: Loaded!"

  Your folder structure should look like this:

    Grand Theft Auto V/
      ScriptHookV.dll
      ScriptHookVDotNet3.dll
      scripts/
        StreetRiders.dll
        StreetRiders.ini

CONFIGURATION:
  Edit StreetRiders.ini in any text editor. Press F10 in-game to hot-reload.
  See the full configuration guide at:
  https://github.com/lorcann-rauzduel/street-riders

KNOWN ISSUES:
  - Weapons: Equipping riders with weapons is optional and may cause them to
    enter combat, interrupting their riding tasks. Use with caution.
  - Bodyguard mode: Present in the INI but still under development.
    Do NOT enable BodyguardEnabled — it will be released in a future update.
  - Remount: Riders usually recover after falling, but edge cases exist.
    Increase RemountTaskCooldown if riders get stuck.

===============================================================================
