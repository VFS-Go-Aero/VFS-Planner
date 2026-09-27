using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using ZedGraph;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Three separate plots of OBSTACLE_DISTANCE_3D x, y, and z (meters) against time since boot.
    /// compid 255 on the subscription means any component on the current system.
    /// </summary>
    public class ObstacleDistanceGraph : UserControl
    {
        const int CompIdAny = 255;
        const double WindowSeconds = 30;

        readonly System.Windows.Forms.Label _header;
        readonly ZedGraphControl _graphX;
        readonly ZedGraphControl _graphY;
        readonly ZedGraphControl _graphZ;
        readonly RollingPointPairList _x = new RollingPointPairList(2000);
        readonly RollingPointPairList _y = new RollingPointPairList(2000);
        readonly RollingPointPairList _z = new RollingPointPairList(2000);
        readonly ConcurrentQueue<MAVLink.mavlink_obstacle_distance_3d_t> _pending =
            new ConcurrentQueue<MAVLink.mavlink_obstacle_distance_3d_t>();
        readonly Timer _timer;

        int _subscription;
        byte _subscribedSysid;
        bool _hasSample;
        MAVLink.mavlink_obstacle_distance_3d_t _last;

        public ObstacleDistanceGraph()
        {
            BackColor = Color.FromArgb(32, 32, 32);

            _header = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Text = "OBSTACLE_DISTANCE_3D — waiting for data"
            };

            _graphX = CreateGraph("X", _x, Color.OrangeRed);
            _graphY = CreateGraph("Y", _y, Color.LimeGreen);
            _graphZ = CreateGraph("Z", _z, Color.DeepSkyBlue);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = BackColor
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            layout.Controls.Add(_header, 0, 0);
            layout.Controls.Add(_graphX, 0, 1);
            layout.Controls.Add(_graphY, 0, 2);
            layout.Controls.Add(_graphZ, 0, 3);
            Controls.Add(layout);

            _timer = new Timer { Interval = 200 };
            _timer.Tick += Timer_Tick;
        }

        public void Start()
        {
            EnsureSubscribed();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void RefreshView()
        {
            ApplyScales();
        }

        ZedGraphControl CreateGraph(string name, RollingPointPairList points, Color color)
        {
            var graph = new ZedGraphControl
            {
                Dock = DockStyle.Fill,
                Name = "zgObstacle" + name
            };

            var pane = graph.GraphPane;
            pane.Title.Text = name;
            pane.Title.FontSpec.Size = 12;
            pane.Title.FontSpec.FontColor = Color.White;
            pane.XAxis.Title.Text = "Time since boot (s)";
            pane.YAxis.Title.Text = name + " (m)";
            pane.Legend.IsVisible = false;
            pane.XAxis.MajorGrid.IsVisible = true;
            pane.YAxis.MajorGrid.IsVisible = true;
            pane.YAxis.MajorGrid.IsZeroLine = true;
            pane.Chart.Fill = new Fill(Color.FromArgb(32, 32, 32));
            pane.Fill = new Fill(Color.FromArgb(32, 32, 32));
            pane.Margin.All = 4;

            foreach (var axis in new Axis[] { pane.XAxis, pane.YAxis })
            {
                axis.Color = Color.Gray;
                axis.Scale.FontSpec.FontColor = Color.White;
                axis.Title.FontSpec.FontColor = Color.White;
            }

            pane.AddCurve(name, points, color, SymbolType.None);
            graph.AxisChange();
            return graph;
        }

        void EnsureSubscribed()
        {
            if (MainV2.comPort == null)
                return;

            var sysid = (byte)MainV2.comPort.sysidcurrent;
            if (sysid == 0 || (_subscription != 0 && _subscribedSysid == sysid))
                return;

            if (_subscription != 0)
                MainV2.comPort.UnSubscribeToPacketType(_subscription);

            _subscription = MainV2.comPort.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.OBSTACLE_DISTANCE_3D,
                OnObstacle,
                sysid,
                CompIdAny);
            _subscribedSysid = sysid;
        }

        bool OnObstacle(MAVLink.MAVLinkMessage message)
        {
            try
            {
                var sample = (MAVLink.mavlink_obstacle_distance_3d_t)message.data;
                _pending.Enqueue(sample);
            }
            catch
            {
            }

            return true;
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            EnsureSubscribed();

            var added = false;
            while (_pending.TryDequeue(out var sample))
            {
                var t = sample.time_boot_ms / 1000.0;
                _x.Add(t, sample.x);
                _y.Add(t, sample.y);
                _z.Add(t, sample.z);
                _last = sample;
                _hasSample = true;
                added = true;
            }

            if (!added || !Visible)
                return;

            ApplyScales();
        }

        void ApplyScales()
        {
            if (!_hasSample)
                return;

            var tLast = _last.time_boot_ms / 1000.0;
            var tMin = Math.Max(0, tLast - WindowSeconds);
            ScaleGraph(_graphX, tMin, tLast);
            ScaleGraph(_graphY, tMin, tLast);
            ScaleGraph(_graphZ, tMin, tLast);
            _header.Text = string.Format(
                "OBSTACLE_DISTANCE_3D  id {0}    X {1:0.00} m    Y {2:0.00} m    Z {3:0.00} m",
                _last.obstacle_id, _last.x, _last.y, _last.z);
        }

        static void ScaleGraph(ZedGraphControl graph, double tMin, double tMax)
        {
            var pane = graph.GraphPane;
            pane.XAxis.Scale.Min = tMin;
            pane.XAxis.Scale.Max = tMax;
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;
            pane.YAxis.Scale.MinAuto = true;
            pane.YAxis.Scale.MaxAuto = true;
            graph.AxisChange();
            pane.XAxis.Scale.Min = tMin;
            pane.XAxis.Scale.Max = tMax;
            graph.Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                ApplyScales();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                if (_subscription != 0 && MainV2.comPort != null)
                {
                    MainV2.comPort.UnSubscribeToPacketType(_subscription);
                    _subscription = 0;
                }
            }

            base.Dispose(disposing);
        }
    }
}
