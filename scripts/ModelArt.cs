using Godot;

namespace Star;

internal static class ModelArt
{
    private static readonly StandardMaterial3D PaintedMetal = new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.86f,
    };

    public static Node3D Instantiate(string asset)
    {
        var model = GD.Load<PackedScene>($"res://assets/models/{asset}.glb").Instantiate<Node3D>();
        if (model is MeshInstance3D meshRoot)
        {
            meshRoot.MaterialOverride = PaintedMetal;
        }

        foreach (var node in model.FindChildren("*", nameof(MeshInstance3D), owned: false))
        {
            ((MeshInstance3D)node).MaterialOverride = PaintedMetal;
        }

        return model;
    }
}
