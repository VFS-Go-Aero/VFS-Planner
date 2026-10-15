using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using GMap.NET;
using log4net;

namespace MissionPlanner.Utilities.AviationLayers
{
    public interface IAviationDataProvider<T>
    {
        string SourceName { get; }
        TimeSpan CacheTtl { get; }

        Task<(T Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds, CancellationToken cancel);
    }

    public sealed class TfrDataProvider : IAviationDataProvider<TfrFetchResult>
    {
        static readonly ILog Log = LogManager.GetLogger(typeof(TfrDataProvider));
        readonly AviationDataCache<TfrFetchResult> _cache = new AviationDataCache<TfrFetchResult>();

        public string SourceName => AviationFetch.TfrWfsUrl;
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(3);

        public async Task<(TfrFetchResult Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds,
            CancellationToken cancel)
        {
            var key = AviationDataCache<TfrFetchResult>.BoundsKey(
                fetchBounds.Left, fetchBounds.Bottom, fetchBounds.Right, fetchBounds.Top);
            _cache.TimeToLive = CacheTtl;

            if (_cache.TryGet(key, out var cached, out var fetchedUtc))
            {
                return (cached, new LayerSnapshot
                {
                    LayerId = "tfr",
                    DisplayName = "TFRs",
                    Health = cached.Polygons.Count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                    FetchedAtUtc = fetchedUtc,
                    ExpiresAtUtc = fetchedUtc + CacheTtl,
                    Source = SourceName,
                    FeatureCount = cached.Polygons.Count
                });
            }

            try
            {
                var url = AviationFetch.BuildWfsUrl(AviationFetch.TfrWfsUrl, AviationFetch.TfrLayerName, fetchBounds);
                var json = await url.GetStringAsync(cancellationToken: cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();

                var polygons = AviationGeoJson.ParsePolygonFeatures(json);
                var briefing = AviationGeoJson.ParseTfrBriefingItems(json);
                var result = new TfrFetchResult { Json = json, Polygons = polygons, Briefing = briefing };
                _cache.Set(key, result);

                return (result, new LayerSnapshot
                {
                    LayerId = "tfr",
                    DisplayName = "TFRs",
                    Health = polygons.Count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                    FetchedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow + CacheTtl,
                    Source = SourceName,
                    FeatureCount = polygons.Count
                });
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("TFR fetch failed", ex);
                return (TfrFetchResult.Empty, new LayerSnapshot
                {
                    LayerId = "tfr",
                    DisplayName = "TFRs",
                    Health = LayerHealthState.Error,
                    Source = SourceName,
                    Message = ex.Message
                });
            }
        }
    }

    public sealed class TfrFetchResult
    {
        public static readonly TfrFetchResult Empty = new TfrFetchResult
        {
            Polygons = new List<MapPolygonFeature>(),
            Briefing = new List<NotamBriefingItem>()
        };

        public string Json { get; set; }
        public List<MapPolygonFeature> Polygons { get; set; } = new List<MapPolygonFeature>();
        public List<NotamBriefingItem> Briefing { get; set; } = new List<NotamBriefingItem>();
    }

    public sealed class PolygonFetchResult
    {
        public static readonly PolygonFetchResult Empty = new PolygonFetchResult();
        public List<MapPolygonFeature> Polygons { get; set; } = new List<MapPolygonFeature>();
    }

    public sealed class AirspaceDataProvider : IAviationDataProvider<PolygonFetchResult>
    {
        static readonly ILog Log = LogManager.GetLogger(typeof(AirspaceDataProvider));
        readonly AviationDataCache<PolygonFetchResult> _cache = new AviationDataCache<PolygonFetchResult>();

        public string SourceName => AviationFetch.AirspaceWfsUrl;
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);

        public async Task<(PolygonFetchResult Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds,
            CancellationToken cancel)
        {
            if (AviationFetch.UseOpenAipExport || OpenAipExportAirspace.IsLegacyDeadWfsUrl(AviationFetch.AirspaceWfsUrl))
            {
                return await FetchFromOpenAipExportAsync(fetchBounds, cancel).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(AviationFetch.AirspaceWfsUrl))
            {
                return await FetchFromOpenAipExportAsync(fetchBounds, cancel).ConfigureAwait(false);
            }

            var key = AviationDataCache<PolygonFetchResult>.BoundsKey(
                fetchBounds.Left, fetchBounds.Bottom, fetchBounds.Right, fetchBounds.Top);
            _cache.TimeToLive = CacheTtl;

            if (_cache.TryGet(key, out var cached, out var fetchedUtc))
                return (cached, Snapshot("airspace", "Airspace", cached.Polygons.Count, fetchedUtc));

            try
            {
                var url = AviationFetch.BuildWfsUrl(AviationFetch.AirspaceWfsUrl, AviationFetch.AirspaceLayerName,
                    fetchBounds);
                var json = await url.GetStringAsync(cancellationToken: cancel).ConfigureAwait(false);
                var polygons = AviationGeoJson.ParsePolygonFeatures(json);
                var result = new PolygonFetchResult { Polygons = polygons };
                _cache.Set(key, result);
                return (result, Snapshot("airspace", "Airspace", polygons.Count, DateTime.UtcNow));
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("Airspace WFS fetch failed, trying OpenAIP export", ex);
                return await FetchFromOpenAipExportAsync(fetchBounds, cancel).ConfigureAwait(false);
            }
        }

        async Task<(PolygonFetchResult Data, LayerSnapshot Snapshot)> FetchFromOpenAipExportAsync(
            RectLatLng fetchBounds, CancellationToken cancel)
        {
            try
            {
                var polygons = await OpenAipExportAirspace.GetFeaturesInBoundsAsync(fetchBounds, cancel)
                    .ConfigureAwait(false);
                var cc = OpenAipExportAirspace.CountryCodeForView(fetchBounds);
                var result = new PolygonFetchResult { Polygons = polygons };
                return (result, new LayerSnapshot
                {
                    LayerId = "airspace",
                    DisplayName = "Airspace",
                    Health = polygons.Count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                    FetchedAtUtc = DateTime.UtcNow,
                    Source = "OpenAIP export (" + cc + ")",
                    FeatureCount = polygons.Count,
                    Message = polygons.Count == 0
                        ? "No airspace in view (first load may download ~30MB US dataset)"
                        : null
                });
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("OpenAIP export airspace failed", ex);
                return (PolygonFetchResult.Empty, new LayerSnapshot
                {
                    LayerId = "airspace",
                    DisplayName = "Airspace",
                    Health = LayerHealthState.Error,
                    Source = "OpenAIP export",
                    Message = AviationLayerErrors.Shorten(ex)
                });
            }
        }

        static LayerSnapshot Snapshot(string id, string name, int count, DateTime fetchedUtc) =>
            new LayerSnapshot
            {
                LayerId = id,
                DisplayName = name,
                Health = count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                FetchedAtUtc = fetchedUtc,
                Source = AviationFetch.AirspaceWfsUrl,
                FeatureCount = count
            };
    }

    public sealed class UasFacilityDataProvider : IAviationDataProvider<PolygonFetchResult>
    {
        static readonly ILog Log = LogManager.GetLogger(typeof(UasFacilityDataProvider));
        readonly AviationDataCache<PolygonFetchResult> _cache = new AviationDataCache<PolygonFetchResult>();

        public string SourceName => AviationFetch.UasFacilityMapUrl;
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

        public async Task<(PolygonFetchResult Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds,
            CancellationToken cancel)
        {
            var key = AviationDataCache<PolygonFetchResult>.BoundsKey(
                fetchBounds.Left, fetchBounds.Bottom, fetchBounds.Right, fetchBounds.Top);
            _cache.TimeToLive = CacheTtl;

            if (_cache.TryGet(key, out var cached, out var fetchedUtc))
                return (cached, Ok("uas", "LAANC grid", cached.Polygons.Count, fetchedUtc));

            try
            {
                var max = AviationBounds.MaxArcGisFeatures(fetchBounds);
                var url = AviationFetch.BuildArcGisQueryUrl(AviationFetch.UasFacilityMapUrl, fetchBounds, max);
                var json = await url.GetStringAsync(cancellationToken: cancel).ConfigureAwait(false);
                var polygons = AviationGeoJson.ParseArcGisPolygons(json, (props, id) =>
                {
                    var ceiling = props["CEILING"]?.ToObject<int?>() ?? 0;
                    var apt = props["APT1_NAME"]?.ToString() ?? props["APT1_ICAO"]?.ToString() ?? "";
                    var unit = props["UNIT"]?.ToString() ?? "ft";
                    var grid = props["GRID_ID"]?.ToString() ?? id;
                    return string.IsNullOrEmpty(apt)
                        ? $"Grid {grid}: max {ceiling} {unit} AGL"
                        : $"{apt} · grid {grid}: max {ceiling} {unit} AGL";
                }, LaancCellDetails.ApplyFromProperties);
                var result = new PolygonFetchResult { Polygons = polygons };
                _cache.Set(key, result);
                return (result, Ok("uas", "LAANC grid", polygons.Count, DateTime.UtcNow));
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("UAS facility map fetch failed", ex);
                return (PolygonFetchResult.Empty, Error("uas", "LAANC grid", ex.Message));
            }
        }

        static LayerSnapshot Ok(string id, string name, int count, DateTime fetchedUtc) =>
            new LayerSnapshot
            {
                LayerId = id,
                DisplayName = name,
                Health = count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                FetchedAtUtc = fetchedUtc,
                Source = AviationFetch.UasFacilityMapUrl,
                FeatureCount = count
            };

        static LayerSnapshot Error(string id, string name, string msg) =>
            new LayerSnapshot
            {
                LayerId = id,
                DisplayName = name,
                Health = LayerHealthState.Error,
                Message = msg
            };
    }

    public sealed class SpecialUseDataProvider : IAviationDataProvider<PolygonFetchResult>
    {
        static readonly ILog Log = LogManager.GetLogger(typeof(SpecialUseDataProvider));
        readonly AviationDataCache<PolygonFetchResult> _cache = new AviationDataCache<PolygonFetchResult>();

        public string SourceName => "OpenAIP+DoD";
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);

        public async Task<(PolygonFetchResult Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds,
            CancellationToken cancel)
        {
            var key = AviationDataCache<PolygonFetchResult>.BoundsKey(
                fetchBounds.Left, fetchBounds.Bottom, fetchBounds.Right, fetchBounds.Top);
            _cache.TimeToLive = CacheTtl;

            if (_cache.TryGet(key, out var cached, out var fetchedUtc))
                return (cached, Ok(cached.Polygons.Count, fetchedUtc));

            var merged = new List<MapPolygonFeature>();
            try
            {
                var openAip = await OpenAipExportAirspace.GetFeaturesInBoundsAsync(fetchBounds, cancel)
                    .ConfigureAwait(false);
                merged.AddRange(openAip.Where(AviationGeoJson.IsSpecialUseFeature));

                cancel.ThrowIfCancellationRequested();

                var dodUrl = AviationFetch.BuildArcGisQueryUrl(AviationFetch.DodUasRestrictionUrl, fetchBounds, 500);
                var dodJson = await dodUrl.GetStringAsync(cancellationToken: cancel).ConfigureAwait(false);
                merged.AddRange(AviationGeoJson.ParseArcGisPolygons(dodJson, (props, id) =>
                    "DoD UAS restriction: " + (props["NAME"]?.ToString() ?? props["Title"]?.ToString() ?? id)));

                var result = new PolygonFetchResult { Polygons = merged };
                _cache.Set(key, result);
                return (result, Ok(merged.Count, DateTime.UtcNow));
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("Special-use airspace fetch failed", ex);
                return (PolygonFetchResult.Empty, new LayerSnapshot
                {
                    LayerId = "specialuse",
                    DisplayName = "Special use",
                    Health = LayerHealthState.Error,
                    Message = AviationLayerErrors.Shorten(ex)
                });
            }
        }

        static LayerSnapshot Ok(int count, DateTime fetchedUtc) =>
            new LayerSnapshot
            {
                LayerId = "specialuse",
                DisplayName = "Special use",
                Health = count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                FetchedAtUtc = fetchedUtc,
                FeatureCount = count
            };
    }

    public sealed class MetarDataProvider : IAviationDataProvider<List<MetarObservation>>
    {
        static readonly ILog Log = LogManager.GetLogger(typeof(MetarDataProvider));
        readonly AviationDataCache<List<MetarObservation>> _cache = new AviationDataCache<List<MetarObservation>>();

        public string SourceName => AviationFetch.MetarApiBase;
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(2);

        public async Task<(List<MetarObservation> Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds,
            CancellationToken cancel)
        {
            var key = AviationDataCache<List<MetarObservation>>.BoundsKey(
                fetchBounds.Left, fetchBounds.Bottom, fetchBounds.Right, fetchBounds.Top);
            _cache.TimeToLive = CacheTtl;

            if (_cache.TryGet(key, out var cached, out var fetchedUtc))
                return (cached, MetarSnapshot(cached, fetchedUtc));

            try
            {
                var url = AviationFetch.BuildMetarUrl(fetchBounds);
                var json = await url.GetStringAsync(cancellationToken: cancel).ConfigureAwait(false);
                var list = AviationGeoJson.ParseMetarJson(json);
                _cache.Set(key, list);
                return (list, MetarSnapshot(list, DateTime.UtcNow));
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("METAR fetch failed", ex);
                return (new List<MetarObservation>(), new LayerSnapshot
                {
                    LayerId = "metar",
                    DisplayName = "METAR",
                    Health = LayerHealthState.Error,
                    Source = SourceName,
                    Message = ex.Message
                });
            }
        }

        static LayerSnapshot MetarSnapshot(List<MetarObservation> list, DateTime fetchedUtc) =>
            new LayerSnapshot
            {
                LayerId = "metar",
                DisplayName = "METAR",
                Health = list.Count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                FetchedAtUtc = fetchedUtc,
                Source = AviationFetch.MetarApiBase,
                FeatureCount = list.Count
            };
    }

    public sealed class SigmetDataProvider : IAviationDataProvider<PolygonFetchResult>
    {
        static readonly ILog Log = LogManager.GetLogger(typeof(SigmetDataProvider));
        readonly AviationDataCache<PolygonFetchResult> _cache = new AviationDataCache<PolygonFetchResult>();

        public string SourceName => AviationFetch.SigmetApiBase;
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

        public async Task<(PolygonFetchResult Data, LayerSnapshot Snapshot)> FetchAsync(RectLatLng fetchBounds,
            CancellationToken cancel)
        {
            var key = AviationDataCache<PolygonFetchResult>.BoundsKey(
                fetchBounds.Left, fetchBounds.Bottom, fetchBounds.Right, fetchBounds.Top);
            _cache.TimeToLive = CacheTtl;

            if (_cache.TryGet(key, out var cached, out var fetchedUtc))
                return (cached, SigSnapshot(cached.Polygons.Count, fetchedUtc));

            try
            {
                var url = AviationFetch.BuildSigmetUrl(fetchBounds);
                var json = await url.GetStringAsync(cancellationToken: cancel).ConfigureAwait(false);
                var polygons = AviationGeoJson.ParsePolygonFeatures(json);
                var result = new PolygonFetchResult { Polygons = polygons };
                _cache.Set(key, result);
                return (result, SigSnapshot(polygons.Count, DateTime.UtcNow));
            }
            catch (Exception ex) when (!AviationLayerErrors.IsBenignCancel(ex))
            {
                Log.Warn("SIGMET fetch failed", ex);
                return (PolygonFetchResult.Empty, new LayerSnapshot
                {
                    LayerId = "sigmet",
                    DisplayName = "SIGMET",
                    Health = LayerHealthState.Error,
                    Source = SourceName,
                    Message = ex.Message
                });
            }
        }

        static LayerSnapshot SigSnapshot(int count, DateTime fetchedUtc) =>
            new LayerSnapshot
            {
                LayerId = "sigmet",
                DisplayName = "SIGMET",
                Health = count > 0 ? LayerHealthState.Ok : LayerHealthState.Empty,
                FetchedAtUtc = fetchedUtc,
                Source = AviationFetch.SigmetApiBase,
                FeatureCount = count
            };
    }
}
