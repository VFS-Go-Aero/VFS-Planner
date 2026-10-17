using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using ZedGraph;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// OBSTACLE_DISTANCE_3D: one trace per obstacle_id of |position| (m) vs time.
    /// compid 255 on the subscription means any component on the current system.
    /// </summary>
    public class ObstacleDistanceGraph : UserControl
    {
        const int CompIdAny = 255;
        const double DefaultWindowSeconds = 5;
        const double YPaddingRatio = 0.12;
        const double MinYPadding = 0.05;
        const int MaxTrackedObstacles = 24;
        const int PointsPerObstacle = 2000;

        static readonly Color[] TrackColors =
        {
            Color.White,
            Color.FromArgb(120, 220, 255),
            Color.FromArgb(255, 200, 120),
            Color.FromArgb(180, 255, 140),
            Color.FromArgb(255, 140, 200),
            Color.FromArgb(200, 180, 255),
            Color.FromArgb(255, 240, 120),
            Color.FromArgb(120, 255, 220)
        };

        sealed class ObstacleTrack
        {
            public ushort Id;
            public RollingPointPairList Points = new RollingPointPairList(PointsPerObstacle);
            public Color Color;
            public LineItem Curve;
            public DateTime LastSeenUtc;
        }

        readonly ZedGraphControl _graph;
        readonly Dictionary<ushort, ObstacleTrack> _tracks = new Dictionary<ushort, ObstacleTrack>();
        readonly ConcurrentQueue<(MAVLink.mavlink_obstacle_distance_3d_t sample, double t)> _pending =
            new ConcurrentQueue<(MAVLink.mavlink_obstacle_distance_3d_t, double)>();
        readonly Timer _timer;

        int _subscription;
        byte _subscribedSysid;
        bool _hasSample;
        double _lastPlotTimeSec;
        double _windowSeconds = DefaultWindowSeconds;
        DateTime _plotOriginUtc = DateTime.UtcNow;
        bool _followLive = true;
        bool _manualYScale;
        int _nextColorIndex;

        public ObstacleDistanceGraph()
        {
            BackColor = Color.FromArgb(32, 32, 32);

            _graph = CreateGraph();
            _graph.Dock = DockStyle.Fill;
            _graph.MinimumSize = new Size(120, 80);
            Controls.Add(_graph);

            _timer = new Timer { Interval = 100 };
            _timer.Tick += Timer_Tick;

            HandleCreated += (s, e) => LayoutGraphNow();
            Resize += (s, e) => LayoutGraphNow();
            VisibleChanged += (s, e) =>
            {
                if (Visible)
                    LayoutGraphNow();
            };
        }

        void LayoutGraphNow()
        {
            SyncGraphLayout(_graph);
            if (!_hasSample)
                PaintEmptyChart();
        }

        void PaintEmptyChart()
        {
            if (_graph == null || _graph.IsDisposed)
                return;

            var pane = _graph.GraphPane;
            pane.XAxis.Scale.Min = 0;
            pane.XAxis.Scale.Max = DefaultWindowSeconds;
            pane.YAxis.Scale.Min = 0;
            pane.YAxis.Scale.Max = 10;
            _graph.AxisChange();
            _graph.Refresh();
        }

        public void Start()
        {
            _plotOriginUtc = DateTime.UtcNow;
            EnsureSubscribed();
            ThemeManager.ApplyThemeTo(_graph);
            StyleGraph(_graph);
            ResetLiveView();
            _timer.Start();
            LayoutGraphNow();
            if (IsHandleCreated)
                BeginInvoke(new Action(LayoutGraphNow));
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void RefreshView()
        {
            LayoutGraphNow();
            ApplyScales();
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
            pane.YAxis.Title.Text = "Distance magnitude (m)";
            pane.Legend.IsVisible = false;
            pane.Margin.All = 12;
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
            pane.XAxis.Scale.Max = DefaultWindowSeconds;
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;
            pane.YAxis.Scale.Min = 0;
            pane.YAxis.Scale.Max = 10;
            pane.YAxis.Scale.MinAuto = false;
            pane.YAxis.Scale.MaxAuto = false;

            graph.IsEnableHZoom = true;
            graph.IsEnableVZoom = true;
            graph.IsEnableWheelZoom = true;
            graph.IsEnableHPan = true;
            graph.IsEnableVPan = true;
            graph.IsShowContextMenu = true;

            graph.ZoomEvent += Graph_ZoomOrPan;
            graph.ScrollDoneEvent += (s, scrollBar, oldState, newState) => Graph_ZoomOrPan(s, oldState, newState);
            graph.MouseDoubleClick += Graph_MouseDoubleClick;

            graph.AxisChange();
            return graph;
        }

        ObstacleTrack GetOrCreateTrack(ushort obstacleId)
        {
            if (_tracks.TryGetValue(obstacleId, out var existing))
                return existing;

            while (_tracks.Count >= MaxTrackedObstacles)
                RemoveOldestTrack();

            var color = TrackColors[_nextColorIndex % TrackColors.Length];
            _nextColorIndex++;

            var track = new ObstacleTrack
            {
                Id = obstacleId,
                Color = color
            };

            track.Curve = AddSeries(_graph.GraphPane, track.Points, color);
            _tracks[obstacleId] = track;
            return track;
        }

        void RemoveOldestTrack()
        {
            if (_tracks.Count == 0)
                return;

            var oldest = _tracks.Values.OrderBy(t => t.LastSeenUtc).First();
            RemoveTrack(oldest.Id);
        }

        void RemoveTrack(ushort id)
        {
            if (!_tracks.TryGetValue(id, out var track))
                return;

            if (track.Curve != null)
                _graph.GraphPane.CurveList.Remove(track.Curve);

            _tracks.Remove(id);
        }

        void Graph_ZoomOrPan(ZedGraphControl sender, ZoomState oldState, ZoomState newState)
        {
            _windowSeconds = GetVisibleDuration();
            _followLive = IsViewAlignedWithLive();
            _manualYScale = true;
        }

        void Graph_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            ResetLiveView();
        }

        void ResetLiveView()
        {
            _followLive = true;
            _manualYScale = false;
            _windowSeconds = DefaultWindowSeconds;
            ApplyScales();
        }

        double GetVisibleDuration()
        {
            if (_graph?.GraphPane == null)
                return _windowSeconds;

            var span = _graph.GraphPane.XAxis.Scale.Max - _graph.GraphPane.XAxis.Scale.Min;
            return span > 1e-6 ? span : _windowSeconds;
        }

        bool IsViewAlignedWithLive()
        {
            if (_graph?.GraphPane == null || !_hasSample)
                return true;

            var duration = GetVisibleDuration();
            var tMax = _graph.GraphPane.XAxis.Scale.Max;
            return Math.Abs(_lastPlotTimeSec - tMax) <= Math.Max(0.05, duration * 0.05);
        }

        static LineItem AddSeries(GraphPane pane, RollingPointPairList points, Color lineColor)
        {
            var curve = pane.AddCurve(string.Empty, points, lineColor, SymbolType.None);
            curve.Line.Width = 2.5f;
            curve.Label.IsVisible = false;
            curve.Symbol.Size = 0f;
            return curve;
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

        void StyleGraph(ZedGraphControl graph)
        {
            if (graph?.GraphPane == null)
                return;

            var pane = graph.GraphPane;
            pane.Chart.Fill = new Fill(Color.FromArgb(56, 56, 60));
            pane.Fill = new Fill(Color.FromArgb(40, 40, 44));
            pane.Chart.Border.IsVisible = true;
            pane.Chart.Border.Color = Color.Gainsboro;
            pane.Legend.IsVisible = false;
            StyleAxis(pane.XAxis);
            StyleAxis(pane.YAxis);

            foreach (var track in _tracks.Values)
            {
                if (track.Curve == null)
                    continue;
                track.Curve.Color = track.Color;
                track.Curve.Line.Color = track.Color;
                track.Curve.Line.Width = 2.5f;
            }

            SyncGraphLayout(graph);
        }

        static void SyncGraphLayout(ZedGraphControl graph)
        {
            if (graph == null || graph.IsDisposed || graph.Width < 10 || graph.Height < 10)
                return;

            using (var g = graph.CreateGraphics())
            {
                graph.MasterPane.ReSize(g, new RectangleF(0, 0, graph.Width, graph.Height));
            }

            graph.AxisChange();
            graph.Invalidate(true);
        }

        static bool TryParseObstacleDistance3d(MAVLink.MAVLinkMessage message,
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

        static double DistanceMagnitude(MAVLink.mavlink_obstacle_distance_3d_t sample)
        {
            if (!IsPlottable(sample.x) && !IsPlottable(sample.y) && !IsPlottable(sample.z))
                return double.NaN;

            var x = IsPlottable(sample.x) ? sample.x : 0f;
            var y = IsPlottable(sample.y) ? sample.y : 0f;
            var z = IsPlottable(sample.z) ? sample.z : 0f;
            return Math.Sqrt(x * x + y * y + z * z);
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
            if (!TryParseObstacleDistance3d(message, out var sample))
                return true;

            var t = (DateTime.UtcNow - _plotOriginUtc).TotalSeconds;
            _pending.Enqueue((sample, t));
            return true;
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            EnsureSubscribed();

            while (_pending.TryDequeue(out var item))
            {
                var mag = DistanceMagnitude(item.sample);
                if (double.IsNaN(mag) || double.IsInfinity(mag))
                    continue;

                var track = GetOrCreateTrack(item.sample.obstacle_id);
                track.Points.Add(item.t, mag);
                track.LastSeenUtc = DateTime.UtcNow;

                _lastPlotTimeSec = item.t;
                _hasSample = true;
            }

            if (!_hasSample || !Visible)
                return;

            ApplyScales();
        }

        static bool IsPlottable(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        void ApplyScales()
        {
            if (!_hasSample)
                return;

            if (_followLive)
            {
                var duration = GetVisibleDuration();
                var tLast = Math.Max(_lastPlotTimeSec, duration * 0.1);
                var tMin = Math.Max(0, tLast - duration);
                if (tLast <= tMin)
                    tLast = tMin + duration;

                ScaleGraph(tMin, tLast, autoscaleY: !_manualYScale);
            }
            else
            {
                _graph?.Refresh();
            }
        }

        void ScaleGraph(double tMin, double tMax, bool autoscaleY)
        {
            if (_graph == null || _graph.IsDisposed)
                return;

            SyncGraphLayout(_graph);

            var pane = _graph.GraphPane;
            pane.XAxis.Scale.Min = tMin;
            pane.XAxis.Scale.Max = tMax;
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;

            double yMin = 0;
            double yMax = 0;
            if (autoscaleY && TryGetVisibleYRange(tMin, tMax, out yMin, out yMax))
            {
                pane.YAxis.Scale.Min = yMin;
                pane.YAxis.Scale.Max = yMax;
                pane.YAxis.Scale.MinAuto = false;
                pane.YAxis.Scale.MaxAuto = false;
            }
            else if (autoscaleY)
            {
                pane.YAxis.Scale.Min = 0;
                pane.YAxis.Scale.Max = 10;
                pane.YAxis.Scale.MinAuto = false;
                pane.YAxis.Scale.MaxAuto = false;
            }

            _graph.AxisChange();
            pane.XAxis.Scale.Min = tMin;
            pane.XAxis.Scale.Max = tMax;
            if (autoscaleY && TryGetVisibleYRange(tMin, tMax, out yMin, out yMax))
            {
                pane.YAxis.Scale.Min = yMin;
                pane.YAxis.Scale.Max = yMax;
            }

            _graph.Refresh();
        }

        bool TryGetVisibleYRange(double tMin, double tMax, out double yMin, out double yMax)
        {
            yMin = double.MaxValue;
            yMax = double.MinValue;
            var any = false;

            foreach (var track in _tracks.Values)
                AccumulateVisibleY(track.Points, tMin, tMax, ref yMin, ref yMax, ref any);

            if (!any)
                return false;

            var span = yMax - yMin;
            var pad = span > 1e-6 ? Math.Max(MinYPadding, span * YPaddingRatio) : 0.25;
            yMin = Math.Max(0, yMin - pad);
            yMax += pad;
            return true;
        }

        static void AccumulateVisibleY(RollingPointPairList list, double tMin, double tMax,
            ref double yMin, ref double yMax, ref bool any)
        {
            if (list == null)
                return;

            for (var i = 0; i < list.Count; i++)
            {
                var pt = list[i];
                if (pt.X < tMin || pt.X > tMax || double.IsNaN(pt.Y) || double.IsInfinity(pt.Y))
                    continue;
                any = true;
                if (pt.Y < yMin)
                    yMin = pt.Y;
                if (pt.Y > yMax)
                    yMax = pt.Y;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                LayoutGraphNow();
                ApplyScales();
            }
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
