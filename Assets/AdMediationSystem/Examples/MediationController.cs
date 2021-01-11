﻿
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using Virterix.AdMediation;
using Virterix.Common;

public class MediationController : MonoBehaviour {

    public Text m_interstitialInfoText;
    public Text m_rewardVideoInfoText;
    public Text m_nativeInfoText;
    public Text m_bannerInfoText;
    public Text m_bannerTopInfoText;
    public Text m_eventLogText;
    public Text m_adInterstitialCountText;
    public Text m_adPersonalizedText;
 
    private int m_adInterstitialCount;
    private PollfishAdapter m_pollfishNetwork;

    // Use this for initialization
    void Awake() {
        m_adPersonalizedText.rectTransform.parent.GetComponent<Button>().interactable = false;
        AdMediationSystem.OnInitializeCompleted += OnMediationSystemInitializeComplete;
        AdMediationSystem.OnAdNetworkEvent += OnAdNetworkEvent;
    }

    private void Start() {
        //AdMediationSystem.Instance.Initialize();
        //AdMediationSystem.Instance.SetPersonalizedAds(true);
    }

    private void OnMediationSystemInitializeComplete() {
        m_adPersonalizedText.rectTransform.parent.GetComponent<Button>().interactable = true;
        UpdatePersonalizedAdsButtonText();

        m_pollfishNetwork = AdMediationSystem.Instance.GetNetwork("pollfish") as PollfishAdapter;
        if (m_pollfishNetwork != null) {
            m_pollfishNetwork.OnEvent += OnAdPollfishNetworkEvent;
        }
    }

    private void FetchAllAds()
    {
        AdMediationSystem.Fetch(AdType.Interstitial);
        AdMediationSystem.Fetch(AdType.Incentivized);
        AdMediationSystem.Fetch(AdType.Banner);
        AdMediationSystem.Fetch(AdType.Banner, "Top");
    }

    public void FetchInterstitial() {
        AdMediationSystem.Fetch(AdType.Interstitial);
        UpdateAdInfo(null, AdType.Interstitial, AdMediationSystem._PLACEMENT_DEFAULT_NAME);
    }

    public void FetchRewardVideo() {
        AdMediationSystem.Fetch(AdType.Incentivized);
        UpdateAdInfo(null, AdType.Incentivized, AdMediationSystem._PLACEMENT_DEFAULT_NAME);
    }

    public void FetchBanner(string placement = AdMediationSystem._PLACEMENT_DEFAULT_NAME) {
        AdMediationSystem.Fetch(AdType.Banner, placement);
        UpdateAdInfo(null, AdType.Banner, placement);
    }

    public void ShowInterstitial() {
        AdMediationSystem.Show(AdType.Interstitial);
    }

    public void ShowRewardVideo() {
        AdMediationSystem.Show(AdType.Incentivized);
    }

    public void ShowBanner(string placement = AdMediationSystem._PLACEMENT_DEFAULT_NAME) {
        AdMediationSystem.Show(AdType.Banner, placement);
    }

    public void HideBanner(string placement = AdMediationSystem._PLACEMENT_DEFAULT_NAME) {
        AdMediationSystem.Hide(AdType.Banner, placement);
    }

    public void SetupPersonalizedAds() {
        AdMediationSystem.Instance.SetPersonalizedAds(true, false);
    }

    public void PrepareSurvey() {
        if (m_pollfishNetwork != null) {
            m_pollfishNetwork.Prepare(AdType.Incentivized);
        }
    }

    public void ShowSurvey() {
        if (m_pollfishNetwork != null) {
            m_pollfishNetwork.Show(AdType.Incentivized);
        }
    }

    public void HideSurvey() {
        if (m_pollfishNetwork != null) {
            m_pollfishNetwork.Hide(AdType.Incentivized);
        }
    }

    public void ToggleChangePersonalizedAds() {
        if (AdMediationSystem.Instance.IsInitialized) {
            if (AdMediationSystem.Instance.IsPersonalizedAds) {
                AdMediationSystem.Instance.SetPersonalizedAds(false);
            }
            else {
                AdMediationSystem.Instance.SetPersonalizedAds(true);
            }
            UpdatePersonalizedAdsButtonText();
        }
    }

    void UpdatePersonalizedAdsButtonText() {
        string buttonText = "";
        if (AdMediationSystem.Instance.IsPersonalizedAds) {
            buttonText = "Change to Non-Personalized Ads";
        }
        else {
            buttonText = "Change to Personalized Ads ";
        }
        m_adPersonalizedText.text = buttonText;
    }

    public void TogglePollfishReleaseMode(Text label) {
        if (m_pollfishNetwork != null) {
            m_pollfishNetwork.m_isDebugMode = !m_pollfishNetwork.m_isDebugMode;
            label.text = m_pollfishNetwork.m_isDebugMode ? "Toggle to Release" : "Toggle to Debug";
        }
    }

    void OnAdPollfishNetworkEvent(AdNetworkAdapter network, AdType adType, AdEvent adEvent, AdInstanceData placementData) {
        OnAdNetworkEvent(null, network, adType, adEvent, placementData == null ? AdMediationSystem._PLACEMENT_DEFAULT_NAME : placementData.Name);
    }

    void OnAdNetworkEvent(AdMediator mediator, AdNetworkAdapter network, AdType adType, AdEvent adEvent, string adInstanceName) {
        if (network.m_networkName != "pollfish") {        
            UpdateAdInfo(mediator, adType, mediator != null ? mediator.m_placementName : mediator.m_placementName);
        }
        
        m_eventLogText.text = adType.ToString() + " Placement:" + mediator.m_placementName + " Instance:" + adInstanceName + " " + network.m_networkName + 
            " " + adEvent.ToString() + "\n" + m_eventLogText.text;

        if (adEvent == AdEvent.Show && network.m_networkName != "pollfish" &&
            (adType == AdType.Interstitial || adType == AdType.Incentivized)) {
            m_adInterstitialCount++;
        }
        m_adInterstitialCountText.text = m_adInterstitialCount.ToString();
    }

    void UpdateAdInfo(AdMediator mediator, AdType adType, string placement) {
        if (mediator == null)
        {
            mediator = AdMediationSystem.Instance.GetMediator(adType, placement);
        }

        Text guiText = null;
        switch(adType) {
            case AdType.Interstitial:
                guiText = m_interstitialInfoText;
                break;
            case AdType.Incentivized:
                guiText = m_rewardVideoInfoText;
                break;
            case AdType.Banner:
                if (placement == AdMediationSystem._PLACEMENT_DEFAULT_NAME) {
                    guiText = m_bannerInfoText;
                }
                else if (placement == "Top") {
                    guiText = m_bannerTopInfoText;
                }
                break;
        }
        
        string networkName = "";
        bool isReady = false;

        if (mediator != null) {
            networkName = mediator.CurrentNetworkName;
            isReady = mediator.IsCurrentNetworkReadyToShow;
        }

 
        if (guiText != null) {
            guiText.text = "network: " + networkName + "\n" + 
                "ready curr ads: " + isReady.ToString() + "\n" +
                "ready mediator: " + mediator?.IsReadyToShow.ToString();
        }
    }

}
