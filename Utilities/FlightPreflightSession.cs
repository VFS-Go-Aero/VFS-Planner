using System;

namespace MissionPlanner.Utilities
{
    public enum FlightOperationStage
    {
        Unselected = 0,
        Stage2 = 2,
        Stage3 = 3
    }

    /// <summary>
    /// Session state for staged flight operations (Stage 2 vs Stage 3) and PIC/GCO mission confirmation.
    /// </summary>
    public static class FlightPreflightSession
    {
        public static FlightOperationStage Stage { get; set; } = FlightOperationStage.Unselected;

        /// <summary>
        /// Bench / indoor: no GPS sky view — Stage 2 checklist ignores automatic telemetry blockers.
        /// Autopilot PreArm (GPS, PRX, EKF) may still fail until you arm on the bench or use SITL.
        /// </summary>
        public static bool IndoorBenchMode
        {
            get => Settings.Instance.GetBoolean("preflight_indoor_bench", false);
            set => Settings.Instance["preflight_indoor_bench"] = value.ToString();
        }

        /// <summary>Display name of the aircraft selected for this session.</summary>
        public static string SelectedVehicle { get; set; }

        public static DateTime? MissionConfirmedUtc { get; private set; }

        public static bool PicConfirmed { get; private set; }

        public static bool GcoConfirmed { get; private set; }

        public static string ConfirmedMissionFingerprint { get; private set; }

        /// <summary>
        /// Debug/session override: PIC affirmed “all green” — GCS arm blockers are skipped.
        /// Cleared when stage changes or affirm is cleared.
        /// </summary>
        public static bool ArmAffirmedOverride { get; private set; }

        public static DateTime? ArmAffirmedUtc { get; private set; }

        /// <summary>Operator pressed Ready to arm after the stage checklist passed (GCS gate).</summary>
        public static bool GcsReadyToArm { get; private set; }

        public static DateTime? GcsReadyToArmUtc { get; private set; }

        public static void SetStage(FlightOperationStage stage)
        {
            Stage = stage;
            ClearMissionConfirmation();
            ClearArmAffirm();
            ClearGcsReadyToArm();
        }

        public static void RecordMissionConfirmation(string missionFingerprint, bool pic, bool gco)
        {
            PicConfirmed = pic;
            GcoConfirmed = gco;
            ConfirmedMissionFingerprint = missionFingerprint ?? "";
            MissionConfirmedUtc = DateTime.UtcNow;
        }

        public static void ClearMissionConfirmation()
        {
            PicConfirmed = false;
            GcoConfirmed = false;
            ConfirmedMissionFingerprint = null;
            MissionConfirmedUtc = null;
        }

        public static void AffirmAllGreenForArm()
        {
            ArmAffirmedOverride = true;
            ArmAffirmedUtc = DateTime.UtcNow;
            var fp = PreflightTelemetryChecks.BuildMissionFingerprint();
            RecordMissionConfirmation(fp, true, true);
        }

        public static void ClearArmAffirm()
        {
            ArmAffirmedOverride = false;
            ArmAffirmedUtc = null;
        }

        public static void SetGcsReadyToArm(bool ready)
        {
            GcsReadyToArm = ready;
            GcsReadyToArmUtc = ready ? DateTime.UtcNow : (DateTime?)null;
        }

        public static void ClearGcsReadyToArm()
        {
            GcsReadyToArm = false;
            GcsReadyToArmUtc = null;
        }

        /// <summary>Record PIC/GCO confirmation from a completed checklist (Stage 2 manual sign-offs).</summary>
        public static void ConfirmMissionFromChecklist(bool pic, bool gco)
        {
            var fp = PreflightTelemetryChecks.BuildMissionFingerprint();
            RecordMissionConfirmation(fp, pic, gco);
        }

        public static bool IsMissionConfirmationCurrent(string currentFingerprint, bool requirePic, bool requireGco)
        {
            if (ArmAffirmedOverride)
                return true;

            if (string.IsNullOrEmpty(ConfirmedMissionFingerprint) || MissionConfirmedUtc == null)
                return false;

            if (!string.Equals(ConfirmedMissionFingerprint, currentFingerprint ?? "", StringComparison.Ordinal))
                return false;

            if (requirePic && !PicConfirmed)
                return false;

            if (requireGco && !GcoConfirmed)
                return false;

            return true;
        }
    }
}
