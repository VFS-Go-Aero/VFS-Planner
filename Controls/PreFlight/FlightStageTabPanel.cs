using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.PreFlight
{
    /// <summary>
    /// Flight Data tab: Stage 2 / Stage 3 selection and debug arm affirm.
    /// </summary>
    public sealed class FlightStageTabPanel : UserControl
    {
        readonly Label _lblStatus;
        readonly Button _btnStage2;
        readonly Button _btnStage3;
        readonly Button _btnAffirm;
        readonly CheckBox _chkIndoorBench;

        public event Action<FlightOperationStage> StageSelected;
        public event Action AffirmArmRequested;

        public FlightStageTabPanel()
        {
            BackColor = Color.FromArgb(32, 32, 32);
            Padding = new Padding(16);

            var title = new Label
            {
                Text = "Select vehicle",
                Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 16)
            };

            _btnStage2 = MakeStageButton("Stage 2", 56, FlightOperationStage.Stage2);
            _btnStage3 = MakeStageButton("Stage 3", 108, FlightOperationStage.Stage3);

            _chkIndoorBench = new CheckBox
            {
                Text = "Indoor / bench (Stage 2: manual checklist only; FC PreArm may still fail)",
                Location = new Point(16, 168),
                Size = new Size(420, 40),
                ForeColor = Color.FromArgb(200, 200, 210),
                AutoSize = false,
                Checked = FlightPreflightSession.IndoorBenchMode
            };
            _chkIndoorBench.CheckedChanged += (s, e) =>
            {
                FlightPreflightSession.IndoorBenchMode = _chkIndoorBench.Checked;
                FlightPreflightSession.ClearGcsReadyToArm();
                StageSelected?.Invoke(FlightPreflightSession.Stage);
                RefreshStatus();
            };

            _btnAffirm = new Button
            {
                Text = "All is green — affirm for arm",
                Location = new Point(16, 212),
                Size = new Size(360, 44),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 120, 60),
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold)
            };
            _btnAffirm.FlatAppearance.BorderColor = Color.FromArgb(70, 160, 90);
            _btnAffirm.Click += (s, e) => AffirmArmRequested?.Invoke();

            _lblStatus = new Label
            {
                Location = new Point(16, 268),
                Size = new Size(420, 60),
                ForeColor = Color.Gold,
                Text = "No stage selected"
            };

            var hint = new Label
            {
                Location = new Point(16, 328),
                Size = new Size(420, 56),
                ForeColor = Color.FromArgb(160, 160, 170),
                Text = "Underground / no GPS: use indoor bench for GCS workflow.\r\nAffirm skips GCS blockers; autopilot may still refuse arm (PRX, GPS, EKF)."
            };

            Controls.Add(title);
            Controls.Add(_btnStage2);
            Controls.Add(_btnStage3);
            Controls.Add(_chkIndoorBench);
            Controls.Add(_btnAffirm);
            Controls.Add(_lblStatus);
            Controls.Add(hint);

            RefreshStatus();
        }

        Button MakeStageButton(string caption, int top, FlightOperationStage stage)
        {
            var btn = new Button
            {
                Text = caption,
                Location = new Point(16, top),
                Size = new Size(360, 44),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(55, 55, 60),
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
                Tag = stage
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 100);
            btn.Click += (s, e) =>
            {
                FlightPreflightSession.SetStage(stage);
                Settings.Instance["preflight_last_stage"] = ((int)stage).ToString();
                StageSelected?.Invoke(stage);
                RefreshStatus();
            };
            return btn;
        }

        public void RefreshStatus()
        {
            var stage = FlightPreflightSession.Stage;
            var stageText = stage == FlightOperationStage.Unselected ? "none" : stage.ToString();
            var affirm = FlightPreflightSession.ArmAffirmedOverride
                ? "AFFIRMED for arm (GCS blockers bypassed)"
                : "not affirmed";
            _lblStatus.ForeColor = FlightPreflightSession.ArmAffirmedOverride
                ? Color.LightGreen
                : Color.Gold;
            var bench = FlightPreflightSession.IndoorBenchMode ? "\r\nIndoor bench: ON" : "";
            _lblStatus.Text = "Stage: " + stageText + "\r\nArm: " + affirm + bench;
            if (_chkIndoorBench != null)
                _chkIndoorBench.Checked = FlightPreflightSession.IndoorBenchMode;

            HighlightStage(stage);
        }

        void HighlightStage(FlightOperationStage stage)
        {
            StyleStageButton(_btnStage2, stage == FlightOperationStage.Stage2);
            StyleStageButton(_btnStage3, stage == FlightOperationStage.Stage3);
        }

        static void StyleStageButton(Button btn, bool selected)
        {
            btn.BackColor = selected
                ? Color.FromArgb(70, 90, 140)
                : Color.FromArgb(55, 55, 60);
        }
    }
}
