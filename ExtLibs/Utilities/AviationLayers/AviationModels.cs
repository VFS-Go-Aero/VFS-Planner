using System;
using System.Collections.Generic;
using GMap.NET;

namespace MissionPlanner.Utilities.AviationLayers
{
    public enum LayerHealthState
    {
        Disabled,
        Ok,
        Empty,
        Stale,
        Error
    }

    public sealed class LayerSnapshot
    {
        public string LayerId { get; set; }
        public string DisplayName { get; set; }
        public LayerHealthState Health { get; set; } = LayerHealthState.Disabled;
        public DateTime? FetchedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string Source { get; set; }
        public int FeatureCount { get; set; }
        public string Message { get; set; }
    }

    public struct GeoPoint
    {
        public double Lat { get; set; }
        public double Lng { get; set; }

        public GeoPoint(double lat, double lng)
        {
            Lat = lat;
            Lng = lng;
        }

        public PointLatLng ToPointLatLng() => new PointLatLng(Lat, Lng);
    }

    public sealed class MapPolygonFeature
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string ToolTip { get; set; }
        public string DetailText { get; set; }
        public List<PointLatLng> Ring { get; set; } = new List<PointLatLng>();
    }

    public sealed class Tfr
    {
        public string Id { get; set; }
        public string NotamKey { get; set; }
        public string Name { get; set; }
        public string LegalText { get; set; }
        public DateTime? StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
        public List<PointLatLng> Boundary { get; set; } = new List<PointLatLng>();
    }

    public sealed class AviationNotice
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Text { get; set; }
        public GeoPoint Location { get; set; }
        public double? RadiusNm { get; set; }
        public DateTime? StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
        public string Source { get; set; }
        public string DetailUrl { get; set; }
        public List<PointLatLng> Polygon { get; set; }
    }

    public sealed class MetarObservation
    {
        public string StationId { get; set; }
        public GeoPoint Location { get; set; }
        public string RawText { get; set; }
        public DateTime? ObservedUtc { get; set; }
    }

    public sealed class PirepObservation
    {
        public string Id { get; set; }
        public GeoPoint Location { get; set; }
        public string RawText { get; set; }
        public string AircraftType { get; set; }
        public string ReportType { get; set; }
        public DateTime? ObservedUtc { get; set; }
    }

    public sealed class FlightSafetyWarning
    {
        public string Code { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        public PointLatLng Location { get; set; }
        public string RelatedId { get; set; }
    }

    public sealed class RestrictionBriefingSummary
    {
        public int TfrNotamCount { get; set; }
        public int UasGridCells { get; set; }
        public int SpecialUseAreas { get; set; }
        public int MetarCount { get; set; }
        public int SigmetCount { get; set; }
        public int GairmetCount { get; set; }
        public int PirepCount { get; set; }
        public int FlightSafetyWarnings { get; set; }
    }
}
