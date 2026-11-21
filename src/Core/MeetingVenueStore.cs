using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Persistance des lieux de meetings dans meetings.ini, sections [Venue_NOM] :
    ///   Group            = groupe de groups.ini
    ///   Anchor           = x,y,z (centre du lieu posé à la création)
    ///   ParkingPoint1..N = x,y,z,cap
    ///   CircleCenter     = x,y,z (optionnel)
    ///   StandSpot1..N    = x,y,z,cap (un motard à pied par spot)
    ///   Line1..N         = ax,ay,az,bx,by,bz,road|offroad
    /// Les anciens CirclePointN (schéma WIP) sont migrés en CircleCenter = leur moyenne.
    /// </summary>
    public partial class StreetRidersScript
    {
        private const int LegacyCirclePointCount = 8;

        private void LoadMeetingVenues(string meetingsPath)
        {
            _meetingVenues = new List<MeetingVenue>();
            if (string.IsNullOrEmpty(meetingsPath) || !File.Exists(meetingsPath)) return;

            foreach (string section in GetIniSections(meetingsPath))
            {
                if (!section.StartsWith("Venue_", StringComparison.OrdinalIgnoreCase)) continue;
                var venue = new MeetingVenue
                {
                    Name = section.Substring(6),
                    GroupName = GetIniString(meetingsPath, section, "Group", "")
                };

                for (int i = 1; i <= MeetingVenue.MaxParkingPoints; i++)
                {
                    if (!ParseVenuePosition(GetIniString(meetingsPath, section, "ParkingPoint" + i, ""),
                        out Vector3 pp, out float ph))
                        break;
                    venue.ParkingPoints.Add(new MeetingPoint { Position = pp, Heading = ph });
                }

                if (ParseVenuePosition(GetIniString(meetingsPath, section, "CircleCenter", ""), out Vector3 center, out _))
                    venue.CircleCenter = center;
                else
                    venue.CircleCenter = ReadLegacyCircleCenter(meetingsPath, section);

                for (int i = 1; i <= MeetingVenue.MaxStandSpots; i++)
                {
                    if (!ParseVenuePosition(GetIniString(meetingsPath, section, "StandSpot" + i, ""),
                        out Vector3 sp, out float sh))
                        break;
                    venue.StandSpots.Add(new MeetingPoint { Position = sp, Heading = sh });
                }

                for (int i = 1; i <= MeetingVenue.MaxLines; i++)
                {
                    MeetingLine line;
                    if (!ParseMeetingLine(GetIniString(meetingsPath, section, "Line" + i, ""), out line))
                        break;
                    venue.Lines.Add(line);
                }

                int ridersMin = GetIniInt(meetingsPath, section, "RidersMin", 0);
                int ridersMax = GetIniInt(meetingsPath, section, "RidersMax", 0);
                if (ridersMin > 0 && ridersMax >= ridersMin)
                {
                    venue.RidersMin = ridersMin;
                    venue.RidersMax = ridersMax;
                }
                venue.ExtraRiders = ParseExtraRiders(GetIniString(meetingsPath, section, "ExtraRiders", ""));

                if (ParseVenuePosition(GetIniString(meetingsPath, section, "Anchor", ""), out Vector3 anchor, out _))
                {
                    venue.Anchor = anchor;
                }
                else if (venue.ParkingPoints.Count > 0)
                {
                    // Lieux créés avant l'ancre : la première place servait de centre.
                    venue.Anchor = venue.ParkingPoints[0].Position;
                }
                else
                {
                    LogMeeting($"Load: {section} ignored (no anchor and no parking spot)");
                    continue;
                }
                _meetingVenues.Add(venue);
                LogMeeting($"Load: {venue.Name} group={venue.GroupName} parking={venue.ParkingPoints.Count} circle={(venue.CircleCenter.HasValue ? "yes" : "no")} standspots={venue.StandSpots.Count} lines={venue.Lines.Count}");
            }
        }

        private Vector3? ReadLegacyCircleCenter(string meetingsPath, string section)
        {
            Vector3 sum = Vector3.Zero;
            int count = 0;
            for (int i = 1; i <= LegacyCirclePointCount; i++)
            {
                if (!ParseVenuePosition(GetIniString(meetingsPath, section, "CirclePoint" + i, ""), out Vector3 cp, out _))
                    break;
                sum += cp;
                count++;
            }
            if (count == 0) return null;
            LogMeeting($"Load: {section} migrated {count} legacy circle points to a single center");
            return sum / count;
        }

        private void SaveVenueToIni(MeetingVenue venue)
        {
            string file = Path.GetFullPath(MEETINGS_INI_PATH);
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string section = VenueSection(venue);
            WriteIniValue(file, section, "Group", venue.GroupName);
            WriteIniValue(file, section, "Anchor", SerializeVector(venue.Anchor));
            DeleteIniValue(file, section, "Position");

            for (int i = 1; i <= MeetingVenue.MaxParkingPoints; i++)
            {
                if (i <= venue.ParkingPoints.Count)
                {
                    MeetingPoint p = venue.ParkingPoints[i - 1];
                    WriteIniValue(file, section, "ParkingPoint" + i, SerializeVenuePosition(p.Position, p.Heading));
                }
                else
                {
                    DeleteIniValue(file, section, "ParkingPoint" + i);
                }
            }

            if (venue.CircleCenter.HasValue)
                WriteIniValue(file, section, "CircleCenter", SerializeVector(venue.CircleCenter.Value));
            else
                DeleteIniValue(file, section, "CircleCenter");
            for (int i = 1; i <= LegacyCirclePointCount; i++)
                DeleteIniValue(file, section, "CirclePoint" + i);

            for (int i = 1; i <= MeetingVenue.MaxStandSpots; i++)
            {
                if (i <= venue.StandSpots.Count)
                {
                    MeetingPoint s = venue.StandSpots[i - 1];
                    WriteIniValue(file, section, "StandSpot" + i, SerializeVenuePosition(s.Position, s.Heading));
                }
                else
                {
                    DeleteIniValue(file, section, "StandSpot" + i);
                }
            }

            for (int i = 1; i <= MeetingVenue.MaxLines; i++)
            {
                if (i <= venue.Lines.Count)
                    WriteIniValue(file, section, "Line" + i, SerializeMeetingLine(venue.Lines[i - 1]));
                else
                    DeleteIniValue(file, section, "Line" + i);
            }

            if (venue.RidersMin.HasValue && venue.RidersMax.HasValue)
            {
                WriteIniValue(file, section, "RidersMin", venue.RidersMin.Value.ToString(CultureInfo.InvariantCulture));
                WriteIniValue(file, section, "RidersMax", venue.RidersMax.Value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                DeleteIniValue(file, section, "RidersMin");
                DeleteIniValue(file, section, "RidersMax");
            }

            if (venue.ExtraRiders.HasValue)
                WriteIniValue(file, section, "ExtraRiders", SerializeExtraRiders(venue.ExtraRiders.Value));
            else
                DeleteIniValue(file, section, "ExtraRiders");
        }

        private static MeetingExtraRiders? ParseExtraRiders(string value)
        {
            string v = (value ?? "").Trim();
            if (v.Equals("limit", StringComparison.OrdinalIgnoreCase)) return MeetingExtraRiders.Limit;
            if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return MeetingExtraRiders.AutoSpots;
            return null;
        }

        private static string SerializeExtraRiders(MeetingExtraRiders mode)
        {
            return mode == MeetingExtraRiders.AutoSpots ? "auto" : "limit";
        }

        private void RemoveVenueFromIni(MeetingVenue venue)
        {
            DeleteIniSection(Path.GetFullPath(MEETINGS_INI_PATH), VenueSection(venue));
        }

        private static string VenueSection(MeetingVenue venue)
        {
            return "Venue_" + SanitizeVenueName(venue.Name);
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

        // =========================================================================
        //  Serialization
        // =========================================================================

        private static string FormatFloat(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string SerializeVector(Vector3 v)
        {
            return FormatFloat(v.X) + "," + FormatFloat(v.Y) + "," + FormatFloat(v.Z);
        }

        private static string SerializeVenuePosition(Vector3 position, float heading)
        {
            return SerializeVector(position) + "," + FormatFloat(heading);
        }

        private static string SerializeMeetingLine(MeetingLine line)
        {
            return SerializeVector(line.A) + "," + SerializeVector(line.B) + "," + (line.OffRoad ? "offroad" : "road");
        }

        private static bool TryParseFloats(string[] parts, int start, int count, float[] output)
        {
            for (int i = 0; i < count; i++)
            {
                if (!float.TryParse(parts[start + i].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out output[i]))
                    return false;
            }
            return true;
        }

        private static bool ParseVenuePosition(string value, out Vector3 position, out float heading)
        {
            position = Vector3.Zero;
            heading = 0f;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split(',');
            if (parts.Length < 3) return false;
            var xyz = new float[3];
            if (!TryParseFloats(parts, 0, 3, xyz)) return false;
            position = new Vector3(xyz[0], xyz[1], xyz[2]);
            if (parts.Length >= 4)
                float.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out heading);
            return true;
        }

        private static bool ParseMeetingLine(string value, out MeetingLine line)
        {
            line = null;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split(',');
            if (parts.Length < 6) return false;
            var f = new float[6];
            if (!TryParseFloats(parts, 0, 6, f)) return false;
            line = new MeetingLine
            {
                A = new Vector3(f[0], f[1], f[2]),
                B = new Vector3(f[3], f[4], f[5]),
                OffRoad = parts.Length >= 7 && parts[6].Trim().Equals("offroad", StringComparison.OrdinalIgnoreCase)
            };
            return true;
        }
    }
}
