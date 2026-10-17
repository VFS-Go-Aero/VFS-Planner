using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MissionPlanner.Controls.PreFlight;
using static MAVLink;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Synthetic Messages-tab lines for GCS preflight (checklist / stage / Ready to arm).
    /// Does not modify telemetry <see cref="MissionPlanner.ArduPilot.CurrentState.messages"/>.
    /// </summary>
    public static class PreflightGcsMessages
    {
        public const string Prefix = "GCS Preflight: ";

        public static string GetFingerprint(CheckListControl checklist)
        {
            var sb = new StringBuilder();
            sb.Append((int)FlightPreflightSession.Stage);
            sb.Append('|');
            sb.Append(FlightPreflightSession.IndoorBenchMode);
            sb.Append('|');
            sb.Append(FlightPreflightSession.GcsReadyToArm);
            sb.Append('|');
            sb.Append(FlightPreflightSession.ArmAffirmedOverride);

            if (checklist == null)
                return sb.ToString();

            lock (checklist.CheckListItems)
            {
                foreach (var item in GetBlockingItems(checklist))
                {
                    if (item == null)
                        continue;
                    sb.Append('|');
                    sb.Append(item.Description);
                    sb.Append('=');
                    sb.Append(item.checkCond(item));
                }
            }

            return sb.ToString();
        }

        public static List<(DateTime time, string message, byte severity)> MergeForDisplay(
            List<(DateTime time, string message, byte severity)> fcMessages,
            CheckListControl checklist)
        {
            var result = fcMessages != null
                ? new List<(DateTime, string, byte)>(fcMessages)
                : new List<(DateTime, string, byte)>();

            foreach (var line in BuildLines(checklist))
                result.Add(line);

            return result;
        }

        static IEnumerable<(DateTime time, string message, byte severity)> BuildLines(CheckListControl checklist)
        {
            var now = DateTime.Now;
            var stage = FlightPreflightSession.Stage;

            if (stage == FlightOperationStage.Unselected)
            {
                yield return (now, Prefix + "Select Stage 2 or Stage 3 on the Flight Stage tab.",
                    (byte)MAV_SEVERITY.NOTICE);
                yield break;
            }

            if (checklist == null)
                yield break;

            var pending = GetBlockingItems(checklist).Where(i => !i.checkCond(i)).ToList();
            if (pending.Count > 0 ||
                (!FlightPreflightSession.GcsReadyToArm && !FlightPreflightSession.ArmAffirmedOverride))
            {
                yield return (now, Prefix + PreflightArmGuard.VehiclePreflightChecklistMessage,
                    (byte)MAV_SEVERITY.WARNING);
                yield break;
            }

            yield return (now, Prefix + "Checklist complete — GCS ready gate set for " + stage + ".",
                (byte)MAV_SEVERITY.INFO);

            if (FlightPreflightSession.IndoorBenchMode && stage == FlightOperationStage.Stage2)
            {
                yield return (now,
                    Prefix + "Indoor bench: automatic checklist rows are not GCS blockers (FC PreArm may still fail).",
                    (byte)MAV_SEVERITY.INFO);
            }
        }

        static IEnumerable<CheckListItem> GetBlockingItems(CheckListControl checklist)
        {
            IEnumerable<CheckListItem> blockers = checklist.CheckListItems.Where(i => i != null && i.IsArmingBlocker);
            if (FlightPreflightSession.IndoorBenchMode &&
                FlightPreflightSession.Stage == FlightOperationStage.Stage2)
            {
                blockers = blockers.Where(i => i.ConditionType == CheckListItem.Conditional.NONE);
            }

            return blockers;
        }
    }
}
