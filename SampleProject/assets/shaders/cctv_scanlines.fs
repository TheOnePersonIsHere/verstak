#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

uniform float uTime = 0.0;
uniform float uIntensity = 1.0;
uniform vec2 uResolution = vec2(960.0, 540.0);

out vec4 finalColor;

void main()
{
    vec2 uv = fragTexCoord;

    // Subtle horizontal signal jitter scaled by intensity
    float wave = sin(uv.y * 42.0 + uTime * 5.0) * 0.0012 * uIntensity;
    vec4 texel = texture(texture0, uv + vec2(wave, 0.0)) * colDiffuse * fragColor;

    // Scanline bands
    float scan = sin(uv.y * max(120.0, uResolution.y * 1.35) + uTime * 3.0) * 0.5 + 0.5;
    float scanMask = 1.0 - clamp(uIntensity, 0.0, 2.0) * 0.14 * scan;

    // Vignette towards edges
    vec2 centered = uv * 2.0 - 1.0;
    float vignette = 1.0 - dot(centered, centered) * 0.22 * clamp(uIntensity, 0.0, 1.5);

    vec3 rgb = texel.rgb * scanMask * clamp(vignette, 0.25, 1.0);
    // Gentle cool desaturated monitor tint
    float luma = dot(rgb, vec3(0.299, 0.587, 0.114));
    vec3 monitorTint = mix(rgb, vec3(luma * 0.90, luma * 1.02, luma * 0.96), clamp(uIntensity * 0.45, 0.0, 0.85));

    finalColor = vec4(monitorTint, texel.a);
}
