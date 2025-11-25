using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ShimejiBakadere
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;
        private DispatcherTimer _talkTimer;
        private readonly Random _rng = new();

        // Física
        private bool _isDragging = false;
        private System.Windows.Point _dragOffset;
        private double _vy = 0.0;
        private const double Gravity = 0.6;
        private const double WalkSpeed = 2.0;

        private bool _sitLock = false;
        private bool _lockMovement = false;

        // Superficies
        private enum Surface { None, Floor, Ceiling, LeftWall, RightWall }
        private Surface _surface = Surface.Floor;
        private int _direction = -1;   // -1 = izquierda (sprite original), 1 = derecha

        // Estados de animación
        private enum ShimejiState { Idle, Walk, Sit, Blush, Fall, Climb, Happy }
        private ShimejiState _state = ShimejiState.Idle;

        // Diálogos simples
        private readonly string[] _idleLines =
        {
            "Eh… Oye… ¿estás ahí todavía?",
            "Me pregunto si ya descansó…",
            "Jeje… casi me caigo otra vez.",
            "Oye… ¿te acuerdas cuando jugábamos cerca de aquí?",
            "N-no estoy distraída… solo estoy pensando cosas…",
            "Eeeeh… Oye… si no dices nada me pongo nerviosa…",
            "No es que te esté mirando todo el tiempo… pero sí… un poquito.",
            "Si me quedo aquí cerquita es porque… amm… es cómodo, ¿ok?",
            "Tus teclas suenan bonito… me gusta escucharte trabajar.",
            "Si desapareces sin avisar… me preocupo… ¡b-baka!",
            "Yo… no necesito atención… pero si me das un poquito tampoco me quejo.",
            "¿Podrías… quedarte aquí un momento? No sé, me siento mejor contigo.",
            "No es que me sienta sola… solo que contigo no da tanta pereza existir.",
            "Oye… si te distraes, yo también me distraigo… así que… no te vayas.",
            "No te miro porque me gustes… solo porque… ¿qué tal si te caes de la silla?",
            "Si me sonrojo no es por ti… es por… calor… sí, eso.",
            "Te estaba esperando… digo, vigilando… digo… ¡ay, olvídalo!",
            "Si te cansas… puedes apoyarte en mí… bueno, mentalmente, supongo.",
            "Me esfuerzo mucho en no ser una molestia… pero… quiero estar contigo.",
            "A veces pienso que soy torpe porque… tú me pones así.",
            "Si te ríes… me dan ganas de quedarme aquí más tiempo.",
            "No sé qué me pasa cuando estás cerca… pero no quiero que se vaya.",
            "Prometo portarme bien… si te quedas conmigo un ratito.",
            "Si me ves callada… es porque estoy pensando cosas lindas de ti.",
            "Oye… ¿puedo quedarme aquí? Me siento segura contigo."
        };

        public MainWindow()
        {
            InitializeComponent();

            // Sprite inicial
            SetState(ShimejiState.Idle);

            // Colocar en el suelo al inicio
            AttachToSurface(Surface.Floor);

            // Timer principal (movimiento)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _timer.Tick += Update;
            _timer.Start();

            // Timer de diálogo
            _talkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(18)
            };
            _talkTimer.Tick += (s, e) =>
            {
                if (!_isDragging && _surface != Surface.None)
                    SayRandom(_idleLines);
            };
            _talkTimer.Start();
            RenderOptions.SetBitmapScalingMode(ShimejiImage, BitmapScalingMode.Fant);

            // Arrastrar
            MouseLeftButtonDown += OnMouseDown;
            MouseLeftButtonUp += OnMouseUp;
            MouseMove += OnMouseMove;

            // Que diga algo al empezar
            ShowSpeech("Eh… Saiko, creo que ya desperté en tu escritorio.");
        }

        private void MenuCerrar_Click(object sender, RoutedEventArgs e)
        {
            Close(); // cierra la ventana, adiós bakadere (por ahora)
        }

        // ====================
        // Entrada: ratón
        // ====================

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragOffset = e.GetPosition(this);
            CaptureMouse();
            _vy = 0;
            SetState(ShimejiState.Blush);
            ShowSpeech("¿Eeeeh? ¡No me agarres tan de repente!");
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ReleaseMouseCapture();
            // Cuando lo sueltas, cae con gravedad
            BeginFall();
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;

            var screenPos = PointToScreen(e.GetPosition(this));
            Left = screenPos.X - _dragOffset.X;
            Top = screenPos.Y - _dragOffset.Y;

            PositionSpeechBubble();
        }

        private (double screenX, double screenY, double screenW, double screenH) GetCurrentScreenBounds()
        {
            var windowRect = new System.Drawing.Rectangle(
                (int)Left,
                (int)Top,
                (int)Width,
                (int)Height
            );

            var screen = System.Windows.Forms.Screen.FromRectangle(windowRect);
            return (
                screen.WorkingArea.X,
                screen.WorkingArea.Y,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height
            );
        }


        // ====================
        // Update principal
        // ====================

        private void Update(object? sender, EventArgs e)
        {
            if (_isDragging) return;

            if (_sitLock)
            {
                PositionSpeechBubble();
                return;
            }

            if (_lockMovement)
            {
                PositionSpeechBubble();
                return;
            }

            var (screenX, screenY, screenW, screenH) = GetCurrentScreenBounds();
            double w = Width;
            double h = Height;

            if (_surface == Surface.None)
            {
                _vy += Gravity;
                Top += _vy;

                if (Top + h >= screenY + screenH)
                {
                    Top = screenY + screenH - h;    // no atraviesa el piso
                    AttachToSurface(Surface.Floor);
                    StartSitAnimation();
                }
            }
            else
            {
                // CAMINAR SEGÚN SUPERFICIE
                switch (_surface)
                {
                    case Surface.Floor:
                        Top = screenY + screenH - h;
                        Left += _direction * WalkSpeed;
                        RotateTransform.Angle = 0;
                        UpdateFlipForHorizontal();

                        if (_state != ShimejiState.Walk && _state != ShimejiState.Happy)
                            SetState(ShimejiState.Walk);

                        if (Left <= screenX)
                        {
                            Left = screenX;
                            AttachToSurface(Surface.LeftWall);
                        }
                        else if (Left + w >= screenX + screenW)
                        {
                            Left = screenX + screenW - w;
                            AttachToSurface(Surface.RightWall);
                        }
                        break;

                    case Surface.LeftWall:
                        Left = screenX;
                        Top -= WalkSpeed;

                        if (_state != ShimejiState.Climb)
                            SetState(ShimejiState.Climb);

                        // Llega al techo
                        if (Top <= screenY)
                        {
                            Top = screenY;
                            _direction = 1;
                            AttachToSurface(Surface.Ceiling);
                        }
                        break;

                    case Surface.RightWall:
                        Left = screenX + screenW - w;
                        Top -= WalkSpeed;

                        if (_state != ShimejiState.Climb)
                            SetState(ShimejiState.Climb);

                        // Llega al techo
                        if (Top <= screenY)
                        {
                            Top = screenY;
                            _direction = -1;
                            AttachToSurface(Surface.Ceiling);
                        }
                        break;

                    case Surface.Ceiling:
                        Top = screenY;
                        Left += _direction * WalkSpeed;

                        if (_state != ShimejiState.Climb)
                            SetState(ShimejiState.Climb);

                        // A veces se tira del techo
                        if (_rng.NextDouble() < 0.002)
                        {
                            Top += 20;
                            BeginFall();
                            break;
                        }

                        if (Left <= screenX)
                        {
                            Left = screenX;
                            AttachToSurface(Surface.LeftWall);
                        }
                        else if (Left + w >= screenX + screenW)
                        {
                            Left = screenX + screenW - w;
                            AttachToSurface(Surface.RightWall);
                        }
                        break;
                }


                //HandleScreenCorners(screenW, screenH, w, h);

                // A veces se pone feliz random mientras camina
                if (_rng.NextDouble() < 0.002 && _surface == Surface.Floor)
                {
                    SetState(ShimejiState.Happy);
                }
            }

            PositionSpeechBubble();
        }

        private void BeginFall()
        {
            var (screenX, screenY, screenW, screenH) = GetCurrentScreenBounds();
            double w = Width;
            double h = Height;

            if (Top < screenY) Top = screenY;
            if (Top > screenY + screenH - h) Top = screenY + screenH - h;
            if (Left < screenX) Left = screenX;
            if (Left > screenX + screenW - w) Left = screenX + screenW - w;

            _surface = Surface.None;
            _vy = 0;

            ShimejiImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            ShimejiImage.VerticalAlignment = VerticalAlignment.Bottom;

            SetState(ShimejiState.Fall);
        }


        private void StartSitAnimation()
        {
            // Bloquear movimiento mientras está sentada
            _sitLock = true;

            SetState(ShimejiState.Sit);

            var sitTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4.0) // ← CAMBIA ESTE VALOR PARA TIEMPO SENTADA
            };

            sitTimer.Tick += (s, e) =>
            {
                sitTimer.Stop();
                _sitLock = false;
                SetState(ShimejiState.Walk);
            };

            sitTimer.Start();
        }


        // ====================
        // Superficies / esquinas
        // ====================

        private void HandleScreenCorners(double screenW, double screenH, double w, double h)
        {
            switch (_surface)
            {
                case Surface.Floor:
                    Left += _direction * WalkSpeed;
                    Top = screenH - h;
                    RotateTransform.Angle = 0;
                    UpdateFlipForHorizontal();
                    if (_state != ShimejiState.Walk && _state != ShimejiState.Happy)
                        SetState(ShimejiState.Walk);
                    break;

                case Surface.Ceiling:
                    Left += _direction * WalkSpeed;
                    Top = 0;
                    RotateTransform.Angle = 0;
                    if (_state != ShimejiState.Climb)
                        SetState(ShimejiState.Climb);
                    break;

                case Surface.LeftWall:
                    Top -= _direction * WalkSpeed;
                    Left = 0;
                    RotateTransform.Angle = 0;
                    if (_state != ShimejiState.Climb)
                        SetState(ShimejiState.Climb);
                    break;

                case Surface.RightWall:
                    Top += _direction * WalkSpeed;
                    Left = screenW - w;
                    RotateTransform.Angle = 0;
                    if (_state != ShimejiState.Climb)
                        SetState(ShimejiState.Climb);
                    break;
            }
        }

        private void AttachToSurface(Surface s, int? dirOverride = null)
        {
            _surface = s;
            _vy = 0;

            var (screenX, screenY, screenW, screenH) = GetCurrentScreenBounds();
            double w = Width;
            double h = Height;

            if (dirOverride.HasValue)
                _direction = dirOverride.Value;
            else if (_direction == 0)
                _direction = -1;

            RotateTransform.Angle = 0;

            switch (s)
            {
                case Surface.Floor:
                    Top = screenY + screenH - h;
                    if (Left < screenX) Left = screenX;
                    if (Left > screenX + screenW - w) Left = screenX + screenW - w;

                    ShimejiImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    ShimejiImage.VerticalAlignment = VerticalAlignment.Bottom;

                    UpdateFlipForHorizontal();
                    SetState(ShimejiState.Walk);
                    break;

                case Surface.LeftWall:
                    Left = screenX;
                    if (Top < screenY) Top = screenY;
                    if (Top > screenY + screenH - h) Top = screenY + screenH - h;

                    ShimejiImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    ShimejiImage.VerticalAlignment = VerticalAlignment.Center;

                    FlipTransform.ScaleX = 1;
                    SetState(ShimejiState.Climb);
                    break;

                case Surface.RightWall:
                    Left = screenX + screenW - w;
                    if (Top < screenY) Top = screenY;
                    if (Top > screenY + screenH - h) Top = screenY + screenH - h;

                    ShimejiImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                    ShimejiImage.VerticalAlignment = VerticalAlignment.Center;

                    FlipTransform.ScaleX = 1;
                    SetState(ShimejiState.Climb);
                    break;

                case Surface.Ceiling:
                    Top = screenY;
                    if (Left < screenX) Left = screenX;
                    if (Left > screenX + screenW - w) Left = screenX + screenW - w;

                    ShimejiImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    ShimejiImage.VerticalAlignment = VerticalAlignment.Top;

                    SetState(ShimejiState.Climb);
                    break;
            }
        }


        // sprite mirando según dirección:
        // PNG original está mirando a la IZQUIERDA.
        private void UpdateFlipForHorizontal()
        {
            // Sprite original mira a la izquierda
            FlipTransform.ScaleX = _direction == -1 ? 1 : -1;
        }


        // ====================
        // Animaciones (PNGs)
        // ====================

        private void SetState(ShimejiState newState)
        {
            // Permitimos reentrar cuando es Climb o Happy
            if (_state == newState &&
                newState != ShimejiState.Climb &&
                newState != ShimejiState.Happy)
                return;

            _state = newState;

            string file;

            switch (_state)
            {
                case ShimejiState.Idle:
                    file = "Assets/idle.png";
                    break;

                case ShimejiState.Walk:
                    file = "Assets/walk.png";
                    break;

                case ShimejiState.Sit:
                    file = "Assets/sit.png";
                    break;

                case ShimejiState.Blush:
                    file = "Assets/blush.png";
                    break;

                case ShimejiState.Fall:
                    file = "Assets/cae.png";
                    break;

                case ShimejiState.Happy:
                    file = _rng.Next(0, 2) == 0
                        ? "Assets/happy.png"
                        : "Assets/happy2.png";
                    break;

                case ShimejiState.Climb:
                    // Elegimos sprite según superficie + dirección
                    file = GetClimbSprite();
                    break;

                default:
                    file = "Assets/idle.png";
                    break;
            }

            SetSprite(file);

            // ===== Comportamiento especial por estado =====

            // Caída
            if (_state == ShimejiState.Fall)
            {
                RotateTransform.Angle = 0;
                FlipTransform.ScaleX = _direction == -1 ? 1 : -1;

                ShimejiImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                ShimejiImage.VerticalAlignment = VerticalAlignment.Bottom;
            }

            //se queda quieta unos segundos
            if (_state == ShimejiState.Happy)
            {
                _lockMovement = true;

                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    _lockMovement = false;
                    if (_state == ShimejiState.Happy)
                        SetState(ShimejiState.Walk);
                };
                t.Start();
            }
        }


        private string GetClimbSprite()
        {
            // climbr: pared derecha
            // climbl: pared izquierda
            // climbcr: techo moviéndose a la derecha
            // climbcl: techo moviéndose a la izquierda
            return _surface switch
            {
                Surface.LeftWall => "Assets/climbl.png",
                Surface.RightWall => "Assets/climbr.png",
                Surface.Ceiling => _direction == 1
                                        ? "Assets/climbcr.png"
                                        : "Assets/climbcl.png",
                _ => "Assets/climbl.png"
            };
        }


        private void SetSprite(string relativePath)
        {
            try
            {
                // relativePath tipo: "Assets/idle.png"
                var uri = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                ShimejiImage.Source = bmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error cargando sprite: " + ex.Message);
            }
        }


        // ====================
        // Diálogos
        // ====================

        private void ShowSpeech(string text)
        {
            SpeechText.Text = text;
            PositionSpeechBubble();
            SpeechBubble.Opacity = 1;

            var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            hideTimer.Tick += (s, e) =>
            {
                SpeechBubble.Opacity = 0;
                hideTimer.Stop();
            };
            hideTimer.Start();
        }

        private void SayRandom(string[] lines)
        {
            if (lines.Length == 0) return;
            string t = lines[_rng.Next(lines.Length)];
            ShowSpeech(t);
        }

        private void PositionSpeechBubble()
        {
            // Ya no hace falta moverlo, el Grid lo mantiene arriba centrado.
        }
    }
}
