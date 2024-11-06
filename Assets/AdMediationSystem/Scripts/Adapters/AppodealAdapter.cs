#define _AMS_APPODEAL

using System;
using System.Collections.Generic;
using System.Reflection;
using Boomlagoon.JSON;
using UnityEngine;
#if _AMS_APPODEAL
using AppodealStack.Monetization.Api;
using AppodealStack.Monetization.Common;
#endif

namespace Virterix.AdMediation
{
    public class AppodealAdapter : AdNetworkAdapter
    {
        [Flags]
        public enum RequestedAdsType
        {
            Interstitial = 1,
            RewardedVideo = 2,
            Banner = 4,
            Mrec = 8
        }

        public enum AppodealBannerPosition
        {
            Top,
            Bottom,
            Left,
            Right,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        public enum AppodealBannerSize
        {
            Standart,
            MREC
        }

        public const string Identifier = "appodeal";

        public int m_timeout = 120;
        public RequestedAdsType m_requestedAdsType;

        public override bool RequiredWaitingInitializationResponse => true;

        private AdInstance m_interstitialInstance;
        private AdInstance m_incentivizedInstance;

        private AdInstance m_currBannerInstance;
        private AdInstance m_currMrecBannerInstance;

        private bool m_bannerDisplayed;
        private bool m_mrecBannerDisplayed;

        private bool m_wasEventsSubscribed;

        protected override string AdInstanceParametersFolder =>
            AppodealAdInstanceBannerParameters._AD_INSTANCE_PARAMETERS_FOLDER;

        public static string GetSDKVersion()
        {
#if _AMS_APPODEAL
            return Appodeal.GetNativeSDKVersion();
#else
            return "";
#endif
        }

        public static int ConvertToNativeBannerPosition(AppodealBannerPosition bannerPosition)
        {
#if _AMS_APPODEAL
            int nativeAdPosition = AppodealShowStyle.BannerBottom;
            switch (bannerPosition)
            {
                case AppodealBannerPosition.Top:
                case AppodealBannerPosition.TopLeft:
                case AppodealBannerPosition.TopRight:
                    nativeAdPosition = AppodealShowStyle.BannerTop;
                    break;
                case AppodealBannerPosition.Bottom:
                case AppodealBannerPosition.BottomLeft:
                case AppodealBannerPosition.BottomRight:
                    nativeAdPosition = AppodealShowStyle.BannerBottom;
                    break;
                case AppodealBannerPosition.Left:
                    nativeAdPosition = AppodealShowStyle.BannerLeft;
                    break;
                case AppodealBannerPosition.Right:
                    nativeAdPosition = AppodealShowStyle.BannerRight;
                    break;
            }

            return nativeAdPosition;
#else
            return 0;
#endif
        }

        public static void SetupNetworkNativeSettings(string androidAppId, string iOSAppId)
        {
#if UNITY_EDITOR && _AMS_APPODEAL

            string path = "";
            string[] foundAssets =
                UnityEditor.AssetDatabase.FindAssets("t:AppodealStack.UnityEditor.InternalResources.AppodealSettings");

            if (foundAssets.Length == 1)
            {
                path = UnityEditor.AssetDatabase.GUIDToAssetPath(foundAssets[0]);
            }

            ScriptableObject appodealSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

            if (appodealSettings != null)
            {
                Type settingsType = appodealSettings.GetType();

                PropertyInfo prop = settingsType.GetProperty("AdMobAndroidAppId");
                prop.SetValue(appodealSettings, androidAppId);

                prop = settingsType.GetProperty("AdMobIosAppId");
                prop.SetValue(appodealSettings, iOSAppId);

                UnityEditor.EditorUtility.SetDirty(appodealSettings);
            }
            else
            {
                Debug.LogWarning("[AMS] Appodeal Settings not found! Try again!");
            }
#endif
        }

        protected override void InitializeParameters(Dictionary<string, string> parameters, JSONArray jsonPlacements)
        {
            base.InitializeParameters(parameters, jsonPlacements);

            SubscribeEvents();
            string appKey = "";
            if (parameters != null && parameters.TryGetValue("appId", out var parameter))
            {
                appKey = parameter;
            }

            m_interstitialInstance = AdFactory.CreateAdInstacne(this, AdType.Interstitial,
                AdInstance.AD_INSTANCE_DEFAULT_NAME, "", m_timeout);
            AddAdInstance(m_interstitialInstance);
            m_incentivizedInstance = AdFactory.CreateAdInstacne(this, AdType.Incentivized,
                AdInstance.AD_INSTANCE_DEFAULT_NAME, "", m_timeout);
            AddAdInstance(m_incentivizedInstance);

#if UNITY_EDITOR
            WasInitializationResponse = true;
#endif

#if _AMS_APPODEAL
            int adTypes = AppodealAdType.None;

            if (AdMediationSystem.AdsDisabled)
            {
                adTypes = m_requestedAdsType.HasFlag(RequestedAdsType.RewardedVideo)
                    ? AppodealAdType.RewardedVideo
                    : AppodealAdType.None;
            }
            else
            {
                adTypes = (m_requestedAdsType.HasFlag(RequestedAdsType.Interstitial) ? AppodealAdType.Interstitial : 0)
                          | (m_requestedAdsType.HasFlag(RequestedAdsType.RewardedVideo)
                              ? AppodealAdType.RewardedVideo
                              : 0)
                          | (m_requestedAdsType.HasFlag(RequestedAdsType.Banner) ? AppodealAdType.Banner : 0)
                          | (m_requestedAdsType.HasFlag(RequestedAdsType.Mrec) ? AppodealAdType.Mrec : 0);
            }

            Appodeal.SetAutoCache(AppodealAdType.Interstitial, false);
            Appodeal.SetAutoCache(AppodealAdType.RewardedVideo, false);
            Appodeal.SetSmartBanners(true);

            if (AdMediationSystem.Instance.ChildrenMode == ChildDirectedMode.NotAssign)
            {
                Appodeal.SetChildDirectedTreatment(
                    AdMediationSystem.Instance.ChildrenMode == ChildDirectedMode.Directed);
            }

            if (AdMediationSystem.Instance.IsTestModeEnabled)
            {
                Appodeal.SetLogLevel(AppodealLogLevel.Verbose);
                Appodeal.SetTesting(true);
            }

            SubscribeEvents();

            Appodeal.Initialize(appKey, adTypes);
#endif
        }

        protected override AdInstance CreateAdInstanceData(JSONValue jsonAdInstance)
        {
            AdInstance adInstance = new AdInstance(this);
            return adInstance;
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        private void SubscribeEvents()
        {
            if (m_wasEventsSubscribed)
            {
                return;
            }

            m_wasEventsSubscribed = true;

#if _AMS_APPODEAL
            AppodealCallbacks.Sdk.OnInitialized += OnInitializationFinished;

            AppodealCallbacks.Interstitial.OnLoaded += OnInterstitialLoaded;
            AppodealCallbacks.Interstitial.OnFailedToLoad += OnInterstitialFailedToLoad;
            AppodealCallbacks.Interstitial.OnShown += OnInterstitialShown;
            AppodealCallbacks.Interstitial.OnShowFailed += OnInterstitialShowFailed;
            AppodealCallbacks.Interstitial.OnClosed += OnInterstitialClosed;
            AppodealCallbacks.Interstitial.OnClicked += OnInterstitialClicked;
            AppodealCallbacks.Interstitial.OnExpired += OnInterstitialExpired;

            AppodealCallbacks.Banner.OnLoaded += OnBannerLoaded;
            AppodealCallbacks.Banner.OnFailedToLoad += OnBannerFailedToLoad;
            AppodealCallbacks.Banner.OnShown += OnBannerShown;
            AppodealCallbacks.Banner.OnShowFailed += OnBannerShowFailed;
            AppodealCallbacks.Banner.OnClicked += OnBannerClicked;
            AppodealCallbacks.Banner.OnExpired += OnBannerExpired;

            AppodealCallbacks.RewardedVideo.OnLoaded += OnRewardedVideoLoaded;
            AppodealCallbacks.RewardedVideo.OnFailedToLoad += OnRewardedVideoFailedToLoad;
            AppodealCallbacks.RewardedVideo.OnShown += OnRewardedVideoShown;
            AppodealCallbacks.RewardedVideo.OnShowFailed += OnRewardedVideoShowFailed;
            AppodealCallbacks.RewardedVideo.OnClosed += OnRewardedVideoClosed;
            AppodealCallbacks.RewardedVideo.OnFinished += OnRewardedVideoFinished;
            AppodealCallbacks.RewardedVideo.OnClicked += OnRewardedVideoClicked;
            AppodealCallbacks.RewardedVideo.OnExpired += OnRewardedVideoExpired;

            AppodealCallbacks.Mrec.OnLoaded += OnMrecLoaded;
            AppodealCallbacks.Mrec.OnFailedToLoad += OnMrecFailedToLoad;
            AppodealCallbacks.Mrec.OnShown += OnMrecShown;
            AppodealCallbacks.Mrec.OnShowFailed += OnMrecShowFailed;
            AppodealCallbacks.Mrec.OnClicked += OnMrecClicked;
            AppodealCallbacks.Mrec.OnExpired += OnMrecExpired;
#endif
        }

        private void UnsubscribeEvents()
        {
            m_wasEventsSubscribed = false;

#if _AMS_APPODEAL
            AppodealCallbacks.Sdk.OnInitialized -= OnInitializationFinished;

            AppodealCallbacks.Interstitial.OnLoaded -= OnInterstitialLoaded;
            AppodealCallbacks.Interstitial.OnFailedToLoad -= OnInterstitialFailedToLoad;
            AppodealCallbacks.Interstitial.OnShown -= OnInterstitialShown;
            AppodealCallbacks.Interstitial.OnShowFailed -= OnInterstitialShowFailed;
            AppodealCallbacks.Interstitial.OnClosed -= OnInterstitialClosed;
            AppodealCallbacks.Interstitial.OnClicked -= OnInterstitialClicked;
            AppodealCallbacks.Interstitial.OnExpired -= OnInterstitialExpired;

            AppodealCallbacks.Banner.OnLoaded -= OnBannerLoaded;
            AppodealCallbacks.Banner.OnFailedToLoad -= OnBannerFailedToLoad;
            AppodealCallbacks.Banner.OnShown -= OnBannerShown;
            AppodealCallbacks.Banner.OnShowFailed -= OnBannerShowFailed;
            AppodealCallbacks.Banner.OnClicked -= OnBannerClicked;
            AppodealCallbacks.Banner.OnExpired -= OnBannerExpired;

            AppodealCallbacks.RewardedVideo.OnLoaded -= OnRewardedVideoLoaded;
            AppodealCallbacks.RewardedVideo.OnFailedToLoad -= OnRewardedVideoFailedToLoad;
            AppodealCallbacks.RewardedVideo.OnShown -= OnRewardedVideoShown;
            AppodealCallbacks.RewardedVideo.OnShowFailed -= OnRewardedVideoShowFailed;
            AppodealCallbacks.RewardedVideo.OnClosed -= OnRewardedVideoClosed;
            AppodealCallbacks.RewardedVideo.OnFinished -= OnRewardedVideoFinished;
            AppodealCallbacks.RewardedVideo.OnClicked -= OnRewardedVideoClicked;
            AppodealCallbacks.RewardedVideo.OnExpired -= OnRewardedVideoExpired;

            AppodealCallbacks.Mrec.OnLoaded -= OnMrecLoaded;
            AppodealCallbacks.Mrec.OnFailedToLoad -= OnMrecFailedToLoad;
            AppodealCallbacks.Mrec.OnShown -= OnMrecShown;
            AppodealCallbacks.Mrec.OnShowFailed -= OnMrecShowFailed;
            AppodealCallbacks.Mrec.OnClicked -= OnMrecClicked;
            AppodealCallbacks.Mrec.OnExpired -= OnMrecExpired;
#endif
        }

#if _AMS_APPODEAL
        public override bool Show(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdType adType = adInstance.m_adType;
            bool success = false;
            bool isPreviousBannerDisplayed = adInstance.m_bannerDisplayed;

            if (adInstance.m_adType == AdType.Banner)
            {
                adInstance.m_bannerDisplayed = true;
                adInstance.CurrPlacement = placement;
            }

            if (IsReady(adInstance))
            {
                switch (adType)
                {
                    case AdType.Interstitial:
                        SharedFullscreenAdWillShow = true;
                        Appodeal.Show(AppodealShowStyle.Interstitial);
                        break;
                    case AdType.Incentivized:
                        SharedFullscreenAdWillShow = true;
                        Appodeal.Show(AppodealShowStyle.RewardedVideo);
                        break;
                    case AdType.Banner:
                        var bannerPosition = (AppodealBannerPosition)GetBannerPosition(adInstance, placement);
                        var instanceParams = (AppodealAdInstanceBannerParameters)adInstance.m_adInstanceParams;

                        if (instanceParams.m_bannerSize == AppodealBannerSize.MREC)
                        {
                            m_mrecBannerDisplayed = true;
                        }
                        else if (instanceParams.m_bannerSize == AppodealBannerSize.Standart)
                        {
                            m_bannerDisplayed = true;
                        }
                        
                        ShowBanner(adInstance, placement, bannerPosition);
                        break;
                }

                success = true;
            }

            return success;
        }

        public override void Hide(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            if (adInstance.m_adType == AdType.Banner)
            {
                var instanceParams = adInstance.m_adInstanceParams as AppodealAdInstanceBannerParameters;

                if (m_bannerDisplayed || m_mrecBannerDisplayed)
                {
                    NotifyEvent(AdEvent.Hiding, adInstance);
                }

                switch (instanceParams.m_bannerSize)
                {
                    case AppodealBannerSize.MREC:
                        m_mrecBannerDisplayed = false;
                        Appodeal.HideMrecView();
                        break;
                    case AppodealBannerSize.Standart:
                        m_bannerDisplayed = false;
                        Appodeal.Hide(AppodealAdType.Banner);
                        break;
                }

                adInstance.m_bannerDisplayed = false;
            }
        }

        public override bool IsReady(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            bool isReady = false;
            AdType adType = adInstance.m_adType;

            switch (adType)
            {
                case AdType.Interstitial:
                    isReady = Appodeal.IsLoaded(AppodealAdType.Interstitial);
                    break;
                case AdType.Incentivized:
                    isReady = Appodeal.IsLoaded(AppodealAdType.RewardedVideo);
                    break;
                case AdType.Banner:
                    var instanceParams = (AppodealAdInstanceBannerParameters)adInstance.m_adInstanceParams;
                    int bannerType = instanceParams.m_bannerSize == AppodealBannerSize.MREC
                        ? AppodealAdType.Mrec
                        : AppodealAdType.Banner;

                    isReady = Appodeal.IsLoaded(bannerType);
                    break;
            }

            return isReady;
        }

        public override void Prepare(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdType adType = adInstance.m_adType;

            if (!IsReady(adInstance))
            {
                adInstance.State = AdState.Loading;
                switch (adType)
                {
                    case AdType.Interstitial:
                        Appodeal.Cache(AppodealAdType.Interstitial);
                        break;
                    case AdType.Incentivized:
                        Appodeal.Cache(AppodealAdType.RewardedVideo);
                        break;
                    case AdType.Banner:
                        var bannerParams = (AppodealAdInstanceBannerParameters)adInstance.m_adInstanceParams;

                        if (bannerParams.m_bannerSize == AppodealBannerSize.MREC)
                        {
                            m_currMrecBannerInstance = adInstance;
                        }
                        else
                        {
                            m_currBannerInstance = adInstance;
                        }

                        adInstance.CurrPlacement = placement;
                        break;
                }
            }
        }
        
        private void ShowBanner(AdInstance adInstance, string placement, AppodealBannerPosition bannerPosition)
        {
            var instanceParams = adInstance.m_adInstanceParams as AppodealAdInstanceBannerParameters;

            var nativeBannerPosition = ConvertToNativeBannerPosition(bannerPosition);
            int xPosition = AppodealViewPosition.HorizontalSmart;
            int yPosition = AppodealViewPosition.VerticalBottom;

            switch (bannerPosition)
            {
                case AppodealBannerPosition.BottomLeft:
                    xPosition = AppodealViewPosition.HorizontalLeft;
                    break;
                case AppodealBannerPosition.BottomRight:
                    xPosition = AppodealViewPosition.HorizontalRight;
                    break;
                case AppodealBannerPosition.Top:
                    xPosition = AppodealViewPosition.HorizontalSmart;
                    yPosition = AppodealViewPosition.VerticalTop;
                    break;
                case AppodealBannerPosition.TopLeft:
                    xPosition = AppodealViewPosition.HorizontalLeft;
                    yPosition = AppodealViewPosition.VerticalTop;
                    break;
                case AppodealBannerPosition.TopRight:
                    xPosition = AppodealViewPosition.HorizontalRight;
                    yPosition = AppodealViewPosition.VerticalTop;
                    break;
            }

            switch (instanceParams.m_bannerSize)
            {
                case AppodealBannerSize.MREC:
                    m_currMrecBannerInstance = adInstance;
                    Appodeal.ShowMrecView(yPosition, xPosition, placement);
                    break;
                case AppodealBannerSize.Standart:
                    m_currBannerInstance = adInstance;
                    Appodeal.Show(nativeBannerPosition);
                    if (!m_bannerDisplayed)
                    {
                        Appodeal.Hide(AppodealAdType.Banner);
                    }
#if AD_MEDIATION_DEBUG_MODE
                    Debug.Log($"[AMS] Show banner displayed: {m_bannerDisplayed}");
#endif
                    break;
            }
        }
#endif
        
#if _AMS_APPODEAL
        private void OnInitializationFinished(object sender, SdkInitializedEventArgs e)
        {
            WasInitializationResponse = true;

#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter OnInitializationFinished()");
            
            
            if (e.Errors != null)
            {
                Debug.Log("[AMS] AppodealAdapter Have Errors After Initialization");
                foreach (var error in e.Errors)
                {
                    Debug.Log($"error: {error}");
                }
            }
#endif
        }

        //____________________________________________________________

        #region Interstitial callback handlers

        // Called when interstitial was loaded (precache flag shows if the loaded ad is precache)
        private void OnInterstitialLoaded(object sender, AdLoadedEventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] Interstitial loaded");
#endif
            m_interstitialInstance.State = AdState.Received;
            AddEvent(m_interstitialInstance.m_adType, AdEvent.Prepared, m_interstitialInstance);
        }

        // Called when interstitial failed to load
        private void OnInterstitialFailedToLoad(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Interstitial failed to load");
#endif
            m_interstitialInstance.State = AdState.Unavailable;
            AddEvent(m_interstitialInstance.m_adType, AdEvent.PreparationFailed, m_interstitialInstance);
        }

        // Called when interstitial was loaded, but cannot be shown (internal network errors, placement settings, etc.)
        private void OnInterstitialShowFailed(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Interstitial show failed");
#endif
            m_interstitialInstance.State = AdState.Unavailable;
            SharedFullscreenAdWillShow = false;
        }

        // Called when interstitial is shown
        private void OnInterstitialShown(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Interstitial shown");
#endif
            SharedFullscreenAdWillShow = false;
            AddEvent(m_interstitialInstance.m_adType, AdEvent.Showing, m_interstitialInstance);
        }

        // Called when interstitial is closed
        private void OnInterstitialClosed(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Interstitial closed");
#endif
            SharedFullscreenAdWillShow = false;
            AddEvent(m_interstitialInstance.m_adType, AdEvent.Hiding, m_interstitialInstance);
        }

        // Called when interstitial is clicked
        private void OnInterstitialClicked(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Interstitial clicked");
#endif
        }

        // Called when interstitial is expired and can not be shown
        private void OnInterstitialExpired(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Interstitial expired");
#endif
            m_interstitialInstance.State = AdState.Unavailable;
        }

        #endregion

        //____________________________________________________________

        #region Rewarded Video callback handlers

        //Called when rewarded video was loaded (precache flag shows if the loaded ad is precache).
        private void OnRewardedVideoLoaded(object sender, AdLoadedEventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo loaded");
#endif
            m_incentivizedInstance.State = AdState.Received;
            AddEvent(m_incentivizedInstance.m_adType, AdEvent.Prepared, m_incentivizedInstance);
        }

        // Called when rewarded video failed to load
        private void OnRewardedVideoFailedToLoad(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo failed to load");
#endif
            m_incentivizedInstance.State = AdState.Unavailable;
            AddEvent(m_incentivizedInstance.m_adType, AdEvent.PreparationFailed, m_incentivizedInstance);
        }

        // Called when rewarded video was loaded, but cannot be shown (internal network errors, placement settings, etc.)
        private void OnRewardedVideoShowFailed(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo show failed");
#endif
            m_incentivizedInstance.State = AdState.Unavailable;
            SharedFullscreenAdWillShow = false;
        }

        // Called when rewarded video is shown
        private void OnRewardedVideoShown(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo shown");
#endif
            SharedFullscreenAdWillShow = false;
            AddEvent(m_incentivizedInstance.m_adType, AdEvent.Showing, m_incentivizedInstance);
        }

        // Called when reward video is clicked
        private void OnRewardedVideoClicked(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo clicked");
#endif
        }

        // Called when rewarded video is closed
        private void OnRewardedVideoClosed(object sender, RewardedVideoClosedEventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo closed");
#endif
            SharedFullscreenAdWillShow = false;
            AddEvent(m_incentivizedInstance.m_adType, AdEvent.Hiding, m_incentivizedInstance);
        }

        // Called when rewarded video is viewed until the end
        private void OnRewardedVideoFinished(object sender, RewardedVideoFinishedEventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo finished");
#endif
            m_lastReward.label = e.Currency;
            m_lastReward.amount = e.Amount;
            AddEvent(m_incentivizedInstance.m_adType, AdEvent.IncentivizationCompleted, m_incentivizedInstance);
        }

        //Called when rewarded video is expired and can not be shown
        private void OnRewardedVideoExpired(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter RewardedVideo expired");
#endif
            m_incentivizedInstance.State = AdState.Unavailable;
        }

        #endregion

        //____________________________________________________________

        #region Banner callback handlers

        // Called when a banner is loaded (height arg shows banner's height, precache arg shows if the loaded ad is precache
        private void OnBannerLoaded(object sender, BannerLoadedEventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Banner loaded");
#endif

            if (!m_bannerDisplayed)
            {
                Appodeal.Hide(AppodealAdType.Banner);
            }

            if (m_currBannerInstance != null)
            {
                AddEvent(AdType.Banner, AdEvent.Prepared, m_currBannerInstance);
            }
        }

        // Called when banner failed to load
        private void OnBannerFailedToLoad(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Banner failed to load");
#endif
        }

        // Called when banner is shown
        private void OnBannerShown(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Banner shown");
#endif
            if (m_currBannerInstance != null)
            {
                AddEvent(AdType.Banner, AdEvent.Showing, m_currBannerInstance);
            }
        }

        // Called when banner failed to show
        private void OnBannerShowFailed(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Banner show failed");
#endif
        }

        // Called when banner is clicked
        private void OnBannerClicked(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Banner clicked");
#endif
        }

        // Called when banner is expired and can not be shown
        private void OnBannerExpired(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Banner expired");
#endif
        }

        #endregion

        //____________________________________________________________

        #region MrecAd callback handlers

        // Called when mrec is loaded precache flag shows if the loaded ad is precache)
        private void OnMrecLoaded(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Mrec loaded");
#endif

            if (!m_mrecBannerDisplayed)
            {
                Appodeal.HideMrecView();
            }

            if (m_currMrecBannerInstance != null)
            {
                AddEvent(AdType.Banner, AdEvent.Prepared, m_currMrecBannerInstance);
            }
        }

        // Called when mrec failed to load
        private void OnMrecFailedToLoad(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Mrec failed to load");
#endif
        }

        // Called when mrec is shown
        private void OnMrecShown(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Mrec shown");
#endif

            if (m_currMrecBannerInstance != null)
            {
                AddEvent(AdType.Banner, AdEvent.Showing, m_currMrecBannerInstance);
            }
        }

        // Called when mrec is failed to show
        private void OnMrecShowFailed(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Mrec show failed");
#endif
        }

        // Called when mrec is clicked
        private void OnMrecClicked(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Mrec clicked");
#endif
        }

        // Called when mrec is expired and can not be shown
        private void OnMrecExpired(object sender, EventArgs e)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AppodealAdapter Mrec expired");
#endif
        }

        #endregion

#endif
    }
}