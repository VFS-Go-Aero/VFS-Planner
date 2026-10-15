using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using static MAVLink;

namespace MissionPlanner.Controls.PreFlight
{
    public class PreflightSummaryPanel : UserControl
    {
        private readonly Label _lblReady;
        private readonly TableLayoutPanel _table;
        private readonly Button _btnStage;
        private readonly Button _btnMissionConfirm;
        private readonly Button _btnAffirm;
        private DateTime _lastPrearmRequest = DateTime.MinValue;

        public event Action FlightStageRequested;
        public event Action MissionConfirmRequested;
        public event Action AffirmArmRequested;

        public PreflightSummaryPanel()
        {
            BackColor = Color.FromArgb(32, 32, 32);
            Padding = new Padding(8);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = BackColor
            };

            _btnStage = new Button
            {
                Text = "Flight stage…",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(55, 55, 60)
            };
            _btnStage.Click += (s, e) => FlightStageRequested?.Invoke();

            _btnMissionConfirm = new Button
            {
                Text = "Mission confirm…",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(55, 55, 60)
            };
            _btnMissionConfirm.Click += (s, e) => MissionConfirmRequested?.Invoke();

            _btnAffirm = new Button
            {
                Text = "All green — affirm arm",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 120, 60)
            };
            _btnAffirm.Click += (s, e) => AffirmArmRequested?.Invoke();

            toolbar.Controls.Add(_btnStage);
            toolbar.Controls.Add(_btnMissionConfirm);
            toolbar.Controls.Add(_btnAffirm);

            _lblReady = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = Color.Gold,
                Text = "Preflight summary — verifying…"
            };

            _table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = false,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Controls.Add(_table);
            Controls.Add(_lblReady);
            Controls.Add(toolbar);
        }

        public void RefreshSummary(CheckListControl checklist)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => RefreshSummary(checklist)));
                return;
            }

            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = 0;

            var cs = MainV2.comPort?.MAV?.cs;
            if (cs == null || !cs.connected)
            {
                AddRow("Link", false, "Not connected");
                SetReady(false, "Connect vehicle to run preflight checks.");
                _table.ResumeLayout();
                return;
            }

            RequestPrearmChecksIfNeeded(cs.prearmstatus);

            var stage = FlightPreflightSession.Stage;
            var req = stage != FlightOperationStage.Unselected
                ? FlightPreflightProfiles.GetRequirements(stage)
                : null;

            AddRow("Flight stage", stage != FlightOperationStage.Unselected,
                stage == FlightOperationStage.Unselected
                    ? "Not selected — use Flight Stage tab"
                    : FlightPreflightProfiles.GetVehicleRequirementsSummary(stage));

            if (FlightPreflightSession.IndoorBenchMode)
            {
                AddRow("Indoor bench", true,
                    "Stage 2 manual checklist only — expect red PreArm/GPS/PRX on the autopilot until outdoors or SITL");
            }

            AddRow("Ready to arm", FlightPreflightSession.GcsReadyToArm || FlightPreflightSession.ArmAffirmedOverride,
                FlightPreflightSession.GcsReadyToArm
                    ? "Checklist affirmed — use Ready to arm on Mission Checklist"
                    : FlightPreflightSession.ArmAffirmedOverride
                        ? "All green affirmed — GCS arm blockers bypassed"
                        : "Press Ready to arm after checklist (Mission Checklist tab)");

            AddRow("Arm affirm", FlightPreflightSession.ArmAffirmedOverride,
                FlightPreflightSession.ArmAffirmedOverride
                    ? "Debug affirm — all GCS blockers bypassed"
                    : "Optional (summary toolbar)");

            AddRow("Telemetry", true, "Connected · GCS link " + cs.linkqualitygcs + "%");

            var gpsOk = cs.gpsstatus >= 3 && cs.satcount >= 6 && cs.gpshdop > 0 && cs.gpshdop <= 2.5f;
            AddRow("GPS", gpsOk,
                $"Fix {cs.gpsstatus:0} · {cs.satcount:0} sats · HDOP {cs.gpshdop:0.0}");

            AddRow("PreArm", cs.prearmstatus, cs.prearmstatus ? "Autopilot ready to arm" : "PreArm failing — see status text");

            var battOk = cs.battery_voltage >= 1;
            AddRow("Battery", battOk, cs.battery_voltage.ToString("0.0") + " V");

            if (req != null)
            {
                var sensors = PreflightTelemetryChecks.EvaluateSensors(cs, req);
                AddRow("Sensors", sensors.Ok, sensors.Summary);

                var radio = PreflightTelemetryChecks.EvaluateRadio(cs, req);
                AddRow("Radio", radio.Ok, radio.Summary);

                var est = MissionFlightEstimate.Compute();
                AddRow("Est. flight", est.WaypointCount > 1, est.Summary);

                var homePt = cs.Base != PointLatLngAlt.Zero ? cs.Base : (PointLatLngAlt)cs.Location;
                var adsb = PreflightTelemetryChecks.EvaluateAdsbTraffic(homePt, req, MainV2.instance?.EnableADSB == true);
                AddRow("ADS-B area", adsb.Ok, adsb.Summary);

                var fp = PreflightTelemetryChecks.BuildMissionFingerprint();
                var confirmed = FlightPreflightSession.IsMissionConfirmationCurrent(fp, req.RequirePicSignOff,
                    req.RequireGcoSignOff);
                AddRow("PIC/GCO confirm", !req.RequireMissionConfirm || confirmed,
                    confirmed ? "Mission confirmed for current plan" : "Use Mission confirm… before arming");
            }

            var checklistOk = checklist == null || checklist.ArmingChecksPassed;
            var pending = checklist?.CheckListItems?.Count(i => i.IsArmingBlocker && !i.checkCond(i)) ?? 0;
            AddRow("Checklist", checklistOk,
                checklistOk ? "All arming blockers complete" : pending + " arming item(s) incomplete");

            var wpCount = GCSViews.FlightPlanner.instance?.pointlist?.Count ?? 0;
            AddRow("Mission", wpCount > 0, wpCount > 0 ? wpCount + " waypoint(s) in planner" : "No mission loaded");

            var fenceOn = false;
            if (MainV2.comPort.MAV.param.ContainsKey("FENCE_ENABLE"))
                fenceOn = MainV2.comPort.MAV.param["FENCE_ENABLE"].Value >= 1;
            AddRow("Geofence", fenceOn || wpCount == 0,
                fenceOn ? "FENCE_ENABLE active" : (wpCount > 0 ? "FENCE_ENABLE off" : "N/A (no mission)"));

            var tfrCount = GCSViews.FlightData.tfrpolygons?.Polygons?.Count ?? 0;
            var conflicts = PreflightArmGuard.EvaluateTfrConflicts();
            var tfrOk = conflicts.Count == 0;
            AddRow("TFR / NOTAM", tfrOk,
                tfrOk
                    ? (tfrCount > 0 ? tfrCount + " TFR(s) in map view · none on home/mission" : "No TFR polygons loaded in view")
                    : conflicts.Count + " conflict(s) with home/mission");

            if (!string.IsNullOrEmpty(GCSViews.FlightData.LastRestrictionBriefingSummary))
                AddRow("Airspace", true, GCSViews.FlightData.LastRestrictionBriefingSummary);

            string ignore;
            var armOk = PreflightArmGuard.CanArm(checklist, out ignore);
            SetReady(armOk, armOk ? "Ready to arm (all ArmGuard checks passed)" : PreflightArmGuard.LastBlockReason);

            _table.ResumeLayout(true);
        }

        private void RequestPrearmChecksIfNeeded(bool prearmOk)
        {
            if (prearmOk || !MainV2.comPort.BaseStream.IsOpen)
                return;

            if ((DateTime.UtcNow - _lastPrearmRequest).TotalSeconds < 5)
                return;

            _lastPrearmRequest = DateTime.UtcNow;
            try
            {
                MainV2.comPort.doCommand(
                    (byte)MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent,
                    MAV_CMD.RUN_PREARM_CHECKS,
                    0, 0, 0, 0, 0, 0, 0,
                    false);
            }
            catch
            {
            }
        }

        private void SetReady(bool ok, string message)
        {
            _lblReady.ForeColor = ok ? Color.LightGreen : Color.OrangeRed;
            _lblReady.Text = message;
        }

        private void AddRow(string title, bool ok, string detail)
        {
            var row = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblTitle = new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                Margin = new Padding(0, 2, 6, 2)
            };

            var lblDetail = new Label
            {
                Text = detail,
                AutoSize = true,
                ForeColor = ok ? Color.PaleGreen : Color.Salmon,
                Margin = new Padding(0, 2, 0, 2)
            };

            _table.Controls.Add(lblTitle, 0, row);
            _table.Controls.Add(lblDetail, 1, row);
        }
    }
}
