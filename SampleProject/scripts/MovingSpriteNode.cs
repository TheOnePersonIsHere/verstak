using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Engine.Core.Abstractions;
using Raylib_cs;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace SampleProject.Scripts;

public sealed class MovingSpriteNode : IExecutableNode
{
    private ExecutionContext? _context;
    private float _pulseFlash;
    private string? _lastConfiguredSprite;
    private string? _cachedSpritePath;
    private float _fallbackScanCooldown;
    private bool _hasSceneDocument;

    public string NodeId => "moving_sprite_demo";

    public void OnInit(ExecutionContext context)
    {
        _context = context;
        _lastConfiguredSprite = null;
        _cachedSpritePath = null;
        _fallbackScanCooldown = 0f;
        _hasSceneDocument = context.Workspace.EnumerateFiles("scenes", "*.json", recursive: true).Any();
    }

    public void OnBeat(string? argument)
    {
        _pulseFlash = 1.0f;
    }

    public void OnUpdate(float deltaTime)
    {
        if (_pulseFlash > 0f)
        {
            _pulseFlash = MathF.Max(0f, _pulseFlash - deltaTime * 2.5f);
        }

        if (_fallbackScanCooldown > 0f)
        {
            _fallbackScanCooldown = MathF.Max(0f, _fallbackScanCooldown - deltaTime);
        }
    }

    public void OnDraw(IGraphicsService graphics)
    {
        if (_context == null)
        {
            return;
        }

        int width = _context.VirtualWidth;
        int height = _context.VirtualHeight;

        string? spritePath = ResolveSpritePath();

        // When no scene JSON document exists, render the sprite directly as a script-only fallback.
        if (!_hasSceneDocument && !string.IsNullOrWhiteSpace(spritePath))
        {
            float x = _context.GetFloat("sprite.x", width * 0.5f);
            float y = _context.GetFloat("sprite.y", height * 0.5f);
            float rotation = _context.GetFloat("sprite.rotation", 0f);
            float timelineScale = _context.GetFloat("sprite.scale", 0.72f);

            float finalScale = timelineScale * (1.0f + _pulseFlash * 0.04f);
            Color tint = _pulseFlash > 0.01f
                ? new Color((byte)255, (byte)245, (byte)215, (byte)255)
                : Color.White;

            graphics.DrawTextureEx(
                spritePath,
                new Vector2(MathF.Round(x), MathF.Round(y)),
                rotation,
                new Vector2(finalScale, finalScale),
                tint);
        }

        if (_pulseFlash > 0.01f)
        {
            byte alpha = (byte)Math.Clamp(_pulseFlash * 180f, 0f, 255f);
            graphics.DrawRectangleLines(6, 6, Math.Max(1, width - 12), Math.Max(1, height - 12), new Color((byte)110, (byte)190, (byte)255, alpha));
        }
    }

    private string? ResolveSpritePath()
    {
        if (_context == null)
        {
            return null;
        }

        string configured = _context.GetString("sprite.path");
        if (!string.Equals(configured, _lastConfiguredSprite, StringComparison.Ordinal))
        {
            _lastConfiguredSprite = configured;
            if (!string.IsNullOrWhiteSpace(configured) &&
                File.Exists(_context.Workspace.ResolvePath(configured)))
            {
                _cachedSpritePath = configured;
                return _cachedSpritePath;
            }

            _cachedSpritePath = null;
            _fallbackScanCooldown = 0f;
        }

        if (!string.IsNullOrWhiteSpace(_cachedSpritePath))
        {
            return _cachedSpritePath;
        }

        if (_fallbackScanCooldown > 0f)
        {
            return null;
        }

        _fallbackScanCooldown = 1.0f;
        string? fallback = _context.Workspace
            .EnumerateFiles("assets/sprites", "*.*", recursive: true)
            .FirstOrDefault(p =>
                p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

        _cachedSpritePath = fallback != null ? _context.Workspace.ToRelativePath(fallback) : null;
        return _cachedSpritePath;
    }
}
