using System;
using System.Drawing;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;

namespace MissionPlanner.Utilities.AviationLayers
{
    /// <summary>
    /// Applies aviation layer models to GMap overlays (must run in MissionPlanner.exe assembly).
    /// </summary>
    public static class AviationGMapApply
    {
        public static void ApplyLayer(IFlightMapLayer layer)
        {
            if (layer == null)
                return;

            switch (layer)
            {
                case GMapPolygonFlightLayer polyLayer:
                    ApplyPolygonLayer(polyLayer);
                    break;
                case NotamMapLayer notam:
                    ApplyNotamLayer(notam);
                    break;
                case MetarMapLayer metar:
                    ApplyMetarLayer(metar);
                    break;
                case PirepMapLayer pirep:
                    ApplyPirepLayer(pirep);
                    break;
                case FlightSafetyMapLayer safety:
                    ApplySafetyLayer(safety);
                    break;
                default:
                    layer.ApplyToMap();
                    break;
            }
        }

        public static void ApplyPolygonLayer(GMapPolygonFlightLayer layer)
        {
            if (layer?.MapOverlay == null)
                return;

            if (!layer.Enabled)
            {
                ClearOverlay(layer.MapOverlay, false);
                return;
            }

            try
            {
                layer.MapOverlay.Polygons.Clear();
                foreach (var feat in layer.GetFeatures())
                {
                    if (feat?.Ring == null || feat.Ring.Count < 3)
                        continue;

                    var poly = new GMapPolygon(feat.Ring, layer.PolygonNamePrefix + feat.Id);
                    poly.Fill = new SolidBrush(layer.FillColor);
                    poly.Stroke = new Pen(layer.StrokeColor, layer.StrokeWidthPx);
                    poly.Tag = feat;
                    poly.IsVisible = true;
                    layer.MapOverlay.Polygons.Add(poly);
                }

                layer.MapOverlay.IsVisibile = true;
                SyncGeometry(layer.MapOverlay);
            }
            catch (MissingFieldException ex)
            {
                log4net.LogManager.GetLogger(typeof(AviationGMapApply))
                    .Error("GMap polygon Fill/Stroke mismatch — rebuild MissionPlanner from source (delete bin/obj).", ex);
                throw;
            }
        }

        static readonly Color NotamFill = Color.FromArgb(72, Color.Gold);
        static readonly Color NotamStroke = Color.FromArgb(220, 180, 0);
        const float NotamStrokeWidth = 2.5f;

        static void ApplyNotamLayer(NotamMapLayer layer)
        {
            if (layer?.MapOverlay == null)
                return;

            if (!layer.Enabled)
            {
                ClearOverlay(layer.MapOverlay, false);
                return;
            }

            layer.MapOverlay.Markers.Clear();
            layer.MapOverlay.Polygons.Clear();

            foreach (var n in layer.GetNotices())
            {
                if (n.Polygon != null && n.Polygon.Count >= 3)
                {
                    var poly = new GMapPolygon(n.Polygon, "NOTAM-" + n.Id);
                    poly.Fill = new SolidBrush(NotamFill);
                    poly.Stroke = new Pen(NotamStroke, NotamStrokeWidth);
                    poly.Tag = n.Text;
                    poly.IsVisible = true;
                    layer.MapOverlay.Polygons.Add(poly);
                    continue;
                }

                var pt = n.Location.ToPointLatLng();
                if (pt.IsEmpty)
                    continue;

                layer.MapOverlay.Markers.Add(new GMarkerGoogle(pt, GMarkerGoogleType.red_dot)
                {
                    ToolTipText = n.Text,
                    ToolTipMode = MarkerTooltipMode.OnMouseOver,
                    Tag = n.Id
                });
            }

            layer.MapOverlay.IsVisibile = true;
            SyncGeometry(layer.MapOverlay);
        }

        /// <summary>Recompute screen positions after pan/zoom without rebuilding features.</summary>
        public static void RefreshOverlayGeometry(GMapOverlay overlay) => SyncGeometry(overlay);

        static void ApplyPirepLayer(PirepMapLayer layer)
        {
            if (layer?.MapOverlay == null)
                return;

            if (!layer.Enabled)
            {
                ClearOverlay(layer.MapOverlay, false);
                return;
            }

            layer.MapOverlay.Markers.Clear();
            foreach (var r in layer.GetReports())
            {
                var pt = r.Location.ToPointLatLng();
                if (pt.IsEmpty)
                    continue;

                var tip = string.IsNullOrEmpty(r.RawText)
                    ? (r.ReportType + " " + r.AircraftType).Trim()
                    : r.RawText;
                layer.MapOverlay.Markers.Add(new GMarkerGoogle(pt, GMarkerGoogleType.green_dot)
                {
                    ToolTipText = tip,
                    ToolTipMode = MarkerTooltipMode.OnMouseOver,
                    Tag = r.Id
                });
            }

            layer.MapOverlay.IsVisibile = true;
            SyncGeometry(layer.MapOverlay);
        }

        static void ApplyMetarLayer(MetarMapLayer layer)
        {
            if (layer?.MapOverlay == null)
                return;

            if (!layer.Enabled)
            {
                ClearOverlay(layer.MapOverlay, false);
                return;
            }

            layer.MapOverlay.Markers.Clear();
            foreach (var m in layer.GetMetars())
            {
                var pt = m.Location.ToPointLatLng();
                var label = string.IsNullOrEmpty(m.RawText) ? m.StationId : m.StationId + "\n" + m.RawText;
                layer.MapOverlay.Markers.Add(new GMarkerGoogle(pt, GMarkerGoogleType.blue_dot)
                {
                    ToolTipText = label,
                    ToolTipMode = MarkerTooltipMode.OnMouseOver,
                    Tag = m.StationId
                });
            }

            layer.MapOverlay.IsVisibile = true;
            SyncGeometry(layer.MapOverlay);
        }

        static void ApplySafetyLayer(FlightSafetyMapLayer layer)
        {
            if (layer?.MapOverlay == null)
                return;

            if (!layer.Enabled)
            {
                ClearOverlay(layer.MapOverlay, false);
                return;
            }

            layer.MapOverlay.Markers.Clear();
            foreach (var w in layer.GetWarnings())
            {
                layer.MapOverlay.Markers.Add(new GMarkerGoogle(w.Location, GMarkerGoogleType.yellow_dot)
                {
                    ToolTipText = w.Message,
                    ToolTipMode = MarkerTooltipMode.Always,
                    Tag = w.Code
                });
            }

            layer.MapOverlay.IsVisibile = true;
            SyncGeometry(layer.MapOverlay);
        }

        static void ClearOverlay(GMapOverlay overlay, bool visible)
        {
            overlay.Polygons.Clear();
            overlay.Markers.Clear();
            overlay.IsVisibile = visible;
            overlay.ForceUpdate();
        }

        static void SyncGeometry(GMapOverlay overlay)
        {
            if (overlay?.Control == null)
            {
                overlay?.ForceUpdate();
                return;
            }

            foreach (GMapPolygon poly in overlay.Polygons)
            {
                poly.IsVisible = true;
                overlay.Control.UpdatePolygonLocalPosition(poly);
            }

            foreach (GMapMarker marker in overlay.Markers)
                overlay.Control.UpdateMarkerLocalPosition(marker);

            overlay.ForceUpdate();
        }
    }
}
