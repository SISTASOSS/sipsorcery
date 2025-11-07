//-----------------------------------------------------------------------------
// Filename: SettingsWindow.xaml.cs
//
// Description: UI for configuring local media devices and previewing webcam feed.
//
//-----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Windows.VirtualBackground;

namespace SIPSorcery.SoftPhone
{
    public partial class SettingsWindow : Window
    {
        private readonly List<AudioDeviceOption> _audioInputDevices = new();
        private readonly List<AudioDeviceOption> _audioOutputDevices = new();
        private readonly List<VideoDeviceOption> _videoDevices = new();
        private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<SettingsWindow>();

        private WindowsVideoEndPoint _previewEndPoint;
        private bool _isInitialising;
        private bool _hasPreviewFrame;
        private const int MinPreviewWidth = 160;
        private const int MaxPreviewWidth = 3840;
        private const int MinPreviewHeight = 120;
        private const int MaxPreviewHeight = 2160;

        private record AudioDeviceOption(int Index, string DisplayName);

        private record VideoDeviceOption(string Name, string Id, string DisplayName);

        public SettingsWindow()
        {
            InitializeComponent();
            logger.LogDebug("Settings window constructed.");
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("Settings window loaded; updating device lists.");
            _isInitialising = true;
            LoadAudioInputDevices();
            LoadAudioOutputDevices();
            await LoadVideoDevicesAsync();
            SetInitialSelections();
            InitializeVirtualBackgroundControls();
            InitializePreviewDimensionControls();
            _isInitialising = false;

            logger.LogDebug("Initial device selections applied; starting preview.");
            await StartPreviewForSelectionAsync();
        }

        private void LoadAudioInputDevices()
        {
            logger.LogDebug("Loading audio input devices.");
            _audioInputDevices.Clear();
            _audioInputDevices.Add(new AudioDeviceOption(-1, "Default device"));

            try
            {
                for (int index = 0; index < WaveInEvent.DeviceCount; index++)
                {
                    var capabilities = WaveInEvent.GetCapabilities(index);
                    var displayName = string.IsNullOrWhiteSpace(capabilities.ProductName)
                        ? $"Microphone {index}"
                        : capabilities.ProductName;

                    _audioInputDevices.Add(new AudioDeviceOption(index, displayName));
                }

                logger.LogDebug("Detected {Count} audio input devices.", _audioInputDevices.Count - 1);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to enumerate audio input devices.");
                SetStatusMessage($"Unable to enumerate audio devices: {ex.Message}");
            }

            AudioInputDevicesComboBox.ItemsSource = _audioInputDevices;
        }

        private void LoadAudioOutputDevices()
        {
            logger.LogDebug("Loading audio output devices.");
            _audioOutputDevices.Clear();
            _audioOutputDevices.Add(new AudioDeviceOption(-1, "Default device"));

            try
            {
                for (int index = 0; index < WaveOut.DeviceCount; index++)
                {
                    var capabilities = WaveOut.GetCapabilities(index);
                    var displayName = string.IsNullOrWhiteSpace(capabilities.ProductName)
                        ? $"Speaker {index}"
                        : capabilities.ProductName;

                    _audioOutputDevices.Add(new AudioDeviceOption(index, displayName));
                }

                logger.LogDebug("Detected {Count} audio output devices.", _audioOutputDevices.Count - 1);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to enumerate audio output devices.");
                SetStatusMessage($"Unable to enumerate audio output devices: {ex.Message}");
            }

            AudioOutputDevicesComboBox.ItemsSource = _audioOutputDevices;
        }

        private async Task LoadVideoDevicesAsync()
        {
            logger.LogDebug("Loading video devices.");
            _videoDevices.Clear();
            _videoDevices.Add(new VideoDeviceOption(string.Empty, string.Empty, "None (use test pattern)"));

            try
            {
                var webcams = await WindowsVideoEndPoint.GetVideoCatpureDevices();
                if (webcams != null)
                {
                    foreach (var webcam in webcams)
                    {
                        string name = webcam.Name;
                        string id = webcam.ID;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _videoDevices.Add(new VideoDeviceOption(name, id ?? name, name));
                        }
                    }

                    logger.LogDebug("Detected {Count} video devices.", _videoDevices.Count - 1);
                }
                else
                {
                    logger.LogWarning("No video capture devices were returned by the system.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to enumerate video devices.");
                SetStatusMessage($"Unable to enumerate video devices: {ex.Message}");
            }

            VideoDevicesComboBox.ItemsSource = _videoDevices;
        }

        private void SetInitialSelections()
        {
            logger.LogDebug("Applying initial device selections from SIPSoftPhoneState.");
            AudioInputDevicesComboBox.SelectedValue = _audioInputDevices.Any(x => x.Index == SIPSoftPhoneState.AudioInDeviceIndex)
                ? SIPSoftPhoneState.AudioInDeviceIndex
                : -1;

            AudioOutputDevicesComboBox.SelectedValue = _audioOutputDevices.Any(x => x.Index == SIPSoftPhoneState.AudioOutDeviceIndex)
                ? SIPSoftPhoneState.AudioOutDeviceIndex
                : -1;

            if (!string.IsNullOrWhiteSpace(SIPSoftPhoneState.VideoDeviceName) &&
                _videoDevices.Any(v => string.Equals(v.Name, SIPSoftPhoneState.VideoDeviceName, StringComparison.OrdinalIgnoreCase)))
            {
                VideoDevicesComboBox.SelectedValue = SIPSoftPhoneState.VideoDeviceName;
            }
            else
            {
                VideoDevicesComboBox.SelectedIndex = 0;
            }

            logger.LogDebug("Initial selections set: audio in {AudioIn}, audio out {AudioOut}, video {Video}.",
                AudioInputDevicesComboBox.SelectedValue,
                AudioOutputDevicesComboBox.SelectedValue,
                VideoDevicesComboBox.SelectedValue);
        }

        private void InitializeVirtualBackgroundControls()
        {
            logger.LogDebug("Initialising virtual background controls.");

            var modes = Enum.GetValues(typeof(VirtualBackgroundMode)).Cast<VirtualBackgroundMode>().ToArray();
            VirtualBackgroundModeComboBox.ItemsSource = modes;
            VirtualBackgroundModeComboBox.SelectedItem = SIPSoftPhoneState.VirtualBackgroundMode;

            BlurRadiusTextBox.Text = SIPSoftPhoneState.VirtualBackgroundBlurRadius.ToString();
            MaskReuseFrameCountTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskReuseFrameCount.ToString();
            SegmentationModelPathTextBox.Text = SIPSoftPhoneState.VirtualBackgroundSegmentationModelPath ?? string.Empty;
            BackgroundImagePathTextBox.Text = SIPSoftPhoneState.VirtualBackgroundBackgroundImagePath ?? string.Empty;
            PerformanceBudgetMillisecondsTextBox.Text = SIPSoftPhoneState.VirtualBackgroundPerformanceBudgetMilliseconds.ToString();
            PerformanceFallbackFrameCountTextBox.Text = SIPSoftPhoneState.VirtualBackgroundPerformanceFallbackFrameCount.ToString();
            EnablePerformanceFallbackCheckBox.IsChecked = SIPSoftPhoneState.VirtualBackgroundEnablePerformanceFallback;
            MaskSmoothingRadiusTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskSmoothingRadius.ToString(CultureInfo.InvariantCulture);
            MaskSmoothingIterationsTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskSmoothingIterations.ToString(CultureInfo.InvariantCulture);
            MaskFeatherTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskFeather.ToString(CultureInfo.InvariantCulture);
            MaskForegroundClampTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskForegroundClamp.ToString(CultureInfo.InvariantCulture);
            MaskForegroundExponentTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskForegroundExponent.ToString(CultureInfo.InvariantCulture);
            MaskForegroundBiasTextBox.Text = SIPSoftPhoneState.VirtualBackgroundMaskForegroundBias.ToString(CultureInfo.InvariantCulture);
            SegmentationScaleTextBox.Text = SIPSoftPhoneState.VirtualBackgroundSegmentationScale.ToString(CultureInfo.InvariantCulture);
            EffectProcessingScaleTextBox.Text = SIPSoftPhoneState.VirtualBackgroundEffectProcessingScale.ToString(CultureInfo.InvariantCulture);
            EnableAdaptiveSegmentationScaleCheckBox.IsChecked = SIPSoftPhoneState.VirtualBackgroundEnableAdaptiveSegmentationScale;
            UseDirectMlExecutionProviderCheckBox.IsChecked = SIPSoftPhoneState.VirtualBackgroundUseOnnxDirectMlExecutionProvider;

            UpdateVirtualBackgroundControlsState();
            UpdatePerformanceFallbackControlsState();

            logger.LogDebug("Initial virtual background UI state: Mode={Mode}, Blur={Blur}, MaskReuse={MaskReuse}, MaskSmoothRadius={MaskSmoothRadius}, MaskSmoothIterations={MaskSmoothIterations}, MaskFeather={MaskFeather}, MaskClamp={MaskClamp}, MaskExponent={MaskExponent}, SegScale={SegScale}, EffectScale={EffectScale}, AdaptiveSeg={AdaptiveSeg}, FallbackEnabled={Fallback}, DirectML={DirectML}.",
                SIPSoftPhoneState.VirtualBackgroundMode,
                SIPSoftPhoneState.VirtualBackgroundBlurRadius,
                SIPSoftPhoneState.VirtualBackgroundMaskReuseFrameCount,
                SIPSoftPhoneState.VirtualBackgroundMaskSmoothingRadius,
                SIPSoftPhoneState.VirtualBackgroundMaskSmoothingIterations,
                SIPSoftPhoneState.VirtualBackgroundMaskFeather,
                SIPSoftPhoneState.VirtualBackgroundMaskForegroundClamp,
                SIPSoftPhoneState.VirtualBackgroundMaskForegroundExponent,
                SIPSoftPhoneState.VirtualBackgroundSegmentationScale,
                SIPSoftPhoneState.VirtualBackgroundEffectProcessingScale,
                SIPSoftPhoneState.VirtualBackgroundEnableAdaptiveSegmentationScale,
                SIPSoftPhoneState.VirtualBackgroundEnablePerformanceFallback,
                SIPSoftPhoneState.VirtualBackgroundUseOnnxDirectMlExecutionProvider);
        }

        private void InitializePreviewDimensionControls()
        {
            PreviewWidthTextBox.Text = SIPSoftPhoneState.VideoPreviewWidth.ToString();
            PreviewHeightTextBox.Text = SIPSoftPhoneState.VideoPreviewHeight.ToString();
        }

        private void UpdateVirtualBackgroundControlsState()
        {
            var mode = GetSelectedVirtualBackgroundMode();
            bool effectsEnabled = mode != VirtualBackgroundMode.None;
            bool imageMode = mode == VirtualBackgroundMode.Image;

            BlurRadiusTextBox.IsEnabled = effectsEnabled;
            MaskReuseFrameCountTextBox.IsEnabled = effectsEnabled;
            SegmentationModelPathTextBox.IsEnabled = effectsEnabled;
            BrowseSegmentationModelButton.IsEnabled = effectsEnabled;

            BackgroundImagePathTextBox.IsEnabled = imageMode;
            BrowseBackgroundImageButton.IsEnabled = imageMode;
            MaskSmoothingRadiusTextBox.IsEnabled = effectsEnabled;
            MaskSmoothingIterationsTextBox.IsEnabled = effectsEnabled;
            MaskFeatherTextBox.IsEnabled = effectsEnabled;
            MaskForegroundClampTextBox.IsEnabled = effectsEnabled;
            MaskForegroundExponentTextBox.IsEnabled = effectsEnabled;
            MaskForegroundBiasTextBox.IsEnabled = effectsEnabled;
            SegmentationScaleTextBox.IsEnabled = effectsEnabled;
            EffectProcessingScaleTextBox.IsEnabled = effectsEnabled;
            EnableAdaptiveSegmentationScaleCheckBox.IsEnabled = effectsEnabled;
            UseDirectMlExecutionProviderCheckBox.IsEnabled = effectsEnabled;
            BackgroundImageRow.Visibility = imageMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePerformanceFallbackControlsState()
        {
            bool fallbackEnabled = EnablePerformanceFallbackCheckBox.IsChecked == true;
            PerformanceBudgetMillisecondsTextBox.IsEnabled = fallbackEnabled;
            PerformanceFallbackFrameCountTextBox.IsEnabled = fallbackEnabled;
        }

        private VirtualBackgroundMode GetSelectedVirtualBackgroundMode()
        {
            if (VirtualBackgroundModeComboBox.SelectedItem is VirtualBackgroundMode mode)
            {
                return mode;
            }

            if (VirtualBackgroundModeComboBox.SelectedValue is VirtualBackgroundMode selectedMode)
            {
                return selectedMode;
            }

            return VirtualBackgroundMode.None;
        }

        private VirtualBackgroundOptions CreateVirtualBackgroundOptions(out string statusMessage)
        {
            var mode = GetSelectedVirtualBackgroundMode();
            int blurRadius = ParseIntOrDefault(BlurRadiusTextBox.Text, SIPSoftPhoneState.VirtualBackgroundBlurRadius, 0, 128);
            int maskReuseCount = ParseIntOrDefault(MaskReuseFrameCountTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskReuseFrameCount, 0, 60);
            string segmentationPath = NormalizePath(SegmentationModelPathTextBox.Text);
            string backgroundImagePath = NormalizePath(BackgroundImagePathTextBox.Text);
            bool enableFallback = EnablePerformanceFallbackCheckBox.IsChecked == true;
            int performanceBudget = ParseIntOrDefault(PerformanceBudgetMillisecondsTextBox.Text, SIPSoftPhoneState.VirtualBackgroundPerformanceBudgetMilliseconds, 1, 1000);
            int fallbackFrameCount = ParseIntOrDefault(PerformanceFallbackFrameCountTextBox.Text, SIPSoftPhoneState.VirtualBackgroundPerformanceFallbackFrameCount, 1, 120);
            bool useDirectMl = UseDirectMlExecutionProviderCheckBox.IsChecked == true;
            int maskSmoothingRadius = ParseIntOrDefault(MaskSmoothingRadiusTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskSmoothingRadius, 0, 16);
            int maskSmoothingIterations = ParseIntOrDefault(MaskSmoothingIterationsTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskSmoothingIterations, 0, 4);
            float maskFeather = ParseFloatOrDefault(MaskFeatherTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskFeather, 0f, 1f);
            float maskForegroundClamp = ParseFloatOrDefault(MaskForegroundClampTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskForegroundClamp, 0f, 1f);
            float maskForegroundExponent = ParseFloatOrDefault(MaskForegroundExponentTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskForegroundExponent, 0.2f, 2.5f);
            float maskForegroundBias = ParseFloatOrDefault(MaskForegroundBiasTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskForegroundBias, -1f, 1f);
            float segmentationScale = ParseFloatOrDefault(SegmentationScaleTextBox.Text, SIPSoftPhoneState.VirtualBackgroundSegmentationScale, 0.1f, 1f);
            float effectProcessingScale = ParseFloatOrDefault(EffectProcessingScaleTextBox.Text, SIPSoftPhoneState.VirtualBackgroundEffectProcessingScale, 0.1f, 1f);
            bool enableAdaptiveSegmentation = EnableAdaptiveSegmentationScaleCheckBox.IsChecked == true;

            statusMessage = null;

            if (mode != VirtualBackgroundMode.None)
            {
                if (string.IsNullOrEmpty(segmentationPath) || !File.Exists(segmentationPath))
                {
                    statusMessage ??= "Segmentation model path not found. Virtual background disabled.";
                    mode = VirtualBackgroundMode.None;
                    segmentationPath = null;
                    backgroundImagePath = null;
                    useDirectMl = false;
                    effectProcessingScale = 1f;
                    enableAdaptiveSegmentation = false;
                }
            }

            if (mode == VirtualBackgroundMode.Image)
            {
                if (string.IsNullOrEmpty(backgroundImagePath) || !File.Exists(backgroundImagePath))
                {
                    statusMessage ??= "Background image not found. Falling back to blur.";
                    mode = VirtualBackgroundMode.Blur;
                    backgroundImagePath = null;
                }
            }
            else if (mode == VirtualBackgroundMode.None)
            {
                useDirectMl = false;
                effectProcessingScale = 1f;
                enableAdaptiveSegmentation = false;
            }

            var options = new VirtualBackgroundOptions
            {
                Mode = mode,
                BlurRadius = blurRadius,
                MaskReuseFrameCount = maskReuseCount,
                SegmentationModelPath = segmentationPath,
                BackgroundImagePath = backgroundImagePath,
                EnablePerformanceFallback = enableFallback,
                PerformanceBudgetMilliseconds = performanceBudget,
                PerformanceFallbackFrameCount = fallbackFrameCount,
                MaskSmoothingRadius = maskSmoothingRadius,
                MaskSmoothingIterations = maskSmoothingIterations,
                MaskFeather = maskFeather,
                MaskForegroundClamp = maskForegroundClamp,
                MaskForegroundExponent = maskForegroundExponent,
                MaskForegroundBias = maskForegroundBias,
                SegmentationScale = segmentationScale,
                EffectProcessingScale = effectProcessingScale,
                EnableAdaptiveSegmentationScale = enableAdaptiveSegmentation,
                EnableWinML = false,
                UseOnnxDirectMlExecutionProvider = useDirectMl
            };

            logger.LogDebug("Virtual background options constructed: Mode={Mode}, BlurRadius={Blur}, MaskReuse={MaskReuse}, MaskSmoothRadius={MaskSmoothRadius}, MaskSmoothIterations={MaskSmoothIterations}, MaskFeather={MaskFeather}, MaskClamp={MaskClamp}, MaskExponent={MaskExponent}, MaskBias={MaskBias}, SegScale={SegScale}, EffectScale={EffectScale}, AdaptiveSeg={AdaptiveSeg}, FallbackEnabled={Fallback}, Budget={Budget}, FallbackFrames={FallbackFrames}.",
                options.Mode,
                options.BlurRadius,
                options.MaskReuseFrameCount,
                options.MaskSmoothingRadius,
                options.MaskSmoothingIterations,
                options.MaskFeather,
                options.MaskForegroundClamp,
                options.MaskForegroundExponent,
                options.MaskForegroundBias,
                options.SegmentationScale,
                options.EffectProcessingScale,
                options.EnableAdaptiveSegmentationScale,
                options.EnablePerformanceFallback,
                options.PerformanceBudgetMilliseconds,
                options.PerformanceFallbackFrameCount);

            return options;
        }

        private Task RestartPreviewIfReadyAsync()
        {
            if (IsLoaded && !_isInitialising)
            {
                return StartPreviewForSelectionAsync();
            }

            return Task.CompletedTask;
        }

        private async Task StartPreviewForSelectionAsync()
        {
            if (_isInitialising)
            {
                logger.LogTrace("Preview start skipped during initialization.");
                return;
            }

            var selectedVideoDevice = VideoDevicesComboBox.SelectedItem as VideoDeviceOption;
            logger.LogDebug("Starting preview for selected video device {Device}.", selectedVideoDevice?.Name ?? "(none)");
            await StartVideoPreviewAsync(selectedVideoDevice?.Name);
        }

        private async Task StartVideoPreviewAsync(string deviceName)
        {
            logger.LogDebug("Starting video preview for device {Device}.", deviceName ?? "(none)");
            await StopVideoPreviewAsync();

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                logger.LogInformation("No video device selected; using test pattern.");
                _hasPreviewFrame = false;
                Dispatcher.Invoke(() =>
                {
                    PreviewPlaceholder.Visibility = Visibility.Visible;
                    VideoPreviewImage.Source = null;
                    SetStatusMessage("Select a video input to preview.");
                });
                return;
            }

            try
            {
                var options = CreateVirtualBackgroundOptions(out string optionsMessage);
                var (previewWidth, previewHeight) = GetDesiredPreviewDimensions();
                NormalizePreviewDimensionInputs();

                _previewEndPoint = new WindowsVideoEndPoint(
                    new VpxVideoEncoder(),
                    deviceName,
                    width: (uint)previewWidth,
                    height: (uint)previewHeight,
                    15,
                    VideoCaptureFormatPreference.ClosestToRequest);
                _previewEndPoint.ConfigureVirtualBackground(options);

                _previewEndPoint.SetVideoSourceFormat(new VideoFormat(VideoCodecsEnum.VP8,96));
                _previewEndPoint.OnVideoSourceRawSample += PreviewVideoSampleReady;
                _hasPreviewFrame = false;
                await _previewEndPoint.StartVideo();

                logger.LogInformation("Video preview started for device {Device}.", deviceName);
                var status = optionsMessage ?? $"Starting video preview ({previewWidth}x{previewHeight})...";
                SetStatusMessage(status);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to start video preview for device {Device}.", deviceName);
                await StopVideoPreviewAsync();
                SetStatusMessage($"Unable to start video preview: {ex.Message}");
            }
        }

        private async Task StopVideoPreviewAsync()
        {
            if (_previewEndPoint != null)
            {
                logger.LogDebug("Stopping video preview.");
                var previewEndPoint = _previewEndPoint;
                try
                {
                    await previewEndPoint.CloseVideo();
                }
                catch (Exception)
                {
                    
                    // Ignore errors when shutting down preview.
                    logger.LogTrace("Ignoring exception while closing preview endpoint.");
                }
                finally
                {
                    previewEndPoint.OnVideoSourceRawSample -= PreviewVideoSampleReady;
                    _previewEndPoint = null;
                }
            }

            Dispatcher.Invoke(() =>
            {
                _hasPreviewFrame = false;
                VideoPreviewImage.Source = null;
                PreviewPlaceholder.Visibility = Visibility.Visible;
            });
        }

        private void PreviewVideoSampleReady(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (sample == null || sample.Length == 0)
            {
                logger.LogWarning("Received empty video sample for preview.");
                return;
            }

            byte[] buffer = sample;
            PixelFormat wpfPixelFormat;
            int stride;

            switch (pixelFormat)
            {
                case VideoPixelFormatsEnum.Bgra:
                    stride = width * 4;
                    wpfPixelFormat = PixelFormats.Bgra32;
                    break;

                case VideoPixelFormatsEnum.Bgr:
                    stride = width * 3;
                    wpfPixelFormat = PixelFormats.Bgr24;
                    break;

                case VideoPixelFormatsEnum.Rgb:
                    stride = width * 3;
                    wpfPixelFormat = PixelFormats.Rgb24;
                    break;

                case VideoPixelFormatsEnum.NV12:
                    buffer = ConvertNv12ToBgr(sample, width, height);
                    stride = width * 3;
                    wpfPixelFormat = PixelFormats.Bgr24;
                    break;

                case VideoPixelFormatsEnum.I420:
                    buffer = ConvertI420ToBgr(sample, width, height);
                    stride = width * 3;
                    wpfPixelFormat = PixelFormats.Bgr24;
                    break;

                default:
                    logger.LogWarning("Unsupported pixel format received: {PixelFormat}", pixelFormat);
                    SetStatusMessage($"Unsupported pixel format: {pixelFormat}");
                    return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PreviewPlaceholder.Visibility == Visibility.Visible)
                {
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                }

                if (!_hasPreviewFrame)
                {
                    _hasPreviewFrame = true;
                    logger.LogInformation("First preview frame rendered ({Width}x{Height} {PixelFormat}).", width, height, pixelFormat);
                    SetStatusMessage($"Preview running ({width}x{height} {pixelFormat})");
                }

                var bitmap = BitmapSource.Create(width, height, 96, 96, wpfPixelFormat, null, buffer, stride);
                bitmap.Freeze();
                VideoPreviewImage.Source = bitmap;
            }));
        }

        private static int ParseIntOrDefault(string text, int defaultValue, int minValue, int maxValue)
        {
            if (int.TryParse(text, out int parsedValue))
            {
                return Math.Clamp(parsedValue, minValue, maxValue);
            }

            return Math.Clamp(defaultValue, minValue, maxValue);
        }

        private static float ParseFloatOrDefault(string text, float defaultValue, float minValue, float maxValue)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue))
            {
                return Math.Clamp(parsedValue, minValue, maxValue);
            }

            return Math.Clamp(defaultValue, minValue, maxValue);
        }

        private static string NormalizePath(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(text.Trim());
            return expanded.Length == 0 ? null : expanded;
        }

        private void SetStatusMessage(string message)
        {
            logger.LogDebug("Status message updated: {Message}", message);
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message ?? string.Empty;
            });
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int audioInputIndex = AudioInputDevicesComboBox.SelectedValue is int selectedAudioInputIndex ? selectedAudioInputIndex : -1;
            SIPSoftPhoneState.AudioInDeviceIndex = audioInputIndex;

            int audioOutputIndex = AudioOutputDevicesComboBox.SelectedValue is int selectedAudioOutputIndex ? selectedAudioOutputIndex : -1;
            SIPSoftPhoneState.AudioOutDeviceIndex = audioOutputIndex;

            string selectedVideoName = VideoDevicesComboBox.SelectedValue as string;
            bool hasVideoSelection = !string.IsNullOrWhiteSpace(selectedVideoName);
            SIPSoftPhoneState.VideoDeviceName = hasVideoSelection ? selectedVideoName : null;

            var mode = GetSelectedVirtualBackgroundMode();
            int previousBlurRadius = SIPSoftPhoneState.VirtualBackgroundBlurRadius;
            int previousMaskReuse = SIPSoftPhoneState.VirtualBackgroundMaskReuseFrameCount;
            int previousBudget = SIPSoftPhoneState.VirtualBackgroundPerformanceBudgetMilliseconds;
            int previousFallbackFrames = SIPSoftPhoneState.VirtualBackgroundPerformanceFallbackFrameCount;

            SIPSoftPhoneState.VirtualBackgroundMode = mode;
            SIPSoftPhoneState.VirtualBackgroundBlurRadius = ParseIntOrDefault(BlurRadiusTextBox.Text, previousBlurRadius, 0, 128);
            SIPSoftPhoneState.VirtualBackgroundMaskReuseFrameCount = ParseIntOrDefault(MaskReuseFrameCountTextBox.Text, previousMaskReuse, 0, 60);
            SIPSoftPhoneState.VirtualBackgroundSegmentationModelPath = NormalizePath(SegmentationModelPathTextBox.Text);
            SIPSoftPhoneState.VirtualBackgroundBackgroundImagePath = NormalizePath(BackgroundImagePathTextBox.Text);
            SIPSoftPhoneState.VirtualBackgroundEnablePerformanceFallback = EnablePerformanceFallbackCheckBox.IsChecked == true;
            SIPSoftPhoneState.VirtualBackgroundPerformanceBudgetMilliseconds = ParseIntOrDefault(PerformanceBudgetMillisecondsTextBox.Text, previousBudget, 1, 1000);
            SIPSoftPhoneState.VirtualBackgroundPerformanceFallbackFrameCount = ParseIntOrDefault(PerformanceFallbackFrameCountTextBox.Text, previousFallbackFrames, 1, 120);
            SIPSoftPhoneState.VirtualBackgroundUseOnnxDirectMlExecutionProvider = UseDirectMlExecutionProviderCheckBox.IsChecked == true;
            SIPSoftPhoneState.VirtualBackgroundMaskSmoothingRadius = ParseIntOrDefault(MaskSmoothingRadiusTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskSmoothingRadius, 0, 16);
            SIPSoftPhoneState.VirtualBackgroundMaskSmoothingIterations = ParseIntOrDefault(MaskSmoothingIterationsTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskSmoothingIterations, 0, 4);
            SIPSoftPhoneState.VirtualBackgroundMaskFeather = ParseFloatOrDefault(MaskFeatherTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskFeather, 0f, 1f);
            SIPSoftPhoneState.VirtualBackgroundMaskForegroundClamp = ParseFloatOrDefault(MaskForegroundClampTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskForegroundClamp, 0f, 1f);
            SIPSoftPhoneState.VirtualBackgroundMaskForegroundExponent = ParseFloatOrDefault(MaskForegroundExponentTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskForegroundExponent, 0.2f, 2.5f);
            SIPSoftPhoneState.VirtualBackgroundMaskForegroundBias = ParseFloatOrDefault(MaskForegroundBiasTextBox.Text, SIPSoftPhoneState.VirtualBackgroundMaskForegroundBias, -1f, 1f);
            SIPSoftPhoneState.VirtualBackgroundSegmentationScale = ParseFloatOrDefault(SegmentationScaleTextBox.Text, SIPSoftPhoneState.VirtualBackgroundSegmentationScale, 0.1f, 1f);
            SIPSoftPhoneState.VirtualBackgroundEffectProcessingScale = ParseFloatOrDefault(EffectProcessingScaleTextBox.Text, SIPSoftPhoneState.VirtualBackgroundEffectProcessingScale, 0.1f, 1f);
            SIPSoftPhoneState.VirtualBackgroundEnableAdaptiveSegmentationScale = EnableAdaptiveSegmentationScaleCheckBox.IsChecked == true;
            SIPSoftPhoneState.VideoPreviewWidth = ParseIntOrDefault(PreviewWidthTextBox.Text, SIPSoftPhoneState.VideoPreviewWidth, MinPreviewWidth, MaxPreviewWidth);
            SIPSoftPhoneState.VideoPreviewHeight = ParseIntOrDefault(PreviewHeightTextBox.Text, SIPSoftPhoneState.VideoPreviewHeight, MinPreviewHeight, MaxPreviewHeight);
            NormalizePreviewDimensionInputs();

            logger.LogInformation("Settings saved. AudioIn={AudioIn}, AudioOut={AudioOut}, Video={Video}.",
                audioInputIndex,
                audioOutputIndex,
                SIPSoftPhoneState.VideoDeviceName ?? "(none)");
            logger.LogInformation("Virtual background settings saved. Mode={Mode}, BlurRadius={Blur}, MaskReuse={MaskReuse}, FallbackEnabled={FallbackEnabled}, Budget={Budget}, FallbackFrames={FallbackFrames}, DirectML={DirectMl}, MaskSmoothingRadius={MaskSmoothRadius}, MaskSmoothingIterations={MaskSmoothIterations}, MaskFeather={MaskFeather}, MaskClamp={MaskClamp}, MaskExponent={MaskExponent}, MaskBias={MaskBias}, SegScale={SegScale}, EffectScale={EffectScale}, AdaptiveSegScale={AdaptiveSegScale}.",
                SIPSoftPhoneState.VirtualBackgroundMode,
                SIPSoftPhoneState.VirtualBackgroundBlurRadius,
                SIPSoftPhoneState.VirtualBackgroundMaskReuseFrameCount,
                SIPSoftPhoneState.VirtualBackgroundEnablePerformanceFallback,
                SIPSoftPhoneState.VirtualBackgroundPerformanceBudgetMilliseconds,
                SIPSoftPhoneState.VirtualBackgroundPerformanceFallbackFrameCount,
                SIPSoftPhoneState.VirtualBackgroundUseOnnxDirectMlExecutionProvider,
                SIPSoftPhoneState.VirtualBackgroundMaskSmoothingRadius,
                SIPSoftPhoneState.VirtualBackgroundMaskSmoothingIterations,
                SIPSoftPhoneState.VirtualBackgroundMaskFeather,
                SIPSoftPhoneState.VirtualBackgroundMaskForegroundClamp,
                SIPSoftPhoneState.VirtualBackgroundMaskForegroundExponent,
                SIPSoftPhoneState.VirtualBackgroundMaskForegroundBias,
                SIPSoftPhoneState.VirtualBackgroundSegmentationScale,
                SIPSoftPhoneState.VirtualBackgroundEffectProcessingScale,
                SIPSoftPhoneState.VirtualBackgroundEnableAdaptiveSegmentationScale);
            logger.LogInformation("Preview dimensions saved: {Width}x{Height}.", SIPSoftPhoneState.VideoPreviewWidth, SIPSoftPhoneState.VideoPreviewHeight);

            if (!SIPSoftPhoneState.TryPersistSettings(out string persistError))
            {
                logger.LogError("Unable to persist settings to configuration: {Error}", persistError);
                SetStatusMessage($"Settings saved but config update failed: {persistError}");
                return;
            }

            logger.LogInformation("Settings persisted to configuration file.");
            SetStatusMessage("Settings saved. Changes apply to new calls.");

            if (hasVideoSelection)
            {
                logger.LogDebug("Reinitialising video preview after saving settings.");
                await RestartPreviewIfReadyAsync();
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("Settings changes cancelled by user.");
            await StopVideoPreviewAsync();
            Close();
        }

        private async void VideoDevicesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _isInitialising)
            {
                logger.LogTrace("Video selection change ignored (IsLoaded={IsLoaded}, Initialising={Initialising}).", IsLoaded, _isInitialising);
                return;
            }

            logger.LogDebug("Video device selection changed; restarting preview.");
            await StartPreviewForSelectionAsync();
        }

        private async void VirtualBackgroundModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            logger.LogDebug("Virtual background mode changed.");
            UpdateVirtualBackgroundControlsState();
            await RestartPreviewIfReadyAsync();
        }

        private async void BrowseSegmentationModelButton_Click(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("Segmentation model browse requested.");
            var dialog = new OpenFileDialog
            {
                Title = "Select Segmentation Model",
                Filter = "ONNX files (*.onnx)|*.onnx|All files (*.*)|*.*"
            };

            string existingPath = NormalizePath(SegmentationModelPathTextBox.Text);
            if (!string.IsNullOrEmpty(existingPath))
            {
                string directory = Path.GetDirectoryName(existingPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                SegmentationModelPathTextBox.Text = dialog.FileName;
                logger.LogInformation("Segmentation model selected: {Path}", dialog.FileName);
                await RestartPreviewIfReadyAsync();
            }
        }

        private (int Width, int Height) GetDesiredPreviewDimensions()
        {
            int width = ParseIntOrDefault(PreviewWidthTextBox.Text, SIPSoftPhoneState.VideoPreviewWidth, MinPreviewWidth, MaxPreviewWidth);
            int height = ParseIntOrDefault(PreviewHeightTextBox.Text, SIPSoftPhoneState.VideoPreviewHeight, MinPreviewHeight, MaxPreviewHeight);
            return (width, height);
        }

        private void NormalizePreviewDimensionInputs()
        {
            var (width, height) = GetDesiredPreviewDimensions();
            PreviewWidthTextBox.Text = width.ToString();
            PreviewHeightTextBox.Text = height.ToString();
        }

        private async void PreviewDimensionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            NormalizePreviewDimensionInputs();
            await RestartPreviewIfReadyAsync();
        }

        private async void BrowseBackgroundImageButton_Click(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("Background image browse requested.");
            var dialog = new OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
            };

            string existingPath = NormalizePath(BackgroundImagePathTextBox.Text);
            if (!string.IsNullOrEmpty(existingPath))
            {
                string directory = Path.GetDirectoryName(existingPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                BackgroundImagePathTextBox.Text = dialog.FileName;
                logger.LogInformation("Background image selected: {Path}", dialog.FileName);
                await RestartPreviewIfReadyAsync();
            }
        }

        private async void EnablePerformanceFallbackCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("Performance fallback toggled to {Enabled}.", EnablePerformanceFallbackCheckBox.IsChecked);
            UpdatePerformanceFallbackControlsState();
            await RestartPreviewIfReadyAsync();
        }

        private async void EnableAdaptiveSegmentationScaleCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("Adaptive segmentation scaling toggled to {Enabled}.", EnableAdaptiveSegmentationScaleCheckBox.IsChecked);
            await RestartPreviewIfReadyAsync();
        }

        private async void UseDirectMlExecutionProviderCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            logger.LogDebug("DirectML execution provider toggled to {Enabled}.", UseDirectMlExecutionProviderCheckBox.IsChecked);
            await RestartPreviewIfReadyAsync();
        }

        private async void OnClosed(object sender, EventArgs e)
        {
            try
            {
                logger.LogDebug("Settings window closing; stopping preview.");
                await StopVideoPreviewAsync();
            }
            catch
            {
                // Swallow exceptions on shutdown.
                logger.LogTrace("Exception ignored while shutting down preview on close.");
            }
        }

        private static byte[] ConvertNv12ToBgr(byte[] nv12Buffer, int width, int height)
        {
            if (nv12Buffer == null)
            {
                return Array.Empty<byte>();
            }

            var bgrBuffer = new byte[width * height * 3];
            int frameSize = width * height;
            int resultIndex = 0;

            for (int y = 0; y < height; y++)
            {
                int yRow = y * width;
                int uvRow = frameSize + (y / 2) * width;

                for (int x = 0; x < width; x++)
                {
                    int yValue = nv12Buffer[yRow + x];
                    int uvIndex = uvRow + (x / 2) * 2;
                    int uValue = nv12Buffer[uvIndex] - 128;
                    int vValue = nv12Buffer[uvIndex + 1] - 128;

                    WriteBgrFromYuv(yValue, uValue, vValue, bgrBuffer, ref resultIndex);
                }
            }

            return bgrBuffer;
        }

        private static byte[] ConvertI420ToBgr(byte[] i420Buffer, int width, int height)
        {
            if (i420Buffer == null)
            {
                return Array.Empty<byte>();
            }

            var bgrBuffer = new byte[width * height * 3];
            int frameSize = width * height;
            int chromaStride = width / 2;
            int uPlaneOffset = frameSize;
            int vPlaneOffset = frameSize + (frameSize / 4);
            int resultIndex = 0;

            for (int y = 0; y < height; y++)
            {
                int yRow = y * width;
                int chromaRow = (y / 2) * chromaStride;

                for (int x = 0; x < width; x++)
                {
                    int yValue = i420Buffer[yRow + x];
                    int chromaIndex = chromaRow + (x / 2);
                    int uValue = i420Buffer[uPlaneOffset + chromaIndex] - 128;
                    int vValue = i420Buffer[vPlaneOffset + chromaIndex] - 128;

                    WriteBgrFromYuv(yValue, uValue, vValue, bgrBuffer, ref resultIndex);
                }
            }

            return bgrBuffer;
        }

        private static void WriteBgrFromYuv(int yValue, int uValue, int vValue, byte[] destination, ref int index)
        {
            double c = yValue;
            double r = c + 1.402 * vValue;
            double g = c - 0.344136 * uValue - 0.714136 * vValue;
            double b = c + 1.772 * uValue;

            destination[index++] = ClampToByte(b);
            destination[index++] = ClampToByte(g);
            destination[index++] = ClampToByte(r);
        }

        private static byte ClampToByte(double value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return (byte)(value + 0.5);
        }
    }
}
