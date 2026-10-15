using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using ZedGraph;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// OBSTACLE_DISTANCE_3D x, y, and z (meters) overlaid on one plot against time.
    /// compid 255 on the subscription means any component on the current system.
    /// </summary>
    public class ObstacleDistanceGraph : UserControl
    {
        const int CompIdAny = 255;
        const double DefaultWindowSeconds = 5;
        const double YPaddingRatio = 0.12;
        const double MinYPadding = 0.05;
        const int ScrubMax = 1000;
        const int ScrubLiveSnapThreshold = 20;

        static readonly Color ColorX = Color.White;
        static readonly Color ColorY = Color.FromArgb(120, 220, 255);
        static readonly Color ColorZ = Color.FromArgb(255, 200, 120);

        readonly System.Windows.Forms.Label _header;
        readonly Panel _scrubPanel;
        readonly TrackBar _timeScrub;
        readonly Button _liveButton;
        readonly ZedGraphControl _graph;
        readonly RollingPointPairList _x = new RollingPointPairList(2000);
        readonly RollingPointPairList _y = new RollingPointPairList(2000);
        readonly RollingPointPairList _z = new RollingPointPairList(2000);
        readonly ConcurrentQueue<(MAVLink.mavlink_obstacle_distance_3d_t sample, double t)> _pending =
            new ConcurrentQueue<(MAVLink.mavlink_obstacle_distance_3d_t, double)>();
        readonly Timer _timer;

        int _subscription;
        byte _subscribedSysid;
        bool _hasSample;
        MAVLink.mavlink_obstacle_distance_3d_t _last;
        double _lastPlotTimeSec;
        double _windowSeconds = DefaultWindowSeconds;
        DateTime _plotOriginUtc = DateTime.UtcNow;
        bool _followLive = true;
        bool _manualYScale;
        bool _scrubDragging;
        bool _suppressScrubSync;

        public ObstacleDistanceGraph()
        {
            BackColor = Color.FromArgb(32, 32, 32);

            _header = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Text = "OBSTACLE_DISTANCE_3D — waiting for data"
            };

            _timeScrub = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = ScrubMax,
                TickStyle = TickStyle.None,
                LargeChange = 40,
                SmallChange = 8,
                BackColor = Color.FromArgb(32, 32, 32)
            };
            _timeScrub.Scroll += TimeScrub_Scroll;
            _timeScrub.MouseDown += (s, e) => _scrubDragging = true;
            _timeScrub.MouseUp += (s, e) =>
            {
                _scrubDragging = false;
                TrySnapToLiveFromScrub();
            };

            _liveButton = new Button
            {
                Text = "Live",
                Dock = DockStyle.Right,
                Width = 52,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(56, 56, 60)
            };
            _liveButton.FlatAppearance.BorderColor = Color.Gainsboro;
            _liveButton.Click += (s, e) => ResetLiveView();

            _scrubPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = BackColor,
                Padding = new Padding(4, 2, 4, 2)
            };
            _scrubPanel.Controls.Add(_timeScrub);
            _scrubPanel.Controls.Add(_liveButton);

            _graph = CreateGraph();

            Controls.Add(_graph);
            Controls.Add(_scrubPanel);
            Controls.Add(_header);

            _timer = new Timer { Interval = 100 };
            _timer.Tick += Timer_Tick;

            HandleCreated += (s, e) => SyncGraphLayout(_graph);
            Resize += (s, e) => SyncGraphLayout(_graph);
        }

        public void Start()
        {
            _plotOriginUtc = DateTime.UtcNow;
            ResetLiveView();
            EnsureSubscribed();
            ThemeManager.ApplyThemeTo(_graph);
            StyleGraph(_graph);
            _timer.Start();
            SyncGraphLayout(_graph);
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void RefreshView()
        {
            ApplyScales();
        }

        ZedGraphControl CreateGraph()
        {
            var graph = new ZedGraphControl
            {
                Dock = DockStyle.Fill,
                Name = "zgObstacleDistance",
                BackColor = Color.FromArgb(48, 48, 52)
            };

            var pane = graph.GraphPane;
            pane.Title.IsVisible = false;
            pane.XAxis.Title.Text = "Time (s)";
            pane.YAxis.Title.Text = "Distance (m)";
            pane.Legend.IsVisible = true;
            pane.Legend.Position = LegendPos.TopCenter;
            pane.Legend.FontSpec.Size = 9;
            pane.Legend.FontSpec.FontColor = Color.White;
            pane.Legend.Fill = new Fill(Color.FromArgb(56, 56, 60));
            pane.Legend.Border.IsVisible = false;
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
            pane.YAxis.Scale.Min = -1;
            pane.YAxis.Scale.Max = 1;
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

            AddSeries(pane, "X", _x, ColorX);
            AddSeries(pane, "Y", _y, ColorY);
            AddSeries(pane, "Z", _z, ColorZ);

            graph.AxisChange();
            return graph;
        }

        void Graph_ZoomOrPan(ZedGraphControl sender, ZoomState oldState, ZoomState newState)
        {
            _windowSeconds = GetVisibleDuration();
            _followLive = IsViewAlignedWithLive();
            _manualYScale = true;
            SyncScrubFromView();
            UpdateHeader();
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
            _suppressScrubSync = true;
            _timeScrub.Value = ScrubMax;
            _suppressScrubSync = false;
            ApplyScales();
        }

        void TimeScrub_Scroll(object sender, EventArgs e)
        {
            if (_suppressScrubSync || !_hasSample)
                return;

            _followLive = _timeScrub.Value >= ScrubMax - ScrubLiveSnapThreshold;
            if (_followLive)
            {
                TrySnapToLiveFromScrub();
                return;
            }

            var duration = GetVisibleDuration();
            var maxStart = GetMaxViewStart(duration);
            var tMin = ScrubToViewStart(_timeScrub.Value, maxStart);
            var tMax = tMin + duration;
            ScaleGraph(tMin, tMax, autoscaleY: !_manualYScale);
            UpdateHeader();
        }

        void TrySnapToLiveFromScrub()
        {
            if (_timeScrub.Value >= ScrubMax - ScrubLiveSnapThreshold)
                ResetLiveView();
        }

        void SyncScrubFromView()
        {
            if (_suppressScrubSync || _scrubDragging || !_hasSample || _graph?.GraphPane == null)
                return;

            _suppressScrubSync = true;
            if (_followLive)
            {
                _timeScrub.Value = ScrubMax;
            }
            else
            {
                var duration = GetVisibleDuration();
                var maxStart = GetMaxViewStart(duration);
                var tMin = _graph.GraphPane.XAxis.Scale.Min;
                _timeScrub.Value = ViewStartToScrub(tMin, maxStart);
            }

            _suppressScrubSync = false;
        }

        double GetVisibleDuration()
        {
            if (_graph?.GraphPane == null)
                return _windowSeconds;

            var span = _graph.GraphPane.XAxis.Scale.Max - _graph.GraphPane.XAxis.Scale.Min;
            return span > 1e-6 ? span : _windowSeconds;
        }

        static double GetMaxViewStart(double duration, double lastTime)
        {
            return Math.Max(0, lastTime - duration);
        }

        double GetMaxViewStart(double duration)
        {
            return GetMaxViewStart(duration, _lastPlotTimeSec);
        }

        static double ScrubToViewStart(int scrubValue, double maxStart)
        {
            if (maxStart <= 1e-9)
                return 0;
            var ratio = scrubValue / (double)ScrubMax;
            return ratio * maxStart;
        }

        static int ViewStartToScrub(double viewStart, double maxStart)
        {
            if (maxStart <= 1e-9)
                return ScrubMax;
            var ratio = viewStart / maxStart;
            ratio = Math.Max(0, Math.Min(1, ratio));
            return (int)Math.Round(ratio * ScrubMax);
        }

        bool IsViewAlignedWithLive()
        {
            if (_graph?.GraphPane == null || !_hasSample)
                return true;

            var duration = GetVisibleDuration();
            var tMax = _graph.GraphPane.XAxis.Scale.Max;
            return Math.Abs(_lastPlotTimeSec - tMax) <= Math.Max(0.05, duration * 0.05);
        }

        static void AddSeries(GraphPane pane, string label, RollingPointPairList points, Color lineColor)
        {
            var curve = pane.AddCurve(label, points, lineColor, SymbolType.Circle);
            curve.Line.Width = 2.5f;
            curve.Symbol.Size = 4f;
            curve.Symbol.Fill = new Fill(lineColor);
            curve.Symbol.Border.Color = lineColor;
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
            pane.Legend.IsVisible = true;
            pane.Legend.FontSpec.FontColor = Color.White;
            StyleAxis(pane.XAxis);
            StyleAxis(pane.YAxis);

            ApplySeriesStyle(pane, "X", ColorX);
            ApplySeriesStyle(pane, "Y", ColorY);
            ApplySeriesStyle(pane, "Z", ColorZ);

            SyncGraphLayout(graph);
        }

        static void ApplySeriesStyle(GraphPane pane, string label, Color lineColor)
        {
            foreach (var item in pane.CurveList)
            {
                if (item is LineItem line && line.Label.Text == label)
                {
                    line.Color = lineColor;
                    line.Line.Color = lineColor;
                    line.Line.Width = 2.5f;
                    line.Symbol.Fill = new Fill(lineColor);
                    line.Symbol.Border.Color = lineColor;
                    return;
                }
            }
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
                _lastPlotTimeSec = item.t;
                if (IsPlottable(item.sample.x))
                    _x.Add(item.t, item.sample.x);
                if (IsPlottable(item.sample.y))
                    _y.Add(item.t, item.sample.y);
                if (IsPlottable(item.sample.z))
                    _z.Add(item.t, item.sample.z);
                _last = item.sample;
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
                SyncScrubFromView();
            }
            else if (!_scrubDragging)
            {
                _graph?.Refresh();
                SyncScrubFromView();
            }

            UpdateHeader();
        }

        void UpdateHeader()
        {
            string mode;
            if (_followLive)
                mode = string.Empty;
            else
                mode = "  [scrubbing history — drag slider right or click Live to catch up]";

            _header.Text = string.Format(
                "OBSTACLE_DISTANCE_3D  id {0}    X {1:0.00} m    Y {2:0.00} m    Z {3:0.00} m{4}",
                _last.obstacle_id, _last.x, _last.y, _last.z, mode);
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
                pane.YAxis.Scale.Min = -1;
                pane.YAxis.Scale.Max = 1;
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

            AccumulateVisibleY(_x, tMin, tMax, ref yMin, ref yMax, ref any);
            AccumulateVisibleY(_y, tMin, tMax, ref yMin, ref yMax, ref any);
            AccumulateVisibleY(_z, tMin, tMax, ref yMin, ref yMax, ref any);

            if (!any)
                return false;

            var span = yMax - yMin;
            var pad = span > 1e-6 ? Math.Max(MinYPadding, span * YPaddingRatio) : 0.25;
            yMin -= pad;
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
                SyncGraphLayout(_graph);
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
