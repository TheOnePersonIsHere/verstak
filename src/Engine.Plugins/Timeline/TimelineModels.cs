using System.Text.Json.Serialization;

namespace Engine.Plugins.Timeline;

public sealed class PropertyKeyframe
{
    [JsonPropertyName("time")]
    public float Time { get; set; }

    [JsonPropertyName("value")]
    public float Value { get; set; }

    [JsonPropertyName("easing")]
    public string Easing { get; set; } = "Linear";
}

public sealed class PropertyTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("targetProperty")]
    public string TargetProperty { get; set; } = string.Empty;

    [JsonPropertyName("keyframes")]
    public List<PropertyKeyframe> Keyframes { get; set; } = new();

    public float Evaluate(float time)
    {
        if (Keyframes.Count == 0)
        {
            return 0f;
        }

        if (Keyframes.Count == 1 || time <= Keyframes[0].Time)
        {
            return Keyframes[0].Value;
        }

        PropertyKeyframe last = Keyframes[^1];
        if (time >= last.Time)
        {
            return last.Value;
        }

        for (int i = 0; i < Keyframes.Count - 1; i++)
        {
            PropertyKeyframe a = Keyframes[i];
            PropertyKeyframe b = Keyframes[i + 1];
            if (time >= a.Time && time <= b.Time)
            {
                float span = b.Time - a.Time;
                if (span <= 1e-6f)
                {
                    return b.Value;
                }

                float t = Math.Clamp((time - a.Time) / span, 0f, 1f);
                float eased = ApplyEasing(t, a.Easing);
                return a.Value + (b.Value - a.Value) * eased;
            }
        }

        return last.Value;
    }

    public void SortKeyframes()
    {
        Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static float ApplyEasing(float t, string easing)
    {
        if (string.Equals(easing, "Step", StringComparison.OrdinalIgnoreCase))
        {
            return 0f;
        }

        if (string.Equals(easing, "SmoothStep", StringComparison.OrdinalIgnoreCase))
        {
            return t * t * (3f - 2f * t);
        }

        return t;
    }
}

public sealed class TimelineTrigger
{
    [JsonPropertyName("time")]
    public float Time { get; set; }

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "sound";

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("argument")]
    public string? Argument { get; set; }

    [JsonPropertyName("volume")]
    public float Volume { get; set; } = 1.0f;

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; } = 1.0f;
}

public sealed class TriggerTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("triggers")]
    public List<TimelineTrigger> Triggers { get; set; } = new();

    public void SortTriggers()
    {
        Triggers.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
}

public sealed class TimelineConfig
{
    [JsonPropertyName("maxDuration")]
    public float MaxDuration { get; set; } = 4.0f;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    [JsonPropertyName("propertyTracks")]
    public List<PropertyTrack> PropertyTracks { get; set; } = new();

    [JsonPropertyName("triggerTracks")]
    public List<TriggerTrack> TriggerTracks { get; set; } = new();
}

public sealed class DisplayConfig
{
    [JsonPropertyName("virtualWidth")]
    public int? VirtualWidth { get; set; }

    [JsonPropertyName("virtualHeight")]
    public int? VirtualHeight { get; set; }

    [JsonPropertyName("scalingMode")]
    public string? ScalingMode { get; set; }

    [JsonPropertyName("snapping")]
    public string? Snapping { get; set; }
}

public sealed class ProjectNodeConfig
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("properties")]
    public Dictionary<string, string>? Properties { get; set; }
}

public sealed record TimelineUpsertKeyframeEvent(
    string TargetProperty,
    string DisplayName,
    float Value,
    float? TimeOverride = null,
    string Easing = "SmoothStep",
    bool SaveProjectJson = true);

public sealed class ProjectDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "SampleProject";

    [JsonPropertyName("virtualWidth")]
    public int VirtualWidth { get; set; } = 960;

    [JsonPropertyName("virtualHeight")]
    public int VirtualHeight { get; set; } = 540;

    [JsonPropertyName("display")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DisplayConfig? Display { get; set; }

    [JsonPropertyName("audioBuses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, float>? AudioBuses { get; set; }

    [JsonPropertyName("nodes")]
    public List<ProjectNodeConfig> Nodes { get; set; } = new();

    [JsonPropertyName("timeline")]
    public TimelineConfig Timeline { get; set; } = new();
}
