using System.Collections.Generic;
using Godot;

namespace Star;

/// <summary>灰烬星的地表、基地地坪、战争迷雾与沙暴光照。</summary>
internal sealed partial class AshLandscape : Node3D
{
    private const float GroundHeight = 0.14f;
    private const string TerrainShaderPath = "res://shaders/ash_terrain.gdshader";
    private static readonly Color ClearFog = new("#a67b59");
    private static readonly Color StormFog = new("#b77b4b");
    private static readonly Color ClearSun = new("#ffe2b4");
    private static readonly Color StormSun = new("#eaa474");

    private readonly HexMap _map;
    private readonly HashSet<HexCoord> _basePads;
    private readonly Vector2[] _corners = new Vector2[6];
    private readonly Image _cells;
    private readonly ImageTexture _cellTexture;
    private readonly ShaderMaterial _terrain;
    private readonly ShaderMaterial _surroundings;
    private readonly ShaderMaterial _dust;
    private readonly ColorRect _veil;
    private readonly Godot.Environment _environment;
    private readonly DirectionalLight3D _sun;
    private readonly MeshInstance3D _surface;
    private float _storm;

    public AshLandscape(HexMap map, IEnumerable<Building> buildings)
    {
        Name = "AshLandscape";
        _map = map;
        _basePads = new HashSet<HexCoord>();
        foreach (var building in buildings)
        {
            _basePads.Add(building.Coord);
        }

        for (int i = 0; i < _corners.Length; i++)
        {
            float angle = i * Mathf.Tau / _corners.Length;
            _corners[i] = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        int width = 2 * map.Radius + 1;
        _cells = Image.CreateEmpty(width, width, false, Image.Format.Rgba8);
        _cellTexture = ImageTexture.CreateFromImage(_cells);
        var shader = GD.Load<Shader>(TerrainShaderPath);
        _terrain = new ShaderMaterial { Shader = shader };
        _terrain.SetShaderParameter("cell_data", _cellTexture);
        _surroundings = new ShaderMaterial { Shader = shader };
        _surroundings.SetShaderParameter("charted", false);

        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#514436"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("#b7c2cd"),
            AmbientLightEnergy = 0.68f,
            FogEnabled = true,
            FogLightColor = ClearFog,
            FogDensity = 0.004f,
        };
        AddChild(new WorldEnvironment { Environment = _environment });
        _sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-46f, -34f, 0f),
            LightColor = ClearSun,
            LightEnergy = 1.35f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 94f,
        };
        AddChild(_sun);

        AddChild(new MeshInstance3D
        {
            Name = "OuterSands",
            Mesh = new PlaneMesh { Size = new Vector2(260f, 260f) },
            MaterialOverride = _surroundings,
            Position = new Vector3(0f, GroundHeight - 0.03f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _surface = new MeshInstance3D { Name = "HexTerrain", MaterialOverride = _terrain };
        AddChild(_surface);

        _dust = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ash_dust.gdshader") };
        var dustLayer = new CanvasLayer { Name = "WeatherDust", Layer = 0 };
        _veil = new ColorRect
        {
            Material = _dust,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        dustLayer.AddChild(_veil);
        AddChild(dustLayer);
        _veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    public void SyncTiles(HashSet<HexCoord> visible, HashSet<HexCoord> explored, bool inStorm)
    {
        bool upload = _surface.Mesh == null;
        bool rebuild = upload;
        float dimVision = inStorm ? 0.29f : 0.53f;
        foreach (var coord in _map.Cells())
        {
            float terrain = _map.IsBlocked(coord) ? 1f : _map.IsRuins(coord) ? 0.5f : 0f;
            float vision = visible.Contains(coord) ? 1f : explored.Contains(coord) ? dimVision : 0f;
            float pad = _basePads.Contains(coord) ? 1f : 0f;
            int x = coord.Q + _map.Radius;
            int y = coord.R + _map.Radius;
            Color previous = _cells.GetPixel(x, y);
            if (Mathf.Abs(previous.R - terrain) < 0.005f && Mathf.Abs(previous.G - vision) < 0.005f && previous.B == pad)
            {
                continue;
            }

            rebuild |= (previous.R > 0.8f) != (terrain > 0.8f);
            upload = true;
            _cells.SetPixel(x, y, new Color(terrain, vision, pad));
        }

        if (rebuild)
        {
            _surface.Mesh = BuildSurface();
        }

        if (upload)
        {
            _cellTexture.Update(_cells);
        }
    }

    public void UpdateWeather(float delta, bool inStorm, bool warning)
    {
        float target = inStorm ? 1f : warning ? 0.26f : 0f;
        float next = Mathf.MoveToward(_storm, target, delta * 0.52f);
        if (next == _storm)
        {
            return;
        }

        _storm = next;
        _environment.FogDensity = Mathf.Lerp(0.004f, 0.014f, _storm);
        _environment.FogLightColor = ClearFog.Lerp(StormFog, _storm);
        _environment.AmbientLightEnergy = Mathf.Lerp(0.68f, 0.53f, _storm);
        _sun.LightColor = ClearSun.Lerp(StormSun, _storm);
        _sun.LightEnergy = Mathf.Lerp(1.35f, 0.61f, _storm);
        _terrain.SetShaderParameter("storm", _storm);
        _surroundings.SetShaderParameter("storm", _storm);
        _veil.Visible = _storm > 0f;
        _dust.SetShaderParameter("storm", _storm);
    }

    public float SurfaceHeight(HexCoord coord) =>
        _map.IsBlocked(coord) ? RidgeHeight(coord) : GroundHeight;

    private ArrayMesh BuildSurface()
    {
        var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        float textureWidth = _cells.GetWidth();
        foreach (var coord in _map.Cells())
        {
            Vector3 center = _map.ToWorld(coord);
            var texel = new Vector2(coord.Q + _map.Radius + 0.5f, coord.R + _map.Radius + 0.5f) / textureWidth;
            bool ridge = _map.IsBlocked(coord);
            float height = SurfaceHeight(coord);
            float crown = ridge ? 0.69f : 1f;
            var summit = new Vector3(0f, height, 0f);
            mesh.SetUV2(texel);
            mesh.SetColor(new Color(Tone(coord), 0f, 0f));
            for (int i = 0; i < _corners.Length; i++)
            {
                Vector2 a = _corners[i];
                Vector2 b = _corners[(i + 1) % _corners.Length];
                Vector3 topA = ToVertex(a * crown, height);
                Vector3 topB = ToVertex(b * crown, height);
                AddTriangle(mesh, center, summit, topB, topA);
                if (ridge)
                {
                    Vector3 toeA = ToVertex(a, GroundHeight);
                    Vector3 toeB = ToVertex(b, GroundHeight);
                    AddTriangle(mesh, center, topA, topB, toeA);
                    AddTriangle(mesh, center, toeA, topB, toeB);
                }
            }
        }

        return mesh.Commit();
    }

    private static Vector3 ToVertex(Vector2 local, float height) =>
        new(local.X * HexMap.HexSize, height, local.Y * HexMap.HexSize);

    private static void AddTriangle(SurfaceTool mesh, Vector3 center, Vector3 a, Vector3 b, Vector3 c)
    {
        mesh.SetNormal((c - a).Cross(b - a).Normalized());
        AddVertex(mesh, center, a);
        AddVertex(mesh, center, b);
        AddVertex(mesh, center, c);
    }

    private static void AddVertex(SurfaceTool mesh, Vector3 center, Vector3 local)
    {
        mesh.SetUV(new Vector2(local.X, local.Z) / HexMap.HexSize);
        mesh.AddVertex(center + local);
    }

    private static float Tone(HexCoord coord) =>
        Mathf.PosMod(coord.Q * 73 + coord.R * 39, 23) / 22f;

    private static float RidgeHeight(HexCoord coord) => 0.83f + Tone(coord) * 0.49f;
}
