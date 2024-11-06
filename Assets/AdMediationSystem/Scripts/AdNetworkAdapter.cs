using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Boomlagoon.JSON;
using System.Linq;

namespace Virterix.AdMediation
{
    public enum AdEvent
    {
        Selected,
        Prepared,
        Showing,
        Clicked,
        Hiding,
        PreparationFailed,
        IncentivizationCompleted,
        IncentivizationUncompleted
    }

    public enum ConsentType
    {
        GDPR,
        CCPA
    }

    public struct IncentivizedReward
    {
        public string label;
        public double amount;
    }

    public class AdNetworkAdapter : MonoBehaviour
    {
        //_______________________________________________________________________________

        #region Classes & Structs

        //-------------------------------------------------------------------------------
        /// <summary>
        /// Describes the parameters of the disabling network from handling when failed load
        /// </summary>
        public struct TimeoutParams
        {
            public AdType m_adType;
            public float m_timeout;

            public TimeoutParams(float timeoutMultiplier)
            {
                TimeoutMultiplier = timeoutMultiplier;
                m_adType = AdType.Unknown;
                m_timeout = 0.0f;
                m_isSetupFailedLoadTime = false;
                m_failedLoadTime = 0.0f;
            }

            public float FailedLoadingTime
            {
                set
                {
                    m_failedLoadTime = value;
                    m_isSetupFailedLoadTime = true;
                }
                get { return m_failedLoadTime; }
            }

            bool m_isSetupFailedLoadTime;
            float m_failedLoadTime;

            public float TimeoutMultiplier { get; set; }

            public bool IsTimeout
            {
                get
                {
                    bool active = false;
                    bool canUsed = m_adType != AdType.Unknown && m_timeout > 0.01f;

                    if (canUsed && m_isSetupFailedLoadTime)
                    {
                        float elapsedTime = Time.realtimeSinceStartup - m_failedLoadTime;
                        float timeoutTime = m_timeout * TimeoutMultiplier;
                        active = elapsedTime < timeoutTime;
                        m_isSetupFailedLoadTime = active;
                    }

                    return active;
                }
            }
        }

        [System.Serializable]
        public struct AdParam
        {
            public AdType m_adType;
            public bool m_isCheckAvailabilityWhenPreparing;
        }

        public struct EventParam
        {
            public AdType m_adType;
            public AdInstance m_adInstance;
            public AdEvent m_adEvent;
        }

        public enum AdState
        {
            Uncertain = 0,
            Loading,
            Received,
            Unavailable
        }

        public enum NetworkState
        {
        }

        #endregion Classes & Structs

        public static string RESPONSE_WAIT_TIME_KEY = "responseWaitTime";
        public event Action<AdNetworkAdapter, AdType, AdEvent, AdInstance> OnEvent = delegate { };

        public string m_networkName;
        public AdParam[] m_adSupportParams;
        public float m_responseWaitTime = 30f;


        //_______________________________________________________________________________

        #region Properties

        //-------------------------------------------------------------------------------

        public IncentivizedReward LastReward => m_lastReward;

        public string CurrBannerPlacement => m_currBannerPlacement;

        public virtual bool UseSingleBannerInstance => false;

        public virtual bool RequiredWaitingInitializationResponse => false;

        public virtual bool WasInitializationResponse { get; protected set; }

        public virtual IAppOpenAdManager AppOpenAdManager
        {
            get
            {
                if (!HasAppOpenAdManager)
                {
                    _appOpenAdManager = CreateAppOpenAdManager();
                }

                return _appOpenAdManager;
            }
            protected set => _appOpenAdManager = value;
        }

        public bool HasAppOpenAdManager => _appOpenAdManager != null;

        private IAppOpenAdManager _appOpenAdManager;


        private string AdInstanceParametersPath
        {
            get
            {
                string path = "";
                if (AdInstanceParametersFolder.Length > 0)
                {
                    path = String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}/{1}/{2}/{3}/",
                        AdMediationSystem.AD_SETTINGS_FOLDER,
                        AdMediationSystem.Instance.ProjectName, AdMediationSystem.AD_INSTANCE_PARAMETERS_ROOT_FOLDER,
                        AdInstanceParametersFolder);
                }

                return path;
            }
        }

        protected virtual string AdInstanceParametersFolder => "";

        #endregion Properties

        private Queue<EventParam> m_events = new();
        protected List<IAdInstanceParameters> m_adInstanceParameters = new();
        protected List<AdInstance> m_adInstances = new();
        protected IncentivizedReward m_lastReward;
        protected string m_currBannerPlacement;
        private static AdInstance s_lastShowingFullscreenAdInstance;
        private static AdNetworkAdapter s_lastShowingFullscreenNetwork;

        public static bool SharedFullscreenAdShowing { get; protected set; }
        public static bool SharedFullscreenAdWillShow { get; protected set; }
        private static float s_waitResponseHandlingInterval;
        private WaitForSeconds _waitResponseIntervalInstruction;
        private readonly WaitForSecondsRealtime _updateEventsIntervalInstruction = new WaitForSecondsRealtime(0.2f);

        protected bool IsApplicationQuiting { get; private set; }

        public bool IsFullscreenAdOpened { get; private set; }
        
        //_______________________________________________________________________________

        #region MonoBehavior Methods

        //-------------------------------------------------------------------------------
        protected virtual void OnEnable()
        {
            StartCoroutine(ProcessUpdateEvents());
        }

        protected virtual void OnDisable()
        {
            StopAllCoroutines();
            UpdateEvents();
        }

        #endregion MonoBehavior Methods

        //_______________________________________________________________________________

        #region Public Methods

        //-------------------------------------------------------------------------------

        protected static int GetBannerPosition(AdInstance adInstance, string placement, int defaultPosition = 0)
        {
            var specificBannerPosition = defaultPosition;
            var adInstanceParams = adInstance.m_adInstanceParams as AdInstanceParameters;
            var positions = adInstanceParams.m_bannerPositions;
            if (positions.Length == 1)
            {
                specificBannerPosition = positions[0].m_bannerPosition;
            }
            else if (positions.Length > 0)
            {
                var positionContainer = positions.FirstOrDefault(p => p.m_placementName == placement);
                if (!string.IsNullOrEmpty(positionContainer.m_placementName))
                {
                    specificBannerPosition = positionContainer.m_bannerPosition;
                }
            }

            return specificBannerPosition;
        }

        /// <summary>
        /// Compare banner positions
        /// </summary>
        /// <returns>Returns true if banner positions are identical, false otherwise</returns>
        public static bool CompareBannerPosition(AdInstance adInstance, string placement, AdInstance otherAdInstance,
            string otherPlacement)
        {
            var onePosition = GetBannerPosition(adInstance, placement);
            var otherPosition = GetBannerPosition(otherAdInstance, otherPlacement);
            return onePosition == otherPosition;
        }

        public void Initialize(Dictionary<string, string> parameters = null, JSONArray adInstances = null)
        {
            if (_waitResponseIntervalInstruction == null)
            {
                s_waitResponseHandlingInterval = 0.5f;
                _waitResponseIntervalInstruction = new WaitForSeconds(s_waitResponseHandlingInterval);
            }

            if (parameters != null)
            {
                InitializeParameters(parameters, adInstances);
            }
#if AD_MEDIATION_DEBUG_MODE
            Debug.Log("[AMS] AdNetworkAdapter.Initialize() Initialize network adapter: " + m_networkName +
                      " adInstances:" + m_adInstances.Count);
#endif
        }

        protected virtual IAppOpenAdManager CreateAppOpenAdManager()
        {
            return null;
        }

        public virtual bool IsReady(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            return false;
        }

        public virtual void Prepare(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
        }

        public virtual bool Show(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
            return false;
        }

        public virtual void Hide(AdInstance adInstance, string placement = AdMediationSystem.PLACEMENT_DEFAULT_NAME)
        {
        }

        public bool IsReady(AdType adType, string adInstanceName = AdInstance.AD_INSTANCE_DEFAULT_NAME)
        {
            AdInstance adInstance = GetAdInstance(adType, adInstanceName);
            bool result = IsReady(adInstance);
            return result;
        }

        public void Prepare(AdType adType, string adInstanceName = AdInstance.AD_INSTANCE_DEFAULT_NAME)
        {
            AdInstance adInstance = GetAdInstance(adType, adInstanceName);
            Prepare(adInstance);
        }

        public bool Show(AdType adType, string adInstanceName = AdInstance.AD_INSTANCE_DEFAULT_NAME)
        {
            AdInstance adInstance = GetAdInstance(adType, adInstanceName);
            bool result = Show(adInstance);
            return result;
        }

        public void Hide(AdType adType, string adInstanceName = AdInstance.AD_INSTANCE_DEFAULT_NAME)
        {
            AdInstance adInstance = GetAdInstance(adType, adInstanceName);
            Hide(adInstance);
        }

        public virtual bool IsSupported(AdType adType)
        {
            AdParam adSupportParam = GetAdParam(adType);
            bool isSupported = adSupportParam.m_adType != AdType.Unknown;
            return isSupported;
        }

        public bool IsCheckAvailabilityWhenPreparing(AdType adType)
        {
            AdParam adSupportParam = GetAdParam(adType);
            return adSupportParam.m_isCheckAvailabilityWhenPreparing;
        }

        protected void AddEvent(AdType adType, AdEvent adEvent, AdInstance adInstance, bool instant = false)
        {
            if (AdMediationSystem.IsAdFullscreen(adType))
            {
                if (adEvent == AdEvent.Showing)
                {
                    SharedFullscreenAdShowing = true;
                    s_lastShowingFullscreenAdInstance = adInstance;
                    s_lastShowingFullscreenNetwork = this;
                }
                else if (adEvent == AdEvent.Hiding)
                {
                    SharedFullscreenAdShowing = false;
                }
            }

            if (instant)
            {
                NotifyEvent(adEvent, adInstance);
            }
            else
            {
                EventParam eventParam = new EventParam();
                eventParam.m_adType = adType;
                eventParam.m_adInstance = adInstance;
                eventParam.m_adEvent = adEvent;
                lock (m_events)
                {
                    m_events.Enqueue(eventParam);
                }
            }
        }

        public static void UpdateLockingHiddenFullscreenState(bool userInteractsWithApp)
        {
            if (SharedFullscreenAdShowing && userInteractsWithApp &&
                s_lastShowingFullscreenAdInstance != null && s_lastShowingFullscreenNetwork != null)
            {
                s_lastShowingFullscreenNetwork.AddEvent(s_lastShowingFullscreenAdInstance.m_adType, AdEvent.Hiding,
                    s_lastShowingFullscreenAdInstance);
            }
        }

        public virtual void NotifyEvent(AdEvent adEvent, AdInstance adInstance)
        {
            if (adInstance == null)
                return;

            switch (adEvent)
            {
                case AdEvent.PreparationFailed:
                    CancelWaitResponseHandling(adInstance);
                    adInstance.RegisterFailedLoading();
                    float timeoutMultiplier = Mathf.Clamp(adInstance.FailedLoadingCount * 0.85f, 1.0f,
                        AdInstance.MAX_TIMEOUT_MULTIPLAYER);
                    adInstance.SaveFailedPreparationTime(timeoutMultiplier);
                    break;
                case AdEvent.Prepared:
                    adInstance.ResetFailedLoading();
                    CancelWaitResponseHandling(adInstance);
                    break;
            }

            OnEvent(this, adInstance.m_adType, adEvent, adInstance);
        }

        public IAdInstanceParameters GetAdInstanceParams(AdType adType, string adInstanceName)
        {
            IAdInstanceParameters foundParams = null;
            foreach (IAdInstanceParameters itemParameters in m_adInstanceParameters)
            {
                if (itemParameters.AdvertiseType == adType && itemParameters.Name == adInstanceName)
                {
                    foundParams = itemParameters;
                }
            }

            return foundParams;
        }

        public void AddAdInstance(AdInstance adInstance)
        {
            m_adInstances.Add(adInstance);
        }

        public AdInstance GetAdInstance(AdType adType, string adInstanceName)
        {
            AdInstance foundData = null;
            foreach (AdInstance data in m_adInstances)
            {
                if (data.m_adType != adType)
                {
                    continue;
                }

                if (data.Name == adInstanceName)
                {
                    foundData = data;
                    break;
                }
            }

            return foundData;
        }

        public AdInstance GetAdInstance(string adInstanceName)
        {
            AdInstance foundData = null;
            foreach (AdInstance data in m_adInstances)
            {
                if (data.Name == adInstanceName)
                {
                    foundData = data;
                    break;
                }
            }

            return foundData;
        }

        public AdInstance GetAdInstanceByAdId(string adId)
        {
            AdInstance foundData = null;
            foreach (AdInstance data in m_adInstances)
            {
                if (data.m_adId == adId)
                {
                    foundData = data;
                    break;
                }
            }

            return foundData;
        }

        public AdType GetAdTypeByAdInstanceId(string adId)
        {
            AdType instanceAdType = AdType.Unknown;
            foreach (AdInstance adInstance in m_adInstances)
            {
                if (adInstance.m_adId == adId)
                {
                    instanceAdType = adInstance.m_adType;
                    break;
                }
            }

            return instanceAdType;
        }

        /// <summary>
        /// GDPR, CCPA and other user privacy of regions compliance
        /// </summary>
        public virtual void SetUserConsentToPersonalizedAds(PersonalisationConsent consent)
        {
        }

        public virtual void SetAgeRestrictedUser(bool ageRestricted)
        {
            
        }
        
        public void StartWaitResponseHandling(AdInstance adInstance)
        {
            if (!adInstance.m_isWaitingResponse)
            {
                CancelWaitResponseHandling(adInstance);
                adInstance.m_isWaitingResponse = true;
                adInstance.m_waitResponseHandler = StartCoroutine(WaitResponse(adInstance));
            }
        }

        public void CancelWaitResponseHandling(AdInstance adInstance)
        {
            if (adInstance.m_waitResponseHandler != null)
            {
                StopCoroutine(adInstance.m_waitResponseHandler);
                adInstance.m_waitResponseHandler = null;
            }
        }

        #endregion Public Methods

        //_______________________________________________________________________________

        #region Internal Methods

        //-------------------------------------------------------------------------------

        private IEnumerator ProcessUpdateEvents()
        {
            while (true)
            {
                yield return _updateEventsIntervalInstruction;
                UpdateEvents();
            }
        }

        private void UpdateEvents()
        {
            if (m_events.Count == 0)
                return;

            while (m_events.Count > 0)
            {
                EventParam eventParam = m_events.Dequeue();
                NotifyEvent(eventParam.m_adEvent, eventParam.m_adInstance);
            }

            m_events.Clear();
        }

        /// <summary>
        /// Initialises ad instance from config file (Default instance initialise manually). When overriding it the base method call required or setup parameters
        /// </summary>
        protected virtual void InitializeAdInstanceData(AdInstance adInstance, JSONValue jsonAdInstance)
        {
            adInstance.Name = jsonAdInstance.Obj.ContainsKey("name")
                ? jsonAdInstance.Obj.GetString("name")
                : AdInstance.AD_INSTANCE_DEFAULT_NAME;
            
            string parametersName = jsonAdInstance.Obj.ContainsKey("param")
                ? jsonAdInstance.Obj.GetString("param")
                : AdInstanceParameters.AD_INSTANCE_PARAMETERS_DEFAULT_NAME;
            
            adInstance.m_adType = AdUtils.StringToAdType(jsonAdInstance.Obj.GetString("adType"));
            adInstance.m_adId = jsonAdInstance.Obj.GetString("id");
            adInstance.m_adInstanceParams = GetAdInstanceParams(adInstance.m_adType, parametersName);
            
            if (adInstance.m_adType == AdType.Banner && adInstance.m_adInstanceParams == null)
            {
                Debug.LogWarning("[AMS] Ad instance banner " + m_networkName +
                                 " parameters in NULL! It needs to be fixed.");
            }

            if (jsonAdInstance.Obj.ContainsKey("timeout"))
            {
                TimeoutParams timeoutParameters = new TimeoutParams(1.0f);
                timeoutParameters.m_timeout = (float)jsonAdInstance.Obj.GetNumber("timeout");
                timeoutParameters.m_adType = adInstance.m_adType;
                adInstance.m_timeout = timeoutParameters;
            }

            if (jsonAdInstance.Obj.ContainsKey("loadOnStart"))
                adInstance.LoadingOnStart = jsonAdInstance.Obj.GetBoolean("loadOnStart");
            m_adInstances.Add(adInstance);
        }

        public static string GetAdInstanceFailedLoadingTimeSaveKey(AdNetworkAdapter network, AdInstance adInstance)
        {
            string saveKey = String.Format("adm.timeout.date.{0}.{1}.{2}", network.m_networkName,
                adInstance.m_adType.ToString(), adInstance.Name);
            saveKey = saveKey.ToLower(System.Globalization.CultureInfo.InvariantCulture);
            return saveKey;
        }

        protected virtual void InitializeParameters(Dictionary<string, string> parameters, JSONArray jsonAdInstances)
        {
            if (parameters.ContainsKey(RESPONSE_WAIT_TIME_KEY))
                m_responseWaitTime = (float)Convert.ToDouble(parameters[RESPONSE_WAIT_TIME_KEY]);
            else
                m_responseWaitTime = AdMediationSystem.Instance.DefaultNetworkResponseWaitTime;

            InitializeAdInstanceParameters();
            if (jsonAdInstances != null)
            {
                foreach (JSONValue jsonAdInstance in jsonAdInstances)
                {
                    AdInstance adInstance = CreateAdInstanceData(jsonAdInstance);
                    InitializeAdInstanceData(adInstance, jsonAdInstance);
                }
            }
        }

        /// <summary>
        /// Should implementation in inheritors (Fabric method)
        /// </summary>
        protected virtual AdInstance CreateAdInstanceData(JSONValue jsonAdInstance)
        {
            return new AdInstance(this);
        }

        private AdParam GetAdParam(AdType adType)
        {
            AdParam adSupportParam = new AdParam();
            foreach (AdParam param in m_adSupportParams)
            {
                if (param.m_adType == adType)
                {
                    adSupportParam = param;
                    break;
                }
            }

            return adSupportParam;
        }

        /// <summary>
        /// Initialises parameters from scriptable objects
        /// </summary>
        private void InitializeAdInstanceParameters()
        {
            if (AdInstanceParametersPath.Length > 0)
            {
                string path = AdInstanceParametersPath;
                UnityEngine.Object[] parameters = Resources.LoadAll(path);
                foreach (UnityEngine.Object itemParameters in parameters)
                {
                    if (itemParameters is IAdInstanceParameters adInstanceParameters)
                    {
                        m_adInstanceParameters.Add(adInstanceParameters);
                    }
                }
            }
        }

        private IEnumerator WaitResponse(AdInstance adInstance)
        {
            float passedTime = 0.0f;
            bool isCheckAvailabilityWhenPreparing = IsCheckAvailabilityWhenPreparing(adInstance.m_adType);

            while (true)
            {
                yield return _waitResponseIntervalInstruction;
                passedTime += s_waitResponseHandlingInterval;

                if (passedTime >= m_responseWaitTime)
                {
                    NotifyEvent(AdEvent.PreparationFailed, adInstance);
                    break;
                }
                else if (isCheckAvailabilityWhenPreparing && passedTime > 2.0f)
                {
                    if (IsReady(adInstance))
                    {
                        NotifyEvent(AdEvent.Prepared, adInstance);
                        break;
                    }
                }
            }

            adInstance.m_isWaitingResponse = false;
            yield return _waitResponseIntervalInstruction;
        }

        protected static bool IsAdBannerInstanceUsedInMediator(AdInstance adInstance, AdMediator mediator) =>
            mediator.IsBannerDisplayed && mediator.CurrentUnit != null && mediator.CurrentUnit.AdInstance == adInstance;

        #endregion Internal Methods
    }
}