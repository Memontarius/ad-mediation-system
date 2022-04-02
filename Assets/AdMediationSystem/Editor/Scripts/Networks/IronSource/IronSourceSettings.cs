﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace Virterix.AdMediation.Editor
{
    public class IronSourceSettings : BaseAdNetworkSettings
    {
        public List<IronSourceAdapter.OverridePlacement> _overiddenPlacements;

        public override Type NetworkAdapterType => typeof(IronSourceAdapter);
        protected override string AdapterScriptName => "IronSourceAdapter";
        protected override string AdapterDefinePreprocessorKey => "_AMS_IRONSOURCE";
        public override bool IsCommonTimeroutSupported => true;

        public override bool IsAdSupported(AdType adType)
        {
            bool isSupported = adType == AdType.Interstitial || adType == AdType.Incentivized || adType == AdType.Banner;
            return isSupported;
        }
        public override string GetNetworkSDKVersion() => IronSourceAdapter.GetSDKVersion();

        public override bool IsAdInstanceSupported(AdType adType) 
        {
            return adType == AdType.Banner;
        }

        public override bool IsCheckAvailabilityWhenPreparing(AdType adType)
        {
            return adType == AdType.Incentivized;
        }

        public override void SetupNetworkAdapter(AdMediationProjectSettings settings, Component networkAdapter)
        {
            IronSourceAdapter adapter = networkAdapter as IronSourceAdapter;
            adapter.m_timeout = _timeout;
            var overridenPlacements = _overiddenPlacements.ToArray();
            for(int i = 0; i < overridenPlacements.Length; i++)
            {
                overridenPlacements[i].adType = Utils.ConvertEditorAdType((EditorAdType)overridenPlacements[i].adType);
            }
            adapter.m_overriddenPlacements = overridenPlacements;
        }

        protected override AdInstanceParameters CreateBannerSpecificAdInstanceParameters(string projectName, string instanceName, int bannerType, BannerPositionContainer[] bannerPositions)
        {
            var parameterHolder = IronSourceAdInstanceBannerParameters.CreateParameters(projectName, instanceName);
            parameterHolder.m_bannerSize = (IronSourceAdapter.IrnSrcBannerSize)bannerType;
            SetupBannerPositionContainers(parameterHolder, bannerPositions);
            return parameterHolder;
        }

        protected override int ConvertToSpecificBannerPosition(BannerPosition bannerPosition)
        {
            int specificBannerPosition = (int)IronSourceAdapter.IrnSrcBannerPosition.Bottom;
            switch (bannerPosition)
            {
                case BannerPosition.Bottom:
                    specificBannerPosition = (int)IronSourceAdapter.IrnSrcBannerPosition.Bottom;
                    break;
                case BannerPosition.Top:
                    specificBannerPosition = (int)IronSourceAdapter.IrnSrcBannerPosition.Top;
                    break;
            }
            return specificBannerPosition;
        }
    }
}
