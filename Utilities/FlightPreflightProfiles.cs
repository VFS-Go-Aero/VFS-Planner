using System;
using System.IO;
using MissionPlanner.GCSViews;

namespace MissionPlanner.Utilities
{
    public sealed class FlightStageRequirements
    {
        public int MinLinkQualityPercent { get; set; } = 50;
        public int MinGpsSats { get; set; } = 8;
        public float MaxHdop { get; set; } = 2.5f;
        public bool RequireMissionConfirm { get; set; } = true;
        public bool RequirePicSignOff { get; set; } = true;
        public bool RequireGcoSignOff { get; set; } = true;
        public bool RequireAdsbTrafficReview { get; set; }
        public double AdsbReviewRadiusNm { get; set; } = 5;
        public int AdsbWarnTrafficCount { get; set; } = 3;
    }

    public static class FlightPreflightProfiles
    {
        public static string GetVehicleProfileKey()
        {
            var selected = FlightVehicleCatalog.GetSelectedVehicle();
            if (!string.IsNullOrWhiteSpace(selected))
                return selected.Trim();

            var mav = MainV2.comPort?.MAV;
            if (mav == null)
                return "default";

            var frame = "unknown";
            if (mav.param.ContainsKey("FRAME_CLASS"))
                frame = mav.param["FRAME_CLASS"].Value.ToString("0");
            else if (mav.param.ContainsKey("AIRFRAME"))
                frame = mav.param["AIRFRAME"].Value.ToString("0");

            return mav.cs.vehicleClass + "_" + frame + "_sys" + mav.sysid;
        }

        public static FlightStageRequirements GetRequirements(FlightOperationStage stage)
        {
            var key = "preflight_stage" + (int)stage + "_" + GetVehicleProfileKey();
            if (Settings.Instance.ContainsKey(key + "_minlink"))
            {
                return new FlightStageRequirements
                {
                    MinLinkQualityPercent = Settings.Instance.GetInt32(key + "_minlink", 50),
                    MinGpsSats = Settings.Instance.GetInt32(key + "_minsats", 8),
                    MaxHdop = (float)Settings.Instance.GetDouble(key + "_maxhdop", 2.5),
                    RequireMissionConfirm = Settings.Instance.GetBoolean(key + "_missionconfirm", true),
                    RequirePicSignOff = Settings.Instance.GetBoolean(key + "_pic", true),
                    RequireGcoSignOff = Settings.Instance.GetBoolean(key + "_gco", true),
                    RequireAdsbTrafficReview = Settings.Instance.GetBoolean(key + "_adsb", false),
                    AdsbReviewRadiusNm = Settings.Instance.GetDouble(key + "_adsbnm", 5),
                    AdsbWarnTrafficCount = Settings.Instance.GetInt32(key + "_adsbcount", 3)
                };
            }

            switch (stage)
            {
                case FlightOperationStage.Stage2:
                    return new FlightStageRequirements
                    {
                        MinLinkQualityPercent = 40,
                        MinGpsSats = 6,
                        MaxHdop = 3f,
                        RequireMissionConfirm = true,
                        RequirePicSignOff = true,
                        RequireGcoSignOff = false,
                        RequireAdsbTrafficReview = false,
                        AdsbWarnTrafficCount = 5
                    };
                case FlightOperationStage.Stage3:
                    return new FlightStageRequirements
                    {
                        MinLinkQualityPercent = 50,
                        MinGpsSats = 8,
                        MaxHdop = 2.5f,
                        RequireMissionConfirm = true,
                        RequirePicSignOff = true,
                        RequireGcoSignOff = true,
                        RequireAdsbTrafficReview = true,
                        AdsbReviewRadiusNm = 5,
                        AdsbWarnTrafficCount = 1
                    };
                default:
                    return new FlightStageRequirements { RequireMissionConfirm = false };
            }
        }

        public static string GetChecklistDefaultPath(FlightOperationStage stage, string vehicleKey)
        {
            var overrideKey = "preflight_checklist_" + (int)stage + "_" + vehicleKey;
            if (Settings.Instance.ContainsKey(overrideKey))
            {
                var path = Settings.Instance[overrideKey]?.ToString();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }

            return PreflightChecklistPaths.ResolveDefaultForStage(stage);
        }

        public static string GetVehicleRequirementsSummary(FlightOperationStage stage)
        {
            var req = GetRequirements(stage);
            return stage + " · " + GetVehicleProfileKey()
                   + " · link ≥" + req.MinLinkQualityPercent + "% · sats ≥" + req.MinGpsSats
                   + (req.RequireGcoSignOff ? " · GCO required" : "")
                   + (req.RequireAdsbTrafficReview ? " · ADS-B review" : "");
        }
    }
}
