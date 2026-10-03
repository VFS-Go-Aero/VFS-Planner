using System;

using System.Collections.Concurrent;

using System.Drawing;

using System.Windows.Forms;

using MissionPlanner.Utilities;

using ZedGraph;



namespace MissionPlanner.Controls

{

    /// <summary>

    /// Three separate plots of OBSTACLE_DISTANCE_3D x, y, and z (meters) against time.

    /// compid 255 on the subscription means any component on the current system.

    /// </summary>

    public class ObstacleDistanceGraph : UserControl

    {

        const int CompIdAny = 255;

        const double WindowSeconds = 30;



        readonly System.Windows.Forms.Label _header;

        readonly Panel _plotHost;

        readonly ZedGraphControl _graphX;

        readonly ZedGraphControl _graphY;

        readonly ZedGraphControl _graphZ;

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

        DateTime _plotOriginUtc = DateTime.UtcNow;



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



            _plotHost = new Panel

            {

                Dock = DockStyle.Fill,

                BackColor = BackColor

            };



            _graphX = CreateGraph("X distance (m)", _x, Color.White);

            _graphY = CreateGraph("Y distance (m)", _y, Color.FromArgb(120, 220, 255));

            _graphZ = CreateGraph("Z distance (m)", _z, Color.FromArgb(255, 200, 120));



            _plotHost.Controls.Add(_graphX);

            _plotHost.Controls.Add(_graphY);

            _plotHost.Controls.Add(_graphZ);

            _plotHost.Resize += (s, e) => LayoutGraphControls();



            Controls.Add(_plotHost);

            Controls.Add(_header);



            _timer = new Timer { Interval = 200 };

            _timer.Tick += Timer_Tick;



            HandleCreated += (s, e) => LayoutGraphControls();

            Resize += (s, e) => LayoutGraphControls();

        }



        public void Start()

        {

            _plotOriginUtc = DateTime.UtcNow;

            EnsureSubscribed();

            ThemeManager.ApplyThemeTo(_graphX);

            ThemeManager.ApplyThemeTo(_graphY);

            ThemeManager.ApplyThemeTo(_graphZ);

            StyleGraph(_graphX, Color.White);

            StyleGraph(_graphY, Color.FromArgb(120, 220, 255));

            StyleGraph(_graphZ, Color.FromArgb(255, 200, 120));

            _timer.Start();

            LayoutGraphControls();

        }



        public void Stop()

        {

            _timer.Stop();

        }



        public void RefreshView()

        {

            ApplyScales();

        }



        void LayoutGraphControls()

        {

            if (_plotHost == null || _graphX == null)

                return;



            var w = Math.Max(0, _plotHost.ClientSize.Width);

            var h = Math.Max(0, _plotHost.ClientSize.Height);

            if (w < 20 || h < 60)

                return;



            var gh = Math.Max(80, h / 3);

            _graphX.SetBounds(0, 0, w, gh);

            _graphY.SetBounds(0, gh, w, gh);

            _graphZ.SetBounds(0, gh * 2, w, Math.Max(80, h - gh * 2));



            SyncGraphLayout(_graphX);

            SyncGraphLayout(_graphY);

            SyncGraphLayout(_graphZ);

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



        ZedGraphControl CreateGraph(string yTitle, RollingPointPairList points, Color lineColor)

        {

            var graph = new ZedGraphControl

            {

                Name = "zgObstacle" + yTitle,

                BackColor = Color.FromArgb(48, 48, 52)

            };



            var pane = graph.GraphPane;

            pane.Title.IsVisible = false;

            pane.XAxis.Title.Text = "Time (s)";

            pane.YAxis.Title.Text = yTitle;

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

            pane.XAxis.Scale.Max = WindowSeconds;

            pane.XAxis.Scale.MinAuto = false;

            pane.XAxis.Scale.MaxAuto = false;

            pane.YAxis.Scale.Min = -2;

            pane.YAxis.Scale.Max = 2;

            pane.YAxis.Scale.MinAuto = false;

            pane.YAxis.Scale.MaxAuto = false;



            var curve = pane.AddCurve(yTitle, points, lineColor, SymbolType.Circle);

            curve.Line.Width = 2.5f;

            curve.Symbol.Size = 4f;

            curve.Symbol.Fill = new Fill(lineColor);

            curve.Symbol.Border.Color = lineColor;



            graph.AxisChange();

            return graph;

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



        static void StyleGraph(ZedGraphControl graph, Color lineColor)

        {

            if (graph?.GraphPane == null)

                return;



            var pane = graph.GraphPane;

            pane.Chart.Fill = new Fill(Color.FromArgb(56, 56, 60));

            pane.Fill = new Fill(Color.FromArgb(40, 40, 44));

            pane.Chart.Border.IsVisible = true;

            pane.Chart.Border.Color = Color.Gainsboro;

            StyleAxis(pane.XAxis);

            StyleAxis(pane.YAxis);



            if (pane.CurveList.Count > 0 && pane.CurveList[0] is LineItem line)

            {

                line.Color = lineColor;

                line.Line.Color = lineColor;

                line.Line.Width = 2.5f;

                line.Symbol.Fill = new Fill(lineColor);

                line.Symbol.Border.Color = lineColor;

            }



            SyncGraphLayout(graph);

        }



        /// <summary>

        /// Wire layout follows mavlink XML field order, not the sequential struct layout used by Marshal.

        /// </summary>

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



            var tLast = Math.Max(_lastPlotTimeSec, WindowSeconds * 0.1);

            var tMin = Math.Max(0, tLast - WindowSeconds);

            if (tLast <= tMin)

                tLast = tMin + WindowSeconds;



            ScaleGraph(_graphX, _x, tMin, tLast);

            ScaleGraph(_graphY, _y, tMin, tLast);

            ScaleGraph(_graphZ, _z, tMin, tLast);



            _header.Text = string.Format(

                "OBSTACLE_DISTANCE_3D  id {0}    X {1:0.00} m    Y {2:0.00} m    Z {3:0.00} m",

                _last.obstacle_id, _last.x, _last.y, _last.z);

        }



        static void ScaleGraph(ZedGraphControl graph, RollingPointPairList points, double tMin, double tMax)

        {

            if (graph == null || graph.IsDisposed)

                return;



            SyncGraphLayout(graph);



            var pane = graph.GraphPane;

            pane.XAxis.Scale.Min = tMin;

            pane.XAxis.Scale.Max = tMax;

            pane.XAxis.Scale.MinAuto = false;

            pane.XAxis.Scale.MaxAuto = false;



            if (points != null && points.Count > 0)

            {

                pane.YAxis.Scale.MinAuto = true;

                pane.YAxis.Scale.MaxAuto = true;

            }

            else

            {

                pane.YAxis.Scale.Min = -2;

                pane.YAxis.Scale.Max = 2;

                pane.YAxis.Scale.MinAuto = false;

                pane.YAxis.Scale.MaxAuto = false;

            }



            graph.AxisChange();

            pane.XAxis.Scale.Min = tMin;

            pane.XAxis.Scale.Max = tMax;

            graph.Refresh();

        }



        protected override void OnVisibleChanged(EventArgs e)

        {

            base.OnVisibleChanged(e);

            if (Visible)

            {

                LayoutGraphControls();

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


