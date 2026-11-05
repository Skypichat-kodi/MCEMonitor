using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;        
using Mapsui;
using Mapsui.Extensions;     
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;   
using NetTopologySuite.Geometries;

namespace NetMonitor.UI.Controls
{
    public partial class CountriesMapPanel : UserControl
    {
        private readonly MemoryLayer _choroplethLayer;
        private List<CountryFeature>? _features;
        private Dictionary<string, int> _dataMap = new();
        private string _mode = "active";
        private DateTime _lastHoverTime = DateTime.MinValue;

        public CountriesMapPanel()
        {
            InitializeComponent();

            _choroplethLayer = new MemoryLayer
            {
                Name = "Countries",
                Features = Array.Empty<IFeature>()
            };

            InitializeMap();

            // Chargement lazy au premier Update
            Loaded += (_, __) => EnsureGeoJsonLoaded();
        }

        private void InitializeMap()
        {
            var map = new Mapsui.Map
            {
                BackColor = Mapsui.Styles.Color.FromString("#1A1A1A")
            };

            map.Layers.Add(_choroplethLayer);

            // Bounds limités : monde jusqu'à -58° latitude (juste sous l'Amérique du Sud)
            // Y max = 85° N (au lieu de 85.05° pour éviter une ligne blanche du pôle)
            // Y min = -58° S → coupe l'Antarctique
            var worldBounds = new MRect(
                -20037508.34,   // -180° longitude
                -7970000,       // -58° latitude
                 20037508.34,   // +180° longitude
                 19970000);     // ~85° latitude

            map.Navigator.ZoomToBox(worldBounds);
            map.Navigator.OverridePanBounds = worldBounds;   // empêche de scroller sous le pôle Sud

            MapControl.Map = map;

            MapControl.MouseMove += OnMouseMove;
            MapControl.MouseLeave += (_, __) =>
            {
                TooltipBorder.Visibility = Visibility.Collapsed;
            };
        }

        /// <summary>
        /// Met à jour le choroplèthe avec les données actives ou bloquées.
        /// </summary>
        public void Update(StatsBlockDto block, string mode)
        {
            _mode = mode;
            _dataMap = block?.AlertsByCountryCode ?? new Dictionary<string, int>();

            EnsureGeoJsonLoaded();
            StyleFeatures();
        }

        // ────────────────────────────────────────────────────────────
        //  Chargement du GeoJSON (une seule fois)
        // ────────────────────────────────────────────────────────────
        private void EnsureGeoJsonLoaded()
        {
            if (_features != null) return;

            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Data", "countries.geojson");

                System.Diagnostics.Debug.WriteLine($"[MAP] Chemin GeoJSON : {path}");
                System.Diagnostics.Debug.WriteLine($"[MAP] Existe : {File.Exists(path)}");

                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine($"[MAP] ❌ FICHIER INTROUVABLE");
                    return;
                }

                string json = File.ReadAllText(path);
                _features = ParseGeoJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CountriesMapPanel.LoadGeoJson : {ex.Message}");
            }
        }

        private static List<CountryFeature> ParseGeoJson(string json)
        {
            var features = new List<CountryFeature>();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var featuresEl))
                return features;

            foreach (var f in featuresEl.EnumerateArray())
            {
                if (!f.TryGetProperty("properties", out var props)) continue;
                if (!f.TryGetProperty("geometry", out var geom)) continue;

                string? code = null, name = null;

                // Code pays : essaie plusieurs clés (même logique que le web)
                foreach (var key in new[] { "ISO_A2", "ISO_A2_EH", "ADM0_A3", "SOV_A3", "WB_A2" })
                {
                    if (props.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        var val = p.GetString();
                        if (!string.IsNullOrEmpty(val) && val != "-99") { code = val; break; }
                    }
                }
                if (code == null) continue;

                foreach (var key in new[] { "ADMIN", "NAME", "NAME_LONG" })
                {
                    if (props.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        name = p.GetString();
                        if (!string.IsNullOrEmpty(name)) break;
                    }
                }

                if (!geom.TryGetProperty("type", out var typeEl)) continue;
                if (!geom.TryGetProperty("coordinates", out var coordsEl)) continue;

                string type = typeEl.GetString() ?? "";
                Geometry? geometry = type switch
                {
                    "Polygon"      => ParsePolygon(coordsEl),
                    "MultiPolygon" => ParseMultiPolygon(coordsEl),
                    _              => null
                };
                if (geometry == null) continue;

                features.Add(new CountryFeature
                {
                    Code = code,
                    Name = name ?? code,
                    Geometry = geometry
                });
            }
            
            System.Diagnostics.Debug.WriteLine($"[MAP] ParseGeoJson : {features.Count} pays parsés");
            return features;
        }

        private static Polygon? ParsePolygon(JsonElement coordsEl)
        {
            var rings = new List<LinearRing>();
            foreach (var ringEl in coordsEl.EnumerateArray())
            {
                var coordsList = new List<Coordinate>();
                foreach (var pt in ringEl.EnumerateArray())
                {
                    var arr = pt.EnumerateArray().ToArray();
                    if (arr.Length < 2) continue;
                    double lon = arr[0].GetDouble();
                    double lat = arr[1].GetDouble();
                    var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
                    coordsList.Add(new Coordinate(mx, my));
                }

                if (coordsList.Count < 3) continue;
                // Ferme le ring si nécessaire
                if (!coordsList[0].Equals2D(coordsList[^1]))
                    coordsList.Add(coordsList[0].Copy());

                rings.Add(new LinearRing(coordsList.ToArray()));
            }

            if (rings.Count == 0) return null;
            return new Polygon(rings[0], rings.Skip(1).ToArray());
        }

        private static MultiPolygon? ParseMultiPolygon(JsonElement coordsEl)
        {
            var polygons = new List<Polygon>();
            foreach (var polyEl in coordsEl.EnumerateArray())
            {
                var poly = ParsePolygon(polyEl);
                if (poly != null) polygons.Add(poly);
            }
            if (polygons.Count == 0) return null;
            return new MultiPolygon(polygons.ToArray());
        }

        // ────────────────────────────────────────────────────────────
        //  Style des pays selon le nombre d'alertes
        // ────────────────────────────────────────────────────────────
        private void StyleFeatures()
        {
            System.Diagnostics.Debug.WriteLine($"[MAP] StyleFeatures: features={_features?.Count ?? 0}, dataMap={_dataMap.Count} pays");

            if (_features == null || _features.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[MAP] ❌ Aucun feature chargé");
                return;
            }

            var features = new List<IFeature>(_features.Count);

            foreach (var cf in _features)
            {
                int count = _dataMap.TryGetValue(cf.Code, out var c) ? c : 0;
                var fill = GetColor(count);

                var feature = new GeometryFeature(cf.Geometry)
                {
                    ["code"]  = cf.Code,
                    ["name"]  = cf.Name,
                    ["count"] = count
                };

                feature.Styles.Add(new VectorStyle
                {
                    Fill    = new Brush(fill),
                    Outline = new Pen(Mapsui.Styles.Color.FromString("#666666"), 1)  // contour visible
                });

                features.Add(feature);
            }

            _choroplethLayer.Features = features;
            _choroplethLayer.DataHasChanged();
        }

        private Mapsui.Styles.Color GetColor(int count)
        {
            if (count <= 0) return Mapsui.Styles.Color.FromString("#454545");  // gris pale visible

            if (_mode == "active")
            {
                if (count >= 10) return Mapsui.Styles.Color.FromString("#B10026");
                if (count >= 6)  return Mapsui.Styles.Color.FromString("#F03B20");
                if (count >= 2)  return Mapsui.Styles.Color.FromString("#FEB24C");
                return Mapsui.Styles.Color.FromString("#FFFFB2");
            }
            else
            {
                if (count >= 1000) return Mapsui.Styles.Color.FromString("#005A32");
                if (count >= 500)  return Mapsui.Styles.Color.FromString("#238B45");
                if (count >= 100)  return Mapsui.Styles.Color.FromString("#41AB5D");
                if (count >= 50)   return Mapsui.Styles.Color.FromString("#74C476");
                if (count >= 10)   return Mapsui.Styles.Color.FromString("#A1D99B");
                if (count >= 2)    return Mapsui.Styles.Color.FromString("#C7E9C0");
                return Mapsui.Styles.Color.FromString("#E5F5E0");
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Tooltip au survol
        // ────────────────────────────────────────────────────────────
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            // Throttle 100 ms
            if ((DateTime.Now - _lastHoverTime).TotalMilliseconds < 100) return;
            _lastHoverTime = DateTime.Now;

            try
            {
                var pos = e.GetPosition(MapControl);
                var viewport = MapControl.Map.Navigator.Viewport;
                var worldPoint = viewport.ScreenToWorld(pos.X, pos.Y);

                // Hit test manuel : premier pays qui contient le point
                CountryFeature? hit = null;
                if (_features != null)
                {
                    foreach (var cf in _features)
                    {
                        var ntsPoint = new NetTopologySuite.Geometries.Point(worldPoint.X, worldPoint.Y);
                        if (cf.Geometry != null && cf.Geometry.Contains(ntsPoint))
                        {
                            hit = cf;
                            break;
                        }
                    }
                }

                if (hit != null)
                {
                    int count = _dataMap.TryGetValue(hit.Code, out var c) ? c : 0;
                    string unit = _mode == "active" ? "alerte(s)" : "IP bloquée(s)";

                    TooltipText.Text = $"{hit.Name}\n{count} {unit}";
                    TooltipBorder.Visibility = Visibility.Visible;
                    TooltipBorder.Margin = new Thickness(pos.X + 15, pos.Y + 15, 0, 0);
                }
                else
                {
                    TooltipBorder.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                // Silencieux : peut arriver si la carte n'est pas encore prête
            }
        }

        private class CountryFeature
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public Geometry Geometry { get; set; } = null!;
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            MapControl.Map.Navigator.ZoomIn();
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            MapControl.Map.Navigator.ZoomOut();
        }        
    }
}