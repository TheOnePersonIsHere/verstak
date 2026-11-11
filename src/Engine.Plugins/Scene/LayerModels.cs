using System.Numerics;
using System.Text.Json.Serialization;

namespace Engine.Plugins.Scene;

public static class LayerKinds
{
    public const string Sprite = "Sprite";
    public const string Rect = "Rect";
    public const string Text = "Text";
    public const string Group = "Group";
    public const string Surface = "Surface";

    public static readonly string[] All = [Sprite, Rect, Text, Group, Surface];
}

public sealed class LayerItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Layer";

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = LayerKinds.Sprite;

    [JsonPropertyName("assetPath")]
    public string AssetPath { get; set; } = string.Empty;

    [JsonPropertyName("surfaceKey")]
    public string SurfaceKey { get; set; } = string.Empty;

    [JsonPropertyName("shaderPath")]
    public string ShaderPath { get; set; } = string.Empty;

    [JsonPropertyName("shaderIntensity")]
    public float ShaderIntensity { get; set; } = 1.0f;

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("rotation")]
    public float Rotation { get; set; }

    [JsonPropertyName("scaleX")]
    public float ScaleX { get; set; } = 1.0f;

    [JsonPropertyName("scaleY")]
    public float ScaleY { get; set; } = 1.0f;

    [JsonPropertyName("originX")]
    public float OriginX { get; set; } = 0.5f;

    [JsonPropertyName("originY")]
    public float OriginY { get; set; } = 0.5f;

    [JsonPropertyName("width")]
    public float Width { get; set; }

    [JsonPropertyName("height")]
    public float Height { get; set; }

    [JsonPropertyName("colorR")]
    public byte ColorR { get; set; } = 255;

    [JsonPropertyName("colorG")]
    public byte ColorG { get; set; } = 255;

    [JsonPropertyName("colorB")]
    public byte ColorB { get; set; } = 255;

    [JsonPropertyName("colorA")]
    public byte ColorA { get; set; } = 255;

    [JsonPropertyName("zOrder")]
    public int ZOrder { get; set; }

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "Text";

    [JsonPropertyName("fontSize")]
    public int FontSize { get; set; } = 24;

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    public LayerItem Clone()
    {
        return new LayerItem
        {
            Id = Id,
            Name = Name,
            ParentId = ParentId,
            Kind = Kind,
            AssetPath = AssetPath,
            SurfaceKey = SurfaceKey,
            ShaderPath = ShaderPath,
            ShaderIntensity = ShaderIntensity,
            X = X,
            Y = Y,
            Rotation = Rotation,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            OriginX = OriginX,
            OriginY = OriginY,
            Width = Width,
            Height = Height,
            ColorR = ColorR,
            ColorG = ColorG,
            ColorB = ColorB,
            ColorA = ColorA,
            ZOrder = ZOrder,
            Visible = Visible,
            Locked = Locked,
            Text = Text,
            FontSize = FontSize,
            Tags = Tags != null ? new Dictionary<string, string>(Tags, StringComparer.Ordinal) : null
        };
    }
}

public sealed class LayerDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "main";

    [JsonPropertyName("items")]
    public List<LayerItem> Items { get; set; } = new();
}

public readonly record struct WorldLayerTransform(
    Vector2 Position,
    float RotationDegrees,
    Vector2 Scale);
