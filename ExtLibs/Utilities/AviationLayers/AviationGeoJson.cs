using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GMap.NET;
using MissionPlanner.Utilities;
using Newtonsoft.Json.Linq;

namespace MissionPlanner.Utilities.AviationLayers
{
    public static class AviationGeoJson
    {
        public static List<MapPolygonFeature> ParsePolygonFeatures(string json)
        {
            var result = new List<MapPolygonFeature>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var root = JObject.Parse(json);
            var features = root["features"] as JArray;
            if (features == null)
                return result;

            foreach (var feature in features)
            {
                var props = feature["properties"] as JObject ?? new JObject();
                var id = feature["id"]?.ToString() ?? props["NOTAM_KEY"]?.ToString() ?? "restriction";
                var title = props["TITLE"]?.ToString() ?? props["name"]?.ToString() ?? id;
                var legal = props["LEGAL"]?.ToString() ?? props["class"]?.ToString() ?? props["icaoClass"]?.ToString() ?? "";
                var tooltip = string.IsNullOrEmpty(legal) ? title : legal + ": " + title;

                var geom = feature["geometry"];
                if (geom == null)
                    continue;

                foreach (var ring in ExtractRings(geom))
                {
                    if (ring.Count >= 3)
                    {
                        result.Add(new MapPolygonFeature
                        {
                            Id = id,
                            Title = title,
                            ToolTip = tooltip,
                            Ring = ring
                        });
                    }
                }
            }

            return result;
        }

        public static List<MapPolygonFeature> ParsePolygonFeaturesFiltered(string json, Func<JObject, bool> filter)
        {
            var result = new List<MapPolygonFeature>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var root = JObject.Parse(json);
            var features = root["features"] as JArray;
            if (features == null)
                return result;

            foreach (var feature in features)
            {
                var props = feature["properties"] as JObject ?? new JObject();
                if (filter != null && !filter(props))
                    continue;

                var id = feature["id"]?.ToString() ?? props["NOTAM_KEY"]?.ToString() ?? "restriction";
                var title = props["TITLE"]?.ToString() ?? props["name"]?.ToString() ?? id;
                var legal = props["LEGAL"]?.ToString() ?? props["class"]?.ToString() ?? props["icaoClass"]?.ToString() ?? "";
                var tooltip = string.IsNullOrEmpty(legal) ? title : legal + ": " + title;

                var geom = feature["geometry"];
                if (geom == null)
                    continue;

                foreach (var ring in ExtractRings(geom))
                {
                    if (ring.Count >= 3)
                    {
                        result.Add(new MapPolygonFeature
                        {
                            Id = id,
                            Title = title,
                            ToolTip = tooltip,
                            Ring = ring
                        });
                    }
                }
            }

            return result;
        }

        public static List<MapPolygonFeature> ParseArcGisPolygons(string json, Func<JObject, string, string> tooltipBuilder,
            Action<JObject, MapPolygonFeature> enrichFeature = null)
        {
            var result = new List<MapPolygonFeature>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var root = JObject.Parse(json);
            var features = root["features"] as JArray;
            if (features == null)
                return result;

            foreach (var feature in features)
            {
                var props = feature["properties"] as JObject ?? new JObject();
                var id = feature["id"]?.ToString() ?? props["OBJECTID"]?.ToString() ?? "feature";
                var tooltip = tooltipBuilder(props, id);
                var geom = feature["geometry"];
                if (geom == null)
                    continue;

                foreach (var ring in ExtractRings(geom))
                {
                    if (ring.Count >= 3)
                    {
                        var mapped = new MapPolygonFeature
                        {
                            Id = id,
                            Title = tooltip,
                            ToolTip = tooltip,
                            Ring = ring
                        };
                        enrichFeature?.Invoke(props, mapped);
                        result.Add(mapped);
                    }
                }
            }

            return result;
        }

        public static List<NotamBriefingItem> ParseTfrBriefingItems(string json)
        {
            var items = new List<NotamBriefingItem>();
            if (string.IsNullOrWhiteSpace(json))
                return items;

            var root = JObject.Parse(json);
            var features = root["features"] as JArray;
            if (features == null)
                return items;

            foreach (var feature in features)
            {
                var props = feature["properties"] as JObject ?? new JObject();
                var notamKey = props["NOTAM_KEY"]?.ToString() ?? feature["id"]?.ToString() ?? "";
                var type = props["TYPE"]?.ToString() ?? "TFR";
                var title = props["TITLE"]?.ToString() ?? notamKey;
                var state = props["STATE"]?.ToString() ?? "";
                var modified = props["MODIFIED"]?.ToString() ?? props["MODIFIED_DATE"]?.ToString() ?? "";

                var center = PointLatLng.Empty;
                var geom = feature["geometry"];
                if (geom != null)
                {
                    foreach (var ring in ExtractRings(geom))
                    {
                        center = Centroid(ring);
                        break;
                    }
                }

                items.Add(new NotamBriefingItem
                {
                    NotamKey = notamKey,
                    Type = type,
                    Title = title,
                    State = state,
                    LastModified = FormatFaaDateTime(modified),
                    Center = center,
                    DetailUrl = AviationFetch.BuildTfrDetailUrl(notamKey)
                });
            }

            return items.OrderBy(i => i.DisplayId).ToList();
        }

        public static List<AviationNotice> ToAviationNotices(IList<MapPolygonFeature> tfrFeatures)
        {
            var list = new List<AviationNotice>();
            foreach (var f in tfrFeatures)
            {
                var center = Centroid(f.Ring);
                list.Add(new AviationNotice
                {
                    Id = f.Id,
                    Type = "TFR",
                    Text = f.ToolTip,
                    Location = new GeoPoint(center.Lat, center.Lng),
                    Polygon = f.Ring,
                    Source = AviationFetch.TfrWfsUrl,
                    DetailUrl = AviationFetch.BuildTfrDetailUrl(f.Id)
                });
            }

            return list;
        }

        public static List<MetarObservation> ParseMetarJson(string json)
        {
            var list = new List<MetarObservation>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            try
            {
                var token = JToken.Parse(json);
                if (token is JArray arr)
                {
                    foreach (var item in arr)
                        AddMetar(list, item as JObject);
                }
                else if (token is JObject obj && obj["data"] is JArray data)
                {
                    foreach (var item in data)
                        AddMetar(list, item as JObject);
                }
            }
            catch
            {
                // API shape varies; return empty
            }

            return list;
        }

        static void AddMetar(List<MetarObservation> list, JObject obj)
        {
            if (obj == null)
                return;

            var id = obj["icaoId"]?.ToString() ?? obj["stationId"]?.ToString() ?? obj["icao"]?.ToString();
            if (string.IsNullOrEmpty(id))
                return;

            var lat = obj["lat"]?.ToObject<double?>() ?? obj["latitude"]?.ToObject<double?>();
            var lon = obj["lon"]?.ToObject<double?>() ?? obj["longitude"]?.ToObject<double?>();
            if (!lat.HasValue || !lon.HasValue)
                return;

            list.Add(new MetarObservation
            {
                StationId = id,
                Location = new GeoPoint(lat.Value, lon.Value),
                RawText = obj["rawOb"]?.ToString() ?? obj["rawText"]?.ToString() ?? obj["metar"]?.ToString() ?? id,
                ObservedUtc = ParseMetarTime(obj["obsTime"]?.ToString() ?? obj["reportTime"]?.ToString())
            });
        }

        static DateTime? ParseMetarTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (long.TryParse(raw, out var epoch))
                return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return dt.ToUniversalTime();

            return null;
        }

        public static bool IsSpecialUseFeature(MapPolygonFeature feat)
        {
            if (feat == null)
                return false;

            var text = (feat.ToolTip + " " + feat.Title).ToUpperInvariant();
            if (text.Length >= 1)
            {
                var ic = text.TrimStart();
                if (ic.Length >= 1 && "RPQAW".IndexOf(ic[0]) >= 0)
                    return true;
            }

            return text.Contains("RESTRICT") || text.Contains("PROHIB") || text.Contains("MOA") ||
                   text.Contains("MILITARY") || text.Contains("DANGER") || text.Contains("WARNING");
        }

        public static bool IsSpecialUseProperties(JObject props)
        {
            if (props == null)
                return false;

            var ic = (props["class"] ?? props["icaoClass"] ?? props["ICAOClass"])?.ToString()?.Trim()
                .ToUpperInvariant() ?? "";
            if (ic.Length == 1 && "RPQAW".Contains(ic))
                return true;

            var type = props["type"]?.ToString()?.ToUpperInvariant() ?? "";
            var name = props["name"]?.ToString()?.ToUpperInvariant() ?? "";
            var text = type + " " + name;
            return text.Contains("RESTRICT") || text.Contains("PROHIB") || text.Contains("MOA") ||
                   text.Contains("MILITARY") || text.Contains("DANGER") || text.Contains("WARNING");
        }

        public static IEnumerable<List<PointLatLng>> ExtractRings(JToken geometry)
        {
            var type = geometry["type"]?.ToString();
            var coords = geometry["coordinates"];
            if (type == null || coords == null)
                yield break;

            switch (type)
            {
                case "Polygon":
                    foreach (var ring in PolygonRings(coords))
                        yield return ring;
                    break;
                case "MultiPolygon":
                    foreach (var poly in coords)
                    foreach (var ring in PolygonRings(poly))
                        yield return ring;
                    break;
                case "LineString":
                    {
                        var line = LineRing(coords);
                        if (line != null)
                            yield return line;
                    }
                    break;
            }
        }

        static List<PointLatLng> LineRing(JToken lineCoords)
        {
            var ring = new List<PointLatLng>();
            foreach (var pt in lineCoords)
            {
                if (pt is JArray arr && arr.Count >= 2)
                    ring.Add(new PointLatLng(arr[1].Value<double>(), arr[0].Value<double>()));
            }

            if (ring.Count >= 3 && !ring[0].Equals(ring[ring.Count - 1]))
                ring.Add(ring[0]);
            return ring.Count >= 3 ? ring : null;
        }

        public static List<MapPolygonFeature> ParseGairmetFeatures(string json)
        {
            var result = new List<MapPolygonFeature>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var root = JObject.Parse(json);
            if (!(root["features"] is JArray features))
                return result;

            var idx = 0;
            foreach (var feature in features)
            {
                var props = feature["properties"] as JObject ?? new JObject();
                var hazard = props["hazard"]?.ToString() ?? "G-AIRMET";
                var product = props["product"]?.ToString() ?? "";
                var tag = props["tag"]?.ToString() ?? "";
                var id = $"{product}-{hazard}-{tag}-{idx++}";
                var due = props["dueTo"]?.ToString();
                var tooltip = string.IsNullOrEmpty(due) ? $"{product} {hazard} {tag}".Trim() : due;

                var geom = feature["geometry"];
                if (geom == null)
                    continue;

                foreach (var ring in ExtractRings(geom))
                {
                    if (ring.Count >= 3)
                    {
                        result.Add(new MapPolygonFeature
                        {
                            Id = id,
                            Title = hazard,
                            ToolTip = tooltip,
                            Ring = ring
                        });
                    }
                }
            }

            return result;
        }

        public static List<PirepObservation> ParsePirepGeoJson(string json)
        {
            var list = new List<PirepObservation>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            var root = JObject.Parse(json);
            if (!(root["features"] is JArray features))
                return list;

            foreach (var feature in features)
            {
                var props = feature["properties"] as JObject;
                var geom = feature["geometry"];
                if (props == null || geom == null)
                    continue;

                if (geom["type"]?.ToString() != "Point")
                    continue;

                if (!(geom["coordinates"] is JArray coords) || coords.Count < 2)
                    continue;

                var lng = coords[0].Value<double>();
                var lat = coords[1].Value<double>();
                var raw = props["rawOb"]?.ToString() ?? props["rawText"]?.ToString() ?? "";
                list.Add(new PirepObservation
                {
                    Id = props["icaoId"]?.ToString() + "-" + (props["obsTime"]?.ToString() ?? list.Count.ToString()),
                    Location = new GeoPoint(lat, lng),
                    RawText = raw,
                    AircraftType = props["acType"]?.ToString(),
                    ReportType = props["airepType"]?.ToString() ?? "PIREP",
                    ObservedUtc = ParseAwcTime(props["obsTime"]?.ToString())
                });
            }

            return list;
        }

        static DateTime? ParseAwcTime(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso))
                return null;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return dt.ToUniversalTime();
            return null;
        }

        static IEnumerable<List<PointLatLng>> PolygonRings(JToken polygonCoords)
        {
            var outer = polygonCoords.First as JArray;
            if (outer == null)
                yield break;

            var ring = new List<PointLatLng>();
            foreach (var pt in outer)
            {
                if (pt is JArray arr && arr.Count >= 2)
                {
                    var lng = arr[0].Value<double>();
                    var lat = arr[1].Value<double>();
                    ring.Add(new PointLatLng(lat, lng));
                }
            }

            if (ring.Count >= 3)
                yield return ring;
        }

        public static PointLatLng Centroid(List<PointLatLng> ring)
        {
            if (ring == null || ring.Count == 0)
                return PointLatLng.Empty;

            var lat = ring.Average(p => p.Lat);
            var lng = ring.Average(p => p.Lng);
            return new PointLatLng(lat, lng);
        }

        static string FormatFaaDateTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8)
                return raw;

            if (DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var dt))
                return dt.ToString("yyyy-MM-dd");

            return raw;
        }
    }
}
