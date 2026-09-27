using System;
using System.Collections.Generic;
using System.Linq;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Controls.PreFlight;
using MissionPlanner.GCSViews;

namespace MissionPlanner.Utilities
{
    public static class PreflightArmGuard
    {
        public static string LastBlockReason { get; private set; }

        /// <summary>
        /// Text for an arm-failed dialog. Prefers live status text, then the GCS block reason,
        /// then recent PreArm lines already stored on the vehicle.
        /// </summary>
        public static string ArmFailureDetail(string liveStatusText)
        {
            if (!string.IsNullOrWhiteSpace(liveStatusText))
                return liveStatusText.Trim();

            if (!string.IsNullOrWhiteSpace(LastBlockReason))
                return LastBlockReason;

            var messages = MainV2.comPort?.MAV?.cs?.messages;
            if (messages != null)
            {
                var prearm = messages
                    .Where(m => !string.IsNullOrWhiteSpace(m.message) &&
                                m.message.StartsWith("PreArm", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.message.Trim())
                    .Distinct()
                    .Reverse()
                    .Take(6)
                    .Reverse()
                    .ToList();
                if (prearm.Count > 0)
                    return string.Join(Environment.NewLine, prearm);
            }

            return "No autopilot status text was received.";
        }

        public static bool CanArm(CheckListControl checklist, out string reason)
        {
            reason = null;
            LastBlockReason = null;

            if (MainV2.comPort?.MAV?.cs == null || !MainV2.comPort.MAV.cs.connected)
            {
                reason = "Telemetry link is not connected.";
                LastBlockReason = reason;
                return false;
            }

            // Debug / PIC affirm: skip remaining GCS arm blockers for this session.
            if (FlightPreflightSession.ArmAffirmedOverride)
            {
                LastBlockReason = null;
                return true;
            }

            if (checklist != null && !checklist.ArmingChecksPassed)
            {
                reason = "Mission checklist incomplete (including PIC/GCO sign-off).";
                LastBlockReason = reason;
                return false;
            }

            var cs = MainV2.comPort.MAV.cs;

            if (Settings.Instance.GetBoolean("armguard_require_prearm", true) && !cs.prearmstatus)
            {
                reason = "Autopilot PreArm checks are not passing. Open PreArm Status or resolve FC messages.";
                LastBlockReason = reason;
                return false;
            }

            if (Settings.Instance.GetBoolean("armguard_block_tfr", true))
            {
                var conflicts = EvaluateTfrConflicts();
                if (conflicts.Count > 0)
                {
                    var first = conflicts[0];
                    reason = "Active TFR intersects home or mission: " + first.Label;
                    LastBlockReason = reason;
                    return false;
                }
            }

            if (Settings.Instance.GetBoolean("armguard_require_flight_stage", true) &&
                FlightPreflightSession.Stage == FlightOperationStage.Unselected)
            {
                reason = "Select Stage 2 or Stage 3 (Flight Stage tab).";
                LastBlockReason = reason;
                return false;
            }

            if (FlightPreflightSession.Stage != FlightOperationStage.Unselected)
            {
                var req = FlightPreflightProfiles.GetRequirements(FlightPreflightSession.Stage);

                if (Settings.Instance.GetBoolean("armguard_require_sensors", true))
                {
                    var sensors = PreflightTelemetryChecks.EvaluateSensors(cs, req);
                    if (!sensors.Ok)
                    {
                        reason = "Sensor check failed: " + sensors.Summary;
                        LastBlockReason = reason;
                        return false;
                    }
                }

                if (Settings.Instance.GetBoolean("armguard_require_radio", true))
                {
                    var radio = PreflightTelemetryChecks.EvaluateRadio(cs, req);
                    if (!radio.Ok)
                    {
                        reason = "Radio / link check failed: " + radio.Summary;
                        LastBlockReason = reason;
                        return false;
                    }
                }

                if (Settings.Instance.GetBoolean("armguard_require_mission_confirm", true) && req.RequireMissionConfirm)
                {
                    var fp = PreflightTelemetryChecks.BuildMissionFingerprint();
                    if (!FlightPreflightSession.IsMissionConfirmationCurrent(fp, req.RequirePicSignOff,
                            req.RequireGcoSignOff))
                    {
                        reason = "Mission summary not confirmed by PIC/GCO for the current plan.";
                        LastBlockReason = reason;
                        return false;
                    }
                }

                if (Settings.Instance.GetBoolean("armguard_block_adsb_traffic", false) &&
                    req.RequireAdsbTrafficReview)
                {
                    var homePt = cs.Base != PointLatLngAlt.Zero ? cs.Base : (PointLatLngAlt)cs.Location;
                    var adsb = PreflightTelemetryChecks.EvaluateAdsbTraffic(homePt, req,
                        MainV2.instance?.EnableADSB == true);
                    if (!adsb.Ok)
                    {
                        reason = "ADS-B traffic review required: " + adsb.Summary;
                        LastBlockReason = reason;
                        return false;
                    }
                }
            }

            LastBlockReason = null;
            return true;
        }

        public static List<RestrictionGeometry.TfrConflict> EvaluateTfrConflicts()
        {
            var polygons = GetActiveTfrPolygons();
            if (polygons.Count == 0)
                return new List<RestrictionGeometry.TfrConflict>();

            var cs = MainV2.comPort.MAV.cs;
            var home = cs.Base != PointLatLngAlt.Zero ? cs.Base : cs.Location;
            var mission = FlightPlanner.instance?.pointlist;
            var points = RestrictionGeometry.CollectPreflightCheckPoints(home, cs.Location, mission);
            return RestrictionGeometry.FindTfrConflicts(points, polygons);
        }

        private static List<GMapPolygon> GetActiveTfrPolygons()
        {
            var overlay = FlightData.tfrpolygons;
            if (overlay == null || !overlay.IsVisibile || overlay.Polygons.Count == 0)
                return new List<GMapPolygon>();

            return overlay.Polygons.ToList();
        }
    }
}
