#define _AMS_YANDEX_MOBILE_ADS

using System;
using System.Collections;
using System.Collections.Generic;
using Boomlagoon.JSON;
using UnityEngine;
using UnityEngine.Serialization;
#if _AMS_YANDEX_MOBILE_ADS
using YandexMobileAds;
using YandexMobileAds.Base;
using static UnityEngine.UI.CanvasScaler;
#endif

namespace Virterix.AdMediation
{
    public class YandexMobileAdsAdapter : AdNetworkAdapter
    {
        public const string Identifier = "yandex";

        public bool m_useAppOpenAd;
        public string m_androidAppOpenAdId;
        public string m_iOSAppOpenAdId;
        public bool m_selfControlImpressionAppOpenAd;
        public int m_appOpenAdShowingFrequency;
        public int m_appOpenAdDisplayCooldown;
        public int m_appOpenAdLoadAttemptMaxNumber = 4;

        private int m_appStateForegroundCount;
        private float m_appOpenAdLastAttemptToShowTime;
        private int m_appOpenAdLoadAttemptCount;

        public enum YandexBannerSize
        {
            Inline,
            Sticky,
            Fixed
        }

        public enum YandexBannerPosition
        {
            TopLeft,
            TopCenter,
            TopRight,
            CenterLeft,
            Center,
            CenterRight,
            BottomLeft,
            BottomCenter,
            BottomRight
        }

        public class YandexAdInstanceData : AdInstance
        {
            public YandexAdInstanceData(AdNetworkAdapter network) : base(network)
            {
            }

            public YandexAdInstanceData(AdNetworkAdapter network, AdType adType, string adID,
                string name = AdInstance.AD_INSTANCE_DEFAULT_NAME) :
                base(network, adType, adID, name)
            {
            }

            public float BannerRefreshTime;
            public Coroutine ProcessBannerRefreshing;
            public float BannerDisplayTime;
            public float BannerLastShowingTime;

            public bool IsBannerRefreshProcessRunning => ProcessBannerRefreshing != null;

#if _AMS_YANDEX_MOBILE_ADS
            public Interstitial Interstitial;
            public RewardedAd RewardedAd;
            public Banner Banner;
            public AdPosition BannerPosition;
            public bool HasCallbacksInitialized;

            public EventHandler<EventArgs> OnBannerLoaded;
            public EventHandler<AdFailureEventArgs> OnBannerFailedLoad;
            public EventHandler<EventArgs> OnBannerLeftApplication;
            public EventHandler<EventArgs> OnBannerReturnedToApplication;

            public EventHandler<EventArgs> OnShowed;
            public EventHandler<AdFailureEventArgs> OnFailedShow;
            public EventHandler<EventArgs> OnClosed;
            public EventHandler<Reward> OnRewarded;
#endif
        }

#if _AMS_YANDEX_MOBILE_ADS
        private InterstitialAdLoader _interstitialAdLoader;
        private RewardedAdLoader _rewardedAdLoader;

        public static AdPosition ConvertToNativeBannerPosition(int bannerPosition)
        {
            return ConvertToNativeBannerPosition((YandexBannerPosition)bannerPosition);
        }

        public static AdPosition ConvertToNativeBannerPosition(YandexBannerPosition bannerPosition)
        {
            AdPosition nativeBannerPosition = AdPosition.BottomCenter;
            switch (bannerPosition)
            {
                case YandexBannerPosition.Center:
                    nativeBannerPosition = AdPosition.Center;
                    break;
                case YandexBannerPosition.BottomCenter:
                    nativeBannerPosition = AdPosition.BottomCenter;
                    break;
                case YandexBannerPosition.BottomLeft:
                    nativeBannerPosition = AdPosition.BottomLeft;
                    break;
                case YandexBannerPosition.BottomRight:
                    nativeBannerPosition = AdPosition.BottomRight;
                    break;
                case YandexBannerPosition.CenterLeft:
                    nativeBannerPosition = AdPosition.CenterLeft;
                    break;
                case YandexBannerPosition.CenterRight:
                    nativeBannerPosition = AdPosition.CenterRight;
                    break;
                case YandexBannerPosition.TopCenter:
                    nativeBannerPosition = AdPosition.TopCenter;
                    break;
                case YandexBannerPosition.TopLeft:
                    nativeBannerPosition = AdPosition.TopLeft;
                    break;
                case YandexBannerPosition.TopRight:
                    nativeBannerPosition = AdPosition.TopRight;
                    break;
            }

            return nativeBannerPosition;
        }
#endif

        protected override string AdInstanceParametersFolder =>
            YandexAdInstanceBannerParameters._AD_INSTANCE_PARAMETERS_FOLDER;

        public override bool UseSingleBannerInstance => false;

        public static string GetSDKVersion()
        {
            string version = string.Empty;
#if UNITY_EDITOR && _AMS_YANDEX_MOBILE_ADS
            version = MobileAdsPackageInfo.PackageVersion;
#endif
            return version;
        }

        protected override void InitializeParameters(Dictionary<string, string> parameters, JSONArray jsonAdInstances)
        {
#if _AMS_YANDEX_MOBILE_ADS
            if (AdMediationSystem.Instance.ChildrenMode != ChildDirectedMode.NotAssign)
            {
                SetAgeRestrictedUser(AdMediationSystem.Instance.ChildrenMode == ChildDirectedMode.Directed);
            }
            
            _interstitialAdLoader = new InterstitialAdLoader();
            _interstitialAdLoader.OnAdLoaded += HandleInterstitialAdLoaded;
            _interstitialAdLoader.OnAdFailedToLoad += HandleAdFailedToLoad;

            _rewardedAdLoader = new RewardedAdLoader();
            _rewardedAdLoader.OnAdLoaded += HandleRewardedAdLoaded;
            _rewardedAdLoader.OnAdFailedToLoad += HandleAdFailedToLoad;

            AppStateObserver.OnAppStateChanged += HandleAppStateChanged;
#endif

            base.InitializeParameters(parameters, jsonAdInstances);

            SetUserConsentToPersonalizedAds(AdMediationSystem.UserPersonalisationConsent);
            
            foreach (AdMediator mediator in AdMediationSystem.Instance.BannerMediators)
            {
                if (AdMediationSystem.NonRewardAdsDisabled && mediator.m_adType != AdType.Incentivized)
                {
                    continue;
                }

                for (int i = 0; i < mediator.TotalUnits; i++)
                {
                    AdUnit unit = mediator.GetUnit(i);
                    if (unit.AdInstance.LoadingOnStart && unit.AdNetwork == this &&
                        unit.AdInstance.State == AdState.Uncertain)
                    {
                        Prepare(unit.AdInstance, mediator.m_placementName);
                    }
                }
            }

            if (m_useAppOpenAd && !HasAppOpenAdManager)
            {
                AppOpenAdManager = CreateAppOpenAdManager();
                if (m_selfControlImpressionAppOpenAd)
                {
                    StartCoroutine(RequestAppOpenAd(AppOpenAdManager, 30));
                }
            }
        }

        protected override IAppOpenAdManager CreateAppOpenAdManager()
        {
            IAppOpenAdManager manager = null;

            string openAdUnitId = m_androidAppOpenAdId;
#if UNITY_IOS
            openAdUnitId = m_iOSAppOpenAdId;
#endif
            if (m_useAppOpenAd && !string.IsNullOrEmpty(openAdUnitId))
            {
#if _AMS_YANDEX_MOBILE_ADS
                manager = new YandexAppOpenAdManager(this, openAdUnitId);
#endif
            }

            return manager;
        }

        protected override void InitializeAdInstanceData(AdInstance adInstance, JSONValue jsonAdInstance)
        {
            base.InitializeAdInstanceData(adInstance, jsonAdInstance);
            if (adInstance.LoadingOnStart && adInstance.m_adType != AdType.Banner)
            {
                Prepare(adInstance, "");
            }
        }

        private void ForceHideAllHiddenBanners()
        {
            foreach (AdInstance instance in m_adInstances)
            {
                if (instance != null && instance.m_adType == AdType.Banner && !instance.m_bannerDisplayed)
                {
                    Hide(instance);
                }
            }
        }

        public void ShowDebugPanel()
        {
#if _AMS_YANDEX_MOBILE_ADS
            MobileAds.ShowDebugPanel();
#endif
        }

        public override void Prepare(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdType adType = adInstance.m_adType;
            adInstance.CurrPlacement = placement;

            if (!IsReady(adInstance) && adInstance.State != AdState.Loading)
            {
                adInstance.State = AdState.Loading;
                YandexAdInstanceData yandexAdInstance = (YandexAdInstanceData)adInstance;

#if _AMS_YANDEX_MOBILE_ADS
                switch (adType)
                {
                    case AdType.Interstitial:
                        if (yandexAdInstance.Interstitial == null)
                            RequestInterstitialAd(yandexAdInstance);
                        break;
                    case AdType.Incentivized:
                        if (yandexAdInstance.RewardedAd == null)
                            RequestRewardAd(yandexAdInstance);
                        break;
                    case AdType.Banner:
                        if (yandexAdInstance.Banner == null)
                            CreateBannerAd(yandexAdInstance, placement);
                        yandexAdInstance.Banner.LoadAd(CreateRequest());
                        break;
                }
#endif
            }
        }

#if _AMS_YANDEX_MOBILE_ADS
        private AdRequest CreateRequest()
        {
            return new AdRequest.Builder().Build();
        }
#endif

        public override bool Show(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdType adType = adInstance.m_adType;
            YandexAdInstanceData yandexAdInstance = (YandexAdInstanceData)adInstance;
            adInstance.CurrPlacement = placement;
            bool wasBannerDisplay = adInstance.m_bannerDisplayed;

            if (adType == AdType.Banner)
                adInstance.m_bannerDisplayed = true;

            if (IsReady(adInstance))
            {
                ShowYandexAd(yandexAdInstance);
                if (adType == AdType.Banner)
                {
                    if (!wasBannerDisplay)
                    {
                        yandexAdInstance.BannerLastShowingTime = Time.unscaledTime;
                        StartRefreshBannerProcess(yandexAdInstance);
                    }

                    AddEvent(adInstance.m_adType, AdEvent.Showing, adInstance);
                }
                else if (AdMediationSystem.IsAdFullscreen(adType))
                {
                    SharedFullscreenAdWillShow = true;
                }

                return true;
            }

            return false;
        }

        public override void Hide(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            if (adInstance.m_adType == AdType.Banner)
            {
                YandexAdInstanceData yandexAdInstance = (YandexAdInstanceData)adInstance;
                bool wasBannerDisplay = yandexAdInstance.m_bannerDisplayed;
                yandexAdInstance.m_bannerDisplayed = false;
#if _AMS_YANDEX_MOBILE_ADS
                yandexAdInstance.Banner?.Hide();
#endif
                if (yandexAdInstance.IsBannerRefreshProcessRunning)
                {
                    StopRefreshBannerProcess(yandexAdInstance);
                    yandexAdInstance.BannerDisplayTime += Time.unscaledTime - yandexAdInstance.BannerLastShowingTime;
                }

                if (wasBannerDisplay)
                    NotifyEvent(AdEvent.Hiding, adInstance);
            }
        }

        public override bool IsReady(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
#if UNITY_EDITOR
            return false;
#else
            bool isReady = false;
            if (adInstance != null) {
                YandexAdInstanceData yandexAdInstance = (YandexAdInstanceData)adInstance;
#if _AMS_YANDEX_MOBILE_ADS
                switch (adInstance.m_adType) {
                    case AdType.Interstitial:
                        isReady = yandexAdInstance.Interstitial != null;
                        break;
                    case AdType.Incentivized:
                        isReady = yandexAdInstance.RewardedAd != null;
                        break;
                    case AdType.Banner:
                        isReady = yandexAdInstance.Banner != null && yandexAdInstance.State == AdState.Received;
                        break;
                }
#endif
            }

            return isReady;
#endif
        }

        private IEnumerator RequestAppOpenAd(IAppOpenAdManager appOpenAdManager, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            appOpenAdManager.LoadAd();
        }

        public override void SetUserConsentToPersonalizedAds(PersonalisationConsent consent)
        {
#if _AMS_YANDEX_MOBILE_ADS
            if (consent != PersonalisationConsent.Undefined)
            {
                MobileAds.SetUserConsent(consent == PersonalisationConsent.Accepted);
                MobileAds.SetLocationConsent(consent == PersonalisationConsent.Accepted);
            }
#endif
        }
        
        public override void SetAgeRestrictedUser(bool ageRestricted)
        {
#if _AMS_YANDEX_MOBILE_ADS
            MobileAds.SetAgeRestrictedUser(ageRestricted);
#endif
        }

        protected override AdInstance CreateAdInstanceData(JSONValue jsonAdInstance)
        {
            AdInstance adInstance = new YandexAdInstanceData(this);
            return adInstance;
        }

        private void ShowYandexAd(YandexAdInstanceData yandexAdInstance)
        {
#if _AMS_YANDEX_MOBILE_ADS
            switch (yandexAdInstance.m_adType)
            {
                case AdType.Interstitial:
                    yandexAdInstance.Interstitial?.Show();
                    break;
                case AdType.Incentivized:
                    yandexAdInstance.RewardedAd?.Show();
                    break;
                case AdType.Banner:
                    yandexAdInstance.Banner?.Show();
                    break;
            }
#endif
        }

#if _AMS_YANDEX_MOBILE_ADS
        private AdRequestConfiguration CreateAdRequest(string adUnitId)
        {
            return new AdRequestConfiguration.Builder(adUnitId).Build();
        }

        private void RequestInterstitialAd(YandexAdInstanceData yandexAdInstance)
        {
            DestroyInterstitialAd(yandexAdInstance);

            if (!yandexAdInstance.HasCallbacksInitialized)
            {
                yandexAdInstance.HasCallbacksInitialized = true;
                yandexAdInstance.OnShowed = delegate(object sender, EventArgs args)
                {
                    OnAdShowed(yandexAdInstance, sender, args);
                };
                yandexAdInstance.OnFailedShow = delegate(object sender, AdFailureEventArgs args)
                {
                    OnAdFailedToShow(yandexAdInstance, sender, args);
                };
                yandexAdInstance.OnClosed = delegate(object sender, EventArgs args)
                {
                    OnAdClosed(yandexAdInstance, sender, args);
                };
            }

            _interstitialAdLoader.LoadAd(CreateAdRequest(yandexAdInstance.m_adId));
        }

        private void DestroyInterstitialAd(YandexAdInstanceData yandexAdInstance)
        {
            yandexAdInstance.State = AdState.Uncertain;
            if (yandexAdInstance.Interstitial == null)
                return;

            yandexAdInstance.Interstitial.OnAdShown -= yandexAdInstance.OnShowed;
            yandexAdInstance.Interstitial.OnAdFailedToShow -= yandexAdInstance.OnFailedShow;
            yandexAdInstance.Interstitial.OnAdDismissed -= yandexAdInstance.OnClosed;

            yandexAdInstance.Interstitial.Destroy();
            yandexAdInstance.Interstitial = null;
        }

        private void RequestRewardAd(YandexAdInstanceData yandexAdInstance)
        {
            DestroyRewardAd(yandexAdInstance);

            if (!yandexAdInstance.HasCallbacksInitialized)
            {
                yandexAdInstance.HasCallbacksInitialized = true;

                yandexAdInstance.OnShowed = delegate(object sender, EventArgs args)
                {
                    OnAdShowed(yandexAdInstance, sender, args);
                };
                yandexAdInstance.OnFailedShow = delegate(object sender, AdFailureEventArgs args)
                {
                    OnAdFailedToShow(yandexAdInstance, sender, args);
                };
                yandexAdInstance.OnClosed = delegate(object sender, EventArgs args)
                {
                    OnAdClosed(yandexAdInstance, sender, args);
                };
                yandexAdInstance.OnRewarded = delegate(object sender, Reward reward)
                {
                    OnUserRewarded(yandexAdInstance, sender, reward);
                };
            }

            _rewardedAdLoader.LoadAd(CreateAdRequest(yandexAdInstance.m_adId));
        }

        private void DestroyRewardAd(YandexAdInstanceData yandexAdInstance)
        {
            yandexAdInstance.State = AdState.Uncertain;
            if (yandexAdInstance.RewardedAd == null)
                return;

            yandexAdInstance.RewardedAd.OnAdShown -= yandexAdInstance.OnShowed;
            yandexAdInstance.RewardedAd.OnAdFailedToShow -= yandexAdInstance.OnFailedShow;
            yandexAdInstance.RewardedAd.OnAdDismissed -= yandexAdInstance.OnClosed;
            yandexAdInstance.RewardedAd.OnRewarded -= yandexAdInstance.OnRewarded;

            yandexAdInstance.RewardedAd.Destroy();
            yandexAdInstance.RewardedAd = null;
        }

        private Banner CreateBannerAd(YandexAdInstanceData yandexAdInstance, string placementName)
        {
            DestroyBanner(yandexAdInstance);

            YandexAdInstanceBannerParameters bannerParams =
                (YandexAdInstanceBannerParameters)yandexAdInstance.m_adInstanceParams;
            yandexAdInstance.BannerPosition =
                ConvertToNativeBannerPosition(GetBannerPosition(yandexAdInstance, placementName));
            BannerAdSize bannerMaxSize = null;

            if (bannerParams.m_bannerSize == YandexBannerSize.Inline)
            {
                bannerMaxSize = BannerAdSize.InlineSize(GetScreenWidthDp(), bannerParams.m_maxHeight);
            }
            else if (bannerParams.m_bannerSize == YandexBannerSize.Sticky)
            {
                bannerMaxSize = BannerAdSize.StickySize(GetScreenWidthDp());
            }
            else if (bannerParams.m_bannerSize == YandexBannerSize.Fixed)
            {
                bannerMaxSize = BannerAdSize.FixedSize(GetScreenWidthDp(), bannerParams.m_maxHeight);
            }

            yandexAdInstance.BannerRefreshTime = bannerParams.m_refreshTime;
            yandexAdInstance.BannerDisplayTime = 0.0f;
            yandexAdInstance.Banner =
                new Banner(yandexAdInstance.m_adId, bannerMaxSize, yandexAdInstance.BannerPosition);

            yandexAdInstance.OnBannerLoaded = delegate(object sender, EventArgs args)
            {
                OnAdLoaded(yandexAdInstance, sender, args);
            };
            yandexAdInstance.OnBannerFailedLoad = delegate(object sender, AdFailureEventArgs args)
            {
                OnAdFailedToLoad(yandexAdInstance, sender, args);
            };
            yandexAdInstance.OnBannerLeftApplication = delegate(object sender, EventArgs args)
            {
                OnBannerLeftApplication(yandexAdInstance, sender, args);
            };
            yandexAdInstance.OnBannerReturnedToApplication = delegate(object sender, EventArgs args)
            {
                OnBannerReturnedToApplication(yandexAdInstance, sender, args);
            };

            yandexAdInstance.Banner.OnAdLoaded += yandexAdInstance.OnBannerLoaded;
            yandexAdInstance.Banner.OnAdFailedToLoad += yandexAdInstance.OnBannerFailedLoad;
            yandexAdInstance.Banner.OnLeftApplication += yandexAdInstance.OnBannerLeftApplication;
            yandexAdInstance.Banner.OnReturnedToApplication += yandexAdInstance.OnBannerReturnedToApplication;

            return yandexAdInstance.Banner;
        }

        private void DestroyBanner(YandexAdInstanceData yandexAdInstance)
        {
            yandexAdInstance.State = AdState.Uncertain;
            StopRefreshBannerProcess(yandexAdInstance);
            if (yandexAdInstance.Banner == null)
                return;

            yandexAdInstance.Banner.OnAdLoaded -= yandexAdInstance.OnBannerLoaded;
            yandexAdInstance.Banner.OnAdFailedToLoad -= yandexAdInstance.OnBannerFailedLoad;
            yandexAdInstance.Banner.OnLeftApplication -= yandexAdInstance.OnBannerLeftApplication;
            yandexAdInstance.Banner.OnReturnedToApplication -= yandexAdInstance.OnBannerReturnedToApplication;

            yandexAdInstance.Banner.Destroy();
            yandexAdInstance.Banner = null;
        }

        private void DestroyYandexAd(YandexAdInstanceData adInstance)
        {
            switch (adInstance.m_adType)
            {
                case AdType.Interstitial:
                    DestroyInterstitialAd(adInstance);
                    break;
                case AdType.Incentivized:
                    DestroyRewardAd(adInstance);
                    break;
                case AdType.Banner:
                    DestroyBanner(adInstance);
                    break;
            }
        }

        private int GetScreenWidthDp()
        {
            int screenWidth = (int)Screen.safeArea.width;
            return ScreenUtils.ConvertPixelsToDp(screenWidth);
        }
#endif

        private void StartRefreshBannerProcess(YandexAdInstanceData adInstance)
        {
            StopRefreshBannerProcess(adInstance);
            float timeToBannerRefresh = adInstance.BannerRefreshTime - adInstance.BannerDisplayTime;
            timeToBannerRefresh = Mathf.Clamp(timeToBannerRefresh, 1, adInstance.BannerRefreshTime);
            adInstance.ProcessBannerRefreshing = StartCoroutine(RefreshBanner(adInstance, timeToBannerRefresh));
        }

        private void StopRefreshBannerProcess(YandexAdInstanceData adInstance)
        {
            if (adInstance.ProcessBannerRefreshing != null)
            {
                StopCoroutine(adInstance.ProcessBannerRefreshing);
                adInstance.ProcessBannerRefreshing = null;
            }
        }

        private IEnumerator RefreshBanner(YandexAdInstanceData adInstance, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
#if _AMS_YANDEX_MOBILE_ADS && !UNITY_EDITOR
            CreateBannerAd(adInstance, adInstance.CurrPlacement);
#endif
            Prepare(adInstance, adInstance.CurrPlacement);
        }

        //_______________________________________________________________________________

        #region Callback Event Methods

#if _AMS_YANDEX_MOBILE_ADS

        private void HandleAppStateChanged(object sender, AppStateChangedEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds HandleAppStateChanged. SharedFullscreenAdWillShow:{SharedFullscreenAdWillShow}");
#endif
            
            if (SharedFullscreenAdWillShow)
            {
                return;
            }
            
            if (!args.IsInBackground && m_useAppOpenAd && m_selfControlImpressionAppOpenAd)
            {
                m_appStateForegroundCount++;

#if AD_MEDIATION_DEBUG_MODE
                Debug.Log($"[AMS] YandexMobileAds HandleAppStateChanged. m_appStateForegroundCount:{m_appStateForegroundCount}");
#endif
                
                if (Time.realtimeSinceStartup - m_appOpenAdLastAttemptToShowTime > m_appOpenAdDisplayCooldown &&
                    (m_appOpenAdShowingFrequency <= 1 ||
                     (m_appStateForegroundCount - 1) % m_appOpenAdShowingFrequency == 0))
                {
                    AppOpenAdManager.ShowAdIfAvailable();
                    m_appOpenAdLastAttemptToShowTime = Time.realtimeSinceStartup;
                }
            }
        }

        private void HandleOpenAdLoadComplete(bool success)
        {
            if (m_appOpenAdLoadAttemptCount >= m_appOpenAdLoadAttemptMaxNumber)
            {
                return;
            }

            if (success)
            {
                m_appOpenAdLoadAttemptCount = 0;
            }
            else
            {
                m_appOpenAdLoadAttemptCount++;
                if (m_selfControlImpressionAppOpenAd)
                {
                    StartCoroutine(RequestAppOpenAd(AppOpenAdManager, 90));
                }
            }
        }

        private void HandleRewardedAdLoaded(object sender, RewardedAdLoadedEventArgs args)
        {
            YandexAdInstanceData adInstance =
                (YandexAdInstanceData)GetAdInstanceByAdId(args.RewardedAd.GetInfo().AdUnitId);

            adInstance.RewardedAd = args.RewardedAd;
            adInstance.State = AdState.Received;

            adInstance.RewardedAd.OnAdShown += adInstance.OnShowed;
            adInstance.RewardedAd.OnAdFailedToShow += adInstance.OnFailedShow;
            adInstance.RewardedAd.OnAdDismissed += adInstance.OnClosed;
            adInstance.RewardedAd.OnRewarded += adInstance.OnRewarded;

            AddEvent(adInstance.m_adType, AdEvent.Prepared, adInstance);
        }

        private void HandleInterstitialAdLoaded(object sender, InterstitialAdLoadedEventArgs args)
        {
            YandexAdInstanceData adInstance = (YandexAdInstanceData)GetAdInstanceByAdId(args.Interstitial.GetInfo().AdUnitId);

            adInstance.Interstitial = args.Interstitial;
            adInstance.State = AdState.Received;

            adInstance.Interstitial.OnAdShown += adInstance.OnShowed;
            adInstance.Interstitial.OnAdFailedToShow += adInstance.OnFailedShow;
            adInstance.Interstitial.OnAdDismissed += adInstance.OnClosed;

            AddEvent(adInstance.m_adType, AdEvent.Prepared, adInstance);
        }

        private void HandleAdFailedToLoad(object sender, AdFailedToLoadEventArgs args)
        {
            YandexAdInstanceData adInstance = GetAdInstanceByAdId(args.AdUnitId) as YandexAdInstanceData;
            if (adInstance == null)
            {
                Debug.LogError("[AMS] YandexMobileAds HandleAdFailedToLoad() Instance not found!");
                return;
            }

#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Failed To Load. {adInstance?.m_adType ?? AdType.Unknown} AdUnitId:{args.AdUnitId} Message: {args.Message}");
#endif
            DestroyYandexAd(adInstance);
            AddEvent(adInstance.m_adType, AdEvent.PreparationFailed, adInstance);
        }

        private void OnAdLoaded(YandexAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Loaded. {adInstance.m_adType} Displayed: {adInstance.m_bannerDisplayed}");
#endif
            adInstance.State = AdState.Received;
            if (adInstance.m_adType == AdType.Banner)
            {
                if (adInstance.m_bannerDisplayed)
                {
                    adInstance.Banner.Show();
                    adInstance.BannerLastShowingTime = Time.unscaledTime;
                    if (adInstance.ProcessBannerRefreshing == null)
                        StartRefreshBannerProcess(adInstance);
                }
                else
                {
                    adInstance.Banner.Hide();
                }
            }

            AddEvent(adInstance.m_adType, AdEvent.Prepared, adInstance);
        }

        private void OnAdFailedToLoad(YandexAdInstanceData adInstance, object sender, AdFailureEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Failed To Load. {adInstance.m_adType} Message: {args.Message}");
#endif
            DestroyYandexAd(adInstance);
            AddEvent(adInstance.m_adType, AdEvent.PreparationFailed, adInstance);
        }

        private void OnAdShowed(YandexAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Showed. {adInstance.m_adType}");
#endif
            AddEvent(adInstance.m_adType, AdEvent.Showing, adInstance);
        }

        private void OnAdClosed(YandexAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Closed. {adInstance.m_adType}");
#endif
            DestroyYandexAd(adInstance);

            if (AdMediationSystem.IsAdFullscreen(adInstance.m_adType))
            {
                SharedFullscreenAdWillShow = false;
            }
            
            AddEvent(adInstance.m_adType, AdEvent.Hiding, adInstance);
        }

        private void OnAdFailedToShow(YandexAdInstanceData adInstance, object sender, AdFailureEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Failed To Show. {adInstance.m_adType} Message: {args.Message}");
#endif
            DestroyYandexAd(adInstance);
            AddEvent(adInstance.m_adType, AdEvent.PreparationFailed, adInstance);
        }

        private void OnUserRewarded(YandexAdInstanceData adInstance, object sender, Reward reward)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds User Rewarded. {reward.type}: {reward.amount}");
#endif
            AddEvent(adInstance.m_adType, AdEvent.IncentivizationCompleted, adInstance);
        }
#endif

        private void OnBannerLeftApplication(YandexAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Banner Left Application");
#endif
        }

        private void OnBannerReturnedToApplication(YandexAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log($"[AMS] YandexMobileAds Banner Returned To Application");
#endif
        }

        #endregion
    }
}