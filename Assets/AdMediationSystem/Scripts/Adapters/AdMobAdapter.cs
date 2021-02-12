#define _AMS_ADMOB

using UnityEngine;
using System;
using System.Collections.Generic;
using Boomlagoon.JSON;
using System.Linq;
#if UNITY_EDITOR
using System.Reflection;
#endif
#if _AMS_ADMOB
using GoogleMobileAds;
using GoogleMobileAds.Api;
#endif

namespace Virterix.AdMediation
{
    public class AdMobAdapter : AdNetworkAdapter
    {
        public const string _BANNER_ID_KEY = "bannerId";
        public const string _INTERSTITIAL_ID_KEY = "interstitialId";
        public const string _REWARDED_ID_KEY = "rewardedId";
        public const string _APP_ID_KEY = "appId";

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

        public bool m_tagForChildDirectedTreatment = false;
        public Color m_adBackgoundColor = Color.gray;
        public bool m_isAddTestDevices = false;
        public string[] m_testDeviceListIds;

        protected override string AdInstanceParametersFolder
        {
            get
            {
                return AdMobAdInstanceBannerParameters._AD_INSTANCE_PARAMETERS_FOLDER;
            }
        }

        public static void SetupNetworkNativeSettings(string iOSAppId, string androidAppId)
        {
#if UNITY_EDITOR && _AMS_ADMOB
            string path = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
            ScriptableObject networkSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

            if (networkSettings != null)
            {
                Type settingsType = networkSettings.GetType();
                PropertyInfo prop = settingsType.GetProperty("AdMobAndroidAppId");
                prop.SetValue(networkSettings, androidAppId);

                prop = settingsType.GetProperty("AdMobIOSAppId");
                prop.SetValue(networkSettings, iOSAppId);

                prop = settingsType.GetProperty("DelayAppMeasurementInit");
                prop.SetValue(networkSettings, true);
            }
            else
            {
                Debug.LogWarning("AdMob Settings not found!");
            }
#endif
        }

#if _AMS_ADMOB
        public class AdMobAdInstanceData : AdInstanceData
        {
            public AdMobAdInstanceData() : base()
            {
            }

            public AdMobAdInstanceData(AdType adType, string adID, string name = AdInstanceData._AD_INSTANCE_DEFAULT_NAME) :
                base(adType, adID, name)
            {
            }

            public AdPosition GetBannerPosition(string placement)
            {
                AdPosition nativeBannerPosition = AdPosition.Bottom;
                var adMobAdInstanceParams = m_adInstanceParams as AdMobAdInstanceBannerParameters;
                var bannerPosition = adMobAdInstanceParams.m_bannerPositions.FirstOrDefault(p => p.m_placementName == placement);
                nativeBannerPosition = ConvertToAdPosition(bannerPosition.m_bannerPosition);
                return nativeBannerPosition;
            }

            public EventHandler<EventArgs> onAdLoadedHandler;
            public EventHandler<AdFailedToLoadEventArgs> onAdFailedToLoadHandler;
            public EventHandler<EventArgs> onAdOpeningHandler;
            public EventHandler<EventArgs> onAdClosedHandler;
            public EventHandler<EventArgs> onAdLeavingApplicationHandler;
        }

        private RewardBasedVideoAd m_rewardVideo;
        private AdMobAdInstanceData m_rewardInstance;

        private void OnEnable()
        {
            m_rewardVideo = RewardBasedVideoAd.Instance;
            m_rewardVideo.OnAdLoaded += this.HandleRewardBasedVideoLoaded;
            m_rewardVideo.OnAdFailedToLoad += this.HandleRewardBasedVideoFailedToLoad;
            m_rewardVideo.OnAdOpening += this.HandleRewardBasedVideoOpened;
            m_rewardVideo.OnAdStarted += this.HandleRewardBasedVideoStarted;
            m_rewardVideo.OnAdRewarded += this.HandleRewardBasedVideoRewarded;
            m_rewardVideo.OnAdClosed += this.HandleRewardBasedVideoClosed;
            m_rewardVideo.OnAdLeavingApplication += this.HandleRewardBasedVideoLeftApplication;
        }

        private new void OnDisable()
        {
            base.OnDisable();
            if (m_rewardVideo != null)
            {
                m_rewardVideo.OnAdLoaded -= this.HandleRewardBasedVideoLoaded;
                m_rewardVideo.OnAdFailedToLoad -= this.HandleRewardBasedVideoFailedToLoad;
                m_rewardVideo.OnAdOpening -= this.HandleRewardBasedVideoOpened;
                m_rewardVideo.OnAdStarted -= this.HandleRewardBasedVideoStarted;
                m_rewardVideo.OnAdRewarded -= this.HandleRewardBasedVideoRewarded;
                m_rewardVideo.OnAdClosed -= this.HandleRewardBasedVideoClosed;
                m_rewardVideo.OnAdLeavingApplication -= this.HandleRewardBasedVideoLeftApplication;
            }
        }

        private void OnApplicationPause(bool pause)
        {
#if UNITY_IOS
                MobileAds.SetiOSAppPauseOnBackground(pause);
#endif
        }

        protected override void InitializeParameters(Dictionary<string, string> parameters, JSONArray jsonAdInstances)
        {
            base.InitializeParameters(parameters, jsonAdInstances);
            MobileAds.Initialize(OnInitComplete);
        }

        protected override void InitializeAdInstanceData(AdInstanceData adInstance, JSONValue jsonAdInstance)
        {
            base.InitializeAdInstanceData(adInstance, jsonAdInstance);
            if (adInstance.m_adType == AdType.Incentivized && m_rewardInstance == null)
            {
                m_rewardInstance = adInstance as AdMobAdInstanceData;
            }
        }

        protected override AdInstanceData CreateAdInstanceData(JSONValue jsonAdInstance)
        {
            AdInstanceData adInstance = new AdMobAdInstanceData();
            return adInstance;
        }

        public override void SetPersonalizedAds(bool isPersonalizedAds)
        {
        }

        public override void Prepare(AdInstanceData adInstance = null, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance == null ? null : adInstance as AdMobAdInstanceData;
            AdType adType = adInstance.m_adType;

            if (adInstance.m_state != AdState.Loading)
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
                        RequestRewardVideo(m_rewardInstance);
                        break;
                }
            }
        }

        public override bool Show(AdInstanceData adInstance = null, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance == null ? null : adInstance as AdMobAdInstanceData;
            AdType adType = adInstance.m_adType;
            bool isAdAvailable = adInstance.m_state == AdState.Received;

            if (adType == AdType.Banner)
            {
                adMobAdInstance.m_isBannerAdTypeVisibled = true;
            }

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
                            bannerView.SetPosition(adMobAdInstance.GetBannerPosition(placement));
                        }
                        break;
                    case AdType.Interstitial:
                        InterstitialAd interstitial = adInstance.m_adView as InterstitialAd;
                        interstitial.Show();
                        break;
                    case AdType.Incentivized:
                        m_rewardVideo.Show();
                        break;
                }
            }
            return isAdAvailable;
        }

        public override void Hide(AdInstanceData adInstance = null)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance == null ? null : adInstance as AdMobAdInstanceData;
            AdType adType = adInstance.m_adType;

            switch (adType)
            {
                case AdType.Banner:
                    adMobAdInstance.m_isBannerAdTypeVisibled = false;

                    if (adInstance.m_state == AdState.Received)
                    {
                        BannerView bannerView = adInstance.m_adView as BannerView;
                        bannerView.Hide();
                    }
                    AddEvent(AdType.Banner, AdEvent.Hide, adInstance);
                    break;
            }
        }

        public override void HideBannerTypeAdWithoutNotify(AdInstanceData adInstance = null)
        {
            AdMobAdInstanceData adMobAdInstance = adInstance == null ? null : adInstance as AdMobAdInstanceData;
            adMobAdInstance.m_isBannerAdTypeVisibled = false;
            AdType adType = adInstance.m_adType;

            switch (adType)
            {
                case AdType.Banner:
                    if (adInstance.m_state == AdState.Received)
                    {
                        BannerView bannerView = adInstance.m_adView as BannerView;
                        bannerView.Hide();
                    }
                    break;
            }
        }

        public override bool IsReady(AdInstanceData adInstance = null)
        {
#if UNITY_EDITOR
            //return false;
#endif
            AdType adType = adInstance.m_adType;
            bool isReady = adInstance.m_state == AdState.Received;
            AdMobAdInstanceData adMobAdInstance = adInstance == null ? null : adInstance as AdMobAdInstanceData;

            switch (adType)
            {
                case AdType.Incentivized:
                    isReady = m_rewardVideo.IsLoaded();
                    break;
            }

            return isReady;
        }

        public static AdSize ConvertToAdSize(AdMobBannerSize bannerSize)
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

        public static AdPosition ConvertToAdPosition(AdMobBannerPosition bannerPosition)
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

        private void RequestBanner(AdMobAdInstanceData adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            DestroyBanner(adInstance);

            adInstance.m_state = AdState.Loading;

            AdMobAdInstanceBannerParameters bannerParams = adInstance.m_adInstanceParams as AdMobAdInstanceBannerParameters;  
            AdPosition bannerPosition = adInstance.GetBannerPosition(placement);
  
            BannerView bannerView = new BannerView(adInstance.m_adId, ConvertToAdSize(bannerParams.m_bannerSize), bannerPosition);
            adInstance.m_adView = bannerView;
            bannerView.Hide();

            // Register for ad events.
            adInstance.onAdLoadedHandler = delegate (object sender, EventArgs args)
            {
                HandleAdLoaded(adInstance, sender, args);
            };
            bannerView.OnAdLoaded += adInstance.onAdLoadedHandler;

            adInstance.onAdFailedToLoadHandler = delegate (object sender, AdFailedToLoadEventArgs args)
            {
                HandleAdFailedToLoad(adInstance, sender, args);
            };
            bannerView.OnAdFailedToLoad += adInstance.onAdFailedToLoadHandler;

            adInstance.onAdOpeningHandler = delegate (object sender, EventArgs args)
            {
                HandleAdOpened(adInstance, sender, args);
            };
            bannerView.OnAdOpening += adInstance.onAdOpeningHandler;

            adInstance.onAdClosedHandler = delegate (object sender, EventArgs args)
            {
                HandleAdClosed(adInstance, sender, args);
            };
            bannerView.OnAdClosed += adInstance.onAdClosedHandler;

            adInstance.onAdLeavingApplicationHandler = delegate (object sender, EventArgs args)
            {
                HandleAdLeftApplication(adInstance, sender, args);
            };
            bannerView.OnAdLeavingApplication += adInstance.onAdLeavingApplicationHandler;

            // Load a banner ad.
            bannerView.LoadAd(CreateAdRequest());
        }

        void DestroyBanner(AdMobAdInstanceData adInstance)
        {
            //m_isBannerLoaded = false;

            if (adInstance.m_adView != null)
            {
                BannerView bannerView = adInstance.m_adView as BannerView;
                adInstance.m_adView = null;

                bannerView.OnAdLoaded -= adInstance.onAdLoadedHandler;
                bannerView.OnAdFailedToLoad -= adInstance.onAdFailedToLoadHandler;
                bannerView.OnAdOpening -= adInstance.onAdOpeningHandler;
                bannerView.OnAdClosed -= adInstance.onAdClosedHandler;
                bannerView.OnAdLeavingApplication -= adInstance.onAdLeavingApplicationHandler;

                bannerView.Destroy();
                adInstance.m_state = AdState.Uncertain;
            }
        }

        private void RequestInterstitial(AdMobAdInstanceData adInstance)
        {
            DestroyInterstitial(adInstance);

            adInstance.m_state = AdState.Loading;

            // Create an interstitial.
            InterstitialAd interstitial = new InterstitialAd(adInstance.m_adId);
            adInstance.m_adView = interstitial;

            // Register for ad events.
            adInstance.onAdLoadedHandler = delegate (object sender, EventArgs args)
            {
                HandleInterstitialLoaded(adInstance, sender, args);
            };
            interstitial.OnAdLoaded += adInstance.onAdLoadedHandler;

            adInstance.onAdFailedToLoadHandler = delegate (object sender, AdFailedToLoadEventArgs args)
            {
                HandleInterstitialFailedToLoad(adInstance, sender, args);
            };
            interstitial.OnAdFailedToLoad += adInstance.onAdFailedToLoadHandler;

            adInstance.onAdOpeningHandler = delegate (object sender, EventArgs args)
            {
                HandleInterstitialOpened(adInstance, sender, args);
            };
            interstitial.OnAdOpening += adInstance.onAdOpeningHandler;

            adInstance.onAdClosedHandler = delegate (object sender, EventArgs args)
            {
                HandleInterstitialClosed(adInstance, sender, args);
            };
            interstitial.OnAdClosed += adInstance.onAdClosedHandler;

            adInstance.onAdLeavingApplicationHandler = delegate (object sender, EventArgs args)
            {
                HandleInterstitialLeftApplication(adInstance, sender, args);
            };
            interstitial.OnAdLeavingApplication += adInstance.onAdLeavingApplicationHandler;

            interstitial.LoadAd(CreateAdRequest());
        }

        void DestroyInterstitial(AdMobAdInstanceData adInstance)
        {
            if (adInstance.m_adView != null)
            {
                InterstitialAd interstitial = adInstance.m_adView as InterstitialAd;
                adInstance.m_adView = null;

                interstitial.OnAdLoaded -= adInstance.onAdLoadedHandler;
                interstitial.OnAdFailedToLoad -= adInstance.onAdFailedToLoadHandler;
                interstitial.OnAdOpening -= adInstance.onAdOpeningHandler;
                interstitial.OnAdClosed -= adInstance.onAdClosedHandler;
                interstitial.OnAdLeavingApplication -= adInstance.onAdLeavingApplicationHandler;

                interstitial.Destroy();
                adInstance.m_state = AdState.Uncertain;
            }
        }

        private void RequestRewardVideo(AdMobAdInstanceData adInstance)
        {
            adInstance.m_state = AdState.Loading;
            m_rewardVideo.LoadAd(CreateAdRequest(), adInstance.m_adId);
        }

        // Returns an ad request with custom ad targeting.
        private AdRequest CreateAdRequest()
        {
            AdRequest.Builder requestBuilder = new AdRequest.Builder()
                    .TagForChildDirectedTreatment(m_tagForChildDirectedTreatment)
                    .AddExtra("color_bg", ColorUtility.ToHtmlStringRGB(m_adBackgoundColor));

            // Set non-personalized ads
            if (!AdMediationSystem.Instance.IsPersonalizedAds)
            {
                requestBuilder.AddExtra("npa", "1");
            }

            if (m_isAddTestDevices)
            {
                foreach (string deviceId in m_testDeviceListIds)
                {
                    requestBuilder.AddTestDevice(deviceId);
                }
            }

            AdRequest request = requestBuilder.Build();
            return request;
        }

        //------------------------------------------------------------------------
        // AdMob Callbacks
        private void OnInitComplete(InitializationStatus initStatus)
        {
        }

        //------------------------------------------------------------------------
        #region Banner callback handlers

        public void HandleAdLoaded(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleAdLoaded() " + " adInstance: " + adInstance.Name +
                " isVisibled: " + adInstance.m_isBannerAdTypeVisibled);
#endif

            adInstance.m_state = AdState.Received;
            BannerView bannerView = adInstance.m_adView as BannerView;
            if (adInstance.m_isBannerAdTypeVisibled)
            {
#if UNITY_EDITOR
                bannerView.Hide();
#endif
                bannerView.Show();
            }
            else
            {
                bannerView.Hide();
            }
            AddEvent(AdType.Banner, AdEvent.Prepare, adInstance);
        }

        public void HandleAdFailedToLoad(AdMobAdInstanceData adInstance, object sender, AdFailedToLoadEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleAdFailedToLoad() " + " adInstance: " + adInstance.Name +
                " message: " + args.Message);
#endif
            DestroyBanner(adInstance);
            AddEvent(AdType.Banner, AdEvent.FailedPreparation, adInstance);
        }

        public void HandleAdOpened(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleAdOpened() " + " adInstance: " + adInstance.Name);
#endif
            AddEvent(AdType.Banner, AdEvent.Show, adInstance);
        }

        void HandleAdClosing(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleAdClosing() " + " adInstance: " + adInstance.Name);
#endif
            AddEvent(AdType.Banner, AdEvent.Hide, adInstance);
        }

        public void HandleAdClosed(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleAdClosed() " + " adInstance: " + adInstance.Name);
#endif
        }

        public void HandleAdLeftApplication(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleAdLeftApplication() " + " adInstance: " + adInstance.Name);
#endif
        }

        #endregion // Banner callback handlers

        //------------------------------------------------------------------------
        #region Interstitial callback handlers

        public void HandleInterstitialLoaded(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleInterstitialLoaded()");
#endif
            adInstance.m_state = AdState.Received;
            AddEvent(AdType.Interstitial, AdEvent.Prepare, adInstance);
        }

        public void HandleInterstitialFailedToLoad(AdMobAdInstanceData adInstance, object sender, AdFailedToLoadEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleInterstitialFailedToLoad() message: " + args.Message);
#endif
            DestroyInterstitial(adInstance);
            AddEvent(AdType.Interstitial, AdEvent.FailedPreparation, adInstance);
        }

        public void HandleInterstitialOpened(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleInterstitialOpened()");
#endif
            AddEvent(AdType.Interstitial, AdEvent.Show, adInstance);
        }

        void HandleInterstitialClosing(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleInterstitialClosing()");
#endif
        }

        public void HandleInterstitialClosed(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleInterstitialClosed()");
#endif
            DestroyInterstitial(adInstance);
            AddEvent(AdType.Interstitial, AdEvent.Hide, adInstance);
        }

        public void HandleInterstitialLeftApplication(AdMobAdInstanceData adInstance, object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            print("AdMobAdapter.HandleInterstitialLeftApplication()");
#endif
        }

        #endregion // Interstitial callback handlers

        //------------------------------------------------------------------------
        #region Reward Video callback handlers

        public void HandleRewardBasedVideoLoaded(object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoLoaded event received");
#endif
            m_rewardInstance.m_state = AdState.Received;
            AddEvent(AdType.Incentivized, AdEvent.Prepare, m_rewardInstance);
        }

        public void HandleRewardBasedVideoFailedToLoad(object sender, AdFailedToLoadEventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoFailedToLoad event received with message: " + args.Message);
#endif
            m_rewardInstance.m_state = AdState.Uncertain;
            AddEvent(AdType.Incentivized, AdEvent.FailedPreparation, m_rewardInstance);
        }

        public void HandleRewardBasedVideoOpened(object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoOpened event received");
#endif
            AddEvent(AdType.Incentivized, AdEvent.Show, m_rewardInstance);
        }

        public void HandleRewardBasedVideoStarted(object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoStarted event received");
#endif
        }

        public void HandleRewardBasedVideoClosed(object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoClosed event received");
#endif
            m_rewardInstance.m_state = AdState.Uncertain;
            AddEvent(AdType.Incentivized, AdEvent.Hide, m_rewardInstance);
        }

        public void HandleRewardBasedVideoRewarded(object sender, Reward args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoRewarded event received for " + args.Amount.ToString() + " " + args.Type);
#endif
            m_lastReward.label = args.Type;
            m_lastReward.amount = args.Amount;
            AddEvent(AdType.Incentivized, AdEvent.IncentivizedComplete, m_rewardInstance);
        }

        public void HandleRewardBasedVideoLeftApplication(object sender, EventArgs args)
        {
#if AD_MEDIATION_DEBUG_MODE
            MonoBehaviour.print("HandleRewardBasedVideoLeftApplication event received");
#endif
        }

        #endregion // Reward Video callback handlers

#endif // _AMS_ADMOB
    }
} // namespace Virterix.AdMediation
