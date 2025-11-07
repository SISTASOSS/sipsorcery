//-----------------------------------------------------------------------------
// Filename: SIPSoftPhoneState.cs
//
// Description: A helper class to load the application's settings and to hold 
// some application wide variables. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Settings.Configuration;
using SIPSorceryMedia.Windows.VirtualBackground;

namespace SIPSorcery.SoftPhone
{
    public class SIPSoftPhoneState : IConfigurationSectionHandler
    {
        private const string SIPSOFTPHONE_CONFIGNODE_NAME = "sipsoftphone";
        private const string SIPSOCKETS_CONFIGNODE_NAME = "sipsockets";
        private const string STUN_SERVER_KEY = "STUNServerHostname";

        private static readonly XmlNode m_sipSoftPhoneConfigNode;
        public static readonly XmlNode SIPSocketsNode;
        public static readonly string STUNServerHostname;
        private static readonly Lazy<Microsoft.Extensions.Logging.ILogger> s_logger = new(() => SIPSorcery.LogFactory.CreateLogger<SIPSoftPhoneState>());
        private static Microsoft.Extensions.Logging.ILogger Logger => s_logger.Value;

        public static readonly string SIPUsername = System.Configuration.ConfigurationManager.AppSettings["SIPUsername"];    // Get the SIP username from the config file.
        public static readonly string SIPPassword = System.Configuration.ConfigurationManager.AppSettings["SIPPassword"];    // Get the SIP password from the config file.
        public static readonly string SIPServer = System.Configuration.ConfigurationManager.AppSettings["SIPServer"];        // Get the SIP server from the config file.
        public static readonly string SIPFromName = System.Configuration.ConfigurationManager.AppSettings["SIPFromName"];    // Get the SIP From display name from the config file.
        public static readonly bool UseAudioScope = Boolean.Parse(System.Configuration.ConfigurationManager.AppSettings["UseAudioScope"]);
        public static int AudioInDeviceIndex = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["AudioInDeviceIndex"], out var audioInDeviceIndex) ? audioInDeviceIndex : -1;
        public static int AudioOutDeviceIndex = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["AudioOutDeviceIndex"], out var audioOutDeviceIndex) ? audioOutDeviceIndex : -1;
        public static string VideoDeviceName = System.Configuration.ConfigurationManager.AppSettings["VideoDeviceName"];
        public static VirtualBackgroundMode VirtualBackgroundMode = Enum.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMode"], true, out VirtualBackgroundMode vbMode)
            ? vbMode
            : VirtualBackgroundMode.None;
        public static int VirtualBackgroundBlurRadius = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundBlurRadius"], out var blurRadius)
            ? blurRadius
            : 8;
        public static int VirtualBackgroundMaskReuseFrameCount = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskReuseFrameCount"], out var maskReuseCount)
            ? maskReuseCount
            : 1;
        public static string VirtualBackgroundSegmentationModelPath = System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundSegmentationModelPath"];
        public static string VirtualBackgroundBackgroundImagePath = System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundBackgroundImagePath"];
        public static bool VirtualBackgroundEnablePerformanceFallback = Boolean.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundEnablePerformanceFallback"], out var fallbackEnabled)
            ? fallbackEnabled
            : true;
        public static int VirtualBackgroundPerformanceBudgetMilliseconds = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundPerformanceBudgetMilliseconds"], out var budgetMs)
            ? budgetMs
            : 40;
        public static int VirtualBackgroundPerformanceFallbackFrameCount = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundPerformanceFallbackFrameCount"], out var fallbackFrames)
            ? fallbackFrames
            : 5;
        public static bool VirtualBackgroundUseOnnxDirectMlExecutionProvider =
            System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundUseOnnxDirectMlExecutionProvider"] is string directMlSetting
                ? (Boolean.TryParse(directMlSetting, out var useDirectMl) ? useDirectMl : true)
                : true;
        public static int VirtualBackgroundMaskSmoothingRadius = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskSmoothingRadius"], out var maskSmoothingRadius)
            ? maskSmoothingRadius
            : 1;
        public static int VirtualBackgroundMaskSmoothingIterations = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskSmoothingIterations"], out var maskSmoothingIterations)
            ? maskSmoothingIterations
            : 1;
        public static float VirtualBackgroundMaskFeather =
            TryParseFloat(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskFeather"], 0.35f);
        public static float VirtualBackgroundMaskForegroundClamp =
            TryParseFloat(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskForegroundClamp"], 0.92f);
        public static float VirtualBackgroundMaskForegroundExponent =
            TryParseFloat(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskForegroundExponent"], 0.7f);
        public static float VirtualBackgroundMaskForegroundBias =
            TryParseFloat(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundMaskForegroundBias"], 0f);
        public static float VirtualBackgroundSegmentationScale =
            TryParseFloat(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundSegmentationScale"], 0.3f);
        public static float VirtualBackgroundEffectProcessingScale =
            TryParseFloat(System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundEffectProcessingScale"], 1f);
        public static bool VirtualBackgroundEnableAdaptiveSegmentationScale = Boolean.TryParse(
                System.Configuration.ConfigurationManager.AppSettings["VirtualBackgroundEnableAdaptiveSegmentationScale"], out var adaptiveSegmentation)
            ? adaptiveSegmentation
            : true;
        public static int VideoPreviewWidth = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VideoPreviewWidth"], out var previewWidth)
            ? previewWidth
            : 640;
        public static int VideoPreviewHeight = Int32.TryParse(System.Configuration.ConfigurationManager.AppSettings["VideoPreviewHeight"], out var previewHeight)
            ? previewHeight
            : 480;

        public static IPAddress PublicIPAddress;

        static SIPSoftPhoneState()
        {
            AddDebugLogger();

            if (System.Configuration.ConfigurationManager.GetSection(SIPSOFTPHONE_CONFIGNODE_NAME) != null)
            {
                m_sipSoftPhoneConfigNode = (XmlNode)System.Configuration.ConfigurationManager.GetSection(SIPSOFTPHONE_CONFIGNODE_NAME);
            }

            if (m_sipSoftPhoneConfigNode != null)
            {
                SIPSocketsNode = m_sipSoftPhoneConfigNode.SelectSingleNode(SIPSOCKETS_CONFIGNODE_NAME);
            }

            STUNServerHostname = System.Configuration.ConfigurationManager.AppSettings[STUN_SERVER_KEY];
        }

        /// <summary>
        /// Handler for processing the App.Config file and retrieving a custom XML node.
        /// </summary>
        public object Create(object parent, object context, XmlNode configSection)
        {
            return configSection;
        }

        public static VirtualBackgroundOptions GetVirtualBackgroundOptions()
        {
            return new VirtualBackgroundOptions
            {
                Mode = VirtualBackgroundMode,
                BlurRadius = VirtualBackgroundBlurRadius,
                MaskReuseFrameCount = VirtualBackgroundMaskReuseFrameCount,
                SegmentationModelPath = VirtualBackgroundSegmentationModelPath,
                BackgroundImagePath = VirtualBackgroundBackgroundImagePath,
                EnablePerformanceFallback = VirtualBackgroundEnablePerformanceFallback,
                PerformanceBudgetMilliseconds = VirtualBackgroundPerformanceBudgetMilliseconds,
                PerformanceFallbackFrameCount = VirtualBackgroundPerformanceFallbackFrameCount,
                UseOnnxDirectMlExecutionProvider = VirtualBackgroundUseOnnxDirectMlExecutionProvider,
                MaskSmoothingRadius = VirtualBackgroundMaskSmoothingRadius,
                MaskSmoothingIterations = VirtualBackgroundMaskSmoothingIterations,
                MaskFeather = VirtualBackgroundMaskFeather,
                MaskForegroundClamp = VirtualBackgroundMaskForegroundClamp,
                MaskForegroundExponent = VirtualBackgroundMaskForegroundExponent,
                MaskForegroundBias = VirtualBackgroundMaskForegroundBias,
                SegmentationScale = VirtualBackgroundSegmentationScale,
                EffectProcessingScale = VirtualBackgroundEffectProcessingScale,
                EnableAdaptiveSegmentationScale = VirtualBackgroundEnableAdaptiveSegmentationScale
            };
        }

        public static bool TryPersistSettings(out string errorMessage)
        {
            try
            {
                var config = System.Configuration.ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = config.AppSettings.Settings;

                void SetValue(string key, string value)
                {
                    if (settings[key] == null)
                    {
                        settings.Add(key, value ?? string.Empty);
                    }
                    else
                    {
                        settings[key].Value = value ?? string.Empty;
                    }
                }

                SetValue("AudioInDeviceIndex", AudioInDeviceIndex.ToString());
                SetValue("AudioOutDeviceIndex", AudioOutDeviceIndex.ToString());
                SetValue("VideoDeviceName", VideoDeviceName ?? string.Empty);
                SetValue("VirtualBackgroundMode", VirtualBackgroundMode.ToString());
                SetValue("VirtualBackgroundBlurRadius", VirtualBackgroundBlurRadius.ToString());
                SetValue("VirtualBackgroundMaskReuseFrameCount", VirtualBackgroundMaskReuseFrameCount.ToString());
                SetValue("VirtualBackgroundSegmentationModelPath", VirtualBackgroundSegmentationModelPath ?? string.Empty);
                SetValue("VirtualBackgroundBackgroundImagePath", VirtualBackgroundBackgroundImagePath ?? string.Empty);
                SetValue("VirtualBackgroundEnablePerformanceFallback", VirtualBackgroundEnablePerformanceFallback ? "true" : "false");
                SetValue("VirtualBackgroundPerformanceBudgetMilliseconds", VirtualBackgroundPerformanceBudgetMilliseconds.ToString());
                SetValue("VirtualBackgroundPerformanceFallbackFrameCount", VirtualBackgroundPerformanceFallbackFrameCount.ToString());
                SetValue("VirtualBackgroundUseOnnxDirectMlExecutionProvider", VirtualBackgroundUseOnnxDirectMlExecutionProvider ? "true" : "false");
                SetValue("VirtualBackgroundMaskSmoothingRadius", VirtualBackgroundMaskSmoothingRadius.ToString());
                SetValue("VirtualBackgroundMaskSmoothingIterations", VirtualBackgroundMaskSmoothingIterations.ToString());
                SetValue("VirtualBackgroundMaskFeather", VirtualBackgroundMaskFeather.ToString(CultureInfo.InvariantCulture));
                SetValue("VirtualBackgroundMaskForegroundClamp", VirtualBackgroundMaskForegroundClamp.ToString(CultureInfo.InvariantCulture));
                SetValue("VirtualBackgroundMaskForegroundExponent", VirtualBackgroundMaskForegroundExponent.ToString(CultureInfo.InvariantCulture));
                SetValue("VirtualBackgroundMaskForegroundBias", VirtualBackgroundMaskForegroundBias.ToString(CultureInfo.InvariantCulture));
                SetValue("VirtualBackgroundSegmentationScale", VirtualBackgroundSegmentationScale.ToString(CultureInfo.InvariantCulture));
                SetValue("VirtualBackgroundEffectProcessingScale", VirtualBackgroundEffectProcessingScale.ToString(CultureInfo.InvariantCulture));
                SetValue("VirtualBackgroundEnableAdaptiveSegmentationScale", VirtualBackgroundEnableAdaptiveSegmentationScale ? "true" : "false");
                SetValue("VideoPreviewWidth", VideoPreviewWidth.ToString());
                SetValue("VideoPreviewHeight", VideoPreviewHeight.ToString());

                config.Save(ConfigurationSaveMode.Modified);
                System.Configuration.ConfigurationManager.RefreshSection("appSettings");

                errorMessage = null;
                Logger.LogInformation("Softphone settings persisted to configuration file {ConfigPath}.", config.FilePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to persist softphone settings to configuration.");
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void AddDebugLogger()
        {
            var loggerConfiguration = new LoggerConfiguration().Enrich.FromLogContext();

            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("logging.json", optional: false, reloadOnChange: true)
                    .Build();

                loggerConfiguration.ReadFrom.Configuration(configuration, new ConfigurationReaderOptions { SectionName = "Serilog" });
            }
            catch (Exception ex)
            {
                // Fall back to the legacy debug sink if JSON configuration is missing or invalid.
                loggerConfiguration
                    .MinimumLevel.Debug()
                    .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                System.Diagnostics.Debug.WriteLine($"Serilog configuration fallback: {ex.Message}");
            }

            var serilogLogger = loggerConfiguration.CreateLogger();
            Serilog.Log.Logger = serilogLogger;
            SIPSorcery.LogFactory.Set(new SerilogLoggerFactory(serilogLogger));
        }

        private static float TryParseFloat(string text, float defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return defaultValue;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }
    }
}
