"""Run focused real-Unity tests in an explicit project; never build or publish an app."""
import argparse
import json
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path

import unity_packages


NAMESPACE = 'VRVlog.LilToonExporter.Tests.'
PROFILES = ('compatibility', 'exporter-behavior', 'exporter-integration')
# Run whole classes, or named methods of suites shared with optional integration.
# Named cases also prevent a removed regression or one lost
# parameter variant from turning a smaller, passing XML into release evidence.
BEHAVIOR_CASES = {
    'ExportDetailsTextTests': {
        'AllDiagnosticFieldsAndTheExactNestedExceptionSurviveLargeReports': 1,
        'NativeClipboardAndUtf8FileContainTheSameCompleteUnicodeText': 1,
        'CancelDoesNotTouchAnExistingFileOrLeaveTemporaryFiles': 1,
        'ConfirmedExistingFileIsReplacedWithCompleteTextWithoutTemporaryFiles': 1,
        'WriteFailureIsReportedAndPreservesOtherFiles': 1,
        'FailedReplacementOfDirectoryCleansOnlyItsOwnTemporaryFile': 1,
        'HeaderFailureTextAndSharedDiagnosticNameThePackageVersion': 1,
    },
    'MissingMeshAttributesTests': {
        'AdditionalVertexStreamsPreserveAuthoredChannelInsteadOfUsingFallback': 2,
        'DegenerateGeometryReceivesFiniteUnitNormalsWithoutLosingMorphs': 1,
        'DirectUniVrmExportRepairsOnlyAbsentAttributesAndKeepsAuthoredMorphs': 4,
        'FailureDuringExportRestoresMissingAttributeSourceMesh': 2,
        'MismatchedAdditionalVertexStreamsAreLeftOutWithAWarning': 1,
        'MissingNormalsWithoutMorphsAlsoExportsThroughMeshWriter': 1,
        'MissingUv0NeverBorrowsASecondUvChannel': 1,
        'NormalExporterPreservesMissingAttributeMeshesThroughRealVrmRoundTrip': 4,
        'SharedStaticMeshRepairsEveryExportedMeshGroupWithoutSplittingSourceIdentity': 1,
        'SharedStaticMeshKeepsEachRenderersAdditionalStreamsInFullBindings': 2,
        'ZeroScaleBranchKeepsChildTransformsWithoutValidTrsAssertions': 4,
    },
    'MorphNormalExportTests': {
        'AuthoredNormalTargetsKeepSourceIdentityAndSubmeshVertexOrder': 2,
        'EveryMorphOfOneMeshKeepsItsOwnAuthoredNormals': 1,
    },
    'AppearanceRecoveryTests': {
        'ActiveOrKeywordEnabledLayersKeepTheOriginalDynamicImageDiagnostic': 3,
        'EmptyMeshDoesNotCreateAFullBindingOrRemoveItsBone': 2,
        'FullExportAcceptsAnInactiveDynamicLayerWithoutEditingSourceAppearance': 1,
        'LiveOrConsumedMaterialAnimationPreventsInactiveTextureOmission': 2,
        'MaterialPropertyBlockPreventsInactiveTextureOmission': 1,
        'NonemptyUnsupportedTopologyIsNotClassifiedAsEmpty': 1,
        'SupportedImageInAnInactiveLayerIsStillStored': 1,
        'UnsupportedImageInAProvenInactiveLayerKeepsEveryMaterialProperty': 4,
        'UnsupportedImagesOutsideTheProvenLayerSetAreNotOmitted': 2,
    },
    'BlinkExportTests': {
        'ACompleteBilateralShapeCanCoverAnIncompleteAlternativePair': 1,
        'ACompleteRendererPairKeepsPriorityOverAlternatePartialFamilies': 1,
        'ACompleteUnifiedPairDoesNotAdoptAnUnrelatedLegacyPartial': 1,
        'ACompleteUnifiedPairIncludesCompatibleOneSidedEyelashes': 2,
        'APartialEyelashPairCannotBeHiddenByAnotherRenderersBlink': 1,
        'AUnifiedPartialKeepsPriorityOverMultipleLegacyPartialFamilies': 1,
        'ActualExportContinuesWithoutBlinkWhenTheClosingShapeIsAmbiguous': 2,
        'ActualExportPreservesAuthoredClipsIncludingExplicitEmpty': 2,
        'ActualUniVrmExportBindsGeneratedTargetsToTheirFinalNodes': 2,
        'AllBlinkRenderersMustSupportTheIndividualPair': 1,
        'AllPartialFamiliesRemainAvailableForTheirMatchingOtherRenderer': 2,
        'AvatarWithoutAnyClosingShapeFallsBackToNoBlink': 1,
        'CompatibleMergedAutomaticBlinkKeepsOneBindingAndTheSourceEndpoint': 2,
        'CompleteUnifiedBlinkPairWinsBeforeAnEarlierLegacySingleSide': 1,
        'ConflictingMergedAutomaticEndpointsLeaveEverySourceBindingUsable': 2,
        'CrossRendererLegacyPairsDoNotHideAnUnmatchedThirdRenderer': 1,
        'CrossRendererLegacySidesCannotMixNameFamilies': 1,
        'DescriptorBlinkUsesOnlyTheClosedSlotAndValidatesMissingIndices': 1,
        'DisjointMergedAutomaticBlinkChannelsKeepBothSidesAndTheirEndpoints': 1,
        'DuplicateSemanticNamesAreNotGuessed': 1,
        'EmptyAuthoredVrmClipIsPreservedUnlessExplicitlyOverridden': 1,
        'ExplicitBindingsRequireExactNamesAndRejectRepeatedChannels': 1,
        'ExplicitManualChoiceResolvesCaseDistinctAliasesAndBakesTheSelectedChannel': 2,
        'ExplicitTrackingMarkerKeepsPartialBlinkValidation': 1,
        'EyeSpacingNeverWinsOverAnEyelidShape': 1,
        'EyelidFamilyAndSdkEyelidPairResolveAutomatically': 1,
        'FailedAutomaticSetupOffersAnEmptyManualRowWithoutGuessing': 1,
        'InertPreferredUnifiedAliasCannotSelectTheMovingLowerAlias': 1,
        'InertUnifiedClosureCannotWaiveMissingBlinkForSharedJaw': 2,
        'InertUnifiedClosureIsNotSelectedAlongsideUsableExplicitJaw': 2,
        'LateEyelidNamesNeverReplaceAnEarlierChoice': 1,
        'LeftAndRightMustBelongToOneNameFamily': 1,
        'LegacyEyesOnDifferentRenderersResolveWithoutTrackingEvidence': 4,
        'ManualAdjustmentCopiesTheResolvedBindingsWithoutChangingTheAutomaticResult': 1,
        'ManualChoiceOverridesInferenceAndRejectsForeignOrRemovedShapes': 1,
        'MergedBlinkRetainsDeformationChecksForEveryCapturedReference': 2,
        'MergedManualBlinkCoalescesOnlyMatchingClosureAmounts': 2,
        'MergingIndependentLeftAndRightBlinkCannotTurnThemIntoOneSharedChannel': 1,
        'MissingMergedBlinkRendererCannotPartiallyPublishEndpointRelocation': 1,
        'NoneProducesAnInertBindingWithoutDestroyingAuthoredMorphs': 1,
        'PairOnlyEyelashesContributeToTheBilateralFallback': 1,
        'PartialClosureIsBakedRelativeToTheAuthoredRestWithoutTouchingSource': 1,
        'PartialUnifiedBlinkWinsOverLegacyPartialBeforeCrossRendererPairing': 1,
        'PartialUnifiedClosureDoesNotInventBilateralBlink': 1,
        'PreviewOwnsItsCopyAndCleansItUpWithoutChangingTheSource': 1,
        'PreviewRendersOpenClosedAndOpenWithinOneUpdateWithoutChangingSource': 1,
        'PreviewUsesTheBilateralPresetAndFallsBackOnlyWhenItIsAbsent': 2,
        'SourceRendererIdentitySurvivesRenameAndSiblingReorder': 1,
        'TheFirstCompatiblePartialFamilyOwnsEachRenderer': 1,
        'UnifiedEyesOnDifferentRenderersFormOneBilateralClosure': 1,
        'UnknownClosingNamesExportWithoutBlinkInsteadOfGuessing': 3,
    },
    'ExportFallbackConfirmationTests': {
        'AMaterialWithAMissingShaderIsFoundOnceAndOtherSlotsAreIgnored': 1,
        'ARealExportReportsBlinkAndMaterialFallbacksOnce': 1,
        'ARealExportWithAMissingShaderSavesAndWarnsOnce': 1,
        'AStageIsNamedWithItsPhase': 1,
        'AnExcludedRendererWithAMissingShaderIsNotCounted': 1,
        'AnExportWithoutFallbacksNeverAsks': 1,
        'BatchModeSavesWithoutTheList': 1,
        'EveryShippedLocaleTranslatesTheResultWords': 1,
        'KeptItemsAreListedOnceWithoutAsking': 1,
        'NonInteractiveSavingKeepsTheEarlierBehaviour': 1,
        'TargetsAreFoundAgainOnTheAvatarByPath': 1,
        'TheListShowsEveryFallbackWithItsTargets': 1,
        'TheAnswerDecidesBetweenSavingAndShowingWhatToChange': 2,
        'TheSavedTitleWarnsOnlyWhenAShaderIsMissing': 1,
        'TwoMaterialsWithTheSameNameCountAsTwo': 1,
    },
    'LegacyBlinkMenuRoundTripTests': {
        'SplitLegacyBlinkAndDistinctNamedMenusKeepTheirAppearanceAfterReimport': 2,
    },
    'NonLilToonExportTests': {
        'AMissingBehaviourIsNeverTreatedAsAnExportComponent': 1,
        'AvatarWhoseMaterialsAreAllMissingNamesTheRenderers': 1,
        'AvatarWithoutLilToonStillProducesASnapshot': 1,
        'NonLilToonMaterialBecomesFlatMToonWithItsColorAndMainImage': 1,
    },
    'PhysBoneSpringExportTests': {
        'AParentTailCannotShareTheChildOwnersFirstJoint': 1,
        'AbsentSdkAndRigidModelsRemainValidWithoutInventedSprings': 1,
        'ActualExportImportsNestedOwnersWithoutSharedJointsOrChangedAppearance': 1,
        'ActualExportImportsSpringsAndMovesHairWithoutChangingOriginal': 3,
        'AllMotionImmobileDragNeverExceedsOne': 1,
        'AllMotionImmobileRaisesStiffnessAndDragWhileWorldIsLeftToTheApp': 2,
        'AuthoredRootColliderSurvivesWithoutAnyPhysBoneOrSdkDependency': 1,
        'BranchesAndVirtualEndpointsHaveOneOwnerAndKeepSource': 3,
        'CurvesCollidersAndLimitsUseRealSdkSerializedValues': 1,
        'PlaneInsideBoundsAndUnknownShapesDoNotStopConversion': 2,
        'EmptyOrTruncatedSerializedSpringsPreventSaving': 1,
        'ExistingTerminalFieldsAndVerificationBaselinesAreProtected': 1,
        'ExistingVrmSettingsWinAndRemovedExplicitRootsDoNotAnimateTheBody': 1,
        'GeneratedEndpointsDoNotTurnAnInertNestedPhysBoneIntoAnotherDriver': 1,
        'IgnoredSubtreesDisabledBonesAndOverlappingOwnersAreExplicit': 1,
        'InactiveExplicitRootsChildrenAndColliderRootsAreSkipped': 1,
        'MalformedPhysBoneDataStillStopsConversion': 3,
        'NestedOwnersKeepRestShapeForcesAndCollidersRegardlessOfComponentTraversal': 2,
        'PreexistingVrmImplicitJointsAndTerminalsWinWhileDisjointSegmentsSurvive': 1,
        'PreparedCopyWithoutRequiredHumanBonesOrRootAnimatorStopsAndNamesThem': 1,
        'SameRootPrefersAttachedComponentThenStableComponentOrder': 2,
        'ZeroLengthPairsKeepBonesAndOriginalDepthForTheRemainingChain': 1,
    },
    'VrmLookAtExportTests': {
        'EyesWithoutEyeLookUseTheEyeMidpointAndTheDefaultRange': 1,
        'ExistingVrmLookAtWinsOverEyesAndEyeLook': 1,
        'RotatedScaledHeadSwappedSidesAndLinkedPosesMatchUniVrm': 1,
        'UnmeasurableEyeLookKeepsTheDefaultRangeAndWarns': 1,
        'UntouchedVrmLookAtDefaultsAreReplacedByTheEyeMidpoint': 1,
        'WithoutEyeBonesTheExportStillHasNoLookAt': 1,
    },
    'SourceFingerprintCacheTests': {
        'ColdClipEditorCacheDoesNotInvalidateButNativeCurveEditsDo': 1,
        'MaterialDefaultCachePopulationDoesNotInvalidateButAnEffectiveEditDoes': 2,
        'UnknownSavedMaterialPropertiesAndTextureTransformsStillInvalidate': 1,
    },
    'AdditionalPlayableCallbackTests': {
        'DormantSdkFxWeightCommandsPermitNeutralAndFixedSelectionWithoutSourceMutation': 4,
        'DormantNestedFxWeightCommandsAndTheirExitRestoresArePruned': 2,
        'ReachableSdkFxWeightCommandsRemainUnsupported': 7,
        'ReachableInstantAndTimedSdkCommandsAreNotExecutedOrIgnored': 2,
        'EveryRawParameterWriterPreventsDormantCommandProof': 5,
        'ConflictingThirdPlayableParameterTypeCannotCertifyAConstant': 2,
        'ExplicitChangedNormalInputCannotPruneAnAdditionalCommand': 1,
        'LegacySamplingWithoutNormalInputContextRetainsDormantCommands': 1,
        'GenuineUnknownCallbacksInvalidateTheWholeAdditionalProof': 2,
        'AnimatorLayerMetadataDistinguishesValidBodyTargetsFromMalformedCommands': 6,
        'ReachableAdditionalMorphWriterKeepsPreparedNeutralDespiteKnownWeightCommand': 1,
        'ValidAdditionalRandomCanRetainPreparedRestForAKnownWeightCapability': 1,
        'AdditionalWeightCapabilityCannotHideALaterMalformedNeededDriver': 5,
        'UnprovedTypedGateInputsDoNotHideCommands': 2,
    },
    'AdditionalPlayableMorphTests': {
        'ImplicitDefaultClosureRetainsPreparedComponentAndPreservesProvenIndependentTop': 1,
        'KnownAdditionalMorphKeepsCoupledPreparedRestAndSamplesIndependentNeutral': 3,
        'AdditionalMorphCannotBeSilentlyDroppedFromFixedExpressionEndpoints': 1,
        'LaterOverlapDiscardsEarlierSamplesForTheCompleteParameterDependencyComponent': 1,
        'AppearanceOwnedAdditionalMorphStillReportsThePreparedRestCapability': 1,
        'AdditionalMorphCannotHideContradictoryEvaluatedFxLayerCommands': 1,
        'MorphCapabilityCannotHideHardFailuresInALaterController': 6,
        'LocomotionMouthOverlapExportsPreparedRestAndAuthoredAbsoluteEndpoint': 1,
    },
    'AdditionalParameterConstantTests': {
        'IdempotentAdditionalResetsKeepTheOriginalNativeNeutralAndSelection': 3,
        'AResetCannotCertifyAChangingOrAmbiguousProducer': 20,
        'SelectedDifferentInputCannotBeErasedByAnAdditionalReset': 1,
        'AnIdempotentResetCannotConcealAnAdditionalFaceWriter': 1,
        'SuppliedInitialValuesAlsoRequireAnUnchangedReset': 2,
        'AnIdempotentResetDoesNotAuthorizeALaterMalformedNeededOperation': 3,
        'OnlyAnIndependentUnchangedInputCanPruneAConflictingProducer': 1,
        'IdempotenceUsesTheValidatedSdkTypedAndExpressionParameterValue': 3,
        'SoleDormantResetPreservesTheNativeNeutralAndSelectedExpression': 1,
        'DormantResetCannotBeAssumedClosedThroughACandidateOrChangingGate': 5,
        'DormantResetCannotExcuseAnIndependentReachableAdditionalEffect': 3,
        'SavedOrUnknownCandidateMetadataCannotBeBypassedByAutomaticContext': 4,
        'SavedOrUnknownGateMetadataCannotBeBypassedByAutomaticContext': 2,
        'NetworkedResetsRequireAnUnchangedTypedLocalPreview': 9,
        'SerializedUnsavedSdkInputsAreReadBeforeTheListIsRead': 1,
        'DormantAnimatorCurvesKeepTheirNativeWriteDefaultsContribution': 4,
        'AnotherControllersImplicitParameterResetCannotCertifyAnAdditionalSdkSet': 2,
        'WriteDefaultsWithoutAnAnimatorParameterBindingDoesNotInventAReset': 1,
    },
    'TemporalNeutralShapeTests': {
        'TemporalRestKeepsPreparedWeightAndReconstructsTheIndependentConstant': 3,
        'MixedIdleKeepsAllSeventeenTemporalAndSamplesAllOneHundredFifteenStaticChannels': 1,
        'InactiveAlternateTemporalClipDoesNotChangeTheActiveConstantCapture': 1,
        'TemporalCaptureKeepsIndependentNativeOverrideAdditiveAndWriteDefaults': 4,
        'OnlyAFullUnmaskedExplicitConstantOverrideDominatesTheLowerTemporalCurve': 6,
        'UnresolvedTemporalGraphKeepsPreparedRestButStillRejectsUnsupportedDrivers': 5,
        'DirectFixedExpressionSamplingStillRejectsItsActualTemporalCurve': 1,
        'TemporalRequiredEndpointExportsPreparedRestAndTheExactAuthoredProgram': 2,
    },
    'NeutralShapeSamplerTests': {
        'UnknownComponentPropertyPreservesPreparedNeutralAndReportsTarget': 1,
        'ALoopingMuscleCurveInTheRestFaceKeepsThePreparedRest': 1,
        'KnownConstraintSupportKeepsNativeWriteDefaultsAndIndependentMorphRest': 2,
        'ConstraintCoupledMorphKeepsPreparedWeightWhileIndependentFaceStillSamples': 2,
        'ConstraintEnabledSupportKeepsPreparedRestWithoutExecutingConstraint': 2,
        'DynamicsEnabledSupportIsHarmless': 4,
        'ConstraintCoupledExplicitEndpointKeepsPreparedNeutralForLaterEndpointBaking': 1,
        'UnknownPropertyFallbackCannotHideLaterActiveNaNOrAnimationEvent': 2,
        'AppearanceOnlyPreparedRestRetainsKnownFxWeightButRejectsUnknownPlayable': 2,
        'FractionalOrMaskedTopOverrideCannotProveIndependenceFromUnmeasuredLowerInputs': 2,
        'FutureNonMorphChangeBelowTopOverrideRetainsPreparedNeutral': 3,
        'FutureInvalidParameterDriverBelowTopOverrideStillFailsDataChecks': 1,
        'UnmeasuredBuiltinInputKeepsPreparedNeutralInsteadOfChoosingAFace': 1,
        'UnresolvedGroupPreservesAuthoredWeightsWithoutDiscardingIndependentRest': 2,
        'RecoverableExternalInputRetainsKnownFxWeightAndOtherMorphButRejectsUnknownCallbacks': 3,
        'DelayedGenericMorphAnimationKeepsPreparedRestInsteadOfASampledPhase': 1,
        'AutomaticAnimationSharingTheOpeningChannelKeepsItsPreparedRest': 1,
        'AutomaticBlinkWithWriteDefaultsKeepsTheFullNativeClosureAndConstantRest': 1,
        'CurrentCapHairAndMaskStayTogetherWhileIndependentFaceAndPermanentMorphReconstruct': 2,
        'SharedCustomAppearanceControlKeepsSeparateVisibilityAndMaskTogetherWithoutClaimingGenericResetFace': 6,
        'AppearanceOwnershipPrunesOnlyProvedNormalExternalBranches': 7,
        'OptionalUnboundOrUnexportedMorphCannotPoisonIndependentFaceReconstruction': 7,
        'AppearanceSupportThatDisablesTheRendererKeepsPreparedNeutralAndActivation': 2,
        'IndependentTransformSupportCapturesNativeMorphScalarsAndKeepsPreparedPose': 3,
        'IndependentTransformSupportKeepsNativeScalarAcrossOverlappingMorphFrames': 3,
        'IndependentTransformOnSixthSkinInfluenceKeepsNativeScalarAndPreparedBone': 1,
        'IndependentTransformSupportStillRejectsUnknownPropertyComponents': 2,
        'MissingTypedRendererComponentCannotClaimOrBlockARequiredMorph': 2,
        'DisjointAdditiveVisibilityOrMaterialSupportKeepsNativeMorphMathAndPreparedAppearance': 2,
        'ExplicitEndpointRequirementKeepsItsCoupledPreparedNeutral': 1,
        'NeutralUsesTheSameNormalVrChatInputsAsMenuSampling': 2,
        'NeutralContactUsesTheAuthoredDefaultWithoutReceivingLiveInput': 2,
        'DormantAfkActionCannotChangeTheNormalNeutralFace': 2,
        'UnsavedNonMenuSignalWithOnlyDormantOtherPlayableWritersDoesNotOwnTheNormalFace': 2,
        'SavedExposedUnprovedOrWrittenCustomSignalRetainsItsAppearanceAlternatives': 17,
        'TransientOwnershipProofRetainsUnknownMetadataAndExplicitOrRemoteInputs': 5,
        'CompleteMenuInputInventoryHandlesSharedCyclicAndEmptyOptionalSdkInputs': 3,
        'AmbiguousOptionalMorphTargetIsRejectedBeforeIgnoringItsMissingShape': 1,
        'AppearanceCoupledStartupKeepsItsEntirePreparedConfiguration': 2,
        'AuthoredEndpointObligationUsesItsFirstBindingAndRetainsDisabledZeroCoverage': 2,
        'AutomaticChannelTimedInitializerStillResetsItsStaticClosingWeight': 1,
        'CommonDefaultFaceCanResetBlinkWhileIndependentBlinkStaysLive': 1,
        'ConstantStartupWritesIncludeZeroAndLeaveUnwrittenChannelsAlone': 1,
        'DeclaredTrackingProfileRetainsCoupledPreparedNeutralWithWarning': 1,
        'DelayedAppearanceConfigurationKeepsItsPreparedMorphAndActivationTogether': 1,
        'DormantActionDoesNotHideAnUnknownBehaviour': 1,
        'ExcludedRendererDoesNotContributeANeutralPose': 1,
        'FixedContactCannotHideAnotherPlayableLayerWriter': 1,
        'FixedContactIsOnlyAnInitialValueAndDoesNotSuppressAuthoredDriverWrites': 1,
        'FractionalIntSetUsesDriverTruncationAndCannotPruneItsZeroEndpoint': 1,
        'FxDriverWritingTheFixedContactCannotHideAnActionBranch': 1,
        'GeneratedFaceEmoStyleNestedFxSettlesWithQuietVoiceInEitherWaitMode': 2,
        'IndependentAutomaticBlinkAndVisemeDoNotBecomeTheRestFace': 1,
        'KnownNeutralGestureZeroPrunesAnUnreachableAnimatedExpression': 1,
        'MissingAlternateHairLayoutCannotBindOrClaimTheAccompanyingMorph': 1,
        'NestedExitReachesItsParentDestinationAndPrunesFalseAncestorAnyState': 1,
        'NeutralAcceptsAnAbsentConstantDummyActivationTarget': 1,
        'NeutralAcceptsConstantActivationThatPreservesThePreparedObject': 2,
        'NeutralRejectsAnAmbiguousActivationPath': 1,
        'OverrideClipAndWriteDefaultsUseThePreparedFxPose': 1,
        'QuietVoiceContextChoosesTheNeutralOpeningPose': 1,
        'QuietVoicePrunesAncestorAnyStateWhileKeepingTheNestedNeutralState': 1,
        'ReachableActionMorphKeepsPreparedRestButParameterWriterIsStillRejected': 2,
        'RelevantRandomDriverCannotInventANeutralFace': 3,
        'StartupParameterDriverReachesTheMorphLayer': 1,
        'SuppliedBuiltinCanBeCopiedByTheNeutralDriver': 1,
        'WriteDefaultsOffInitializerRetainsItsStartupPoseAfterLeavingTheClip': 1,
    },
    'NeutralLayerControlTests': {
        'IndividualLayerControlKeepsPreparedRestWithoutAssumingItsGoalIsAlreadyApplied': 4,
        'FxBaseLayerIndexZeroIsValidMetadataWithoutInventingItsRuntimeEffect': 1,
        'AControlOfTheTopLayerOrWholeFxCannotCertifyIndependentNeutral': 2,
        'NonFxIndividualLayerControlsDoNotBlockNeutralMorphs': 3,
        'NormalTypedInputsCanProveAValidLayerControlDormant': 3,
        'AReachableAdditionalPlayableFxCommandKeepsPreparedNeutralWithoutRunningCallbacks': 2,
        'MalformedSdkLayerControlDataRemainsHardEvenOnADormantBranch': 8,
        'KnownLayerControlFallbackCannotHideUnknownCallbacksOrBadAnimationData': 3,
        'AdditionalFxControlCannotHideReachableBadDataEvenWhenEveryNeutralIsRetained': 3,
        'ProvenDormantAdditionalBadDataDoesNotBlockPreparedNeutralFallback': 2,
        'InvalidFxDriverReadOnlyByAnotherPlayableRemainsHardBeforeLayerControlFallback': 1,
        'DirectClipEarlyReturnsStillValidateAdditionalFxEffects': 4,
        'ConstantSignalRelayUsesNativeNeutralWithoutNameExceptions': 2,
        'ConstantRelayExecutesItsNativeStartupCommandsWithUniformWriteDefaults': 2,
        'AConstantRelayCannotCertifyDynamicOrCompetingFutureControls': 7,
        'EveryReachableInstantSetToTheDefaultLayerWeightKeepsNativeNeutral': 2,
        'AnIdempotentWeightSetCannotCertifyACompetingOrBlendedProducer': 4,
        'MmdControlledNativeRestAndAuthoredEndpointSurviveFullExportAndVrmRoundTrip': 1,
        'ConstantRelayReproducesASteadySdkWeightDifferentFromItsAuthoredDefault': 2,
        'FiniteTimedRelayCanRemainInItsNativeConditionalInitialState': 2,
        'FiniteTimedRelayUsesItsNativeStartupAndSettledCommands': 2,
        'FiniteTimedRelayKeepsItsOriginalNativeAuthoredLoopDuringPermanentComposition': 2,
    },
    'SelectedLayerControlTests': {
        'DirectClipControlsPreserveNativeSlotAndStandaloneIndexMapping': 5,
        'InactiveDirectClipDistinguishesDormantEnableFromReachedExplicitDisable': 2,
        'StandaloneClipKeepsOriginalSdkBaseLayerAtUnitWeightAfterIndexRemap': 1,
        'DirectBaseClipWithoutStateProvenanceCannotDiscardItsLayerControl': 1,
        'InactiveSelectedStateKeepsItsOwnWeightButExecutesAControlOfAnotherLayer': 1,
        'SelectedCallbackRetainsItsUpperNonEquivalentSelectorTarget': 2,
    },
    'SelectedExpressionAppearanceTests': {
        'UnchangedPreparedAppearanceKeepsOnlyAuthoredMorphAnimation': 7,
        'ARealAppearanceChangeStillRejectsTheWholeSelectedClip': 4,
        'AChangingAppearanceCurveIsNotCertifiedFromItsFirstKey': 2,
        'PropertyBlockPreventsMaterialNoOpProofEvenWhenStoredMaterialMatches': 2,
        'EverySharedMaterialSlotMustHaveTheSameDeclaredValue': 3,
        'NoOpCandidateCannotConcealCorruptOrUnsupportedClipData': 4,
        'AmbiguousPathsRemainHardEvenWhenBothTargetsHaveMatchingValues': 1,
        'ExistingUnknownPropertiesAreNotAuthorizedByCoincidentValues': 6,
        'DynamicsEnabledCurveDoesNotRejectTheGesturePose': 1,
        'UnusedEndpointTangentsDoNotInvalidateAnOtherwiseConstantNoOp': 1,
        'MatchingVectorChannelsKeepTheirActualNativeValue': 1,
        'SharedFacialSetDoesNotClaimMouthMorphsForUnchangedEyeVectorResets': 2,
        'NoOpProofUsesThePreparedCopyRatherThanAnEarlierAuthoringValue': 2,
        'MixedLilToonGesturePreservesPreparedAppearanceAndMorphEndpointsAfterExport': 2,
        'MatchingShaderColorChannelsKeepTheirNativeAppliedValue': 2,
        'MaterialColorAnimationMatchesActualRenderedPixels': 4,
        'ColorCanonicalizationDoesNotRelaxOtherPropertyDomains': 4,
    },
    'VrChatMenuExpressionTests': {
        'CanonicalAnimationKnotsReuseAuthoredEndpointsAcrossCurveRanges': 2,
        'CanonicalAnimationKnotsPreserveSourceFrameGeometryAndRangeEdges': 6,
        'CanonicalAnimationKnotsRetainIndependentChannelsInsideOneFace': 1,
        'ExactZeroScalarMarkersKeepAllExpressionSettingsAndNativeCoactivation': 2,
        'OnlyAnExactlyZeroScalarPntCanReuseAMarker': 3,
        'ZeroScalarMarkersRemainUniqueToTheActualRenderer': 1,
        'ExactZeroScalarReuseRespectsTheOriginalMemoryBudget': 2,
        'ReachedConstantMorphTailsKeepTheNativePoseBeforeTheClipEnd': 2,
        'AReachedMorphTailCannotHideAFutureCurveOrCallback': 6,
        'ReachedNonLoopingMorphTerminalsKeepTheOriginalNativeHeldPose': 2,
        'TerminalMorphProofCannotReplaceAnUnprovenNativeClockOrFutureState': 7,
        'SelectedNativeUpperWritersDominateOnlyTheirExactLowerMorphStream': 5,
        'MutuallyExclusiveAnimationsReuseOnlyExactStoredPnt': 4,
        'AnimationProgramsKeepUniqueBasisNamesWithinEachFace': 2,
        'EqualScalarPosesRetainIndependentSimultaneousTargets': 1,
        'AnimationGeometryCannotShareTargetsAcrossRenderers': 1,
        'ExactAnimationBasisReuseRespectsTheOriginalMemoryBudget': 2,
        'UnsupportedVisibilityIsReportedInsteadOfDroppingHalfAnExpression': 1,
        'AnimatedExpressionDataContracts': 1,
        'BlendTreeUsesTheMenuValueInsteadOfPublishingItsIndividualShapes': 1,
        'DelayedExitIsFollowedAndReported': 1,
        'DelayedNonLoopingStepIsSavedAtItsLastKeyAndReported': 1,
        'DuplicateHierarchyPathsCannotCaptureTheWrongFace': 1,
        'GlbRegistrationKeepsComposedExpressions': 1,
        'MenuSelectionCapturesACombinationAndLeavesSourceUntouched': 1,
        'MenuTraversalKeepsLabelsAncestorGatesAndReportsPuppets': 1,
        'PortableCurveMatchesUnityWeightedAndSteppedAnimation': 1,
        'GestureClipIgnoresHumanoidPoseButRejectsParameterCurves': 1,
        'HumanoidChannelsComeFromUnityVocabulary': 1,
        'WriteDefaultsOffRetainsTheWholeEvaluatedFace': 1,
        'RepeatedFxParameterDeclarationsKeepMenuExpressions': 1,
        'ConflictingFxParameterDeclarationsAreLeftForStrictChecks': 1,
    },
    'ParameterDriverExpressionTests': {
        'AnotherPlayableWriterInvalidatesFalseAndGuardProof': 1,
        'AddRunsOncePerEntryIncludingSelfTransitions': 2,
        'AnimatorCurveWriterInvalidatesFalseAndGuardProof': 1,
        'AnotherLayerWritingTheSameMorphMustPassStabilityChecks': 1,
        'AnotherPlayableWritingAnFxGateRemainsUnsupported': 1,
        'BodyControlDoesNotMakeAnExcludedOnlyLayerLookLikeAFace': 1,
        'BodyPlayableControlAndTrackedHandDoNotBlockFixedFacialSampler': 3,
        'ChangingParameterCurveCannotDisableFxAfterTheSampleWindow': 1,
        'ConflictingReachedFxCommandsCannotDependOnCallbackOrder': 1,
        'DelayedFxDisableOutsideTheSampleWindowRemainsUnsupported': 1,
        'DriverOnExcludedClothingLayerStillReachesTheFace': 1,
        'DriverOperationsUseAuthoredOrderAndCopyConversion': 1,
        'DriverWrittenFalseAndGuardIsNotAssumedInvariant': 1,
        'ExplicitNeutralFxGateAllowsUnreachedDisableWithoutEditingSources': 1,
        'ExternalInputsAndOtherPlayableWritersAreNotAssumedConstant': 1,
        'FxCommandsOnStateMachinesAreNotSilentlySkippedByStateCallbacks': 1,
        'FxGateExternalInputIsNotAssumedConstant': 1,
        'FxOffSelectionIsRejectedAndLaterSamplesStartFromFreshDefaults': 1,
        'ImmediateFxDisableAndRestoreAfterSelectionCannotDisappearBetweenCallbacks': 1,
        'LocalOnlyUsesTheEvaluatedInstance': 2,
        'LocomotionControlDoesNotBlockFixedFacialSampling': 2,
        'LocomotionControlDoesNotHideOtherPlayableParameterWrites': 1,
        'MalformedBodyPlayableControlIsNotIgnored': 7,
        'MalformedTemporaryPoseSpaceIsNotIgnored': 2,
        'MenuDriverCanReachAndDisableAnOtherwiseUnrelatedFxGate': 1,
        'MenuDriverChainReachesAnotherLayerWithoutEditingAnySourceAsset': 1,
        'MissingMenuMorphsAreDeferredUntilPreparedMeshIsKnown': 2,
        'NestedStateMachineEntryAndExitRetainNativeDriverEvents': 1,
        'NonUnitFxCommandCannotBeCancelledByAnotherCommandOnTheSameState': 1,
        'OtherPlayableControlUsesTheSameFxBoundary': 2,
        'RelevantRandomReportsStateAndOperationInsteadOfInventingAFace': 1,
        'SelectedTrueAndGuardCannotAssumeStationInputFalse': 1,
        'ShortIntermediateDriverAndResetAreObservedWithoutHoldingMenuParameter': 1,
        'SupportedNumbersClampOnlyExpressionParametersAndFloorCopiedIntegers': 1,
        'TemporalFxBlendRemainsUnsupportedEvenAtFullGoalWeight': 2,
        'TemporaryPoseSpaceDoesNotBlockFixedFacialSampling': 2,
        'TransientFxDisableDuringDefaultsIsNotResetIntoAnInventedFace': 1,
        'TrueDefaultAndGuardCannotAssumeStationInputFalse': 1,
        'UnguardedReachableStationBranchIsNotHiddenByAnotherFalseAndGuard': 1,
        'UninitializedFxGateDoesNotAssumeFullWeight': 2,
        'UnknownBehaviourIsRejectedWithItsLocation': 1,
        'UnknownPlayableTargetIsNotIgnored': 1,
        'UnrelatedRandomDriverDoesNotBlockTheMenu': 1,
        'UnwrittenFalseAndGuardProvesNeutralFxIndependentOfStationInput': 2,
        'WriteDefaultsCommandLayerDoesNotQualifyForFalseAndGuardProof': 1,
    },
    'ExpressionSettlementTests': {
        'NativeClocksMatchTheSettlementConversion': 2,
        'ANonLoopingClipLongerThanTheWarmUpIsSavedAtItsLastKey': 2,
        'AStateReachedByALateExitTimeIsFollowed': 3,
        'ALoopingClipIsSavedAtTheFirstFrameOfItsFinalState': 1,
        'CyclicExitTimesStopAtTheFirstRevisitedState': 1,
        'AHeldExitTimeIsReportedSeparatelyAndStillChecked': 1,
        'AMovingClipOnAStoppedOrReversedClockIsNotSaved': 2,
        'AnInterruptibleTransitionIsSteppedFrameByFrame': 1,
        'ASelfTransitionIsFollowedOnceAndReported': 2,
        'AMovingClipWithASelfTransitionIsNotSaved': 1,
        'ASelfTransitionWithADurationFinishesBeforeSaving': 2,
        'TheHopLimitIsCountedPerLayer': 1,
        'TheStepLimitGrowsWithTheNumberOfLayers': 1,
        'AnAnyStateEdgeThatCannotReachItsOwnStateIsNotFollowed': 1,
        'AZeroDurationSelfTransitionOfAJustEnteredStateIsReported': 1,
        'ARestartingLayerWithoutFaceCurvesDoesNotStopTheFinalKey': 2,
        'AStateRestartedEveryFrameByAnAnyStateEdgeHoldsTheFace': 2,
        'AStateRestartedEveryFrameWithADriverIsNotSaved': 3,
        'AStateRestartedEveryFrameWithCallbacksThatChangeNothingHoldsTheFace': 1,
        'AStateRestartedEveryFrameWithAMovingClipIsNotSaved': 2,
        'AFadeInBesideARestartingLayerReachesItsFinalKey': 2,
        'LoopsOfDifferentLengthsAreReportedAsOverlapping': 1,
        'AChainLongerThanTheHopLimitIsNotSaved': 1,
        'AFaceAfterMoreThanAMinuteIsReported': 2,
        'SettleDoesNotStepAStationaryFace': 1,
        'AFaceThatSettlesInsideTheWarmUpKeepsItsValueWithoutMessages': 1,
        'AnAddDriverOnAnExitTimeTargetRunsOnce': 1,
        'AMotionTimeStateIsNotStepped': 1,
        'ALoopingHandMuscleCurveBesideTheFaceIsSettled': 2,
        'TheExperimentalPlayerKeepsTheLastKeyOfALongClip': 1,
        'TheExperimentalPlayerFollowsALateExitTime': 1,
    },
    'MergedFxDefaultsTests': {
        'DirectClipProbeAppliesOriginalAnimatorLayerControlUsingNativeWeights': 4,
        'DirectGestureProbeUsesNormalAndAuthoredContactInputs': 5,
        'DirectGestureProbeStillRejectsUnresolvedExternalInputs': 2,
        'DirectScalarSupportKeepsPreparedInfluencingBoneAndWardrobeMorphs': 2,
        'DirectScalarSupportCannotDisableTheCapturedRenderer': 1,
        'DirectScalarProbeDefersUnknownAuthoredChannelsToPreparedResolution': 4,
        'ProvenBaseStateRetainsItsCallbackAndTheDynamicUpperNativeGraph': 1,
        'InactiveProvenStateKeepsAuthoredClipUnlessItsCallbacksAffectRetainedGraph': 3,
        'ProvenMovingDirectClipKeepsNativeSupportWithoutAnUnrelatedUpperExpressionReset': 4,
        'ProvenMovingDirectClipStillHonorsAProvenPermanentUpperOverride': 2,
        'ProvenMovingDirectClipRetainsTheConfiguredSingleStateBlendTreeSupport': 1,
        'SourceFrameRangePolicyPreservesNativeClampedGeometryAndUnlimitedAuthoredMath': 2,
        'FractionalStationaryFxGeometrySurvivesOneClickExportAndVrmReimport': 2,
        'NeutralAdditiveDisjointMorphGroupsMatchTheAuthoredNativeController': 4,
        'NeutralAdditiveAutomaticSupportKeepsNativeDefaultsWithoutBakingMovingBlink': 1,
        'InstalledMaReparentsFaceEmoRendererAndMergesPermanentPupilFxBeforeEvaluation': 1,
        'AdditiveDirectClipIsRejectedWithoutGuessingItsReferencePose': 1,
        'ConstantIndependentEffectAppliesToNeutralAndSelectedExpression': 2,
        'ConstantRelayProofDoesNotAcceptAnSdkBehaviourEvenWhenClipsMatch': 1,
        'ConstantRelayProofRejectsOutputOrCallbackDifferences': 7,
        'DirectAnimatedClipRetainsNativeLayerWeightAndPriority': 1,
        'DirectClipUsesLayerPriorityWithWriteDefaults': 4,
        'EquivalentConstantRelayPreservesNativeNeutralAndMenuEvenDuringTransitions': 4,
        'ExcludedPermanentTargetDoesNotBecomeANeutralSamplingRoot': 1,
        'FractionalGestureWithoutPermanentMorphsKeepsEquivalentParameterOrEmptyBaseLayer': 2,
        'FractionalStationaryFxUsesPreparedBaseOnceForMenuGestureAndFaceEmo': 4,
        'InstalledMaConstantParameterRelayKeepsMergedNeutralAndMenuPupilRemoval': 1,
        'NativeSupportBaseRejectsUnsafePlayedClipAfterItsTransitionCompletes': 4,
        'NativeSupportBaseRejectsUnsafeZeroDurationInitialOrIntermediateMotion': 4,
        'NativeSupportBaseValidatesNestedAndBlendTreeMotionsConservatively': 2,
        'NeutralCommittedRootsRetainNativeClosureAndCheckOtherPlayableEffects': 2,
        'NeutralFractionalBaseActivityRetainsExternalAndWriterDependencies': 2,
        'NeutralFractionalBaseCannotAssumeBuiltinGestureRemainsAtDefault': 1,
        'NeutralFractionalOverrideDoesNotBakeAnUnrelatedConstantFromAMovingBase': 2,
        'NeutralFractionalOverrideRejectsDelayedBaseActivityChanges': 2,
        'NeutralFractionalOverrideStillValidatesMovingRequiredBaseMorph': 2,
        'NeutralWeightedUpperLayerKeepsMovingBaseWithoutBakingUnrelatedMorphs': 4,
        'OrdinaryMenuDoesNotNeedAStationaryLayer': 1,
        'ParameterOrEmptyWriteDefaultsLayerPreservesStandaloneClipsWithoutPermanentMorphs': 2,
        'PermanentEffectUsesAuthoredLayerPriority': 4,
        'PermanentOverrideSuppressesPupilAnimationWithoutLosingOtherAnimatedChannels': 1,
        'RegisteredFaceEmoClipFollowsReparentedTargetAndMergedPermanentFx': 2,
        'RegisteredFaceEmoMissingOriginalTargetCannotBindAnUnrelatedGeneratedRenderer': 1,
        'RegisteredFaceEmoOriginalManualOmissionStaysOmittedAfterReparenting': 1,
        'RegisteredFaceEmoRetainedTargetCanMoveIntoAnOriginallyManuallyExcludedPath': 1,
        'RegisteredFaceEmoTargetRemovalIsReportedEvenIfItsOldPathIsReused': 1,
        'StandaloneRegisteredClipKeepsPermanentFxAboveItsBasePose': 1,
        'TimeVaryingDefaultLayerIsNotDeclaredPermanent': 1,
        'UnrelatedGestureLayerDoesNotBecomeANeutralSamplingRoot': 1,
    },
    'DirectExpressionNativeLayersTests': {
        'FractionalPlayerBlendsWithTheEvaluatedLowerDefaultAcrossPossibleTransitions': 2,
        'InvalidSelectedStateProvenanceCannotRewriteTheDirectEntry': 3,
        'ActiveFractionalSyncedSlotKeepsItsEffectiveMotionAndControllerReplacement': 1,
        'DroppedSelectedDriversCannotSilentlyChooseARetainedLowerPose': 3,
        'EffectiveOverrideClipsSurviveThePrivateFullGraphProbe': 1,
        'ExpressionParameterDefaultsSelectTheDeclaredLowerPose': 1,
        'InactiveAuthoredSlotsKeepTheirDirectClipCandidate': 2,
        'LowerPoseDependenciesAreValidatedBeforeRewritingTheEntry': 4,
        'RetainedSyncedOverrideDriverCannotBePrunedWithItsUnrelatedMotion': 1,
        'SelectedSyncedSlotChecksEffectiveOverrideDriversWithoutChangingSourceBehaviours': 2,
        'UnrelatedSelectedDriverDestinationDoesNotBlockTheNativeProbe': 1,
    },
    'FaceEmoPreparedFxIntegrationTests': {
        'RegisteredPlayerRejectsAmbiguousMissingOrChangedCallbackProvenance': 4,
        'RegisteredPlayerReadsRegistryProvenPreparedAugmentationAndExactCallbacks': 1,
        'RegisteredPlayerRejectsUnprovedOrAmbiguousPreparedAugmentation': 2,
        'RegisteredPlayerRejectsPreparedMotionChangesBeforeDeferredEvaluation': 1,
        'RegisteredBranchUsesOriginalPlayerSlotAndNativeCommonWeightOnce': 2,
    },
    'MergedFixedNeutralSamplingTests': {
        'FractionalNeutralRetainsNativeBaseActivityAndLeavesAutomaticBlinkLive': 4,
        'FractionalNeutralKeepsPreparedRestForDelayedAutomaticBaseActivityChanges': 1,
        'FractionalNeutralDistinguishesDisconnectedAndReachableUnsupportedSupportMotions': 4,
        'NeutralStartupWritesAndPermanentMorphSurviveEquivalentTimedRelay': 2,
    },
    'NeutralCurveConditionTests': {
        'DynamicParameterCurveKeepsPreparedNeutralWhenItsFutureTimedExitIsUnresolved': 1,
        'CompetingParameterDriverPreventsTheRelayProof': 1,
        'FractionalRelayWeightNearExitThresholdCannotCountAsAFullConstantWriter': 1,
        'NativeConstantParameterRelayCanProveItsTimedExitFalseWithoutNames': 2,
    },
    'NeutralParameterCurveTests': {
        'IndependentParameterAnimationKeepsTheNativeBodyPose': 4,
        'ParameterThatControlsCapturedMorphsMustRemainFixedEvenWhenItsChangeIsDelayed': 1,
        'ParameterDependencePropagatesThroughAnotherMotionTimeRelay': 1,
        'FutureTimedTransitionsAreStillValidatedForIndependentParameterSupport': 1,
        'GestureSmoothingFeedbackAndUnrelatedFacialConsumerKeepNativeBody': 2,
        'GestureSmoothingThatActuallyControlsBodyStillRequiresItsParameterProof': 1,
        'FeedbackWithUnprovedNativeDefaultContributionIsNotExempt': 3,
        'GestureSmoothingUnusedEndpointTangentsDoNotRequirePortableEncoding': 2,
        'IndependentNativeParametersAreNotLimitedByPortableMorphDomains': 4,
        'NativeParameterValidationStillRejectsAnActiveInteriorNaNTangent': 2,
    },
    'NeutralFallbackExportTests': {
        'UnresolvedBodyRestExportsWithIndependentOpenEyesAndAbsoluteEndpoints': 6,
    },
    'NeutralShapeEndpointTests': {
        'PreparedSnapshotRetainsIdentityMeshAndExplicitZeroWithoutChangingSource': 1,
        'InvalidAuthorIndexCannotAddressNewlyAppendedBlinkOrExpressionChannel': 1,
        'ExplicitEndpointsSupportNegativeZeroAndAboveHundred': 3,
        'ProfileWeightsAreAbsoluteSourceEndpointsAndMapEachRendererIndependently': 2,
        'AuthoredCustomPrivateCopyKeepsRuntimeKeyAndSettings': 2,
        'AuthoredPartialEndpointUsesSourceMultiFrameCurveAndKeepsUnboundNeutral': 1,
        'AuthoredZeroOnlyRemainsDisabledWhileExplicitFxZeroCanReopen': 1,
        'DuplicateAuthoredBindingsKeepFirstChannelSemantics': 2,
        'DuplicateRendererPathsCannotSilentlyApplySampleToTheFirstSibling': 1,
        'EndpointBakeKeepsBlinkSettingsCreatedAfterCapture': 1,
        'MalformedAuthoredClipKeepsItsDeclarationAndCannotActivateItsValidPortion': 3,
        'PartialEndpointAtNeutralRemainsInertAndExplicitZeroHasGeometry': 1,
        'ProfileRetainsExistingInvalidWeightContract': 2,
    },
    'PreparedNeutralEligibilityTests': {
        'PreBuildGuardAllowsWeightChangesButStillProtectsDeformation': 1,
        'PreparedPathMappingCannotChooseBetweenAmbiguousOriginalOrCurrentPaths': 1,
        'AuthoredPartialEndpointIsVerifiedAgainstItsDeclaredAbsoluteWeight': 1,
        'DeferredCaptureProtectsRendererIdentityAndMovingGeometry': 1,
        'DeferredCaptureStillRejectsMissingBlinkAtPreparedNeutral': 1,
        'ExportCaptureWaitsForFxOpeningBeforeSelectingUnifiedClosure': 1,
        'InferredUnifiedBlinkUsesTheSameFinalSourceFrameAsRawTracking': 1,
        'PreparedPathMappingKeepsUnderlayOnlyRendererIdentityAcrossMoves': 1,
    },
    'PreparedNeutralExportTests': {
        'IndependentLoopOnTheInfluencingSkinBoneKeepsPreparedGeometryAndAuthoredEndpoints': 1,
        'OneClickManualBlinkPreservesCoupledPreparedAppearanceAndAbsoluteClosure': 1,
        'OneClickKeepsSourceTrackingEndpointsAndPreparedRestAfterMarkerRemoval': 1,
        'ClosedPreparedEyesRequireAdjustmentWhenInitialFxIsSkipped': 2,
        'NdmfGeneratedShapeAndReboundFxUsePreparedMeshAndRendererPathInRealVrm': 4,
    },
    'NeutralShapePipelineTests': {
        'NeutralAndAbsoluteEndpointRoundTripWithLargeSharedMeshes': 4,
    },
    'NeutralShapeExportTests': {
        'PreparedRestSurvivesExportWithoutRestoringInitialFx': 6,
    },
    'UnifiedExpressionExportTests': {
        'MissingBlinkRequiresUsableUnifiedRoute': 13,
        'ClampedNegativeRestKeepsNativeGeometryAndProvidesMovingUnifiedBlinkFallback': 1,
        'AuthoredExclusionsMatchFinalReferencePruning': 6,
        'AuthoredMorphEligibilityMatchesActualExportedResidual': 3,
        'AuthoredRouteWithOnlyExcludedTargetsCannotWaiveMissingBlink': 1,
        'AuthoredSplitCoverageWinsBeforeRawAggregateOnItsMeshes': 4,
        'DuplicateAuthoredTargetsMatchPinnedFirstBindingPlayback': 2,
        'DuplicateMaterialNamesKeepTheOriginallySelectedRendererSlot': 4,
        'EmissionEvidenceUsesTheActualSuppressionOptions': 4,
        'ExplicitArkitMarkerKeepsItsExisting52ChannelPriority': 1,
        'ExplicitRawAliasWinsOverCanonicalInExportedBindings': 1,
        'ExtendedRawFramesUseActualRemainingExportRange': 3,
        'FallbackBakingCannotReplaceMovingMaterialEvidenceWithAnInertBase': 2,
        'FinalGlbTrackingContract': 1,
        'GeneratedJawMenuRemainsBinaryAlongsideContinuousUnifiedJaw': 1,
        'LeadingNeutralFrameSurvivesActualGlbAndContinuousPlayback': 2,
        'MaterialCoverageWithMalformedMorphKeepsItsDeclaredNodeScopeInRealGlb': 3,
        'MaterialEndpointEvidenceMatchesActualImportedProxy': 10,
        'MaterialOnlyAuthoredCoverageSurvivesRoundtripWithoutRawAggregate': 3,
        'MaterialOnlyPreparationDoesNotRequireSuppressedMorphs': 3,
        'MixedMaterialPreparationUsesOnlyResolvableRendererScopes': 3,
        'NegativeBracketPreservesNativeRestAndRebasedContinuousPlayback': 18,
        'NegativeIntermediateFramesKeepTheirPositiveExportedEndpoint': 4,
        'PartialChannelsAndMultipleRestingMeshesSurviveExportAndReimport': 1,
        'PreparationAllowsPreviouslyInertOptionalNamesWhileUsableRouteSurvives': 1,
        'PreparationAllowsUnusedAggregateRemovalAndProtectsAuthoredEndpoint': 4,
        'PreparationAuthoredTargetIdentityAllowsOnlyCorrectIndexRemapping': 2,
        'PreparationChecksEarlierAutomaticBlinkBypassWhenEvidenceWasAlreadyLost': 1,
        'PreparationProtectsSharedChannelsOnOtherFaceMeshes': 1,
        'PreparationRejectsChangedCanonicalRouteWhileOtherUnifiedEvidenceSurvives': 7,
        'PreparationRejectsLossOfInitiallyEffectiveRoutesDespitePreservedNames': 8,
        'PreparationRejectsLossOfPreviouslyMovingMaterialEndpoint': 3,
        'PreparationRejectsLostMorphOnTheTrackedRenderer': 1,
        'PreparationSharedMeshCoverageIsScopedPerRenderer': 1,
        'SharedGlbMeshKeepsAuthoredCoverageScopedToItsRendererNode': 1,
        'SplitFaceExportsSharedMouthAndOnlyAvailableBilateralBlink': 2,
        'SplitPartialBlinkUsesTheSameUeClosureInStandardAndDetailedPlayback': 1,
        'StandardEmissionEvidenceUsesItsActualGammaProxy': 3,
        'SupportedUnifiedRouteWithoutBilateralEyeRigSurvivesBothExportModes': 8,
        'UeEvidencePairsOnlyCompatibleLegacySidesAcrossRenderers': 2,
        'UnifiedExpressionFailuresHaveLocalizedActionsAndPreserveTargetNames': 4,
    },
    'MaSceneReferencePreparationTests': {
        'InstalledMaResolvesStaleDirectTargetAndMergesOnlyTheOwnedAvatar': 2,
        'OrdinaryExternalComponentReferencesRemainRejectedWithTheExactField': 2,
        'ActiveMaPathReferenceRetainsTheRequiredInactiveAuthoringBeforeCanonicalBuild': 1,
        'InstalledMaMergesAnInactiveNestedArmatureWithItsDeclaredTargets': 1,
        'EnclosingAvatarRelativePathIsNotReinterpretedAfterTheSelectedSubtreeIsDetached': 1,
        'ExternalDirectTargetWithMissingPathRemainsRejectedWithoutEditingEitherAvatar': 2,
        'InactiveUnresolvedMaSceneReferenceIsRejectedWithoutSilentlyDroppingItsAuthoring': 1,
        'InstalledMaBoneProxyResolvesItsPathUnderTheExportCopyRootWithoutASourceMarker': 1,
        'InstalledMaMergesSelectedAvatarWithoutASourceRootMarker': 2,
        'InstalledMaMergesValidDirectTargetWithMissingPathOnlyOnTheExportCopy': 4,
        'InstalledMaUsesSelectedAvatarRelativePathWhenTheSourceHasAnEnclosingAvatarRoot': 1,
        'InternalSceneReferenceDoesNotPreventTraversalAndIsolationOfItsQueuedRenderer': 1,
        'MissingDirectTargetAndMissingPathRemainRejectedWithoutEditingTheSource': 2,
        'NestedMaReferencesAreResolvedInEverySerializedArrayElementWithoutEditingTheSource': 1,
        'RecoveredDirectMergeTargetStillRespectsExportExclusions': 2,
        'UnresolvedStaleMaPathRemainsRejectedBeforeAnyPass': 1,
        'ValidDirectMergeTargetWithMissingPathPassesReadOnlySourceValidation': 4,
    },
    'NdmfPreparationTests': {
        'ExportPreparationRunsBetweenCanonicalPhasesOnTheSameUnfinishedContext': 1,
        'PreparationFailureSkipsOptimizationAndStillFinishesAndCleansUp': 1,
        'OptimizationFailureStillFinishesAndCleansUp': 1,
        'UnresolvedFollowingTargetCanReachCanonicalResolutionOnTheOwnedCopy': 1,
        'ExplicitExclusionCanOmitInvalidInactiveAuthoringWithoutEditingTheSource': 1,
        'ActiveMergePreservesAnInactiveTargetAttachmentChain': 1,
        'ActiveSkinRetainsItsInactiveRigTagAndAncestors': 2,
        'AuthoredNdmfRootMarkerRetainsItsProcessingSemantics': 1,
        'BeforePlatformsRunsFromResolvingWithoutAPlatform': 2,
        'CallbackFreePreparationStopsAfterTransformingAndRetainsOwnedAssetLifetime': 1,
        'CleanupFailuresStillRestoreScopeAndReleaseOwnedAssets': 3,
        'InactiveAuthoringIsRetainedRegardlessOfRendererVisibility': 2,
        'IncompatibleErrorApiFailsBeforeProcessing': 1,
        'InjectedCopyRootRetainsInactiveAuthoringAcrossValidationAndPreparation': 1,
        'InstalledModularAvatarMergesSleeveRigAndPreservesSourceAndExpression': 1,
        'InstalledModularAvatarRejectsTargetOutsideSelectedSubtreeBeforeCloning': 2,
        'InvalidInactiveRigIsValidatedRegardlessOfSkinVisibility': 2,
        'InvalidInactiveWardrobeRequiresProcessingAndIsValidated': 2,
        'KnownVersionsResolveCompleteBridge': 8,
        'LoggedBuildErrorStopsExportAndReleasesGeneratedAssets': 1,
        'MissingBakedExpressionStopsExport': 1,
        'MixedActiveAndInactiveAuthoringRetainsBothBeforeNdmf': 1,
        'OptionalDependencyIsNotRequiredWithoutAuthoringTags': 1,
        'OriginalAndNestedCopiesAreRejectedBeforeProcessing': 1,
        'PartialPlatformApiFailsBeforeProcessing': 1,
        'PersistentContainerOfUnsavedAssetIsCopiedWithoutEditingOriginalReferences': 2,
        'PersistentPluginAssetsRemainUntilExportLeaseEndsAndNeverTouchSiblingAssets': 1,
        'PhaseExceptionStillFinishesAndReleasesGeneratedAssets': 1,
        'PluginFailureIncludesToolPassAndOriginalExceptionAndCleansTemporaryFolder': 1,
        'PreparationCancellationRemainsCancellationAfterCleanup': 1,
        'ReferencedChildRendererStillIsolatesItsOwnUnsavedMeshBeforeProcessing': 1,
        'RunsCanonicalPhasesWithOwnedAssetContainerAndOwnsOnlyNewReachableAssets': 1,
        'SettingsPointingIntoAnotherSceneObjectStopBeforeAnyPass': 1,
        'SharedSettingsPointingIntoOriginalAvatarStopBeforeAnyPass': 2,
        'UnsavedSharedAssetsAreIsolatedBeforeAnyPassCanMutateThem': 1,
        'UnvalidatedVersionsFailBeforeProcessing': 5,
        'UsesGenericOnlyWhenSourceHasNoPrimaryPlatform': 1,
    },
    'DirectFxExpressionDiscoveryTests': {
        'DescriptorFxIsAuthoritativeWhenRootAnimatorControllerIsNone': 1,
    },
    'MaReactiveExpressionExportTests': {
        'MixedMenuSetAndDeleteKeepsSetWithoutReapplyingDeletion': 1,
        'AuthoredActivationKeepsInactiveDependentShape': 2,
        'MergeMotionActivationKeepsNonMenuReactionThroughCanonicalBuild': 3,
    },
    'InstalledNdmfNeutralExportTests': {
        'InstalledModularAvatarProcessesAnInactiveMergeAnimatorForPreparedFx': 1,
    },
    'HumanoidPoseTests': {
        'ASingleArmMusclePreservesUnboundAuthoredJointLocals': 1,
        'ContractRejectsVersionNonFiniteInvalidBonesAndDuplicates': 1,
        'DiscardedTransformsDoNotForceMixedMuscleSampling': 2,
        'ExportAndReimportKeepsNodeBindingAndSourceThenProducesApplicationFixture': 1,
        'FingerMusclesMasksAndWeightsAreEvaluatedByUnity': 1,
        'HumanoidRootTranslationUsesMetersAndIgnoresScenePlacementScale': 1,
        'ManualSamplingIsIsolatedAndKeepsDifferentTimesAndConditions': 1,
        'MaskedBindingKindsDoNotForceMixedSampling': 2,
        'MenuGraphAppliesSoloFilteringBeforeConditionsAndMute': 1,
        'MenuGraphChecksRealControllerParameterDefinitions': 10,
        'MenuGraphRejectsAnimatedIntermediateStates': 1,
        'MenuGraphRejectsNestedStateMachineTransitions': 1,
        'MenuGraphRequiresConvergenceAndRejectsTimeOrExternalInput': 1,
        'MotionDetectionUsesEffectiveBodyCurves': 5,
        'NoncontributingLayersDoNotForceHumanoidComposition': 3,
        'PoseLimitCountsSelectedDeduplicatedOutputAndKeepsReviewAvailable': 1,
        'RootRotationUsesRootMaskIndependentlyOfBodyMask': 1,
        'TrackingOnlyMusclesCannotBecomeBodyPoses': 3,
    },
    'PoseReviewWindowTests': {
        'IncrementalCollectionSamplesOneCandidatePerStepAndMatchesExport': 1,
        'TwentyAnimDropRemainsUsable': 1,
        'UnsupportedDropIsRejectedAndClosingCancelsPendingWork': 1,
    },
    'SelectedScalarDominanceTests': {
        'ExactUpperScalarDominanceRetainsIndependentRestAndSelectedCallbacks': 2,
        'ScalarDominanceNeverPreservesAnUndominatedMovingChannel': 4,
        'ExactDominanceCannotConcealInvalidPrograms': 4,
    },
    'HeldAutomaticExpressionTests': {
        'HeldAutomaticComponentKeepsExactAuthoredFaceWithSparseBlinkResets': 1,
        'AutomaticSuspensionRequiresCompleteIsolation': 9,
        'AutomaticSuspensionCannotHideMalformedOrUnknownCallbacks': 4,
        'AParameterOnlyForwardRelayIsIncludedInTheSuspendedComponent': 1,
        'AutomaticSuspensionCannotDiscardIncomingOtherPlayableWriters': 2,
        'GeometryEquivalentAutomaticAliasCanCoexistWithTheManualBlink': 3,
        'AutomaticAliasRequiresUniqueExactMeaningAndCompleteNonInertGeometry': 9,
        'CoexistingKnownBlinkFamiliesAllKeepTheirAutomaticOwnership': 1,
        'UnownedRandomRecoveryIsPreservedWithoutRecapturingItsStationaryReset': 2,
        'HeldProbeRunsTheSelectionAnalysesOnce': 1,
        'ReadOnlyRandomGateProducersKeepTheirNativeSelectedCallbacks': 2,
        'ReadOnlyRandomGateCannotConcealMalformedTypedProducerOperations': 3,
        'ReadOnlyRandomGateRequiresReproducibleNativeProducerOperations': 2,
        'UnownedRandomRestStillRequiresEveryOriginalIsolationAndDataGuard': 10,
        'SelectedRandomGateCannotDropADeterministicUnownedFace': 3,
        'RandomGateRestNeedsAFiniteGateIndependentReturn': 3,
        'NestedRandomGateRestMustReturnThroughAnUnconditionalParentExit': 2,
        'NestedRandomRestartNeedsCompleteTypedRestRouting': 3,
        'SparseWriteDefaultsOffRestCannotLatchASelectedUnownedFace': 1,
        'UnownedRandomRestRetainsNativeUnitFxCallbacksBehindAnUnchangedNormalGate': 1,
        'UnownedRandomRestCannotHideAnUnprovenGlobalFxCommand': 10,
        'SelectedGateCannotHideAResetBeyondTheNativeCaptureHorizon': 5,
        'ReadOnlyRestGatesRequireCompleteTransientInputEvidence': 7,
        'KnownUnsavedReadOnlyRestGatesKeepTheirNativeAuthoredFace': 3,
        'SyncedRestGatesRequireAnUnchangedUnwrittenLocalPreview': 3,
        'AStoredGateCanRetainAnOriginalNativeFaceDespiteAnAuthoredZeroDefault': 1,
        'OneAnalysisProvesTheSelectionIndependentRandomRestOnce': 1,
        'TheRandomRestMemoKeepsPrivateCopies': 1,
        'TheRandomRestMemoIsKeyedByTheFixedInputs': 1,
        'TheRandomRestIsNotMemoisedOutsideAnAnalysis': 1,
        'UnsavedControllersDoNotMemoiseTheRandomRest': 2,
    },
    'InvariantMorphOwnershipTests': {
        'ExplicitIdenticalFaceResetsDoNotOwnTheChangingWardrobeOption': 1,
        'AuthoredPureMorphSurvivesAnInvariantWardrobeResetWithoutAFacialDescriptor': 1,
        'UnprovenResetAlternativesKeepTheirAppearanceOwnership': 6,
        'SharedInputPreservesOnlyMorphsThatVaryAcrossTheirOwnAlternatives': 2,
        'BlendTreesNeedAConvexProofBeforeIdenticalLeavesAreInvariant': 2,
        'IdenticalKeysWithCurvedInteriorsAreNotInvariantResets': 1,
        'ASingleMixedConfigurationRetainsItsPairedMorphOwnership': 1,
        'NativeLayerWeightCommandsKeepIdenticalResetsConservative': 1,
    },
    'RandomNeutralRestTests': {
        'IsolatedRandomIdleKeepsPreparedChannelsAndIndependentFixedRest': 1,
        'ValidRandomCapabilityKeepsTheCompletePreparedDependencyComponent': 8,
        'OnlyAPermanentFullOverrideKeepsItsAuthoredConstantAboveTheRandomIdle': 3,
        'AnOverlappingUpperGraphRetainsPreparedRestWhenItsNativeDependencyIsRandom': 1,
        'AnUnreachableRandomBranchDoesNotReplaceTheAuthoredConstantRest': 1,
        'ExplicitFixedExpressionStillRejectsRelevantRandomness': 1,
        'ASeparateDeterministicComponentStillReconstructsWhenTheRandomComponentFallsBack': 1,
        'MalformedRandomNumbersCannotUsePreparedRestFallback': 6,
        'InvalidRandomDestinationsRemainFatal': 4,
        'AValidRandomOperationCannotConcealALaterMalformedNeededOperation': 5,
        'PreparedRandomRestDoesNotHideCorruptDataOrUnknownEffects': 3,
    },
    'FacialProjectionTests': {
        'EnclosedDisconnectedHeadSurfaceIsFacialAtEveryMeshScale': 2,
        'OneEnclosedVertexCannotAuthorizeAnOutsideOrMixedSkinIsland': 2,
        'DescriptorFaceSeamsAcceptOnlyHeadDominantHumanoidNeckSkin': 3,
        'SharedMaterialInputKeepsOnlyProvenFacialAnimation': 2,
        'FaceRendererDisappearanceRemainsUnsupported': 1,
        'MissingDescriptorRendererRetainsStrictFallbackWithoutDereferencingUnityFakeNull': 2,
        'EyeSeedsRequireEnabledBlendshapeMode': 3,
        'LipSyncModesIgnoreInactiveBlendshapeFields': 3,
        'ACanonicalBlinkNameOnAMergedHeadAccessoryIsNotFacialEvidence': 1,
        'LaterFramesAndNonPositionBodyDeformationsCannotEnterTheFaceProjection': 2,
        'AnIndependentWardrobeLayerDoesNotNarrowAnAlreadyValidPureMorphClip': 1,
        'ANewAnalysisRechecksChangedGeometryAfterBothSuccessfulAndFailedFaceProofs': 2,
        'ExportedProjectedFaceReimportsWithPreparedOutfitAndReturnsExactlyToRest': 1,
    },
    'AnimatedManualPoseExportTests': {
        'ActualVrmExportAndReimportRetainsBoundAnimationFramesAndStaticCompatibility': 1,
        'ExistingPreviewButtonPathStartsMotionAndCleanupStopsItsEditorUpdates': 1,
        'ExplicitHeadOrNeckMotionIsIncludedWhileEyeAndJawTracksStayOutsideTheContract': 2,
        'ExplicitMovingHeadAndNeckMusclesMatchNativeHumanoidInterpolation': 2,
        'ManualStartAndLoopArePreservedWithoutAnAdditionalStaticSnapshot': 1,
        'MovingHipsTransformMatchesNativeMetersIndependentlyOfScenePlacement': 1,
        'MovingHumanoidClipKeepsLoopPoseSettingsAndNonMatchingAuthoredEndpoints': 2,
        'MovingHumanoidRootChannelsMatchNativeBodyCoordinatesWithoutSceneRootMotion': 3,
        'MovingMuscleClipMatchesTheNativeHumanoidAtIntermediateTimes': 1,
        'MovingTransformClipRetainsNativeIntermediateRotationsAndOriginalAssets': 3,
        'MultipleEulerTurnsInsideOneSixtiethSecondCannotAliasToAStaticAnimation': 1,
        'NativeTransformSamplingActuallyAppliesAuthoredCurvesBeforeComparison': 1,
        'OversizedMovingClipIsDiagnosedAndNeverFallsBackToAStaticPose': 2,
        'QuaternionCurveKeepsNativeInterpolationThroughItsSignChange': 1,
        'RemainingFrameBudgetAndManualExclusionAreAppliedBeforeSampling': 1,
        'StaticAndAnimatedRowsShareThe128ItemSelectionLimit': 1,
        'SteppedClipStoresBothLimitsAndSelectsTheRightValueAtTheExactKey': 6,
        'UnrepresentableContinuousTurnStillReportsPrecisionFailure': 1,
        'FiniteTangentCurveWithNativeKeyJumpRetainsBothLimits': 3,
        'FiniteTangentMuscleKeyJumpMatchesTheOriginalNativeHumanoidClip': 1,
        'PrecisionFailureReportsTheNativeHipsErrorAndAbsoluteSourceTime': 1,
        'ManualTransformClipFollowsPreparedHumanoidPathWithoutChangingSource': 4,
        'MissingPreparedHumanoidTargetCannotSilentlyFlattenManualMotion': 1,
        'RemovedUnanimatedOptionalBonesDoNotBlockSourceSkeletonSampling': 1,
    },
    'FacialDepthEnvelopeTests': {
        'HeadBoundedDisconnectedDepthWorksWithRotatedRenderersAndMeshScales': 2,
        'DepthCannotAuthorizeUnboundedOrUnprovenTopology': 10,
        'ExactFacialEdgesConnectDuplicatedMaterialSeams': 2,
        'CoincidentPointsAndNearbyEdgesCannotConnectDetachedPosteriorGeometry': 3,
        'ASeamCannotEnlargeTheEnclosureForOtherDetachedPosteriorGeometry': 1,
        'ASeamTouchingNonHeadGeometryCannotAuthorizeTheRemainingSurface': 1,
        'CurrentHeadAnimationDoesNotMoveTheOriginalMeshEnvelope': 1,
    },
    # "アプリに転送（かんたん）" writes what "通常" writes with nothing on the board touched.
    'QuickTransferMatchesDefaultNormalTests': {
        'TheSavedExpressionsMatch': 1,
        'TheGimmicksAndTheSavedExclusionsMatch': 1,
        'APlainAvatarMatches': 1,
        'AFailingStageMatches': 1,
        'ABoardReusedForAnotherAvatarStartsFromTheSameDefaults': 1,
        'PosesOverTheAppLimitMatch': 1,
    },
}
INTEGRATION_CASES = {
    # The integration project must install the official MeshDeleterWithTexture
    # package. Synthetic sharedMesh replacement alone is not its integration
    # evidence, and an absent optional package must fail this explicit profile.
    'MeshDeleterIntegrationTests': {
        'DeletedSharedMeshAndMorphSurviveOneClickExportAndReimport': 4,
    },
    'MaDeletionExportTests': {
        'InstalledDeletionSurvivesOneClickAndLiveMorphsRegardlessOfPreviewSettings': 6,
    },
    'MaPermanentAppearanceExportTests': {
        'MergedPermanentPupilHideSurvivesVrmReloadBlinkAndMenuGeometry': 4,
    },
    'AppearancePreparationTests': {
        'PreparedRestAndMovedRendererKeepExpressionEndpointsWithoutDoubling': 2,
        'InstalledMaResolvesShapeMaterialAndVisibilityBeforeBaseShape': 2,
        'InstalledMeshCutterPreservesMorphsAndSourceMesh': 2,
        'ExcludedRulesCannotChangeRetainedAppearanceAndSimulatorOverridesStillMap': 2,
        'ExclusionsPreserveSimulatorMenuSelectionForAutomaticAndNamedParameters': 4,
    },
    'InstalledNdmfNeutralExportTests': {
        'InstalledModularAvatarRetargetsMovedRendererDefaultFxAndAuthoredEndpointInRealVrm': 2,
        'InstalledModularAvatarReplacementUsesFinalRendererKeyForNeutralWithoutAnAuthoredRoute': 1,
    },
    'InstalledAaoNeutralEndpointExportTests': {
        'MaskMergeAndTraceKeepFxNeutralAuthoredEndpointsTrackingAndDeferredBlink': 3,
    },
    'NdmfAnimationModeSamplingTests': {
        'ExportSurvivesAnOptimizingPassThatSamplesTheRootThroughAnimationMode': 1,
    },
    'NdmfBlinkPreparationTests': {
        'InstalledAaoMergeKeepsTheSelectedRendererInCallbackFreeBlinkPreview': 1,
        'InstalledAaoMergeStillOptimizesAndRemapsBlinkForTheExportCallback': 1,
    },
    'PhysBoneSpringExportTests': {
        'InstalledAvatarOptimizerPreservesSpringMotionAndCollisionsAfterFullExport': 2,
        'InstalledAvatarOptimizerWithoutAvatarDescriptorStopsInsteadOfSavingAVrmWithoutHumanoid': 1,
    },
}
# Pin the new deletion combinations as well as their counts. A missing real
# tool or preview-disabled case cannot be replaced by a different parameter
# variant while retaining the same number of passing tests.
INTEGRATION_VARIANTS = {
    'MeshDeleterIntegrationTests': {
        'DeletedSharedMeshAndMorphSurviveOneClickExportAndReimport': (
            'False,False', 'False,True', 'True,False', 'True,True'),
    },
    'MaDeletionExportTests': {
        'InstalledDeletionSurvivesOneClickAndLiveMorphsRegardlessOfPreviewSettings': (
            'False,0', 'False,1', 'False,2', 'True,0', 'True,1', 'True,2'),
    },
    'MaPermanentAppearanceExportTests': {
        'MergedPermanentPupilHideSurvivesVrmReloadBlinkAndMenuGeometry': (
            'False,False', 'False,True', 'True,False', 'True,True'),
    },
}


def compatibility_counts():
    # The exporter writes with its bundled UniVRM, so every project runs the
    # round trip whatever UniVRM (if any) the project itself has installed.
    return {'DependencyEnvironmentTests': 1, 'DependencyStartupTests': 1, 'DependencyRoundTripTests': 2,
            'RendererSelectionTests': 4, 'SkinnedMeshFallbackWeightTests': 14, 'LilToonStructureCheckTests': 11}


def required_regressions(profile):
    if profile not in PROFILES:
        raise SystemExit('Unknown Unity validation profile: ' + profile)
    required = {
        'DependencyEnvironmentTests': {'ActualInstalledPackagesMatchRequestedTestEnvironment': 1},
        'DependencyStartupTests': {'MenuResolvesBackendWithoutInitializationRegistration': 1},
        'DependencyRoundTripTests': {'SupportedBackendPreservesMeshesMorphsAndMaterialBindingsOnReimport': 2},
    }
    if profile != 'compatibility':
        required.update(BEHAVIOR_CASES)
    if profile == 'exporter-integration':
        for suite, methods in INTEGRATION_CASES.items():
            required[suite] = {**required.get(suite, {}), **methods}
    return required


def profile_filters(profile):
    required = required_regressions(profile)
    filters = []
    for suite in dict.fromkeys([*compatibility_counts(), *required]):
        if profile == 'exporter-behavior' and suite in INTEGRATION_CASES:
            # Shared suites have optional installed-tool cases. Select every
            # required behavior method, without silently accepting skipped ones.
            filters.extend(NAMESPACE + suite + '.' + method for method in required[suite])
        else:
            filters.append(NAMESPACE + suite)
    return filters


def validate_result(xml, profile='compatibility'):
    required = required_regressions(profile)
    tree = ET.parse(xml).getroot()
    cases = tree.findall('.//test-case')
    names = [c.get('fullname') for c in cases]
    if any(not name for name in names) or len(set(names)) != len(names):
        raise SystemExit('Missing or duplicate Unity test identities')
    for suite, methods in required.items():
        for method, expected_count in methods.items():
            name = NAMESPACE + suite + '.' + method
            found = [c for c in cases if c.get('classname') == NAMESPACE + suite and
                     (c.get('fullname') == name or c.get('fullname', '').startswith(name + '('))]
            if len(found) != expected_count or any(c.get('result') != 'Passed' for c in found):
                raise SystemExit('Required real-Unity regression did not pass (expected ' + str(expected_count) + ' cases): ' + name)
            variants = INTEGRATION_VARIANTS.get(suite, {}).get(method) if profile == 'exporter-integration' else None
            if variants is not None and {c.get('fullname') for c in found} != {name + '(' + args + ')' for args in variants}:
                raise SystemExit('Required real-Unity parameter combinations did not pass: ' + name)
    for suite, count in compatibility_counts().items():
        found = [c for c in cases if c.get('classname') == 'VRVlog.LilToonExporter.Tests.' + suite]
        if len(found) != count:
            raise SystemExit('Required compatibility suite has missing or unexpected cases: ' + suite)
    allowed = {NAMESPACE + suite for suite in [*compatibility_counts(), *required]}
    if any(c.get('classname') not in allowed for c in cases):
        raise SystemExit('Unexpected test classes in Unity validation profile: ' + profile)
    if tree.get('result') != 'Passed' or any(c.get('result') != 'Passed' for c in cases):
        raise SystemExit('Failed or skipped real-Unity tests; see ' + str(xml))
    return cases


def source_identity(root):
    """Identify the runner checkout, not an unverified package in the test project."""
    root = root.resolve()
    identity = {'packageVersion': None, 'gitCommit': None, 'gitDirty': None}
    try:
        identity['packageVersion'] = json.loads((root / 'package.json').read_text(encoding='utf-8-sig'))['version']
    except (OSError, ValueError, KeyError):
        pass
    # Git's ownership check expects forward slashes on Windows. A backslash
    # safe.directory can work for the checkout owner yet fail for another user.
    safe_root = root.as_posix()
    requires_git = (root / '.git').exists()
    def unverifiable(reason):
        if requires_git:
            raise SystemExit('Cannot verify runner Git source identity: ' + reason)
    # Do not identify a source archive using an unrelated ancestor repository.
    def git(*args):
        result = subprocess.run(['git', '-c', 'safe.directory=' + safe_root, '-C', str(root), *args],
                                capture_output=True, text=True, timeout=10)
        return result.stdout.strip() if result.returncode == 0 else None
    try:
        top = git('rev-parse', '--show-toplevel')
        if top is not None and Path(top).resolve() == root:
            commit = git('rev-parse', 'HEAD')
            if commit is not None and re.fullmatch(r'[0-9a-f]{40,64}', commit):
                identity['gitCommit'] = commit
                status = git('status', '--porcelain', '--untracked-files=normal')
                if status is not None:
                    identity['gitDirty'] = bool(status)
                else:
                    unverifiable('Git checkout status is unavailable')
            else:
                unverifiable('Git commit is missing or invalid')
        else:
            unverifiable('Git could not identify the runner checkout')
    except (OSError, subprocess.SubprocessError):
        unverifiable('Git is unavailable or could not complete the identity check')
    return identity


def installed_liltoon(project, expected=None):
    """Read the project's resolved lilToon and stop before Unity runs if it is not the expected release."""
    if expected is not None:
        try:
            unity_packages.parse_version(expected)
        except ValueError:
            raise SystemExit('--expect-liltoon must be a release version such as 2.3.4: ' + repr(expected))
    try:
        installed = unity_packages.installed_package(project, unity_packages.LILTOON)
    except (OSError, ValueError) as error:  # json.JSONDecodeError is a ValueError
        if expected is None:
            return {'expected': None, 'installed': None, 'source': None, 'unavailable': str(error)}
        raise SystemExit('Cannot read the installed lilToon version: ' + str(error))
    version = installed['version'] if installed else None
    if expected is not None and version != expected:
        raise SystemExit('Installed lilToon ' + str(version) + ' does not match --expect-liltoon ' + expected)
    return {'expected': expected, 'installed': version, 'source': installed['source'] if installed else None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', required=True, type=Path)
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--expect-univrm', required=True,
                        help="The project's own UniVRM version, or 'missing'; the exporter uses its bundled copy either way")
    parser.add_argument('--expect-unity', default='2022.3.62f3')
    parser.add_argument('--expect-liltoon', help='Stop before running Unity unless the project resolved exactly this lilToon release')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--profile', choices=PROFILES, default='compatibility',
                        help='Compatibility only (default), generated exporter behavior, or behavior plus installed AAO/NDMF integration')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    filters = profile_filters(args.profile)
    identity_before = source_identity(root)
    liltoon_before = installed_liltoon(args.project, args.expect_liltoon)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Unique result names prevent an earlier successful XML from satisfying a failed run.
    stamp = str(time.time_ns())
    xml = output / (stamp + '.xml')
    log = output / (stamp + '.log')
    env = dict(os.environ, VRVLOG_TEST_UNIVRM=args.expect_univrm, VRVLOG_TEST_UNITY=args.expect_unity)
    command = [str(args.unity.resolve()), '-batchmode', '-projectPath', str(args.project.resolve()),
               '-runTests', '-testPlatform', 'EditMode', '-testFilter', ';'.join(filters),
               '-testResults', str(xml), '-logFile', str(log)]
    # GPU-backed editor is needed for real material/texture behavior; no -nographics.
    flags = {'creationflags': subprocess.CREATE_NO_WINDOW} if os.name == 'nt' else {}
    started = time.monotonic()
    result = subprocess.run(command, env=env, timeout=args.timeout, **flags)
    if result.returncode != 0 or not xml.exists():
        raise SystemExit('Unity failed or produced no test result. See ' + str(log))
    cases = validate_result(xml, args.profile)
    identity_after = source_identity(root)
    if any(identity_before[key] != identity_after[key] for key in ('packageVersion', 'gitCommit')):
        raise SystemExit('Runner package version or Git commit changed during Unity validation')
    # Unity may re-resolve packages while it runs; the receipt must name what was tested.
    liltoon_after = installed_liltoon(args.project, args.expect_liltoon)
    # A project that Unity has never opened has no lock before the run; only a
    # version that was readable beforehand can be compared.
    if 'unavailable' not in liltoon_before and liltoon_after['installed'] != liltoon_before['installed']:
        raise SystemExit('Installed lilToon changed during Unity validation')
    report = {'profile': args.profile, 'expectedUniVrm': args.expect_univrm, 'unity': args.expect_unity,
              'lilToon': liltoon_after, 'tests': len(cases), 'result': 'Passed', 'runnerSource': identity_before,
              'seconds': round(time.monotonic() - started), 'xml': xml.name, 'log': log.name}
    (output / (stamp + '.json')).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
