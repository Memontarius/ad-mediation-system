//#define _AMS_ADMOB

using UnityEngine;
using System;
using System.Collections.Generic;
using Boomlagoon.JSON;
#if UNITY_EDITOR
using System.Reflection;
#endif
#if _AMS_ADMOB
using System.Collections;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using GoogleMobileAds.Ump.Api;
#endif

namespace Virterix.AdMediation
{
    public class AdMobAdapter : AdNetworkAdapter
    {
        public const string Identifier = "admob";

        public enum AdMobBannerSize
        {
            SmartBanner,
            Banner,
            MediumRectangle,
            Leaderboard,
            IABBanner
        }

        public enum AdMobBannerPosition
        {
            Center,
            Top,
            TopLeft,
            TopRight,
            Bottom,
            BottomLeft,
            BottomRight
        }

        public enum AdMobTagForUnderAgeOfConsent
        {
            Unspecified = 0,
            False,
            True
        }

        public enum AdMobMaxAdContentRating
        {
            Unspecified = 0,
            G,
            MA,
            PG,
            T
        }

        public struct AdRequestContainer
        {
#if _AMS_ADMOB
            public AdRequestContainer(AdRequest request)
            {
                _request = request;
            }
            public readonly AdRequest _request;
#endif
        }

        public struct InitializationStatusContainer
        {
#if _AMS_ADMOB
            public InitializationStatusContainer(InitializationStatus status)
            {
                _status = status;
            }
            public readonly InitializationStatus _status;
#endif
        }

        public event Action OnWillInitialize = delegate { };
        public event Action OnDidInitialize = delegate { };

        public event Action<AdType, AdRequestContainer> OnAdRequest = delegate { };
        public event Action<InitializationStatusContainer> OnInitializationComplete = delegate { };

        private const string UnderAgeOfConsentSaveKey = AdMediationSystem.PREFIX + "abmob.uac";
        private const string MaxContentRatingSaveKey = AdMediationSystem.PREFIX + "abmob.mcr";
        private AdMobConsentProvider _consentProvider;
        private int m_appStateForegroundCount;
        private AdMobMediationBehavior m_adMobMediationBehavior;
        private bool m_wasAppStateEventNotifierSubscribe;
        private float m_appOpenAdLastAttemptToShowTime;
        private int m_appOpenAdLoadAttemptCount;
        private IAppOpenAdManager m_alternativeOpenAdManager;
        
        public bool m_useAppOpenAd;
        public string m_androidAppOpenAdId;
        public string m_iOSAppOpenAdId;
        public int m_appOpenAdDisplayMultiplicity;
        public int m_appOpenAdDisplayCooldown;
        public int m_appOpenAdLoadAttemptMaxNumber = 4;
        public string m_appOpenAdAlternativeNetwork;
        public bool m_useMediation;
        public bool m_autoConsent;
        
        public bool AppOpenAdDisabled { get; set; } = false;
        /// <summary>
        /// Should be assigned before initialization
        /// </summary>
        public AdMobTagForUnderAgeOfConsent UnderAgeOfConsent { get; set; }
        /// <summary>
        /// Should be assigned before initialization
        /// </summary>
        public AdMobMaxAdContentRating MaxContentRating { get; set; }

        public override bool RequiredWaitingInitializationResponse => true;

        public AdMobConsentProvider ConsentProvider
        {
            get
            {
                if (_consentProvider == null) {
                    _consentProvider = new AdMobConsentProvider();
                }
                return _consentProvider;
            }
        }

        protected override string AdInstanceParametersFolder
        {
            get { return AdMobAdInstanceBannerParameters._AD_INSTANCE_PARAMETERS_FOLDER; }
        }

        public static void SetupNetworkNativeSettings(string iOSAppId, string androidAppId)
        {
#if UNITY_EDITOR && _AMS_ADMOB
            string path = "";
            string[] foundAssets = UnityEditor.AssetDatabase.FindAssets("t:GoogleMobileAdsSettings");
            if (foundAssets.Length == 1)
            {
                path = UnityEditor.AssetDatabase.GUIDToAssetPath(foundAssets[0]);
            }
            ScriptableObject adMobSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

            if (adMobSettings != null)
            {
                Type settingsType = adMobSettings.GetType();

                PropertyInfo prop = settingsType.GetProperty("GoogleMobileAdsAndroidAppId");
                prop.SetValue(adMobSettings, androidAppId);

                prop = settingsType.GetProperty("GoogleMobileAdsIOSAppId");
                prop.SetValue(adMobSettings, iOSAppId);
                
                prop = settingsType.GetProperty("DelayAppMeasurementInit");
                if (prop != null)
                {
                    prop.SetValue(adMobSettings, true);
                }

                UnityEditor.EditorUtility.SetDirty(adMobSettings);
            }
            else
            {
                GoogleMobileAds.Editor.GoogleMobileAdsSettingsEditor.OpenInspector();
                Debug.LogWarning("[AMS] AdMob Settings not found! Try again!");
            }
#endif
        }

        public static string GetSDKVersion()
        {
            string version = string.Empty;
#if UNITY_EDITOR && _AMS_ADMOB
            version = AdRequest.Version;
#endif
            return version;
        }

        public void ShowConsentOptionsForm(bool autoFormLoading = true)
        {
#if _AMS_ADMOB
            if (_consentProvider == null) {
                return;
            }

            _consentProvider.ShowPrivacyOptionsForm((string message) => {
#if AD_MEDIATION_DEBUG_MODE
                Debug.Log($"[AMS] AdMobAdapter.ShowConsentOptionsForm Complete: {message}");
#endif
            }, autoFormLoading);
#endif
        }
        
        public void ResetConsentInformation()
        {
#if _AMS_ADMOB
            if (_consentProvider != null) {
                _consentProvider.ResetConsentInformation();
                _consentProvider.LoadForm();
            }
#endif
        }
        
#if _AMS_ADMOB
        public class AdMobAdInstanceData : AdInstance
        {
            public AdMobAdInstanceData(AdNetworkAdapter network) : base(network)
            {
            }

            public AdMobAdInstanceData(AdNetworkAdapter network, AdType adType, string adID, string name = AdInstance.AD_INSTANCE_DEFAULT_NAME) :
                base(network, adType, adID, name)
            {
            }

            public Action onAdLoadedHandler;
            public Action<LoadAdError> onAdFailedToLoadHandler;
            public Action onAdOpeningHandler;
            public Action<AdError> onAdFailedToOpenHandler;
            public Action<AdError> onAdRewardVideoFailedToShowHandler;
            public Action onAdClosedHandler;
            public EventHandler<AdFailedToLoadEventArgs> onAdRewardVideoFailedToLoadHandler;
            public EventHandler<Reward> onAdRewardVideoEarnedHandler;
            public EventHandler<AdValueEventArgs> onAdRewardVideoPaidHandler;
        }
        
        public static AdSize ConvertToNativeBannerSize(AdMobBannerSize bannerSize)
        {
            AdSize admobAdSize = AdSize.Banner;

            switch (bannerSize)
            {
                case AdMobBannerSize.Banner:
                    admobAdSize = AdSize.Banner;
                    break;
                case AdMobBannerSize.IABBanner:
                    admobAdSize = AdSize.IABBanner;
                    break;
                case AdMobBannerSize.SmartBanner:
                    admobAdSize = AdSize.SmartBanner;
                    break;
                case AdMobBannerSize.Leaderboard:
                    admobAdSize = AdSize.Leaderboard;
                    break;
                case AdMobBannerSize.MediumRectangle:
                    admobAdSize = AdSize.MediumRectangle;
                    break;
            }
            return admobAdSize;
        }

        public static AdPosition ConvertToNativeBannerPosition(AdMobBannerPosition bannerPosition)
        {
            AdPosition admobAdPosition = AdPosition.Center;

            switch (bannerPosition)
            {
                case AdMobBannerPosition.Bottom:
                    admobAdPosition = AdPosition.Bottom;
                    break;
                case AdMobBannerPosition.BottomLeft:
                    admobAdPosition = AdPosition.BottomLeft;
                    break;
                case AdMobBannerPosition.BottomRight:
                    admobAdPosition = AdPosition.BottomRight;
                    break;
                case AdMobBannerPosition.Top:
                    admobAdPosition = AdPosition.Top;
                    break;
                case AdMobBannerPosition.TopLeft:
                    admobAdPosition = AdPosition.TopLeft;
                    break;
                case AdMobBannerPosition.TopRight:
                    admobAdPosition = AdPosition.TopRight;
                    break;
                case AdMobBannerPosition.Center:
                    admobAdPosition = AdPosition.Center;
                    break;
            }
            return admobAdPosition;
        }

        protected override void InitializeParameters(Dictionary<string, string> parameters, JSONArray jsonAdInstances)
        {
            base.InitializeParameters(parameters, jsonAdInstances);

            RequestConfiguration requestConfig = ConfigureAdMob();

            AppOpenAdDisabled = AdMediationSystem.NonRewardAdsDisabled;
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            MobileAds.SetiOSAppPauseOnBackground(true);
            
            if (AdMediationSystem.Instance.IsTestModeEnabled)
            {
                requestConfig.TestDeviceIds = new List<string>(AdMediationSystem.Instance.TestDevices);
            }
            
            if (m_useAppOpenAd && !HasAppOpenAdManager) {
                AppOpenAdManager = CreateAppOpenAdManager();
            }

            if (m_autoConsent) {
                ConsentProvider.GatherConsent((string message) =>
                    {
#if AD_MEDIATION_DEBUG_MODE
                        Debug.Log($"[AMS] AdMobAdapter GatherConsent was complete with message: {message}. CanRequestAds: {ConsentProvider.CanRequestAds}");
#endif
                        InitializeAdMob();
                    });
            }
            else {
                _consentProvider = ConsentProvider;
                InitializeAdMob();
            }
        }
        
        protected override void InitializeAdInstanceData(AdInstance adInstance, JSONValue jsonAdInstance)
        {
            base.InitializeAdInstanceData(adInstance, jsonAdInstance);
        }

        private void InitializeAdMob()
        {
            OnWillInitialize();
            MobileAds.Initialize(OnInitComplete);
            if (m_useAppOpenAd) {
                StartCoroutine(RequestAppOpenAd(AppOpenAdManager, 30));
            }
            OnDidInitialize();
        }
        
        protected override AdInstance CreateAdInstanceData(JSONValue jsonAdInstance)
        {
            AdInstance adInstance = new AdMobAdInstanceData(this);
            return adInstance;
        }

        private RequestConfiguration ConfigureAdMob()
        {
            RequestConfiguration requestConfiguration = new RequestConfiguration();
            if (m_useMediation)
            {
                m_adMobMediationBehavior = new AdMobMediationBehavior(this);
            }

            if (AdMediationSystem.Instance.ChildrenMode != ChildDirectedMode.NotAssign)
            {
                var isChildDirected = AdMediationSystem.Instance.ChildrenMode == ChildDirectedMode.Directed ?
                        TagForChildDirectedTreatment.True :
                        TagForChildDirectedTreatment.False;
                requestConfiguration.TagForChildDirectedTreatment = isChildDirected;
            }

            if (AdMediationSystem.Instance.IsTestModeEnabled) {
                requestConfiguration.TestDeviceIds = new List<string>(AdMediationSystem.Instance.TestDevices);
            }

            if (UnderAgeOfConsent != AdMobTagForUnderAgeOfConsent.Unspecified)
            {
                var tagConsent = UnderAgeOfConsent == AdMobTagForUnderAgeOfConsent.True ? TagForUnderAgeOfConsent.True : TagForUnderAgeOfConsent.False;
                requestConfiguration.TagForUnderAgeOfConsent = tagConsent;
            }

            if (MaxContentRating != AdMobMaxAdContentRating.Unspecified)
            {
                MaxAdContentRating maxAdContentRating = MaxAdContentRating.Unspecified;
                switch (MaxContentRating)
                {
                    case AdMobMaxAdContentRating.G:
                        maxAdContentRating = MaxAdContentRating.G;
                        break;
                    case AdMobMaxAdContentRating.MA:
                        maxAdContentRating = MaxAdContentRating.MA;
                        break;
                    case AdMobMaxAdContentRating.PG:
                        maxAdContentRating = MaxAdContentRating.PG;
                        break;
                    case AdMobMaxAdContentRating.T:
                        maxAdContentRating = MaxAdContentRating.T;
                        break;
                }
                requestConfiguration.MaxAdContentRating = maxAdContentRating;
            }

            MobileAds.SetRequestConfiguration(requestConfiguration);
            return requestConfiguration;
        }

        public void SaveContentConfig()
        {
            PlayerPrefs.SetInt(UnderAgeOfConsentSaveKey, (int)UnderAgeOfConsent);
            PlayerPrefs.SetInt(MaxContentRatingSaveKey, (int)MaxContentRating);
        }

        public void RestoreContentConfig()
        {
            UnderAgeOfConsent = (AdMobTagForUnderAgeOfConsent)PlayerPrefs.GetInt(UnderAgeOfConsentSaveKey, (int)AdMobTagForUnderAgeOfConsent.Unspecified);
            MaxContentRating = (AdMobMaxAdContentRating)PlayerPrefs.GetInt(MaxContentRatingSaveKey, (int)AdMobMaxAdContentRating.Unspecified);
        }

        private void OnDestroy()
        {
            if (m_wasAppStateEventNotifierSubscribe)
            {
                AppStateEventNotifier.AppStateChanged -= OnAppStateChanged;
            }
        }

        public override void Prepare(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance as AdMobAdInstanceData;
            adInstance.CurrPlacement = placement;

            if (!IsReady(adMobAdInstance))
            {
                AdType adType = adInstance.m_adType;

                if (adInstance.State != AdState.Loading)
                {
                    switch (adType)
                    {
                        case AdType.Banner:
                            RequestBanner(adMobAdInstance, placement);
                            break;
                        case AdType.Interstitial:
                            RequestInterstitial(adMobAdInstance);
                            break;
                        case AdType.Incentivized:
                            RequestRewardVideo(adMobAdInstance);
                            break;
                    }
                }
            }
        }

        public override bool Show(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance as AdMobAdInstanceData;
            if (adMobAdInstance == null)
                return false;

            AdType adType = adInstance.m_adType;
            bool isAdAvailable = IsReady(adInstance, placement);
            bool isPreviousBannerDisplayed = adMobAdInstance.m_bannerDisplayed;

            if (adType == AdType.Banner)
                adMobAdInstance.m_bannerDisplayed = true;
            adMobAdInstance.CurrPlacement = placement;

            if (isAdAvailable)
            {
                switch (adType)
                {
                    case AdType.Banner:
                        BannerView bannerView = adInstance.m_adView as BannerView;
                        isAdAvailable = bannerView != null;
                        if (isAdAvailable)
                        {
#if UNITY_EDITOR
                            bannerView.Hide();
#endif
                            bannerView.Show();
                            bannerView.SetPosition(ConvertToNativeBannerPosition((AdMobBannerPosition)GetBannerPosition(adInstance, placement)));
                        }
                        if (!isPreviousBannerDisplayed)
                            AddEvent(adInstance.m_adType, AdEvent.Showing, adInstance);
                        break;
                    case AdType.Interstitial:
                        SharedFullscreenAdWillShow = true;
                        InterstitialAd interstitial = adInstance.m_adView as InterstitialAd;
                        interstitial?.Show();
                        break;
                    case AdType.Incentivized:
                        RewardedAd rewardedAd = adInstance.m_adView as RewardedAd;
                        SharedFullscreenAdWillShow = true;
                        rewardedAd?.Show((Reward reward) =>
                        {
                            HandleRewardVideoEarned(adMobAdInstance, reward);
                        });
                        break;
                }
            }
            return isAdAvailable;
        }

        public override void Hide(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance as AdMobAdInstanceData;
            if (adMobAdInstance == null)
                return;

            switch (adInstance.m_adType)
            {
                case AdType.Banner:
                    bool isBannerDisplayed = adMobAdInstance.m_bannerDisplayed;
                    adMobAdInstance.m_bannerDisplayed = false;
                    if (adInstance.State == AdState.Received && adInstance.m_adView != null)
                    {
                        BannerView bannerView = adInstance.m_adView as BannerView;
                        bannerView.Hide();
                        if (isBannerDisplayed)
                            NotifyEvent(AdEvent.Hiding, adInstance);
                    }
                    break;
            }
        }

        public override bool IsReady(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
#if UNITY_EDITOR
            //return false;
#endif
            AdType adType = adInstance.m_adType;
            bool isReady = adInstance.State == AdState.Received;

            if (adInstance.m_adView != null)
            {
                switch (adType)
                {
                    case AdType.Interstitial:  
                        InterstitialAd interstitialAd = adInstance.m_adView as InterstitialAd;
                        isReady = interstitialAd.CanShowAd();
                        break;
                    case AdType.Incentivized:
                        RewardedAd rewardedAd = adInstance.m_adView as RewardedAd;
                        isReady = rewardedAd.CanShowAd();
                        break;
                }
            }

            return isReady;
        }

        private void RequestBanner(AdMobAdInstanceData adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            DestroyBanner(adInstance);

            adInstance.State = AdState.Loading;
            adInstance.CurrPlacement = placement;

            AdMobAdInstanceBannerParameters bannerParams = adInstance.m_adInstanceParams as AdMobAdInstanceBannerParameters;
            AdPosition bannerPosition = ConvertToNativeBannerPosition((AdMobBannerPosition)GetBannerPosition(adInstance, placement));

            BannerView bannerView = new BannerView(adInstance.m_adId, ConvertToNativeBannerSize(bannerParams.m_bannerSize), bannerPosition);
            adInstance.m_adView = bannerView;
            bannerView.Hide();

            // Register for ad events.
            adInstance.onAdLoadedHandler = delegate ()
            {
                HandleAdLoaded(adInstance);
            };
            bannerView.OnBannerAdLoaded += adInstance.onAdLoadedHandler;

            adInstance.onAdFailedToLoadHandler = delegate (LoadAdError adError)
            {
                HandleAdFailedToLoad(adInstance, adError);
            };
            bannerView.OnBannerAdLoadFailed += adInstance.onAdFailedToLoadHandler;

            // Load a banner ad.
            bannerView.LoadAd(CreateAdRequest(AdType.Banner));
        }

        void DestroyBanner(AdMobAdInstanceData adInstance)
        {
            if (adInstance.m_adView != null)
            {
                BannerView bannerView = adInstance.m_adView as BannerView;
                adInstance.m_adView = null;

                bannerView.OnBannerAdLoaded -= adInstance.onAdLoadedHandler;
                bannerView.OnBannerAdLoadFailed -= adInstance.onAdFailedToLoadHandler;

                bannerView.Destroy();
                adInstance.State = AdState.Unavailable;
            }
        }

        private void RequestInterstitial(AdMobAdInstanceData adInstance)
        {
            DestroyInterstitial(adInstance);

            adInstance.State = AdState.Loading;

            // Load an interstitial ad
            InterstitialAd.Load(adInstance.m_adId, CreateAdRequest(AdType.Interstitial),
                (InterstitialAd ad, LoadAdError loadAdError) =>
                {
                    if (loadAdError != null || ad == null)
                    {
                        HandleInterstitialFailedToLoad(adInstance, loadAdError);
                        return;
                    }
                    else
                    {
                        adInstance.m_adView = ad;

                        adInstance.onAdOpeningHandler = delegate ()
                        {
                            HandleInterstitialOpened(adInstance);
                        };
                        ad.OnAdFullScreenContentOpened += adInstance.onAdOpeningHandler;

                        adInstance.onAdClosedHandler = delegate ()
                        {
                            HandleInterstitialClosed(adInstance);
                        };
                        ad.OnAdFullScreenContentClosed += adInstance.onAdClosedHandler;

                        adInstance.onAdFailedToOpenHandler = delegate (AdError adError)
                        {
                            HandleInterstitialOpenFailed(adInstance, adError);
                        };
                        ad.OnAdFullScreenContentFailed += adInstance.onAdFailedToOpenHandler;

                        HandleInterstitialLoaded(adInstance);
                        return;
                    }
                });
        }

        private void DestroyInterstitial(AdMobAdInstanceData adInstance)
        {
            if (adInstance.m_adView != null)
            {
                InterstitialAd interstitial = adInstance.m_adView as InterstitialAd;
                adInstance.m_adView = null;

                interstitial.OnAdFullScreenContentOpened -= adInstance.onAdOpeningHandler;
                interstitial.OnAdFullScreenContentClosed -= adInstance.onAdClosedHandler;
                interstitial.OnAdFullScreenContentFailed -= adInstance.onAdFailedToOpenHandler;

                interstitial.Destroy();
                adInstance.State = AdState.Uncertain;
            }
        }

        private void RequestRewardVideo(AdMobAdInstanceData adInstance)
        {
            DestroyRewardVideo(adInstance);

            adInstance.State = AdState.Loading;

            RewardedAd.Load(adInstance.m_adId, CreateAdRequest(AdType.Incentivized),
                    (RewardedAd ad, LoadAdError loadError) =>
                    {
                        if (loadError != null)
                        {
                            HandleRewardVideoFailedToLoad(adInstance, loadError);
                            return;
                        }
                        else if (ad == null)
                        {
                            HandleRewardVideoFailedToLoad(adInstance, loadError);
                            return;
                        }

                        adInstance.m_adView = ad;

                        adInstance.onAdOpeningHandler = delegate ()
                        {
                            HandleRewardVideoOpened(adInstance);
                        };
                        ad.OnAdFullScreenContentOpened += adInstance.onAdOpeningHandler;

                        adInstance.onAdClosedHandler = delegate ()
                        {
                            HandleRewardVideoClosed(adInstance);
                        };
                        ad.OnAdFullScreenContentClosed += adInstance.onAdClosedHandler;

                        adInstance.onAdRewardVideoFailedToShowHandler = delegate (AdError adError)
                        {
                            HandleRewardVideoFailedToShow(adInstance, adError);
                        };
                        ad.OnAdFullScreenContentFailed += adInstance.onAdRewardVideoFailedToShowHandler;

                        HandleRewardVideoLoaded(adInstance);
                    });
        }

        private void DestroyRewardVideo(AdMobAdInstanceData adInstance)
        {
            if (adInstance.m_adView != null)
            {
                RewardedAd rewardedAd = adInstance.m_adView as RewardedAd;
                rewardedAd.OnAdFullScreenContentOpened += adInstance.onAdOpeningHandler;
                rewardedAd.OnAdFullScreenContentClosed += adInstance.onAdClosedHandler;
                rewardedAd.OnAdFullScreenContentFailed += adInstance.onAdRewardVideoFailedToShowHandler;

                adInstance.m_adView = null;
                adInstance.State = AdState.Uncertain;
            }
        }

        // Returns an ad request with custom ad targeting.
        private AdRequest CreateAdRequest(AdType adType)
        {
            AdRequest request = new AdRequest();

            if (AdMediationSystem.UserPersonalisationConsent == PersonalisationConsent.Denied)
            {
                request.Extras.Add("npa", "1");
                request.Extras.Add("rdp", "1");
            }
            OnAdRequest(adType, new AdRequestContainer(request));

            return request;
        }

        protected override IAppOpenAdManager CreateAppOpenAdManager()
        {
            IAppOpenAdManager manager = null;
  
            string appOpenAdUnitId = m_androidAppOpenAdId;
#if UNITY_IOS
                appOpenAdUnitId = m_iOSAppOpenAdId;
#endif
            if (m_useAppOpenAd && !string.IsNullOrEmpty(appOpenAdUnitId)) {
                m_alternativeOpenAdManager = null;

                if (m_appOpenAdAlternativeNetwork != "") {
                    var alternativeNetwork = AdMediationSystem.Instance.GetNetwork(m_appOpenAdAlternativeNetwork);
                    if (alternativeNetwork != null) {
                        m_alternativeOpenAdManager = alternativeNetwork.AppOpenAdManager;
                    }
                    else {
                        Debug.LogError($"[AMS] Alternative network ({m_appOpenAdAlternativeNetwork}) for AppOpen ad not found");
                    }
                }

                manager = new AdMobAppOpenAdManager(this, appOpenAdUnitId);
                AppStateEventNotifier.AppStateChanged += OnAppStateChanged;
                m_wasAppStateEventNotifierSubscribe = true;

                if (m_alternativeOpenAdManager != null) {
                    m_alternativeOpenAdManager.LoadComplete += HandleOpenAdLoadComplete;
                }
                manager.LoadComplete += HandleOpenAdLoadComplete;
            }

            return manager;
        }
        
        private IEnumerator RequestAppOpenAd(IAppOpenAdManager appOpenAdManager, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            appOpenAdManager.LoadAd();
        }
        
        public override void NotifyEvent(AdEvent adEvent, AdInstance adInstance)
        {
            switch (adInstance.m_adType)
            {
                case AdType.Banner:
                    HandleBannerEvents(adEvent, adInstance);
                    break;
                case AdType.Interstitial:
                    HandleInterstitialEvents(adEvent, adInstance);
                    break;
                case AdType.Incentivized:
                    HandleRewardedVideoEvents(adEvent, adInstance);
                    break;
            }
            base.NotifyEvent(adEvent, adInstance);
        }

        private void HandleBannerEvents(AdEvent adEvent, AdInstance adInstance)
        {
            switch (adEvent)
            {
                case AdEvent.Prepared:
                    adInstance.State = AdState.Received;
                    break;
                case AdEvent.PreparationFailed:
                    DestroyBanner((AdMobAdInstanceData)adInstance);
                    break;
            }
        }

        private void HandleInterstitialEvents(AdEvent adEvent, AdInstance adInstance)
        {
            switch (adEvent)
            {
                case AdEvent.Prepared:
                    adInstance.State = AdState.Received;
                    break;
                case AdEvent.PreparationFailed:
                case AdEvent.Hiding:
                    if (AdMediationSystem.IsAdFullscreen(adInstance.m_adType))
                    {
                        SharedFullscreenAdWillShow = false;
                    }

                    DestroyInterstitial((AdMobAdInstanceData)adInstance);
                    break;
            }
        }

        private void HandleRewardedVideoEvents(AdEvent adEvent, AdInstance adInstance)
        {
            switch (adEvent)
            {
                case AdEvent.Prepared:
                    adInstance.State = AdState.Received;
                    break;
                case AdEvent.PreparationFailed:
                case AdEvent.Hiding:
                    if (AdMediationSystem.IsAdFullscreen(adInstance.m_adType))
                    {
                        SharedFullscreenAdWillShow = false;
                    }

                    DestroyRewardVideo((AdMobAdInstanceData)adInstance);
                    break;
            }
        }

        //------------------------------------------------------------------------
        // AdMob Callbacks
        private void OnInitComplete(InitializationStatus initStatus)
        {
            WasInitializationResponse = true;
            OnInitializationComplete(new InitializationStatusContainer(initStatus));
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.OnInitComplete()");
#endif
        }

        private void OnAppStateChanged(AppState appState)
        {
            if (AppOpenAdDisabled || !enabled || !HasAppOpenAdManager || SharedFullscreenAdShowing || SharedFullscreenAdWillShow) {
                return;
            }

#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] AdMobAdapter.OnAppStateChanged() App State is {appState} Foreground Count: {m_appStateForegroundCount} Passed Time Last Attempt to Show: {(Time.realtimeSinceStartup - m_appOpenAdLastAttemptToShowTime)}");
#endif
            if (appState == AppState.Foreground)
            {
                if (AppOpenAdManager.IsOpened || m_alternativeOpenAdManager is { IsOpened: true }) {
                    return;
                }
#if UNITY_IOS
                m_appStateForegroundCount++;
#else
                m_appStateForegroundCount++;
#endif
                if (Time.realtimeSinceStartup - m_appOpenAdLastAttemptToShowTime > m_appOpenAdDisplayCooldown &&
                    (m_appOpenAdDisplayMultiplicity <= 1 || (m_appStateForegroundCount - 1) % m_appOpenAdDisplayMultiplicity == 0))
                {
                    if (AppOpenAdManager.IsAdAvailable) {
                        AppOpenAdManager.ShowAdIfAvailable();
                    }
                    else {
                        m_alternativeOpenAdManager?.ShowAdIfAvailable();
                    }
                    
                    m_appOpenAdLastAttemptToShowTime = Time.realtimeSinceStartup;
                }
            }
        }
        
        private void HandleOpenAdLoadComplete(bool success)
        {
            if (m_appOpenAdLoadAttemptCount >= m_appOpenAdLoadAttemptMaxNumber) {
                return;
            }
            
            m_appOpenAdLoadAttemptCount++;
            
            if (success) {
                m_appOpenAdLoadAttemptCount = 0;
            }
            else {
                if (m_alternativeOpenAdManager != null && m_appOpenAdLoadAttemptCount % 2 == 1) {
                    float delay = m_appOpenAdLoadAttemptCount == 1 ? 10 : m_appOpenAdLoadAttemptCount * 90;
                    StartCoroutine(RequestAppOpenAd(m_alternativeOpenAdManager, delay));
                }
                else {
                    StartCoroutine(RequestAppOpenAd(AppOpenAdManager, m_appOpenAdLoadAttemptCount * 90));
                }
            }
        }
        
        //------------------------------------------------------------------------
        #region Banner callback handlers

        public void HandleAdLoaded(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleAdLoaded() " + " adInstance: " + adInstance.Name + " isVisibled: " + adInstance.m_bannerDisplayed);
#endif
            AddEvent(AdType.Banner, AdEvent.Prepared, adInstance);
            lock (adInstance)
            {
                BannerView bannerView = adInstance.m_adView as BannerView;
                if (adInstance.m_bannerDisplayed && bannerView != null)
                {
#if UNITY_EDITOR
                    bannerView.Hide();
#endif
                    bannerView.Show();
                }
                else
                    bannerView.Hide();
            }
        }

        public void HandleAdFailedToLoad(AdMobAdInstanceData adInstance, LoadAdError adError)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleAdFailedToLoad() " + " adInstance: " + adInstance.Name + " message: " + adError.GetMessage());
#endif
            AddEvent(AdType.Banner, AdEvent.PreparationFailed, adInstance);
        }

        public void HandleAdOpened(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleAdOpened() " + " adInstance: " + adInstance.Name);
#endif
        }

        public void HandleAdClosing(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleAdClosing() " + " adInstance: " + adInstance.Name);
#endif
        }

        public void HandleAdClosed(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleAdClosed() " + " adInstance: " + adInstance.Name);
#endif
        }

        #endregion // Banner callback handlers

        //------------------------------------------------------------------------
        #region Interstitial callback handlers

        public void HandleInterstitialLoaded(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleInterstitialLoaded()");
#endif
            AddEvent(AdType.Interstitial, AdEvent.Prepared, adInstance);
        }

        public void HandleInterstitialFailedToLoad(AdMobAdInstanceData adInstance, LoadAdError loadAdError)
        {
#if AD_MEDIATION_DEBUG_MODE
            if (loadAdError != null)
                print("[AMS] AdMobAdapter.HandleInterstitialFailedToLoad() message: " + loadAdError.GetMessage());
#endif
            AddEvent(AdType.Interstitial, AdEvent.PreparationFailed, adInstance);
        }

        public void HandleInterstitialOpened(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleInterstitialOpened()");
#endif
            AddEvent(AdType.Interstitial, AdEvent.Showing, adInstance);
        }

        void HandleInterstitialClosing(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleInterstitialClosing()");
#endif
        }

        public void HandleInterstitialClosed(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("[AMS] AdMobAdapter.HandleInterstitialClosed()");
#endif
            AddEvent(AdType.Interstitial, AdEvent.Hiding, adInstance);
        }

        public void HandleInterstitialOpenFailed(AdMobAdInstanceData adInstance, AdError adErorr)
        {
#if AD_MEDIATION_DEBUG_MODE
            if (adErorr != null)
                print("[AMS] AdMobAdapter.HandleInterstitialOpenFailed() message: " + adErorr.GetMessage());
#endif
            DestroyInterstitial(adInstance);
            AddEvent(AdType.Interstitial, AdEvent.PreparationFailed, adInstance);
        }

        #endregion // Interstitial callback handlers

        //------------------------------------------------------------------------
        #region Reward Video callback handlers

        public void HandleRewardVideoLoaded(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoLoaded()");
#endif
            AddEvent(AdType.Incentivized, AdEvent.Prepared, adInstance);
        }

        public void HandleRewardVideoFailedToLoad(AdMobAdInstanceData adInstance, LoadAdError loadError)
        {
#if AD_MEDIATION_DEBUG_MODE
            if (loadError != null)
                MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoFailedToLoad() message: " + loadError.GetMessage());
#endif
            AddEvent(AdType.Incentivized, AdEvent.PreparationFailed, adInstance);
        }

        public void HandleRewardVideoFailedToShow(AdMobAdInstanceData adInstance, AdError adError)
        {
#if AD_MEDIATION_DEBUG_MODE
            if (adError != null)
                MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoFailedToLoad() message: " + adError.GetMessage());
#endif
            AddEvent(AdType.Incentivized, AdEvent.Hiding, adInstance);
        }

        public void HandleRewardVideoOpened(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoOpened()");
#endif
            AddEvent(AdType.Incentivized, AdEvent.Showing, adInstance);
        }

        public void HandleRewardVideoStarted(AdMobAdInstanceData adInstance, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoStarted()");
#endif
        }

        public void HandleRewardVideoClosed(AdMobAdInstanceData adInstance)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoClosed()");
#endif
            AddEvent(AdType.Incentivized, AdEvent.Hiding, adInstance);
        }

        public void HandleRewardVideoEarned(AdMobAdInstanceData adInstance, Reward reward)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoEarned() " + reward.Amount.ToString() + " " + reward.Type);
#endif
            m_lastReward.label = reward.Type;
            m_lastReward.amount = reward.Amount;
            AddEvent(AdType.Incentivized, AdEvent.IncentivizationCompleted, adInstance);
        }

        private void HandleRewardVideoPaidEvent(AdMobAdInstanceData adInstance, AdValueEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("[AMS] AdMobAdapter.HandleRewardVideoPaidEvent()");
#endif
        }

        #endregion // Reward Video callback handlers

#endif
    }
}
