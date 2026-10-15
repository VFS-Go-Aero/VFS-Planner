using System;
using System.Collections.Generic;
using System.Linq;
using GMap.NET;

namespace MissionPlanner.Utilities.AviationLayers
{
    /// <summary>
    /// Builds the briefing-panel rows from all enabled aviation layers (not TFR-only).
    /// </summary>
    public static class BriefingListBuilder
    {
        public const int MaxLaancRows = 25;
        public const int MaxRowsPerLayer = 200;
        public const int MaxTotalRows = 400;

        public static List<NotamBriefingItem> Build(
            PointLatLng center,
            IList<NotamBriefingItem> tfrBriefing,
            IList<MapPolygonFeature> sigmets,
            IList<MapPolygonFeature> gairmets,
            IList<MapPolygonFeature> specialUse,
            IList<MapPolygonFeature> laancCells,
            IList<MetarObservation> metars,
            IList<PirepObservation> pireps,
            IList<FlightSafetyWarning> safetyWarnings)
        {
            var items = new List<NotamBriefingItem>();

            if (tfrBriefing != null)
            {
                foreach (var t in tfrBriefing.Take(MaxRowsPerLayer))
                    items.Add(t);
            }

            AddPolygons(items, sigmets, "SIGMET", MaxRowsPerLayer);
            AddPolygons(items, gairmets, "G-AIRMET", MaxRowsPerLayer);
            AddPolygons(items, specialUse, "SUA", MaxRowsPerLayer);

            if (laancCells != null && laancCells.Count > 0)
            {
                items.Add(new NotamBriefingItem
                {
                    NotamKey = "LAANC",
                    Type = "LAANC",
                    Title = $"{laancCells.Count} UAS Facility Map grid cell(s) in fetch area (not an authorization)",
                    Center = center
                });

                var nearest = OrderByDistance(laancCells, center).Take(MaxLaancRows);
                foreach (var cell in nearest)
                {
                    items.Add(FromPolygon(cell, "LAANC"));
                }
            }

            if (metars != null)
            {
                foreach (var m in metars.Take(MaxRowsPerLayer))
                {
                    items.Add(new NotamBriefingItem
                    {
                        NotamKey = m.StationId ?? "METAR",
                        Type = "METAR",
                        Title = Truncate(m.RawText ?? m.StationId ?? "METAR", 160),
                        Center = m.Location.ToPointLatLng()
                    });
                }
            }

            if (pireps != null)
            {
                foreach (var p in pireps.Take(MaxRowsPerLayer))
                {
                    items.Add(new NotamBriefingItem
                    {
                        NotamKey = p.Id ?? "PIREP",
                        Type = "PIREP",
                        Title = Truncate(p.RawText ?? p.ReportType ?? "PIREP", 160),
                        Center = p.Location.ToPointLatLng()
                    });
                }
            }

            if (safetyWarnings != null)
            {
                foreach (var w in safetyWarnings.Take(50))
                {
                    items.Add(new NotamBriefingItem
                    {
                        NotamKey = w.Code ?? "SAFE",
                        Type = "Safety",
                        Title = Truncate(w.Message ?? "", 160),
                        Center = w.Location
                    });
                }
            }

            if (items.Count > MaxTotalRows)
                return items.Take(MaxTotalRows).ToList();

            return items;
        }

        static void AddPolygons(List<NotamBriefingItem> dest, IList<MapPolygonFeature> polygons, string type,
            int max)
        {
            if (polygons == null || polygons.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var n = 0;
            foreach (var poly in polygons)
            {
                if (poly == null)
                    continue;
                var key = poly.Id ?? poly.Title ?? Guid.NewGuid().ToString("N");
                if (!seen.Add(key))
                    continue;
                dest.Add(FromPolygon(poly, type));
                if (++n >= max)
                    break;
            }
        }

        public static NotamBriefingItem FromPolygon(MapPolygonFeature poly, string type)
        {
            var center = Centroid(poly.Ring);
            return new NotamBriefingItem
            {
                NotamKey = poly.Id ?? type,
                Type = type,
                Title = Truncate(string.IsNullOrWhiteSpace(poly.ToolTip) ? poly.Title : poly.ToolTip, 160),
                Center = center,
                DetailText = poly.DetailText
            };
        }

        static IEnumerable<MapPolygonFeature> OrderByDistance(IList<MapPolygonFeature> polys, PointLatLng center)
        {
            if (center.IsEmpty)
                return polys;

            return polys
                .Select(p => new { p, c = Centroid(p.Ring) })
                .OrderBy(x => Dist2(center, x.c))
                .Select(x => x.p);
        }

        static double Dist2(PointLatLng a, PointLatLng b)
        {
            var dLat = a.Lat - b.Lat;
            var dLng = a.Lng - b.Lng;
            return dLat * dLat + dLng * dLng;
        }

        static PointLatLng Centroid(IList<PointLatLng> ring)
        {
            if (ring == null || ring.Count == 0)
                return PointLatLng.Empty;

            double lat = 0, lng = 0;
            foreach (var p in ring)
            {
                lat += p.Lat;
                lng += p.Lng;
            }

            return new PointLatLng(lat / ring.Count, lng / ring.Count);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s ?? "";
            return s.Substring(0, max - 1) + "…";
        }
    }
}
