using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using ZedGraph;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// OBSTACLE_DISTANCE_3D messages that share time_boot_ms form one frame.
    /// Each frame plots two magnitude points (closest and farthest). Live mode scrolls
    /// a fixed 5 s window; drag horizontally to scrub time, then Center to resume.
    /// </summary>
    public class ObstacleDistanceGraph : UserControl
    {
        const int CompIdAny = 255;
        const double TimeWindowSeconds = 5;
        const double HistorySeconds = 120;
        const int MaxPoints = 5000;
        /// <summary>
        /// Flush a partially received frame once no new samples arrive for this long.
        /// Late samples for the same time_boot_ms still update the last plotted points.
        /// </summary>
        const double FrameIdleFlushSeconds = 0.15;

        readonly ZedGraphControl _graph;
        readonly Button _btnCenter;
        readonly PointPairList _closestTrace = new PointPairList();
        readonly PointPairList _farthestTrace = new PointPairList();
        LineItem _closestCurve;
        LineItem _farthestCurve;
        readonly ConcurrentQueue<(double magnitude, uint bootMs, double timeSec)> _pending =
            new ConcurrentQueue<(double, uint, double)>();
        readonly Timer _timer;

        int _subscription;
        byte _subscribedSysid;
        DateTime _plotOriginUtc = DateTime.UtcNow;
        double _lastPlotTimeSec;
        bool _followLive = true;

        bool _frameOpen;
        uint _frameBootMs;
        double _frameClosest;
        double _frameFarthest;
        double _frameTimeSec;
        double _frameLastSampleSec;
        bool _hasFlushedFrame;
        uint _lastFlushedBootMs;

        public ObstacleDistanceGraph()
        {
            BackColor = Color.FromArgb(32, 32, 32);

            _graph = CreateGraph();
            _graph.Dock = DockStyle.Fill;
            _graph.MinimumSize = new Size(120, 80);
            Controls.Add(_graph);

            _btnCenter = new Button
            {
                Text = "Center",
                Size = new Size(72, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 56, 60),
                ForeColor = Color.White,
                Visible = false,
                TabStop = false
            };
            _btnCenter.FlatAppearance.BorderColor = Color.FromArgb(120, 220, 255);
            _btnCenter.Click += (s, e) => RecenterLive();
            Controls.Add(_btnCenter);
            _btnCenter.BringToFront();

            _timer = new Timer { Interval = 50 };
            _timer.Tick += Timer_Tick;

            HandleCreated += (s, e) =>
            {
                PositionCenterButton();
                SyncGraphLayout();
            };
            Resize += (s, e) =>
            {
                PositionCenterButton();
                SyncGraphLayout();
            };
            VisibleChanged += (s, e) =>
            {
                if (Visible)
                {
                    PositionCenterButton();
                    SyncGraphLayout();
                }
            };
        }

        public void Start()
        {
            _plotOriginUtc = DateTime.UtcNow;
            _lastPlotTimeSec = 0;
            _followLive = true;
            UpdateCenterButton();
            _closestTrace.Clear();
            _farthestTrace.Clear();
            ResetFrame();
            _hasFlushedFrame = false;
            _lastFlushedBootMs = 0;
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
            SyncGraphLayout();
            if (IsHandleCreated)
                BeginInvoke(new Action(() =>
                {
                    SyncGraphLayout();
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

        void RecenterLive()
        {
            _followLive = true;
            UpdateCenterButton();
            ApplyScales();
            _graph?.Refresh();
        }

        void BeginScrub()
        {
            if (!_followLive)
                return;

            _followLive = false;
            UpdateCenterButton();
        }

        void UpdateCenterButton()
        {
            _btnCenter.Visible = !_followLive;
            if (_btnCenter.Visible)
                _btnCenter.BringToFront();
        }

        void PositionCenterButton()
        {
            const int margin = 10;
            _btnCenter.Location = new Point(
                Math.Max(margin, ClientSize.Width - _btnCenter.Width - margin),
                margin);
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
            pane.Legend.IsVisible = true;
            pane.Legend.FontSpec.Size = 10f;
            pane.Legend.FontSpec.FontColor = Color.White;
            pane.Legend.Fill = new Fill(Color.FromArgb(48, 48, 52));
            pane.Legend.Border.Color = Color.FromArgb(90, 90, 95);
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

            // Time scrub only — no zoom and no vertical pan. Left-drag pans time.
            graph.IsEnableHZoom = false;
            graph.IsEnableVZoom = false;
            graph.IsEnableWheelZoom = false;
            graph.IsEnableHPan = true;
            graph.IsEnableVPan = false;
            graph.PanButtons = MouseButtons.Left;
            graph.PanModifierKeys = Keys.None;
            graph.PanButtons2 = MouseButtons.Middle;
            graph.PanModifierKeys2 = Keys.None;
            graph.IsShowContextMenu = false;
            graph.ZoomEvent += Graph_ZoomEvent;
            // Pause live follow as soon as a scrub drag starts (ZoomEvent only fires on release).
            graph.MouseDownEvent += Graph_MouseDownEvent;
            graph.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    RecenterLive();
            };

            var closestColor = Color.FromArgb(120, 220, 255);
            _closestCurve = pane.AddCurve("Closest", _closestTrace, closestColor, SymbolType.Circle);
            StylePointCurve(_closestCurve, closestColor);

            var farthestColor = Color.FromArgb(255, 170, 90);
            _farthestCurve = pane.AddCurve("Farthest", _farthestTrace, farthestColor, SymbolType.Diamond);
            StylePointCurve(_farthestCurve, farthestColor);

            graph.AxisChange();
            return graph;
        }

        static void StylePointCurve(LineItem curve, Color color)
        {
            curve.Line.Width = 2f;
            curve.Line.IsVisible = true;
            curve.Line.IsSmooth = false;
            curve.Label.IsVisible = true;
            curve.Symbol.Size = 6f;
            curve.Symbol.Fill = new Fill(color);
            curve.Symbol.Border.Color = Color.White;
        }

        bool Graph_MouseDownEvent(ZedGraphControl sender, MouseEventArgs e)
        {
            var isPan =
                (e.Button == sender.PanButtons && Control.ModifierKeys == sender.PanModifierKeys) ||
                (e.Button == sender.PanButtons2 && Control.ModifierKeys == sender.PanModifierKeys2);
            if (isPan && sender.MasterPane.FindChartRect(e.Location) != null)
                BeginScrub();
            return false;
        }

        void Graph_ZoomEvent(ZedGraphControl sender, ZoomState oldState, ZoomState newState)
        {
            // Pan is reported through ZoomEvent; ignore other state types.
            if (newState == null || newState.Type != ZoomState.StateType.Pan)
                return;

            BeginScrub();
            SetAxisAutoscale(sender.GraphPane.YAxis);
            sender.AxisChange();
            LockFixedTimeWindow(sender.GraphPane);
            sender.Invalidate(true);
        }

        void StyleGraph(ZedGraphControl graph)
        {
            if (graph?.GraphPane == null)
                return;

            var pane = graph.GraphPane;
            pane.Chart.Fill = new Fill(Color.FromArgb(56, 56, 60));
            pane.Fill = new Fill(Color.FromArgb(40, 40, 44));
            pane.Legend.IsVisible = true;
            pane.Legend.FontSpec.FontColor = Color.White;
            pane.Legend.Fill = new Fill(Color.FromArgb(48, 48, 52));
            pane.Legend.Border.Color = Color.FromArgb(90, 90, 95);
            StyleAxis(pane.XAxis);
            StyleAxis(pane.YAxis);
            ApplyScales();
            SyncGraphLayout();
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

        void SyncGraphLayout()
        {
            if (_graph == null || _graph.IsDisposed || _graph.Width < 10 || _graph.Height < 10)
                return;

            using (var g = _graph.CreateGraphics())
                _graph.MasterPane.ReSize(g, new RectangleF(0, 0, _graph.Width, _graph.Height));

            ApplyScales();
        }

        void ApplyScales()
        {
            if (_graph?.GraphPane == null)
                return;

            var pane = _graph.GraphPane;
            SetAxisAutoscale(pane.YAxis);

            // Same pattern as FlightData tuning: set X, AxisChange on the control, then re-lock X
            // because AxisChange / Y autoscale can rewrite the visible range.
            if (_followLive)
                ApplyLiveTimeWindow(pane);

            _graph.AxisChange();

            if (_followLive)
                ApplyLiveTimeWindow(pane);
            else
                LockFixedTimeWindow(pane);

            _graph.Invalidate(true);
        }

        /// <summary>
        /// Rolling 5 s X window that follows wall-clock time so the plot scrolls continuously.
        /// </summary>
        void ApplyLiveTimeWindow(GraphPane pane)
        {
            var nowSec = (DateTime.UtcNow - _plotOriginUtc).TotalSeconds;
            if (_lastPlotTimeSec > nowSec)
                nowSec = _lastPlotTimeSec;

            // Keep a small lead so the newest point is not glued to the right edge.
            var major = pane.XAxis.Scale.MajorStep;
            if (double.IsNaN(major) || major <= 0)
                major = 1.0;

            var tMax = Math.Max(TimeWindowSeconds, nowSec + major);
            var tMin = tMax - TimeWindowSeconds;
            if (tMin < 0)
            {
                tMin = 0;
                tMax = TimeWindowSeconds;
            }

            pane.XAxis.Scale.Min = tMin;
            pane.XAxis.Scale.Max = tMax;
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;
        }

        void LockFixedTimeWindow(GraphPane pane)
        {
            var min = pane.XAxis.Scale.Min;
            pane.XAxis.Scale.Min = min;
            pane.XAxis.Scale.Max = min + TimeWindowSeconds;
            pane.XAxis.Scale.MinAuto = false;
            pane.XAxis.Scale.MaxAuto = false;
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            EnsureSubscribed();

            var changed = false;
            while (_pending.TryDequeue(out var item))
            {
                if (IngestSample(item.magnitude, item.bootMs, item.timeSec))
                    changed = true;
            }

            var nowSec = (DateTime.UtcNow - _plotOriginUtc).TotalSeconds;
            if (TryFlushIdleFrame(nowSec))
                changed = true;

            if (!Visible)
                return;

            // Always refresh while following live so the 5 s window scrolls with wall clock,
            // even between sparse OBSTACLE_DISTANCE_3D messages.
            if (_followLive)
            {
                if (changed)
                    TrimTraceHistory();
                ApplyScales();
                _graph?.Refresh();
                return;
            }

            if (changed || _closestTrace.Count > 0 || _farthestTrace.Count > 0)
            {
                TrimTraceHistory();
                // Still refresh so new points appear if they fall in the scrubbed window.
                SetAxisAutoscale(_graph.GraphPane.YAxis);
                _graph.AxisChange();
                LockFixedTimeWindow(_graph.GraphPane);
                _graph.Refresh();
            }
        }

        bool IngestSample(double mag, uint bootMs, double timeSec)
        {
            if (double.IsNaN(mag) || double.IsInfinity(mag))
                return false;

            if (!_frameOpen)
            {
                // Same sensor frame arrived after an idle flush — reopen and keep aggregating
                // so we update the existing closest/farthest points instead of adding extras.
                if (_hasFlushedFrame && bootMs == _lastFlushedBootMs &&
                    _closestTrace.Count > 0 && _farthestTrace.Count > 0)
                {
                    BeginFrame(
                        bootMs,
                        Math.Min(_closestTrace[_closestTrace.Count - 1].Y, mag),
                        Math.Max(_farthestTrace[_farthestTrace.Count - 1].Y, mag),
                        _closestTrace[_closestTrace.Count - 1].X,
                        timeSec);
                    return false;
                }

                BeginFrame(bootMs, mag, mag, timeSec, timeSec);
                return false;
            }

            if (bootMs != _frameBootMs)
            {
                var flushed = FlushFrame();
                BeginFrame(bootMs, mag, mag, timeSec, timeSec);
                return flushed;
            }

            if (mag < _frameClosest)
                _frameClosest = mag;
            if (mag > _frameFarthest)
                _frameFarthest = mag;
            _frameLastSampleSec = timeSec;
            return false;
        }

        void BeginFrame(uint bootMs, double closest, double farthest, double frameTimeSec, double lastSampleSec)
        {
            _frameOpen = true;
            _frameBootMs = bootMs;
            _frameClosest = closest;
            _frameFarthest = farthest;
            _frameTimeSec = frameTimeSec;
            _frameLastSampleSec = lastSampleSec;
        }

        bool TryFlushIdleFrame(double nowSec)
        {
            if (!_frameOpen)
                return false;

            if (nowSec - _frameLastSampleSec < FrameIdleFlushSeconds)
                return false;

            return FlushFrame();
        }

        bool FlushFrame()
        {
            if (!_frameOpen)
                return false;

            var changed = UpsertFramePoints(_frameClosest, _frameFarthest, _frameTimeSec, _frameBootMs);
            _lastPlotTimeSec = _frameTimeSec;
            ResetFrame();
            return changed;
        }

        void ResetFrame()
        {
            _frameOpen = false;
            _frameBootMs = 0;
            _frameClosest = 0;
            _frameFarthest = 0;
            _frameTimeSec = 0;
            _frameLastSampleSec = 0;
        }

        /// <summary>
        /// One closest and one farthest per time_boot_ms. Late samples for an already-plotted
        /// frame update those points instead of creating duplicates.
        /// </summary>
        bool UpsertFramePoints(double closest, double farthest, double timeSec, uint bootMs)
        {
            if (double.IsNaN(closest) || double.IsInfinity(closest) ||
                double.IsNaN(farthest) || double.IsInfinity(farthest))
                return false;

            var updatingSameFrame = _hasFlushedFrame && bootMs == _lastFlushedBootMs &&
                                    _closestTrace.Count > 0 && _farthestTrace.Count > 0;

            if (updatingSameFrame)
            {
                var lastClosest = _closestTrace[_closestTrace.Count - 1];
                var lastFarthest = _farthestTrace[_farthestTrace.Count - 1];
                var newClosest = Math.Min(lastClosest.Y, closest);
                var newFarthest = Math.Max(lastFarthest.Y, farthest);
                if (Math.Abs(lastClosest.Y - newClosest) < 1e-9 &&
                    Math.Abs(lastFarthest.Y - newFarthest) < 1e-9)
                    return false;

                lastClosest.Y = newClosest;
                lastFarthest.Y = newFarthest;
                _closestTrace[_closestTrace.Count - 1] = lastClosest;
                _farthestTrace[_farthestTrace.Count - 1] = lastFarthest;
                return true;
            }

            _closestTrace.Add(timeSec, closest);
            _farthestTrace.Add(timeSec, farthest);
            _hasFlushedFrame = true;
            _lastFlushedBootMs = bootMs;

            while (_closestTrace.Count > MaxPoints)
                _closestTrace.RemoveAt(0);
            while (_farthestTrace.Count > MaxPoints)
                _farthestTrace.RemoveAt(0);

            return true;
        }

        void TrimTraceHistory()
        {
            var tMin = Math.Max(0, _lastPlotTimeSec - HistorySeconds);
            TrimTrace(_closestTrace, tMin);
            TrimTrace(_farthestTrace, tMin);
        }

        static void TrimTrace(PointPairList trace, double tMin)
        {
            if (trace.Count == 0)
                return;

            while (trace.Count > 1 && trace[0].X < tMin)
                trace.RemoveAt(0);
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

            // Dashboard / quick-view still shows latest magnitude.
            var cs = MainV2.comPort?.MAV?.cs;
            if (cs != null)
                cs.obstacle_magnitude = mag;

            var t = (DateTime.UtcNow - _plotOriginUtc).TotalSeconds;
            _pending.Enqueue((mag, sample.time_boot_ms, t));
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
