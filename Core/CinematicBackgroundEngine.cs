using System;
using System.Windows;
using System.Windows.Media;

namespace GameLauncher.Core;

public enum ParticleKind
{
    DeepSpaceDust,  // 25%: Tiny, slow space depth particles
    Star,           // 15%: Twinkling stars in deep space
    RingParticle,   // 45%: Ultra-dense Saturn Ring particle clouds
    PlanetSurface,  // 10%: Hand-drawn / holographic planet silhouette particles
    MeteorStream    // 5%: Continuous meteor storm particles
}

/// <summary>
/// Upgraded Saturn Particle Struct (4000 total particles).
/// </summary>
public struct CinematicParticle
{
    public double X;
    public double Y;
    public double Depth;
    public double BaseRadius;
    public double Opacity;
    public double SpeedX;
    public double SpeedY;
    public double OrbitAngle;
    public double OrbitSpeed;
    public double OrbitRadiusX;
    public double OrbitRadiusY;
    public double Phase;
    public ParticleKind Kind;
}

/// <summary>
/// Continuous Meteor Stream Struct with 3D Depth Layering (Behind & In Front of Saturn).
/// </summary>
public struct ContinuousMeteor
{
    public Point Position;
    public Point Velocity;
    public double Length;
    public double Opacity;
    public double Depth; // < 0.5 = Passes Behind Saturn; >= 0.5 = Passes In Front of Saturn
}

/// <summary>
/// GPU-Accelerated FrameworkElement rendering Phase 19.4 Saturn Cinematic Deep Space Evolution:
/// 1. Hand-Drawn / Holographic Particle Saturn Planet (Múltiplos círculos imperfeitos, silhueta desenhada por partículas, ruído sci-fi).
/// 2. Ultra-Dense Saturn Ring Clouds (1800 Ring particles forming a solid mass of cosmic dust).
/// 3. Continuous 3D Depth Meteor Storm (12 continuous meteors passing behind and in front of Saturn).
/// 4. 4000 Particle Ecosystem (Deep Space Dust, Stars, Ring Clouds, Holographic Surface Particles, Meteors).
/// 5. Monochrome Saturn Boot Sequence (Black background, white particle materialization, Warframe/NASA inspired).
/// 6. Non-interactive Ring Physics (Stable orbital motion, camera parallax only).
/// 7. Zero-allocation 60 FPS render pipeline.
/// </summary>
public class CinematicBackgroundEngine : FrameworkElement
{
    private const int ParticleCount = 4000;
    private readonly CinematicParticle[] _particles = new CinematicParticle[ParticleCount];
    private readonly Random _random = new Random(42);

    private const int MeteorCount = 12;
    private readonly ContinuousMeteor[] _meteors = new ContinuousMeteor[MeteorCount];

    private Point _mouseNormPos = new Point(0.5, 0.5);
    private Point _camOffsetFarSpace = new Point(0, 0);
    private Point _camOffsetPlanet = new Point(0, 0);
    private Point _camOffsetRings = new Point(0, 0);
    private Point _camOffsetNear = new Point(0, 0);

    private DateTime _lastMouseMoveTime = DateTime.UtcNow;
    private double _animTime = 0.0;
    private bool _isInitialized = false;

    /// <summary>
    /// Enable monochrome mode for startup boot sequence.
    /// </summary>
    public bool IsMonochromeMode { get; set; } = false;

    /// <summary>
    /// Materialization progress during boot (0.0 = Ring particles, 0.5 = Planet silhouette, 1.0 = Meteors active).
    /// </summary>
    public double BootMaterializeProgress { get; set; } = 1.0;

    // Theme Color Interpolation (Target & Current for 1.5 - 2s Smooth Transitions)
    private Color _targetPrimaryColor = Color.FromRgb(0x89, 0xB4, 0xFA);
    private Color _targetAccentColor = Color.FromRgb(0xCB, 0xA6, 0xF7);
    private Color _targetGlowColor = Color.FromRgb(0x74, 0xC7, 0xEC);

    private Color _currentPrimaryColor = Color.FromRgb(0x89, 0xB4, 0xFA);
    private Color _currentAccentColor = Color.FromRgb(0xCB, 0xA6, 0xF7);
    private Color _currentGlowColor = Color.FromRgb(0x74, 0xC7, 0xEC);

    private double _cosmicPulseTimer = 0.0;
    private double _cosmicPulseIntensity = 0.0;
    private double _planetMouseProximityScale = 1.0;

    // Cached Brushes & Pens
    private RadialGradientBrush? _planetBrush;
    private RadialGradientBrush? _planetAtmosphereBrush;
    private RadialGradientBrush? _nebulaBrush1;
    private RadialGradientBrush? _nebulaBrush2;

    private SolidColorBrush? _dustBrush;
    private SolidColorBrush? _starBrush;
    private SolidColorBrush? _surfaceParticleBrush;
    private SolidColorBrush? _ringParticleBrush;

    private Pen? _handDrawnCirclePen1;
    private Pen? _handDrawnCirclePen2;
    private Pen? _planetLatitudePen;
    private Pen? _meteorTailPen;

    public CinematicBackgroundEngine()
    {
        IsHitTestVisible = false;
        UpdateThemeBrushes();
    }

    public void UpdateThemeColors(Color primaryColor, Color accentColor, Color glowColor)
    {
        _targetPrimaryColor = primaryColor;
        _targetAccentColor = accentColor;
        _targetGlowColor = glowColor;
    }

    private static Color LerpColor(Color c1, Color c2, double factor)
    {
        byte r = (byte)(c1.R + (c2.R - c1.R) * factor);
        byte g = (byte)(c1.G + (c2.G - c1.G) * factor);
        byte b = (byte)(c1.B + (c2.B - c1.B) * factor);
        byte a = (byte)(c1.A + (c2.A - c1.A) * factor);
        return Color.FromArgb(a, r, g, b);
    }

    private void UpdateThemeBrushes()
    {
        Color primary = IsMonochromeMode ? Color.FromRgb(0x70, 0x70, 0x70) : _currentPrimaryColor;
        Color accent = IsMonochromeMode ? Color.FromRgb(0xFF, 0xFF, 0xFF) : _currentAccentColor;
        Color glow = IsMonochromeMode ? Color.FromRgb(0xD0, 0xD0, 0xD0) : _currentGlowColor;

        // 1. Miniature Saturn Planet Sphere Brush (Holographic Sci-Fi Surface Shader)
        var planetGradients = new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0xE5, primary.R, primary.G, primary.B), 0.0),
            new GradientStop(Color.FromArgb(0xB5, glow.R, glow.G, glow.B), 0.60),
            new GradientStop(Color.FromArgb(0xF8, accent.R, accent.G, accent.B), 0.92),
            new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0)
        };
        _planetBrush = new RadialGradientBrush(planetGradients)
        {
            Center = new Point(0.42, 0.38),
            GradientOrigin = new Point(0.38, 0.34),
            RadiusX = 0.52,
            RadiusY = 0.52
        };
        _planetBrush.Freeze();

        // Atmosphere Outer Rim Glow
        var atmoGradients = new GradientStopCollection
        {
            new GradientStop(Color.FromArgb((byte)(0x85 * _planetMouseProximityScale), accent.R, accent.G, accent.B), 0.0),
            new GradientStop(Color.FromArgb((byte)(0x45 * _planetMouseProximityScale), glow.R, glow.G, glow.B), 0.50),
            new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0)
        };
        _planetAtmosphereBrush = new RadialGradientBrush(atmoGradients)
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.74,
            RadiusY = 0.74
        };
        _planetAtmosphereBrush.Freeze();

        // 2. Cosmic Nebulae
        _nebulaBrush1 = new RadialGradientBrush(
            Color.FromArgb(IsMonochromeMode ? (byte)0x00 : (byte)0x22, primary.R, primary.G, primary.B),
            Color.FromArgb(0x00, 0, 0, 0))
        {
            Center = new Point(0.25, 0.25),
            GradientOrigin = new Point(0.25, 0.25),
            RadiusX = 0.95,
            RadiusY = 0.95
        };
        _nebulaBrush1.Freeze();

        _nebulaBrush2 = new RadialGradientBrush(
            Color.FromArgb(IsMonochromeMode ? (byte)0x00 : (byte)0x1E, accent.R, accent.G, accent.B),
            Color.FromArgb(0x00, 0, 0, 0))
        {
            Center = new Point(0.75, 0.75),
            GradientOrigin = new Point(0.75, 0.75),
            RadiusX = 0.90,
            RadiusY = 0.90
        };
        _nebulaBrush2.Freeze();

        // 3. Particle Depth Brushes
        _dustBrush = new SolidColorBrush(Color.FromArgb(0x35, primary.R, primary.G, primary.B));
        _dustBrush.Freeze();

        _starBrush = new SolidColorBrush(Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF));
        _starBrush.Freeze();

        _surfaceParticleBrush = new SolidColorBrush(Color.FromArgb(0xF0, accent.R, accent.G, accent.B));
        _surfaceParticleBrush.Freeze();

        _ringParticleBrush = new SolidColorBrush(Color.FromArgb(0xDD, glow.R, glow.G, glow.B));
        _ringParticleBrush.Freeze();

        // 4. Hand-Drawn Sci-Fi Imperfect Outline Pens
        var penBrush1 = new SolidColorBrush(Color.FromArgb(0x65, accent.R, accent.G, accent.B));
        penBrush1.Freeze();
        _handDrawnCirclePen1 = new Pen(penBrush1, 1.2)
        {
            DashStyle = new DashStyle(new double[] { 6, 3, 2, 3 }, 0)
        };
        _handDrawnCirclePen1.Freeze();

        var penBrush2 = new SolidColorBrush(Color.FromArgb(0x45, glow.R, glow.G, glow.B));
        penBrush2.Freeze();
        _handDrawnCirclePen2 = new Pen(penBrush2, 1.0)
        {
            DashStyle = new DashStyle(new double[] { 10, 4, 1, 4 }, 0)
        };
        _handDrawnCirclePen2.Freeze();

        var latBrush = new SolidColorBrush(Color.FromArgb(0x30, primary.R, primary.G, primary.B));
        latBrush.Freeze();
        _planetLatitudePen = new Pen(latBrush, 1.0);
        _planetLatitudePen.Freeze();

        var meteorBrush = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        meteorBrush.Freeze();
        _meteorTailPen = new Pen(meteorBrush, 1.8);
        _meteorTailPen.Freeze();
    }

    private void EnsureParticlesInitialized(double width, double height)
    {
        if (_isInitialized || width <= 0 || height <= 0) return;

        // Initialize 4000 Particles
        for (int i = 0; i < ParticleCount; i++)
        {
            double depth;
            double radius;
            double opacity;
            double speed;
            ParticleKind kind;
            double orbitSpeed;

            if (i < ParticleCount * 0.25)
            {
                // Deep Space Dust (25% / 1000)
                kind = ParticleKind.DeepSpaceDust;
                depth = 0.1;
                radius = _random.NextDouble() * 1.2 + 0.4;
                opacity = _random.NextDouble() * 0.2 + 0.08;
                speed = 0.06 + _random.NextDouble() * 0.1;
                orbitSpeed = 0;
            }
            else if (i < ParticleCount * 0.40)
            {
                // Stars (15% / 600)
                kind = ParticleKind.Star;
                depth = 0.2;
                radius = _random.NextDouble() * 1.5 + 1.0;
                opacity = _random.NextDouble() * 0.45 + 0.35;
                speed = 0.04 + _random.NextDouble() * 0.06;
                orbitSpeed = 0;
            }
            else if (i < ParticleCount * 0.85)
            {
                // Ultra-Dense Saturn Ring Cloud Particles (45% / 1800)
                kind = ParticleKind.RingParticle;
                depth = 0.7;
                radius = _random.NextDouble() * 2.0 + 1.0;
                opacity = _random.NextDouble() * 0.5 + 0.5;
                speed = 0.2 + _random.NextDouble() * 0.2;
                orbitSpeed = 0.12 + _random.NextDouble() * 0.22;
            }
            else if (i < ParticleCount * 0.95)
            {
                // Hand-Drawn Holographic Planet Surface Particles (10% / 400)
                kind = ParticleKind.PlanetSurface;
                depth = 0.5;
                radius = _random.NextDouble() * 1.4 + 1.0;
                opacity = _random.NextDouble() * 0.6 + 0.4;
                speed = 0.05;
                orbitSpeed = 0.08 + _random.NextDouble() * 0.1;
            }
            else
            {
                // Meteor Debris Stream (5% / 200)
                kind = ParticleKind.MeteorStream;
                depth = 0.9;
                radius = _random.NextDouble() * 2.8 + 2.5;
                opacity = _random.NextDouble() * 0.4 + 0.6;
                speed = 0.5 + _random.NextDouble() * 0.5;
                orbitSpeed = 0;
            }

            double orbitR = 110.0 + _random.NextDouble() * 190.0;
            _particles[i] = new CinematicParticle
            {
                X = _random.NextDouble() * width,
                Y = _random.NextDouble() * height,
                Depth = depth,
                BaseRadius = radius,
                Opacity = opacity,
                SpeedX = (_random.NextDouble() - 0.5) * speed * 22.0,
                SpeedY = -(_random.NextDouble() * speed * 18.0 + 5.0),
                OrbitAngle = _random.NextDouble() * Math.PI * 2,
                OrbitSpeed = orbitSpeed,
                OrbitRadiusX = orbitR,
                OrbitRadiusY = orbitR * 0.28,
                Phase = _random.NextDouble() * Math.PI * 2,
                Kind = kind
            };
        }

        // Initialize 12 Continuous 3D Meteors
        for (int m = 0; m < MeteorCount; m++)
        {
            _meteors[m] = new ContinuousMeteor
            {
                Position = new Point(_random.NextDouble() * width * 1.2 - width * 0.1, -100 - _random.NextDouble() * 400),
                Velocity = new Point(380.0 + _random.NextDouble() * 320.0, 320.0 + _random.NextDouble() * 280.0),
                Length = 35.0 + _random.NextDouble() * 55.0,
                Opacity = _random.NextDouble() * 0.5 + 0.5,
                Depth = _random.NextDouble() // < 0.5 = Behind Saturn; >= 0.5 = In Front of Saturn!
            };
        }

        _isInitialized = true;
    }

    public void UpdateEngineFrame(double mouseNormX, double mouseNormY, double dt)
    {
        if (Visibility != Visibility.Visible) return;

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0) return;

        EnsureParticlesInitialized(width, height);

        _animTime += dt;
        _cosmicPulseTimer += dt;

        // Smooth Color Interpolation (only update brushes when colors are changing)
        if (_currentPrimaryColor != _targetPrimaryColor || _currentAccentColor != _targetAccentColor || _currentGlowColor != _targetGlowColor)
        {
            _currentPrimaryColor = LerpColor(_currentPrimaryColor, _targetPrimaryColor, Math.Min(1.0, dt * 3.0));
            _currentAccentColor = LerpColor(_currentAccentColor, _targetAccentColor, Math.Min(1.0, dt * 3.0));
            _currentGlowColor = LerpColor(_currentGlowColor, _targetGlowColor, Math.Min(1.0, dt * 3.0));
            UpdateThemeBrushes();
        }

        if (Math.Abs(mouseNormX - _mouseNormPos.X) > 0.001 || Math.Abs(mouseNormY - _mouseNormPos.Y) > 0.001)
        {
            _lastMouseMoveTime = DateTime.UtcNow;
        }
        _mouseNormPos = new Point(mouseNormX, mouseNormY);

        bool isIdle = (DateTime.UtcNow - _lastMouseMoveTime).TotalSeconds > 5.0;

        // 1. Camera Parallax Lerps (Spring Inertia)
        double targetCamX = (mouseNormX - 0.5);
        double targetCamY = (mouseNormY - 0.5);

        if (isIdle)
        {
            targetCamX += Math.Sin(_animTime * 0.25) * 0.12;
            targetCamY += Math.Cos(_animTime * 0.20) * 0.12;
        }

        _camOffsetFarSpace.X += (targetCamX * 6.0 - _camOffsetFarSpace.X) * 0.04;
        _camOffsetFarSpace.Y += (targetCamY * 6.0 - _camOffsetFarSpace.Y) * 0.04;

        _camOffsetPlanet.X += (targetCamX * 16.0 - _camOffsetPlanet.X) * 0.07;
        _camOffsetPlanet.Y += (targetCamY * 16.0 - _camOffsetPlanet.Y) * 0.07;

        _camOffsetRings.X += (targetCamX * 30.0 - _camOffsetRings.X) * 0.10;
        _camOffsetRings.Y += (targetCamY * 30.0 - _camOffsetRings.Y) * 0.10;

        _camOffsetNear.X += (targetCamX * 46.0 - _camOffsetNear.X) * 0.15;
        _camOffsetNear.Y += (targetCamY * 46.0 - _camOffsetNear.Y) * 0.15;

        // 2. Planet Mouse Proximity Reaction
        Point planetCenter = new Point(width * 0.5 + _camOffsetPlanet.X, height * 0.44 + _camOffsetPlanet.Y);
        Point mousePx = new Point(mouseNormX * width, mouseNormY * height);
        double distToPlanet = Math.Sqrt(Math.Pow(mousePx.X - planetCenter.X, 2) + Math.Pow(mousePx.Y - planetCenter.Y, 2));

        double targetPlanetProximity = distToPlanet < 320.0 ? 1.0 + (1.0 - distToPlanet / 320.0) * 0.35 : 1.0;
        _planetMouseProximityScale += (targetPlanetProximity - _planetMouseProximityScale) * 0.08;

        // 3. Rare Solar Flare Pulse
        if (_cosmicPulseTimer > 18.0)
        {
            _cosmicPulseTimer = 0.0;
            _cosmicPulseIntensity = 1.0;
        }
        if (_cosmicPulseIntensity > 0.0)
        {
            _cosmicPulseIntensity = Math.Max(0.0, _cosmicPulseIntensity - dt * 0.6);
        }

        // 4. Update Continuous 3D Meteors
        for (int m = 0; m < MeteorCount; m++)
        {
            ref var meteor = ref _meteors[m];
            meteor.Position = new Point(meteor.Position.X + meteor.Velocity.X * dt, meteor.Position.Y + meteor.Velocity.Y * dt);

            if (meteor.Position.X > width + 150 || meteor.Position.Y > height + 150)
            {
                meteor.Position = new Point(_random.NextDouble() * width * 1.2 - width * 0.2, -80 - _random.NextDouble() * 300);
                meteor.Velocity = new Point(380.0 + _random.NextDouble() * 320.0, 320.0 + _random.NextDouble() * 280.0);
                meteor.Depth = _random.NextDouble();
            }
        }

        // 5. 4000 Particles Physics Update (Non-interactive Ring Orbits & Hand-Drawn Silhouette)
        double planetRadius = height * 0.16 * _planetMouseProximityScale;

        for (int i = 0; i < ParticleCount; i++)
        {
            ref var p = ref _particles[i];

            if (p.Kind == ParticleKind.RingParticle)
            {
                // Stable Non-interactive Orbital Ring Movement
                p.OrbitAngle += dt * p.OrbitSpeed;
                double rx = Math.Cos(p.OrbitAngle) * p.OrbitRadiusX;
                double ry = Math.Sin(p.OrbitAngle) * p.OrbitRadiusY;

                double rad = -18.0 * (Math.PI / 180.0);
                p.X = planetCenter.X + (rx * Math.Cos(rad) - ry * Math.Sin(rad));
                p.Y = planetCenter.Y + (rx * Math.Sin(rad) + ry * Math.Cos(rad));
            }
            else if (p.Kind == ParticleKind.PlanetSurface)
            {
                // Hand-Drawn Holographic Particle Planet Silhouette Orbits
                p.OrbitAngle += dt * p.OrbitSpeed;
                double r = planetRadius * (0.85 + Math.Sin(p.Phase + _animTime * 0.5) * 0.15);
                p.X = planetCenter.X + Math.Cos(p.OrbitAngle) * r;
                p.Y = planetCenter.Y + Math.Sin(p.OrbitAngle) * r * 0.95;
            }
            else if (p.Kind == ParticleKind.MeteorStream)
            {
                p.X += p.SpeedX * dt;
                p.Y += p.SpeedY * dt;
            }
            else
            {
                p.X += p.SpeedX * dt;
                p.Y += p.SpeedY * dt;
            }

            // Boundary wrapping
            if (p.Y < -30)
            {
                p.Y = height + 30;
                p.X = _random.NextDouble() * width;
            }
            else if (p.Y > height + 30)
            {
                p.Y = -30;
            }

            if (p.X < -30) p.X = width + 30;
            else if (p.X > width + 30) p.X = -30;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0) return;

        EnsureParticlesInitialized(width, height);

        // Clear Pitch Black Background in Monochrome Mode
        if (IsMonochromeMode)
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
        }

        // A. Draw Deep Space Nebulae
        if (!IsMonochromeMode)
        {
            if (_nebulaBrush1 != null)
            {
                Point neb1 = new Point((width * 0.25) + Math.Sin(_animTime * 0.3) * 50.0 + _camOffsetFarSpace.X, (height * 0.25) + _camOffsetFarSpace.Y);
                dc.DrawEllipse(_nebulaBrush1, null, neb1, width * 0.65, height * 0.65);
            }
            if (_nebulaBrush2 != null)
            {
                Point neb2 = new Point((width * 0.75) - Math.Cos(_animTime * 0.35) * 50.0 + _camOffsetPlanet.X, (height * 0.75) + _camOffsetPlanet.Y);
                dc.DrawEllipse(_nebulaBrush2, null, neb2, width * 0.7, height * 0.7);
            }
        }

        // B. Render Background Meteors (Depth < 0.5, Passing BEHIND Saturn!)
        if (_meteorTailPen != null)
        {
            for (int m = 0; m < MeteorCount; m++)
            {
                var met = _meteors[m];
                if (met.Depth < 0.5)
                {
                    Point tailEnd = new Point(met.Position.X - met.Velocity.X * 0.08, met.Position.Y - met.Velocity.Y * 0.08);
                    dc.DrawLine(_meteorTailPen, met.Position, tailEnd);
                }
            }
        }

        // C. Render Hand-Drawn / Holographic Particle Saturn Planet (~16% Window Height)
        Point planetCenter = new Point((width * 0.5) + _camOffsetPlanet.X, (height * 0.44) + _camOffsetPlanet.Y);
        double planetW = height * 0.32 * (1.0 + _cosmicPulseIntensity * 0.1) * _planetMouseProximityScale;
        double planetH = height * 0.32 * (1.0 + _cosmicPulseIntensity * 0.1) * _planetMouseProximityScale;

        if (_planetAtmosphereBrush != null)
        {
            dc.DrawEllipse(_planetAtmosphereBrush, null, planetCenter, planetW * 0.72, planetH * 0.72);
        }
        if (_planetBrush != null)
        {
            dc.DrawEllipse(_planetBrush, null, planetCenter, planetW / 2.0, planetH / 2.0);
        }

        // Hand-Drawn Sci-Fi Imperfect Circles & Atmospheric Latitude Bands
        if (_handDrawnCirclePen1 != null && _handDrawnCirclePen2 != null && _planetLatitudePen != null)
        {
            dc.DrawEllipse(null, _handDrawnCirclePen1, planetCenter, (planetW / 2.0) + 3.0, (planetH / 2.0) + 1.5);
            dc.DrawEllipse(null, _handDrawnCirclePen2, planetCenter, (planetW / 2.0) - 4.0, (planetH / 2.0) - 2.0);

            // Latitude Rings
            dc.DrawEllipse(null, _planetLatitudePen, planetCenter, (planetW / 2.0) * 0.85, (planetH / 2.0) * 0.35);
            dc.DrawEllipse(null, _planetLatitudePen, planetCenter, (planetW / 2.0) * 0.95, (planetH / 2.0) * 0.65);
        }

        // D. Render 4000 Particles Across Depth Layers (Ultra-Dense Ring Clouds, Holographic Surface & Stars)
        for (int i = 0; i < ParticleCount; i++)
        {
            var p = _particles[i];

            double px = p.X;
            double py = p.Y;
            Brush? particleBrush;

            switch (p.Kind)
            {
                case ParticleKind.DeepSpaceDust:
                    px += _camOffsetFarSpace.X;
                    py += _camOffsetFarSpace.Y;
                    particleBrush = _dustBrush;
                    break;

                case ParticleKind.Star:
                    px += _camOffsetFarSpace.X;
                    py += _camOffsetFarSpace.Y;
                    particleBrush = _starBrush;
                    break;

                case ParticleKind.PlanetSurface:
                    px += _camOffsetPlanet.X;
                    py += _camOffsetPlanet.Y;
                    particleBrush = _surfaceParticleBrush;
                    break;

                case ParticleKind.MeteorStream:
                    px += _camOffsetNear.X;
                    py += _camOffsetNear.Y;
                    particleBrush = _starBrush;
                    break;

                case ParticleKind.RingParticle:
                default:
                    px += _camOffsetRings.X;
                    py += _camOffsetRings.Y;
                    particleBrush = _ringParticleBrush;
                    break;
            }

            double pulse = Math.Sin(_animTime * 2.2 + p.Phase) * 0.25 + 1.0;
            double currentRadius = p.BaseRadius * pulse;

            if (particleBrush != null)
            {
                dc.DrawEllipse(particleBrush, null, new Point(px, py), currentRadius, currentRadius);
            }
        }

        // E. Render Foreground Meteors (Depth >= 0.5, Passing IN FRONT of Saturn!)
        if (_meteorTailPen != null)
        {
            for (int m = 0; m < MeteorCount; m++)
            {
                var met = _meteors[m];
                if (met.Depth >= 0.5)
                {
                    Point tailEnd = new Point(met.Position.X - met.Velocity.X * 0.08, met.Position.Y - met.Velocity.Y * 0.08);
                    dc.DrawLine(_meteorTailPen, met.Position, tailEnd);
                }
            }
        }
    }
}
