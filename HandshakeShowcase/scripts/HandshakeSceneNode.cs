using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Engine.Core.Abstractions;
using Raylib_cs;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace HandshakeShowcase.Scripts;

public sealed class HandshakeSceneNode : IExecutableNode
{
    private const float GroundY = 502f;
    private const float StageCenterX = 640f;
    private const float ClaspBaseY = 388f;
    private const float LogoTargetMaxSizePx = 228f;

    private ExecutionContext? _context;
    private float _elapsedWallTime;

    private float _contactPulse;
    private float _revealPulse;

    private float _lastSampleTime;
    private float _lastLeftX = 220f;
    private float _lastRightX = 1060f;
    private float _leftVelX;
    private float _rightVelX;
    private float _leftWalkBlend;
    private float _rightWalkBlend;
    private float _leftFacing = 1f;
    private float _rightFacing = -1f;

    private string? _lastConfiguredLogo;
    private string? _cachedLogoPath;
    private float _scanCooldown;

    public string NodeId => "handshake_scene";

    public void OnInit(ExecutionContext context)
    {
        _context = context;
        _elapsedWallTime = 0f;
        _contactPulse = 0f;
        _revealPulse = 0f;

        _lastSampleTime = context.CurrentTime;
        _lastLeftX = context.GetFloat("guyLeft.x", 220f);
        _lastRightX = context.GetFloat("guyRight.x", 1060f);
        _leftVelX = 0f;
        _rightVelX = 0f;
        _leftWalkBlend = 0f;
        _rightWalkBlend = 0f;
        _leftFacing = _lastSampleTime >= 2.70f ? -1f : 1f;
        _rightFacing = _lastSampleTime >= 2.70f ? 1f : -1f;

        _lastConfiguredLogo = null;
        _cachedLogoPath = null;
        _scanCooldown = 0f;
    }

    public void OnHandshakeContact(string? argument)
    {
        _contactPulse = 1.0f;
    }

    public void OnLogoReveal(string? argument)
    {
        _revealPulse = 1.0f;
    }

    public void OnUpdate(float deltaTime)
    {
        if (_context == null)
        {
            return;
        }

        if (deltaTime > 0f)
        {
            _elapsedWallTime += deltaTime;
        }

        if (_contactPulse > 0f && deltaTime > 0f)
        {
            _contactPulse = MathF.Max(0f, _contactPulse - deltaTime * 1.45f);
        }

        if (_revealPulse > 0f && deltaTime > 0f)
        {
            _revealPulse = MathF.Max(0f, _revealPulse - deltaTime * 1.15f);
        }

        if (_scanCooldown > 0f && deltaTime > 0f)
        {
            _scanCooldown = MathF.Max(0f, _scanCooldown - deltaTime);
        }

        float t = _context.CurrentTime;
        if (t < _lastSampleTime - 0.1f)
        {
            // Timeline looped or scrubbed backwards: clear stale transient pulses
            if (t < 1.75f)
            {
                _contactPulse = 0f;
            }

            if (t < 2.62f)
            {
                _revealPulse = 0f;
            }
        }
        else if (deltaTime <= 0f)
        {
            if (t < 1.75f) _contactPulse = 0f;
            if (t < 2.62f) _revealPulse = 0f;
        }

        float leftX = _context.GetFloat("guyLeft.x", 220f);
        float rightX = _context.GetFloat("guyRight.x", 1060f);

        float dtSample = t - _lastSampleTime;
        if (MathF.Abs(dtSample) > 1e-4f && MathF.Abs(dtSample) < 0.35f)
        {
            float instLeftVel = (leftX - _lastLeftX) / dtSample;
            float instRightVel = (rightX - _lastRightX) / dtSample;

            if (deltaTime > 0f)
            {
                float smooth = Math.Clamp(deltaTime * 16f, 0f, 1f);
                _leftVelX += (instLeftVel - _leftVelX) * smooth;
                _rightVelX += (instRightVel - _rightVelX) * smooth;
            }
            else
            {
                _leftVelX = instLeftVel;
                _rightVelX = instRightVel;
            }
        }
        else if (deltaTime <= 0f)
        {
            // Parked on manual scrub or pause at exact t: compute analytical walk intensity on keyframe windows so pose never glitches.
            _leftVelX = EstimateAnalyticalDirection(t, isLeft: true) * (IsWalkingPhase(t) ? 160f : 0f);
            _rightVelX = EstimateAnalyticalDirection(t, isLeft: false) * (IsWalkingPhase(t) ? 160f : 0f);
        }

        float targetLeftWalk = Math.Clamp(MathF.Abs(_leftVelX) / 95f, 0f, 1f);
        float targetRightWalk = Math.Clamp(MathF.Abs(_rightVelX) / 95f, 0f, 1f);

        if (deltaTime > 0f)
        {
            float walkSmooth = Math.Clamp(deltaTime * 14f, 0f, 1f);
            _leftWalkBlend += (targetLeftWalk - _leftWalkBlend) * walkSmooth;
            _rightWalkBlend += (targetRightWalk - _rightWalkBlend) * walkSmooth;
        }
        else
        {
            _leftWalkBlend = targetLeftWalk;
            _rightWalkBlend = targetRightWalk;
        }

        float targetLeftFacing = ResolveTargetFacing(t, _leftVelX, defaultApproach: 1f);
        float targetRightFacing = ResolveTargetFacing(t, _rightVelX, defaultApproach: -1f);

        if (deltaTime > 0f)
        {
            float turnRate = Math.Clamp(deltaTime * 14f, 0f, 1f);
            _leftFacing += (targetLeftFacing - _leftFacing) * turnRate;
            _rightFacing += (targetRightFacing - _rightFacing) * turnRate;
        }
        else
        {
            _leftFacing = targetLeftFacing;
            _rightFacing = targetRightFacing;
        }

        _lastSampleTime = t;
        _lastLeftX = leftX;
        _lastRightX = rightX;
    }

    public void OnDraw(IGraphicsService graphics)
    {
        if (_context == null)
        {
            return;
        }

        float t = _context.CurrentTime;
        float leftX = _context.GetFloat("guyLeft.x", 220f);
        float rightX = _context.GetFloat("guyRight.x", 1060f);
        float handshakeBlend = Math.Clamp(_context.GetFloat("handshake.blend", 0f), 0f, 1f);
        float logoAlpha = Math.Clamp(_context.GetFloat("logo.alpha", 0f), 0f, 1f);
        float logoScale = Math.Clamp(_context.GetFloat("logo.scale", 0f), 0f, 3f);
        float logoY = _context.GetFloat("logo.y", ClaspBaseY);
        float logoRot = _context.GetFloat("logo.rotation", 0f);

        DrawStageAtmosphere(graphics, handshakeBlend, logoAlpha);

        // Compute deterministic clasp position at center with warm double handshake pump around 1.78s..2.48s
        float pumpEnv = MathF.Sin(Math.Clamp((t - 1.78f) / 0.70f, 0f, 1f) * MathF.PI);
        float claspPumpY = MathF.Sin(t * MathF.PI * 6.2f) * 6.5f * pumpEnv * handshakeBlend;
        Vector2 claspPos = new(StageCenterX, ClaspBaseY + claspPumpY);

        // Nod slightly while shaking hands
        float nodAngle = MathF.Abs(MathF.Sin(t * MathF.PI * 3.1f)) * 4.5f * pumpEnv * handshakeBlend;

        // Draw Left Guy & Right Guy when within viewport range
        float leftPhase = (leftX - 220f) * 0.10585f;
        float rightPhase = (rightX - 1060f) * 0.10585f;

        if (leftX > -120f && leftX < _context.VirtualWidth + 120f)
        {
            DrawGroundShadow(graphics, leftX, 34f, 9f, 110);
        }

        if (rightX > -120f && rightX < _context.VirtualWidth + 120f)
        {
            DrawGroundShadow(graphics, rightX, 34f, 9f, 110);
        }

        if (logoAlpha > 0.02f && logoScale > 0.02f)
        {
            DrawGroundShadow(graphics, StageCenterX, 74f * MathF.Min(1.1f, logoScale), 10f, (byte)(85f * logoAlpha));
        }

        if (leftX > -120f && leftX < _context.VirtualWidth + 120f)
        {
            var leftStyle = new CharacterStyle(
                JacketMain: new Color((byte)216, (byte)144, (byte)68, (byte)255),
                JacketShade: new Color((byte)166, (byte)102, (byte)44, (byte)255),
                ShirtColor: new Color((byte)240, (byte)234, (byte)222, (byte)255),
                PantsMain: new Color((byte)58, (byte)66, (byte)84, (byte)255),
                PantsShade: new Color((byte)42, (byte)48, (byte)62, (byte)255),
                ShoeColor: new Color((byte)32, (byte)35, (byte)42, (byte)255),
                ShoeSole: new Color((byte)224, (byte)218, (byte)206, (byte)255),
                SkinMain: new Color((byte)238, (byte)198, (byte)168, (byte)255),
                SkinShade: new Color((byte)206, (byte)160, (byte)130, (byte)255),
                HairColor: new Color((byte)44, (byte)34, (byte)28, (byte)255),
                HasBeanieAccent: false);

            DrawCharacter(
                graphics,
                leftX,
                _leftFacing,
                leftPhase,
                _leftWalkBlend,
                handshakeBlend,
                claspPos,
                nodAngle,
                idleOffset: 0f,
                leftStyle);
        }

        if (rightX > -120f && rightX < _context.VirtualWidth + 120f)
        {
            var rightStyle = new CharacterStyle(
                JacketMain: new Color((byte)78, (byte)148, (byte)176, (byte)255),
                JacketShade: new Color((byte)52, (byte)104, (byte)128, (byte)255),
                ShirtColor: new Color((byte)236, (byte)240, (byte)244, (byte)255),
                PantsMain: new Color((byte)54, (byte)64, (byte)78, (byte)255),
                PantsShade: new Color((byte)40, (byte)48, (byte)60, (byte)255),
                ShoeColor: new Color((byte)30, (byte)34, (byte)40, (byte)255),
                ShoeSole: new Color((byte)220, (byte)226, (byte)232, (byte)255),
                SkinMain: new Color((byte)230, (byte)186, (byte)154, (byte)255),
                SkinShade: new Color((byte)198, (byte)150, (byte)120, (byte)255),
                HairColor: new Color((byte)48, (byte)56, (byte)74, (byte)255),
                HasBeanieAccent: true);

            DrawCharacter(
                graphics,
                rightX,
                _rightFacing,
                rightPhase,
                _rightWalkBlend,
                handshakeBlend,
                claspPos,
                nodAngle,
                idleOffset: 1.4f,
                rightStyle);
        }

        // Clasp overlap hand & impact burst right over both reaching arms
        if (handshakeBlend > 0.35f)
        {
            DrawHandshakeClaspEffect(graphics, claspPos, handshakeBlend, t);
        }

        // Center animated logo reveal right out of the handshake center
        if (logoAlpha > 0.01f && logoScale > 0.005f)
        {
            DrawCenterLogoReveal(graphics, t, new Vector2(StageCenterX, logoY), logoScale, logoAlpha, logoRot);
        }
    }

    private static bool IsWalkingPhase(float t)
    {
        return (t > 0.20f && t < 1.65f) || (t > 2.70f && t < 4.45f);
    }

    private static float EstimateAnalyticalDirection(float t, bool isLeft)
    {
        if (t <= 1.65f)
        {
            return isLeft ? 1f : -1f;
        }

        if (t >= 2.70f && t <= 4.45f)
        {
            return isLeft ? -1f : 1f;
        }

        return 0f;
    }

    private static float ResolveTargetFacing(float t, float velX, float defaultApproach)
    {
        if (MathF.Abs(velX) > 12f)
        {
            return velX > 0f ? 1f : -1f;
        }

        // Default scripted turn moment at handshake release (t = 2.68s)
        if (t >= 2.68f)
        {
            return -defaultApproach;
        }

        return defaultApproach;
    }

    private void DrawStageAtmosphere(IGraphicsService graphics, float handshakeBlend, float logoAlpha)
    {
        float centerEnergy = MathF.Max(handshakeBlend * 0.65f, logoAlpha * 0.95f);

        // True radial gradient studio spotlight on stage backdrop + subtle center pad
        byte haloA1 = (byte)(18 + centerEnergy * 34f);
        byte haloA2 = (byte)(12 + centerEnergy * 28f);
        Raylib.DrawCircleGradient(
            (int)StageCenterX,
            365,
            310f,
            new Color((byte)214, (byte)162, (byte)96, haloA1),
            new Color((byte)214, (byte)162, (byte)96, (byte)0));
        Raylib.DrawCircleGradient(
            (int)StageCenterX,
            365,
            180f,
            new Color((byte)236, (byte)184, (byte)114, haloA2),
            new Color((byte)236, (byte)184, (byte)114, (byte)0));

        // Subtle floor center studio mark
        float markRadius = 46f + centerEnergy * 18f;
        byte ringA = (byte)(55 + centerEnergy * 95f);
        DrawEllipseRing(graphics, new Vector2(StageCenterX, GroundY + 3f), markRadius, 9f, 1.5f, new Color((byte)204, (byte)154, (byte)92, ringA));
    }

    private static void DrawGroundShadow(IGraphicsService graphics, float x, float rx, float ry, byte alpha)
    {
        byte outer = (byte)(alpha * 0.42f);
        byte inner = (byte)(alpha * 0.78f);
        Raylib.DrawEllipse((int)MathF.Round(x), (int)(GroundY + 3f), rx, ry, new Color((byte)4, (byte)5, (byte)7, outer));
        Raylib.DrawEllipse((int)MathF.Round(x), (int)(GroundY + 3f), rx * 0.62f, ry * 0.58f, new Color((byte)3, (byte)4, (byte)6, inner));
    }

    private void DrawCharacter(
        IGraphicsService graphics,
        float baseX,
        float facing,
        float walkPhase,
        float walkBlend,
        float handshakeBlend,
        Vector2 claspPos,
        float handshakeNod,
        float idleOffset,
        CharacterStyle style)
    {
        float f = Math.Clamp(facing, -1f, 1f);
        float absF = MathF.Abs(f);
        float dir = f >= 0f ? 1f : -1f;

        float stride = MathF.Sin(walkPhase) * walkBlend;
        float strideOpp = -stride;
        float liftFront = MathF.Max(0f, MathF.Cos(walkPhase)) * 10f * walkBlend;
        float liftBack = MathF.Max(0f, -MathF.Cos(walkPhase)) * 10f * walkBlend;
        float walkBob = -MathF.Abs(MathF.Sin(walkPhase)) * 5.5f * walkBlend;
        float breathe = MathF.Sin(_elapsedWallTime * 2.4f + idleOffset) * 1.4f * (1f - walkBlend * 0.5f);

        Vector2 hip = new(baseX, 430f + walkBob + breathe * 0.35f);
        Vector2 shoulderCenter = new(baseX + f * (2.5f * walkBlend + handshakeBlend * 3.5f), 362f + walkBob + breathe);
        Vector2 headCenter = new(shoulderCenter.X + f * (2.5f + handshakeNod * 0.7f), 324f + walkBob + breathe * 1.15f + handshakeNod * 0.55f);

        // 1. Back leg
        DrawLeg(
            graphics,
            hip + new Vector2(-f * 4f, 0f),
            baseX + strideOpp * 24f - f * 4f,
            GroundY - liftBack,
            dir,
            absF,
            strideOpp,
            style.PantsShade,
            style.ShoeColor,
            style.ShoeSole);

        // 2. Back arm (swinging opposite stride)
        Vector2 backShoulder = shoulderCenter + new Vector2(-f * 9f, 4f);
        Vector2 backHand = backShoulder + new Vector2(-f * 4f + stride * 18f, 54f);
        Vector2 backElbow = (backShoulder + backHand) * 0.5f + new Vector2(-f * 5f, 3f);
        DrawCapsule(graphics, backShoulder, backElbow, 6.2f, style.JacketShade);
        DrawCapsule(graphics, backElbow, backHand, 5.4f, style.JacketShade);
        graphics.DrawCircle(backHand, 5.6f, style.SkinShade);

        // 3. Front leg
        DrawLeg(
            graphics,
            hip + new Vector2(f * 4f, 0f),
            baseX + stride * 24f + f * 4f,
            GroundY - liftFront,
            dir,
            absF,
            stride,
            style.PantsMain,
            style.ShoeColor,
            style.ShoeSole);

        // 4. Jacket / Torso silhouette + clean hem + shirt collar trim
        float torsoTiltDeg = f * (walkBlend * 4f + handshakeBlend * 4.5f);
        float torsoW = 24f + absF * 12f;
        float torsoH = 74f;
        Vector2 torsoCenter = (shoulderCenter + hip) * 0.5f;
        Raylib.DrawRectanglePro(
            new Rectangle(torsoCenter.X, torsoCenter.Y, torsoW, torsoH),
            new Vector2(torsoW * 0.5f, torsoH * 0.5f),
            torsoTiltDeg,
            style.JacketMain);
        DrawCapsule(graphics, shoulderCenter + new Vector2(-torsoW * 0.24f, 1f), shoulderCenter + new Vector2(torsoW * 0.24f, 1f), 10f, style.JacketMain);
        Raylib.DrawRectanglePro(
            new Rectangle(hip.X - f * 2f, hip.Y - 4f, torsoW - 1f, 8f),
            new Vector2((torsoW - 1f) * 0.5f, 4f),
            torsoTiltDeg,
            style.JacketShade);

        // Collar V-shirt highlight neatly on upper chest facing side
        Vector2 shirtTop = shoulderCenter + new Vector2(f * 5.5f, -4f);
        Vector2 shirtTip = shoulderCenter + new Vector2(f * 7.5f, 13f);
        DrawCapsule(graphics, shirtTop, shirtTip, 4.4f, style.ShirtColor);

        // 5. Neck + Head + Hair + Expressive Face
        DrawCapsule(graphics, shoulderCenter + new Vector2(0f, -4f), headCenter + new Vector2(0f, 12f), 6.5f, style.SkinShade);
        graphics.DrawCircle(headCenter, 23f, style.SkinMain);

        // Clean hair crown / side wave
        Vector2 hairCenter = headCenter + new Vector2(-f * 4.5f, -7.5f);
        graphics.DrawCircle(hairCenter, 21f, style.HairColor);
        DrawCapsule(
            graphics,
            headCenter + new Vector2(-17f, -15f),
            headCenter + new Vector2(f * 15f, -19f),
            7.5f,
            style.HairColor);

        if (style.HasBeanieAccent)
        {
            // Cool amber-trimmed cap cuff/stripe for visual contrast between the two guys
            DrawCapsule(
                graphics,
                headCenter + new Vector2(-18f, -14f),
                headCenter + new Vector2(18f, -14f),
                3.4f,
                new Color((byte)214, (byte)158, (byte)86, (byte)255));
        }

        // Redraw lower face over bottom hair edge for clean jawline silhouette
        graphics.DrawCircle(headCenter + new Vector2(f * 3f, 5f), 17.5f, style.SkinMain);

        // Expressive eyes (turn naturally with facing + subtle periodic blink)
        if (absF > 0.22f)
        {
            float blinkCycle = (_elapsedWallTime + idleOffset * 0.8f) % 3.4f;
            bool isBlinking = blinkCycle > 3.28f;

            Vector2 eyeBase = headCenter + new Vector2(f * 10.5f, 1f);
            Vector2 farEye = headCenter + new Vector2(f * 3.5f, 1f);
            Color eyeColor = new((byte)28, (byte)30, (byte)36, (byte)255);
            if (isBlinking)
            {
                graphics.DrawLine(eyeBase + new Vector2(-2.5f, 0f), eyeBase + new Vector2(2.5f, 0f), 2.0f, eyeColor);
            }
            else
            {
                graphics.DrawCircle(eyeBase, 2.6f, eyeColor);
                if (absF < 0.92f)
                {
                    graphics.DrawCircle(farEye, 2.3f, eyeColor);
                }
            }

            // Friendly subtle smile / cheek warmth
            Vector2 mouthA = headCenter + new Vector2(f * 7.5f, 10.5f);
            Vector2 mouthB = headCenter + new Vector2(f * 13.5f, 9.2f);
            graphics.DrawLine(mouthA, mouthB, 1.8f, new Color((byte)152, (byte)102, (byte)80, (byte)255));
        }

        // 6. Front arm (either walking swing OR smooth 2-bone reach to claspPos during handshake!)
        Vector2 frontShoulder = shoulderCenter + new Vector2(f * 8f, 5f);
        Vector2 idleArmTarget = frontShoulder + new Vector2(f * 4f + strideOpp * 22f, 56f);
        Vector2 handTarget = Vector2.Lerp(idleArmTarget, claspPos, handshakeBlend);

        Vector2 armDir = handTarget - frontShoulder;
        float targetDist = Math.Clamp(armDir.Length(), 12f, 66f);
        Vector2 armUnit = armDir.LengthSquared() > 1e-4f ? armDir / armDir.Length() : Vector2.UnitY;
        handTarget = frontShoulder + armUnit * targetDist;

        // 2-bone IK bend backward/downward cleanly for natural posture both when facing right (+1) and left (-1)
        Vector2 frontElbow = SolveTwoBoneElbow(frontShoulder, handTarget, upperLen: 34f, lowerLen: 34f, bendSign: dir);

        DrawCapsule(graphics, frontShoulder, frontElbow, 6.8f, style.JacketMain);
        DrawCapsule(graphics, frontElbow, handTarget, 5.8f, style.JacketMain);

        // Shirt cuff peeking slightly near wrist when shaking hands
        if (handshakeBlend > 0.25f)
        {
            Vector2 cuffPos = Vector2.Lerp(frontElbow, handTarget, 0.84f);
            graphics.DrawCircle(cuffPos, 5.2f, style.ShirtColor);
        }

        graphics.DrawCircle(handTarget, 6.2f, style.SkinMain);
    }

    private static void DrawLeg(
        IGraphicsService graphics,
        Vector2 hip,
        float footX,
        float footY,
        float facingDir,
        float absFacing,
        float strideValue,
        Color pantsColor,
        Color shoeColor,
        Color shoeSoleColor)
    {
        Vector2 ankle = new(footX, footY - 5f);
        float advanceBend = MathF.Max(0f, strideValue * facingDir) * 5.5f;
        Vector2 knee = (hip + ankle) * 0.5f + new Vector2(facingDir * (3.5f + advanceBend) * absFacing, -2f);

        DrawCapsule(graphics, hip, knee, 7.6f, pantsColor);
        DrawCapsule(graphics, knee, ankle, 6.6f, pantsColor);

        Vector2 heel = new(ankle.X - facingDir * 3f * absFacing, footY - 3.5f);
        Vector2 toe = new(ankle.X + facingDir * 11f * absFacing, footY - 3.5f);
        DrawCapsule(graphics, heel, toe, 4.8f, shoeColor);
        graphics.DrawLine(heel + new Vector2(0f, 3.2f), toe + new Vector2(0f, 3.2f), 2.2f, shoeSoleColor);
    }

    private void DrawHandshakeClaspEffect(IGraphicsService graphics, Vector2 claspPos, float blend, float t)
    {
        // Unified clasped hands silhouette right over both wrists
        Color claspOuter = new((byte)238, (byte)196, (byte)162, (byte)(255 * Math.Clamp(blend * 1.4f, 0f, 1f)));
        Color claspWarm = new((byte)214, (byte)168, (byte)134, (byte)(255 * Math.Clamp(blend * 1.4f, 0f, 1f)));
        graphics.DrawCircle(claspPos + new Vector2(-3f, -1f), 7.8f, claspOuter);
        graphics.DrawCircle(claspPos + new Vector2(3f, 1f), 7.4f, claspWarm);
        graphics.DrawCircle(claspPos, 5.2f, new Color((byte)248, (byte)222, (byte)196, (byte)(180 * blend)));

        // Impact pulse + subtle amber energy rings during handshake peak
        float pulse = MathF.Max(_contactPulse, Math.Clamp((blend - 0.85f) / 0.15f, 0f, 1f) * 0.55f);
        if (pulse > 0.02f)
        {
            float ringR = 16f + (1f - _contactPulse) * 44f;
            byte a = (byte)(190 * pulse);
            DrawCircleRing(graphics, claspPos, ringR, 2.2f, new Color((byte)238, (byte)184, (byte)106, a));
            DrawCircleRing(graphics, claspPos, 11f + MathF.Sin(t * 9f) * 2.5f, 1.4f, new Color((byte)255, (byte)236, (byte)198, (byte)(145 * pulse)));

            // 6 crisp short spark rays around the handshake
            for (int i = 0; i < 6; i++)
            {
                float ang = i * (MathF.PI / 3f) + t * 0.9f;
                Vector2 dir = new(MathF.Cos(ang), MathF.Sin(ang));
                Vector2 p0 = claspPos + dir * (14f + (1f - pulse) * 8f);
                Vector2 p1 = claspPos + dir * (25f + (1f - pulse) * 16f);
                graphics.DrawLine(p0, p1, 2.0f, new Color((byte)242, (byte)192, (byte)112, a));
            }
        }
    }

    private void DrawCenterLogoReveal(
        IGraphicsService graphics,
        float t,
        Vector2 center,
        float animScale,
        float alpha,
        float rotationDeg)
    {
        // Expanding radial shockwave burst at logo emergence
        if (_revealPulse > 0.02f)
        {
            float wave = 1f - _revealPulse;
            float ringRadius = 24f + wave * 175f;
            byte ringAlpha = (byte)(_revealPulse * 195f);
            DrawCircleRing(graphics, center, ringRadius, 2.6f, new Color((byte)232, (byte)176, (byte)96, ringAlpha));
            DrawCircleRing(graphics, center, 16f + wave * 110f, 1.5f, new Color((byte)252, (byte)228, (byte)184, (byte)(ringAlpha * 0.7f)));
        }

        // Soft smooth radial studio halo & rotating burst rays behind the revealed logo
        float haloRadius = 155f * MathF.Min(1.25f, animScale);
        Raylib.DrawCircleGradient(
            (int)MathF.Round(center.X),
            (int)MathF.Round(center.Y),
            haloRadius,
            new Color((byte)238, (byte)184, (byte)112, (byte)(86f * alpha)),
            new Color((byte)214, (byte)158, (byte)86, (byte)0));

        for (int i = 0; i < 8; i++)
        {
            float ang = i * (MathF.PI * 0.25f) + t * 0.35f;
            Vector2 dir = new(MathF.Cos(ang), MathF.Sin(ang));
            Vector2 p0 = center + dir * (98f * animScale);
            Vector2 p1 = center + dir * (134f * animScale);
            graphics.DrawLine(p0, p1, 1.8f, new Color((byte)222, (byte)172, (byte)104, (byte)(62f * alpha)));
        }

        string? logoPath = ResolveLogoPath();
        byte aByte = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        Color tint = new((byte)255, (byte)255, (byte)255, aByte);

        if (!string.IsNullOrWhiteSpace(logoPath))
        {
            Vector2 texSize = graphics.GetTextureSize(logoPath);
            float maxDim = MathF.Max(texSize.X, texSize.Y);
            float normalize = maxDim > 1f ? LogoTargetMaxSizePx / maxDim : 1f;
            float finalScale = normalize * animScale;

            graphics.DrawTextureEx(
                logoPath,
                center,
                rotationDeg,
                new Vector2(finalScale, finalScale),
                tint);
            return;
        }

        // Clean fallback vector badge if all sprite files are temporarily deleted
        float r = 74f * animScale;
        graphics.DrawCircle(center, r, new Color((byte)32, (byte)34, (byte)40, aByte));
        DrawCircleRing(graphics, center, r, 4f, new Color((byte)214, (byte)160, (byte)90, aByte));
        graphics.DrawText("LOGO", (int)(center.X - 30f * animScale), (int)(center.Y - 10f * animScale), Math.Max(12, (int)(22f * animScale)), new Color((byte)236, (byte)232, (byte)224, aByte));
    }

    private string? ResolveLogoPath()
    {
        if (_context == null)
        {
            return null;
        }

        // Priority 1: If user clicked a sprite in Project panel to preview their logo live
        string selectedSprite = _context.GetString("sprite.path");
        if (!string.IsNullOrWhiteSpace(selectedSprite) &&
            File.Exists(_context.Workspace.ResolvePath(selectedSprite)))
        {
            return selectedSprite;
        }

        // Priority 2: Configured logo.path in project.json (default: assets/sprites/logo.png)
        string configured = _context.GetString("logo.path", "assets/sprites/logo.png");
        if (!string.Equals(configured, _lastConfiguredLogo, StringComparison.Ordinal))
        {
            _lastConfiguredLogo = configured;
            _cachedLogoPath = null;
            _scanCooldown = 0f;
        }

        if (!string.IsNullOrWhiteSpace(configured) &&
            File.Exists(_context.Workspace.ResolvePath(configured)))
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(_cachedLogoPath) &&
            File.Exists(_context.Workspace.ResolvePath(_cachedLogoPath)))
        {
            return _cachedLogoPath;
        }

        if (_scanCooldown > 0f)
        {
            return null;
        }

        // Priority 3: Any logo.* or image placed into assets/sprites/
        _scanCooldown = 0.5f;
        string[] images = _context.Workspace
            .EnumerateFiles("assets/sprites", "*.*", recursive: true)
            .Where(IsImageFile)
            .ToArray();

        string? preferred = images.FirstOrDefault(p =>
            Path.GetFileNameWithoutExtension(p).Equals("logo", StringComparison.OrdinalIgnoreCase));

        string? chosen = preferred ?? images.FirstOrDefault();
        _cachedLogoPath = chosen != null ? _context.Workspace.ToRelativePath(chosen) : null;
        return _cachedLogoPath;
    }

    private static bool IsImageFile(string path)
    {
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static Vector2 SolveTwoBoneElbow(Vector2 shoulder, Vector2 target, float upperLen, float lowerLen, float bendSign)
    {
        Vector2 d = target - shoulder;
        float dist = Math.Clamp(d.Length(), 4f, upperLen + lowerLen - 0.5f);
        Vector2 dir = d.LengthSquared() > 1e-4f ? d / d.Length() : Vector2.UnitY;
        Vector2 perp = new(-dir.Y * bendSign, dir.X * bendSign);

        // Law of cosines for clean 2D arm bend
        float cosA = Math.Clamp((upperLen * upperLen + dist * dist - lowerLen * lowerLen) / (2f * upperLen * dist), -1f, 1f);
        float a = upperLen * cosA;
        float h = MathF.Sqrt(MathF.Max(0f, upperLen * upperLen - a * a));
        return shoulder + dir * a + perp * h;
    }

    private static void DrawCapsule(IGraphicsService graphics, Vector2 a, Vector2 b, float radius, Color color)
    {
        graphics.DrawLine(a, b, radius * 2f, color);
        graphics.DrawCircle(a, radius, color);
        graphics.DrawCircle(b, radius, color);
    }

    private static void DrawCircleRing(IGraphicsService graphics, Vector2 center, float radius, float thickness, Color color)
    {
        DrawEllipseRing(graphics, center, radius, radius, thickness, color);
    }

    private static void DrawEllipseRing(IGraphicsService graphics, Vector2 center, float rx, float ry, float thickness, Color color)
    {
        const int segments = 36;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * (MathF.PI * 2f / segments);
            float a1 = (i + 1) * (MathF.PI * 2f / segments);
            Vector2 p0 = new(center.X + MathF.Cos(a0) * rx, center.Y + MathF.Sin(a0) * ry);
            Vector2 p1 = new(center.X + MathF.Cos(a1) * rx, center.Y + MathF.Sin(a1) * ry);
            graphics.DrawLine(p0, p1, thickness, color);
        }
    }

    private readonly record struct CharacterStyle(
        Color JacketMain,
        Color JacketShade,
        Color ShirtColor,
        Color PantsMain,
        Color PantsShade,
        Color ShoeColor,
        Color ShoeSole,
        Color SkinMain,
        Color SkinShade,
        Color HairColor,
        bool HasBeanieAccent);
}
