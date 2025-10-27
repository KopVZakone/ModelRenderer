using GraphicsLib;
using GraphicsLib.Shaders;
using GraphicsLib.Types;
using GraphicsLib.Types2;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static GraphicsLib.Types2.Shaders.PbrShader;
using static GraphicsLib.Types2.Shaders.PhongShader;

namespace Lab1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private LightEditorViewModel ViewModel => (LightEditorViewModel)DataContext;

        private readonly OpenFileDialog ofd;

        private static readonly Camera camera = new Camera();
        private Point oldPos;
        private WriteableBitmap? bitmap;
        private readonly ModelRenderer modelRenderer;
        private ModelScene? modelScene;
        private LightSource[] lightSources = [];
        public MainWindow()
        {
            InitializeComponent();

            ofd = new OpenFileDialog
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                ValidateNames = true,
                Filter = "GLTF files(*.gltf)|*.gltf|OBJ files (*.obj)|*.obj|All files (*.*)|*.*"
            };
            ofd.FileOk += OnFileOpened;
            Height = SystemParameters.PrimaryScreenHeight / 1.25;
            Width = SystemParameters.PrimaryScreenWidth / 1.25;
            modelRenderer = new ModelRenderer();
            DebugPanel.Visibility = Visibility.Visible;
            Stopwatch s = Stopwatch.StartNew();
            Stopwatch s2 = new();
            Stopwatch elapsed = Stopwatch.StartNew();
            int frameCount = 0;
            int currentFrameCount = 0;
            CompositionTarget.Rendering += (o, e) => {
                double delta = s2.Elapsed.TotalMilliseconds;
                s2.Restart();
                if (pause)
                {
                    elapsed.Stop();
                }
                else
                {
                    elapsed.Start();
                }
                if (ViewModel.ChangesPending)
                {
                    lightSources = MapLightSources(ViewModel.LightSources);
                    if (modelScene != null)
                    {
                        
                        modelScene.LightSources = lightSources;
                    }                  
                    ViewModel.ApproveChanges();
                }
                ModelDraw((float)elapsed.Elapsed.TotalSeconds);
                frameCount++;
                DebugPanel.Text = $"fps {currentFrameCount} time {(int)(delta)}";
                if (s.ElapsedMilliseconds >= 1000)
                {
                    currentFrameCount = frameCount;
                    s.Restart();
                    frameCount = 0;
                }
            };
        }
        private static LightSource[] MapLightSources(ObservableCollection<LightSourceModel> lightSources)
        {
            LightSource[] result = new LightSource[lightSources.Count];
            foreach (LightSourceModel lightSource in lightSources)
            {
                switch (lightSource)
                {
                    case PointLightSourceModel pointLight:
                        result[lightSources.IndexOf(lightSource)] = new PointLightSource()
                        {
                            Color = new Vector3(pointLight.Color.X, pointLight.Color.Y, pointLight.Color.Z),
                            Position = new Vector3(pointLight.Position.X, pointLight.Position.Y, pointLight.Position.Z),
                            Intensity = pointLight.Intensity,
                            ShadowMapSize = pointLight.ShadowMapSize,
                        };
                        break;
                    case DirectionalLightSourceModel directionalLight:
                        result[lightSources.IndexOf(lightSource)] = new DirectionalLightSource()
                        {
                            Color = new Vector3(directionalLight.Color.X, directionalLight.Color.Y, directionalLight.Color.Z),
                            CoverSize = directionalLight.CoverSize,
                            Direction = Vector3.Normalize(new Vector3(directionalLight.Direction.X, directionalLight.Direction.Y, directionalLight.Direction.Z)),
                            Intensity = directionalLight.Intensity,
                            ShadowMapSize = directionalLight.ShadowMapSize,
                        };
                        break;
                    case SpotLightSourceModel spotLight:
                        result[lightSources.IndexOf(lightSource)] = new SpotLightSource()
                        {
                            Color = new Vector3(spotLight.Color.X, spotLight.Color.Y, spotLight.Color.Z),
                            Position = new Vector3(spotLight.Position.X, spotLight.Position.Y, spotLight.Position.Z),
                            Direction = Vector3.Normalize(new Vector3(spotLight.Direction.X, spotLight.Direction.Y, spotLight.Direction.Z)),
                            Intensity = spotLight.Intensity,
                            CutOffCos = float.Cos(float.DegreesToRadians(spotLight.CutOff)),
                            OuterCutCos = float.Cos(float.DegreesToRadians(spotLight.OuterCutOff)),
                            ShadowMapSize = spotLight.ShadowMapSize,
                        };
                        break;
                }
            }
            return result;
        }

        private void OnFileOpened(object? sender, CancelEventArgs e)
        {
            fileName.Text = string.Join(' ', Resources["fileString"].ToString(), ofd.SafeFileName);
            try
            {
                ModelScene scene = ModelParser.ParseGltfFile(ofd.FileName);
                scene.Camera = camera;
                camera.Polar = MathF.PI / 2;
                camera.Target = new Vector3(0, 1, 0);
                modelScene = scene;
                scene.LightSources = lightSources;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            bitmap = new WriteableBitmap(
                    ((int)canvas.ActualWidth), ((int)canvas.ActualHeight), 96, 96, PixelFormats.Bgra32, null);
            ForcedDraw();
        }
        private void OpenPopupButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsPopupOpen = true;
        }
        private void ModelDraw(float secondsElapsed)
        {
            if(modelScene == null || bitmap == null)
            {
                return;
            }
            camera.UpdateViewPort(bitmap.PixelWidth, bitmap.PixelHeight);
            ModelRenderer.TimeElapsed = secondsElapsed;
            switch (renderMode)
            {
                case RenderMode.Textured:
                    {
                        modelRenderer.Render<GraphicsLib.Types2.Shaders.PbrShader, PbrVertex>(modelScene, bitmap);
                    }
                    break;
                case RenderMode.Shadowed:
                    {
                        modelRenderer.RenderShadow<GraphicsLib.Types2.Shaders.ShadowedPbrShader, PbrVertex>(modelScene, bitmap);
                    }
                    break;
                case RenderMode.Wireframe:
                    break;
                case RenderMode.Solid:
                    break;
                case RenderMode.Smooth:
                    {
                        modelRenderer.Render<GraphicsLib.Types2.Shaders.PhongShader, PhongVertex>(modelScene, bitmap);
                    }
                    break;
                default:
                    break;
            }

            bitmap.Lock();
            bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            bitmap.Unlock();
            canvas.Child = new Image { Source = bitmap };
        }
        private void ForcedDraw()
        {
            //unused
        }

        private void ButtonOpenFile_Click(object sender, RoutedEventArgs e)
        {
            ofd.ShowDialog();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Mouse.Capture(canvas);
            oldPos = Mouse.GetPosition(canvas);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && Mouse.Captured == canvas && oldPos.X != -1)
            {

                Point newPos = Mouse.GetPosition(canvas);
                float dx = (float)(newPos.X - oldPos.X);
                float dy = (float)(newPos.Y - oldPos.Y);
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    Vector3 target = camera.Target;
                    Matrix4x4.Invert(camera.ViewMatrix, out Matrix4x4 view);
                    float distanceX = (float)((dx / canvas.ActualWidth * 2) * Math.Tan(camera.FieldOfView / 2) * camera.Distance * (canvas.ActualWidth / canvas.ActualHeight));
                    float distanceY = (float)((dy / canvas.ActualHeight * 2) * Math.Tan(camera.FieldOfView / 2)) * camera.Distance;
                    float length = MathF.Sqrt(distanceX * distanceX + distanceY * distanceY);
                    Vector3 direction = Vector3.Normalize(Vector3.TransformNormal(new Vector3(-distanceX, distanceY, 0), view));
                    direction *= length;
                    camera.Target = target + direction;

                }
                else
                {
                    camera.RotateAroundTargetHorizontal((float)(-dx * MathF.PI / canvas.ActualWidth));
                    camera.RotateAroundTargetVertical((float)(-dy * MathF.PI / canvas.ActualHeight));
                }
                ForcedDraw();
                oldPos = newPos;
            }
            else
            {
                oldPos = new Point(-1, -1);
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Mouse.Capture(canvas, CaptureMode.None);
            oldPos = new Point(-1, -1);
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            float dz = (float)e.Delta;
            float step = Keyboard.Modifiers == ModifierKeys.Control ? 0.002f : 0.0005f;
            camera.MoveTowardTarget(dz * step * camera.Distance);
            ForcedDraw();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }

        private void canvas_GotMouseCapture(object sender, MouseEventArgs e) { }

        private void canvas_LostMouseCapture(object sender, MouseEventArgs e) { }

        private RenderMode renderMode = RenderMode.Textured;

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton && radioButton.IsChecked == true)
            {
                switch (radioButton.GroupName)
                {
                    case "RenderingMode":
                        renderMode = Enum.Parse<RenderMode>(((RadioButton)sender).Content.ToString()!);
                        ForcedDraw();
                        break;
                }
            }

        }
        private static bool pause = false;
        private readonly Dictionary<Key, Action> moveActions = new() {
            {
                Key.Q, () =>
                {
                    pause = !pause;
                }
            }
        };

        
        private void canvas_KeyDown(object sender, KeyEventArgs e)
        {
            Dictionary<Key, Action> handlers = moveActions;
            if (handlers.TryGetValue(e.Key, out Action? action))
            {
                action();
            }
            ForcedDraw();
        }
    }
}