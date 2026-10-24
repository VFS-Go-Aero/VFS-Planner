using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using ZedGraph;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Each OBSTACLE_DISTANCE_3D message adds one point: Y = magnitude (m), X = time (s).
    /// X axis scrolls a 5 s window; Y autoscales from the visible data.
    /// </summary>
    public class ObstacleDistanceGraph : UserControl
    {
        const int CompIdAny = 255;
        const double TimeWindowSeconds = 5;
        const int MaxPoints = 5000;

        readonly ZedGraphControl _graph;
        readonly PointPairList _trace = new PointPairList();
        LineItem _traceCurve;
        readonly ConcurrentQueue<(double magnitude, double timeSec)> _pending =
            new ConcurrentQueue<(double, double)>();
        readonly Timer _timer;

        int _subscription;
        byte _subscribedSysid;
        DateTime _plotOriginUtc = DateTime.UtcNow;
        double _lastPlotTimeSec;

        public ObstacleDistanceGraph()
        {
            BackColor = Color.FromArgb(32, 32, 32);

            _graph = CreateGraph();
            _graph.Dock = DockStyle.Fill;
            _graph.MinimumSize = new Size(120, 80);
            Controls.Add(_graph);

            _timer = new Timer { Interval = 50 };
            _timer.Tick += Timer_Tick;

            HandleCreated += (s, e) => SyncGraphLayout(_graph);
            Resize += (s, e) => SyncGraphLayout(_graph);
            VisibleChanged += (s, e) =>
            {
                if (Visible)
                    SyncGraphLayout(_graph);
            };
        }

        public void Start()
        {
            _plotOriginUtc = DateTime.UtcNow;
            _lastPlotTimeSec = 0;
            _trace.Clear();
            var cs = MainV2.comPort?.MAV?.cs;
            if (cs != null)
                cs.obstacle_magnitude = 0;
            while (_pending.TryDequeue(out _))
            {
            }

            EnsureSubscribed();
            ThemeManager.ApplyThemeTo(_graph);
            StyleGraph(_graph);
            ApplyScales();
            _timer.Start();
            SyncGraphLayout(_graph);
            if (IsHandleCreated)
                BeginInvoke(new Action(() =>
                {
                    SyncGraphLayout(_graph);
                    ApplyScales();
                }));
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void RefreshView()
        {
            ApplyScales();
            _graph?.Refresh();
        }

        ZedGraphControl CreateGraph()
        {
            var graph = new ZedGraphControl
            {
                Name = "zgObstacleDistance",
                BackColor = Color.FromArgb(48, 48, 52)
            };

            var pane = graph.GraphPane;
            pane.Title.IsVisible = false;
            pane.XAxis.Title.Text = "Time (s)";
            pane.YAxis.Title.Text = "Magnitude (m)";
            pane.Legend.IsVisible = false;
            pane.Margin.All = 14;
            pane.XAxis.MajorGrid.IsVisible = true;
            pane.YAxis.MajorGrid.IsVisible = true;
            pane.YAxis.MajorGrid.IsZeroLine = true;
            pane.Chart.Border.IsVisible = true;
            pane.Chart.Border.Color = Color.Gainsboro;
            pane.Chart.Border.Width = 1f;
            pane.Chart.Fill = new Fill(Color.FromArgb(56, 56, 60));
            pane.Fill = new Fill(Color.FromArgb(40, 40, 44));

            StyleAxis(pane.XAxis);
            StyleAxis(pane.YAxis);
            pane.XAxis.Scale.Min = 0;
            pane.XAxis.Scale.Max = TimeWindowSeconds;
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;
            SetAxisAutoscale(pane.YAxis);

            graph.IsEnableHZoom = true;
            graph.IsEnableVZoom = true;
            graph.IsEnableWheelZoom = true;
            graph.IsEnableHPan = true;
            graph.IsEnableVPan = true;
            graph.IsShowContextMenu = true;
            graph.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ApplyScales();
            };

            _traceCurve = pane.AddCurve(string.Empty, _trace, Color.FromArgb(120, 220, 255), SymbolType.Circle);
            _traceCurve.Line.Width = 2f;
            _traceCurve.Line.IsVisible = true;
            _traceCurve.Line.IsSmooth = false;
            _traceCurve.Label.IsVisible = false;
            _traceCurve.Symbol.Size = 4f;
            _traceCurve.Symbol.Fill = new Fill(Color.FromArgb(120, 220, 255));
            _traceCurve.Symbol.Border.Color = Color.White;

            graph.AxisChange();
            return graph;
        }

        void StyleGraph(ZedGraphControl graph)
        {
            if (graph?.GraphPane == null)
                return;

            var pane = graph.GraphPane;
            pane.Chart.Fill = new Fill(Color.FromArgb(56, 56, 60));
            pane.Fill = new Fill(Color.FromArgb(40, 40, 44));
            pane.Legend.IsVisible = false;
            StyleAxis(pane.XAxis);
            StyleAxis(pane.YAxis);
            ApplyScales();
            SyncGraphLayout(graph);
        }

        static void StyleAxis(Axis axis)
        {
            axis.Color = Color.Gainsboro;
            axis.MajorGrid.Color = Color.FromArgb(90, 90, 95);
            axis.MajorTic.Color = Color.Gainsboro;
            axis.MinorTic.Color = Color.Gainsboro;
            axis.Scale.FontSpec.FontColor = Color.White;
            axis.Title.FontSpec.FontColor = Color.White;
        }

        static void SetAxisAutoscale(Axis axis)
        {
            axis.Scale.MinAuto = true;
            axis.Scale.MaxAuto = true;
        }

        static void SyncGraphLayout(ZedGraphControl graph)
        {
            if (graph == null || graph.IsDisposed || graph.Width < 10 || graph.Height < 10)
                return;

            using (var g = graph.CreateGraphics())
                graph.MasterPane.ReSize(g, new RectangleF(0, 0, graph.Width, graph.Height));

            graph.AxisChange();
            graph.Invalidate(true);
        }

        void ApplyScales()
        {
            if (_graph?.GraphPane == null)
                return;

            var pane = _graph.GraphPane;
            ApplyLiveTimeWindow(pane);
            SetAxisAutoscale(pane.YAxis);
            pane.AxisChange();
            _graph.Invalidate(true);
        }

        void ApplyLiveTimeWindow(GraphPane pane)
        {
            var tLast = Math.Max(_lastPlotTimeSec, TimeWindowSeconds * 0.1);
            var tMin = Math.Max(0, tLast - TimeWindowSeconds);
            pane.XAxis.Scale.Min = tMin;
            pane.XAxis.Scale.Max = Math.Max(tMin + TimeWindowSeconds, tLast);
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            EnsureSubscribed();

            var changed = false;
            while (_pending.TryDequeue(out var item))
            {
                if (AppendPoint(item.magnitude, item.timeSec))
                    changed = true;
            }

            if (!Visible)
                return;

            if (changed || _trace.Count > 0)
            {
                TrimTraceToWindow();
                ApplyScales();
                _graph?.Refresh();
            }
        }

        bool AppendPoint(double mag, double timeSec)
        {
            if (double.IsNaN(mag) || double.IsInfinity(mag))
                return false;

            if (_trace.Count > 0)
            {
                var last = _trace[_trace.Count - 1];
                if (Math.Abs(last.X - timeSec) < 1e-9 && Math.Abs(last.Y - mag) < 1e-9)
                    return false;
            }

            _trace.Add(timeSec, mag);
            _lastPlotTimeSec = timeSec;

            while (_trace.Count > MaxPoints)
                _trace.RemoveAt(0);

            return true;
        }

        void TrimTraceToWindow()
        {
            if (_trace.Count == 0)
                return;

            var tMin = Math.Max(0, _lastPlotTimeSec - TimeWindowSeconds - 1);
            while (_trace.Count > 1 && _trace[0].X < tMin)
                _trace.RemoveAt(0);
        }

        /// <summary>
        /// MAVLink wire layout (ardupilotmega.xml), not the C# struct field order.
        /// </summary>
        static bool TryDecodeObstacleDistance3d(MAVLink.MAVLinkMessage message,
            out MAVLink.mavlink_obstacle_distance_3d_t sample)
        {
            sample = default;
            if (message?.buffer == null || message.payloadlength < 28)
                return false;

            var offset = message.ismavlink2 ? MAVLink.MAVLINK_NUM_HEADER_BYTES : 6;
            var buf = message.buffer;
            if (buf.Length < offset + 28)
                return false;

            sample = MAVLink.mavlink_obstacle_distance_3d_t.PopulateXMLOrder(
                BitConverter.ToUInt32(buf, offset),
                buf[offset + 4],
                buf[offset + 5],
                BitConverter.ToUInt16(buf, offset + 6),
                BitConverter.ToSingle(buf, offset + 8),
                BitConverter.ToSingle(buf, offset + 12),
                BitConverter.ToSingle(buf, offset + 16),
                BitConverter.ToSingle(buf, offset + 20),
                BitConverter.ToSingle(buf, offset + 24));
            return true;
        }

        static double MagnitudeMeters(MAVLink.mavlink_obstacle_distance_3d_t sample)
        {
            var x = sample.x;
            var y = sample.y;
            var z = sample.z;

            if (!IsFinite(x) && !IsFinite(y) && !IsFinite(z))
                return double.NaN;

            if (!IsFinite(x)) x = 0;
            if (!IsFinite(y)) y = 0;
            if (!IsFinite(z)) z = 0;

            return Math.Sqrt(x * x + y * y + z * z);
        }

        static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
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
            if (!TryDecodeObstacleDistance3d(message, out var sample))
                return true;

            var mag = MagnitudeMeters(sample);
            if (double.IsNaN(mag) || double.IsInfinity(mag))
                return true;

            var cs = MainV2.comPort?.MAV?.cs;
            if (cs != null)
                cs.obstacle_magnitude = mag;

            var t = (DateTime.UtcNow - _plotOriginUtc).TotalSeconds;
            _pending.Enqueue((mag, t));
            return true;
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
