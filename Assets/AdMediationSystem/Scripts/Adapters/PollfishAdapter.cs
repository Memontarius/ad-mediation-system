﻿#define _AMS_POLLFISH

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Boomlagoon.JSON;

namespace Virterix.AdMediation
{
    public class PollfishAdapter : AdNetworkAdapter
    {

        public enum SurveyPosition
        {
            TOP_LEFT = 0,
            BOTTOM_LEFT,
            TOP_RIGHT,
            BOTTOM_RIGHT,
            MIDDLE_LEFT,
            MIDDLE_RIGHT
        }

        public struct SurveyInfo
        {
            public string m_surveyTypeName;
            public int m_costPerAction; // (CPA)
            public int m_ir;
            public int m_loi;
            public string m_surveyClass;
            public string m_rewardName; // Name of reward
            public int m_rewardValue;
        }

        public string m_defaultApiKey = "";
        public bool m_isDebugMode = false;
        public SurveyPosition m_pollfishPosition = SurveyPosition.BOTTOM_RIGHT;
        public bool m_isAutoFetchOnHide = true;
        public bool m_isRestoreBannersOnHideSurvey;
        [Tooltip("Auto prepere survey by time. 0 - disabled.")]
        public int m_autoPrepareIntervalInMinutes = 0;

        private string m_apiKey = "";
        private bool m_surveyOnDevice = false;    // true if survey was received on device
        private bool m_surveyCompleted = false;   // true if survey is completed
        private bool m_surveyRejected = false;    // true if survey got rejected
        private ScreenOrientation m_currentScreenOrientation;
        private Coroutine m_procInitializePollfishWithDelay;
        private AdMediator[] m_bannerMediators;
        private bool[] m_bannerDisplayStates;
        private SurveyInfo m_lastReceivedSurveyInfo;
        private Coroutine m_procAutoPrepereSurvey;
        private AdInstanceData m_adInstance;

#if _AMS_POLLFISH

        private void Awake()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            Pollfish.SetEventObjectPollfish(this.gameObject.name);
#endif
            m_currentScreenOrientation = Screen.orientation;
        }

        private new void Update()
        {
            base.Update();
            if (m_currentScreenOrientation != Screen.orientation)
            {
                m_currentScreenOrientation = Screen.orientation;
                InitializePollfish();
            }
        }

        private void OnEnable()
        {
#if UNITY_ANDROID || UNITY_IOS
            Pollfish.surveyCompletedEvent += surveyCompleted;
            Pollfish.surveyOpenedEvent += surveyOpened;
            Pollfish.surveyClosedEvent += surveyClosed;
            Pollfish.surveyReceivedEvent += surveyReceived;
            Pollfish.surveyNotAvailableEvent += surveyNotAvailable;
            Pollfish.userNotEligibleEvent += userNotEligible;
            Pollfish.userRejectedSurveyEvent += userRejectedSurvey;
#endif
        }

        private new void OnDisable()
        {
            base.OnDisable();

#if UNITY_ANDROID || UNITY_IOS
            Pollfish.surveyCompletedEvent -= surveyCompleted;
            Pollfish.surveyOpenedEvent -= surveyOpened;
            Pollfish.surveyClosedEvent -= surveyClosed;
            Pollfish.surveyReceivedEvent -= surveyReceived;
            Pollfish.surveyNotAvailableEvent -= surveyNotAvailable;
            Pollfish.userNotEligibleEvent -= userNotEligible;
            Pollfish.userRejectedSurveyEvent -= userRejectedSurvey;
#endif

            if (m_procAutoPrepereSurvey != null)
            {
                StopCoroutine(m_procAutoPrepereSurvey);
                m_procAutoPrepereSurvey = null;
            }
        }

        protected override void InitializeParameters(Dictionary<string, string> parameters, JSONArray jsonAdInstances)
        {
            base.InitializeParameters(parameters, jsonAdInstances);

            StopProcInitializePollfishWithDelay();

            string apiKey = m_defaultApiKey;
            if (parameters != null)
            {
                if (!parameters.TryGetValue("apiKey", out apiKey))
                {
                    apiKey = "";
                }
            }

            m_adInstance = AdFactory.CreateAdInstacne(AdType.Incentivized);
            AddAdInstance(m_adInstance);

            m_apiKey = apiKey;
            InitializePollfish();
            InitializeStoreBanners();
        }

        void InitializePollfish()
        {
            ResetStatus();

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            m_adInstance.m_state = AdState.Loading;

            Pollfish.PollfishParams pollfishParams = new Pollfish.PollfishParams();

            bool offerwallMode = false;
            int indPadding = 10;
            bool releaseMode = !m_isDebugMode;
            bool rewardMode = true;
            string requestUUID = SystemInfo.deviceUniqueIdentifier;
            Dictionary<string, string> userAttributes = new Dictionary<string, string>();

            pollfishParams.OfferwallMode(offerwallMode);
            pollfishParams.IndicatorPadding(indPadding);
            pollfishParams.ReleaseMode(releaseMode);
            pollfishParams.RewardMode(rewardMode);
            pollfishParams.IndicatorPosition((int)m_pollfishPosition);
            pollfishParams.RequestUUID(requestUUID);
            pollfishParams.UserAttributes(userAttributes);

            PollfishEventListener.resetStatus();
            Pollfish.PollfishInitFunction(m_apiKey, pollfishParams);
#endif
        }

        private void StartAutoPrepare()
        {
            if (m_autoPrepareIntervalInMinutes > 0 && m_procAutoPrepereSurvey == null)
            {
                m_procAutoPrepereSurvey = StartCoroutine(ProcAutoPrepereSurvey(m_autoPrepareIntervalInMinutes));
            }
        }

        private void StopAutoPrepare()
        {
            if (m_procAutoPrepereSurvey != null)
            {
                StopCoroutine(m_procAutoPrepereSurvey);
                m_procAutoPrepereSurvey = null;
            }
        }

        public override void Prepare(AdInstanceData adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdType adType = m_adInstance.m_adType;
            if (m_adInstance.m_state != AdState.Loading)
            {
                switch (adType)
                {
                    case AdType.Incentivized:
                        InitializePollfish();
                        break;
                }
            }
        }

        public override bool Show(AdInstanceData adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            AdType adType = m_adInstance.m_adType;
            if (IsReady(m_adInstance))
            {
                switch (adType)
                {
                    case AdType.Incentivized:
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                            Pollfish.ShowPollfish();
#endif
                        break;
                }
                return true;
            }
            return false;
        }

        public override void Hide(AdInstanceData adInstance = null)
        {
            AdType adType = m_adInstance.m_adType;
            switch (adType)
            {
                case AdType.Incentivized:
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                        Pollfish.HidePollfish();
#endif
                    break;
            }
        }

        public override bool IsReady(AdInstanceData adInstance = null)
        {
            AdType adType = m_adInstance.m_adType;
            bool isReady = false;
            switch (adType)
            {
                case AdType.Incentivized:
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                        isReady = IsSurveyReceived() && Pollfish.IsPollfishPresent();
#endif
                    break;
            }
            return isReady;
        }

        public SurveyInfo GetLastReceivedSurveyInfo()
        {
            return m_lastReceivedSurveyInfo;
        }

        public bool IsSurveyCompleted()
        {
            return m_surveyCompleted;
        }

        public bool IsSurveyReceived()
        {
            return m_surveyOnDevice;
        }


        public bool IsSurveyRejected()
        {
            return m_surveyRejected;
        }

        public void ResetStatus()
        {
            m_surveyOnDevice = false;
            m_surveyCompleted = false;
            m_surveyRejected = false;
        }

        private IEnumerator InitializePollfishWithDelay(float dalay)
        {
            yield return new WaitForSecondsRealtime(dalay);
            InitializePollfish();
            yield break;
        }

        private void StopProcInitializePollfishWithDelay()
        {
            if (m_procInitializePollfishWithDelay != null)
            {
                StopCoroutine(m_procInitializePollfishWithDelay);
                m_procInitializePollfishWithDelay = null;
            }
        }

        private void InitializeStoreBanners()
        {
            m_bannerMediators = AdMediationSystem.Instance.GetAllMediators(AdType.Banner);
            m_bannerDisplayStates = new bool[m_bannerMediators.Length];
        }

        private void HideBanners()
        {
            int index = 0;
            foreach (AdMediator bannerMediator in m_bannerMediators)
            {
                m_bannerDisplayStates[index++] = bannerMediator.IsBannerDisplayed;
                bannerMediator.Hide();
            }
        }

        private void RestoreBanners()
        {
            int index = 0;
            foreach (AdMediator bannerMediator in m_bannerMediators)
            {
                if (m_bannerDisplayStates[index++])
                {
                    bannerMediator.Show();
                }
            }
        }

        private IEnumerator ProcAutoPrepereSurvey(int periodInMinutes)
        {
            WaitForSecondsRealtime waitInstruction = new WaitForSecondsRealtime(periodInMinutes * 60);
            while (true)
            {
                yield return waitInstruction;
                AdState state = m_adInstance.m_state;
                if (!IsReady(m_adInstance) && state != AdState.Loading)
                {
                    if (m_autoPrepareIntervalInMinutes > 0)
                    {
                        Prepare(m_adInstance);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            m_procAutoPrepereSurvey = null;
            yield break;
        }

        //------------------------------------------------------------------------
            #region Pollfish callback

        private void surveyCompleted(string surveyInfo)
        {
            m_surveyCompleted = true;
            m_surveyOnDevice = false;
            m_surveyRejected = false;
            m_adInstance.m_state = AdState.Uncertain;

            string[] surveyCharacteristics = surveyInfo.Split(',');

#if AD_MEDIATION_DEBUG_MODE
            if (surveyCharacteristics.Length >= 6)
            {
                Debug.Log("PollfishAdapter: Survey was completed - SurveyInfo with CPA: " + surveyCharacteristics[0] + " and IR: " + surveyCharacteristics[1] + " and LOI: " + surveyCharacteristics[2] + " and SurveyClass: " + surveyCharacteristics[3] + " and RewardName: " + surveyCharacteristics[4] + " and RewardValue: " + surveyCharacteristics[5]);
            }
            else
            {
                Debug.Log("PollfishAdapter: Survey Offerwall received");
            }
#endif

            AddEvent(AdType.Incentivized, AdEvent.IncentivizedComplete, null);
        }

        private void surveyOpened()
        {
            AddEvent(AdType.Incentivized, AdEvent.Show, null);

            HideBanners();
        }

        private void surveyClosed()
        {
            AddEvent(AdType.Incentivized, AdEvent.Hide, null);

            if (m_isRestoreBannersOnHideSurvey)
            {
                RestoreBanners();
            }

            if (m_isAutoFetchOnHide)
            {
                AdState state = m_adInstance.m_state;
                if (state == AdState.Uncertain || state == AdState.NotAvailable)
                {
                    StopProcInitializePollfishWithDelay();
                    m_procInitializePollfishWithDelay = StartCoroutine(InitializePollfishWithDelay(3.0f));
                }
            }
        }

        private void surveyReceived(string surveyInfo)
        {
            m_surveyCompleted = false;
            m_surveyOnDevice = true;
            m_surveyRejected = false;
            m_adInstance.m_state = AdState.Received;

            string[] surveyCharacteristics = surveyInfo.Split(',');

#if AD_MEDIATION_DEBUG_MODE
            if (surveyCharacteristics.Length >= 6)
            {
                Debug.Log("PollfishAdapter: Survey was received - : Survey was completed - SurveyInfo with CPA: " + surveyCharacteristics[0] + " and IR: " + surveyCharacteristics[1] + " and LOI: " + surveyCharacteristics[2] + " and SurveyClass: " + surveyCharacteristics[3] + " and RewardName: " + surveyCharacteristics[4] + " and RewardValue: " + surveyCharacteristics[5]);
            }
            else
            {
                Debug.Log("PollfishAdapter: Survey Offerwall received");
            }
#endif

            if (surveyCharacteristics.Length >= 6)
            {
                m_lastReceivedSurveyInfo = new SurveyInfo();
                m_lastReceivedSurveyInfo.m_surveyTypeName = "reward";
                m_lastReceivedSurveyInfo.m_costPerAction = System.Convert.ToInt32(surveyCharacteristics[0]); // (CPA)
                m_lastReceivedSurveyInfo.m_ir = System.Convert.ToInt32(surveyCharacteristics[1]);
                m_lastReceivedSurveyInfo.m_loi = System.Convert.ToInt32(surveyCharacteristics[2]);
                m_lastReceivedSurveyInfo.m_surveyClass = surveyCharacteristics[3];
                m_lastReceivedSurveyInfo.m_rewardName = surveyCharacteristics[4]; // Name of reward
                m_lastReceivedSurveyInfo.m_rewardValue = System.Convert.ToInt32(surveyCharacteristics[5]); // Reward points
            }
            else
            { // Survey Offerwall received
                m_lastReceivedSurveyInfo = new SurveyInfo();
                m_lastReceivedSurveyInfo.m_surveyTypeName = "offerwall";
            }

            AddEvent(AdType.Incentivized, AdEvent.Prepare, null);
        }

        private void surveyNotAvailable()
        {
            ResetStatus();
            m_adInstance.m_state = AdState.NotAvailable;

            AddEvent(AdType.Incentivized, AdEvent.FailedPreparation, null);
            StartAutoPrepare();
        }

        private void userNotEligible()
        {
            ResetStatus();
            m_adInstance.m_state = AdState.NotAvailable;

            AddEvent(AdType.Incentivized, AdEvent.FailedPreparation, null);
        }

        private void userRejectedSurvey()
        {
            m_surveyCompleted = false;
            m_surveyOnDevice = false;
            m_surveyRejected = true;
            m_adInstance.m_state = AdState.NotAvailable;

            AddEvent(AdType.Incentivized, AdEvent.FailedPreparation, null);
        }

        #endregion // Pollfish callback

#endif // _AMS_POLLFISH

    }
} // namespace Virterix.AdMediation

