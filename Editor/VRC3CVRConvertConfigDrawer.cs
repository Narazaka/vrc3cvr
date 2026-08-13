#if VRC_SDK_VRCSDK3
using System.Collections.Generic;
using UnityEngine;
using PeanutTools_VRC3CVR.Localization;
using UnityEditor;

[CustomPropertyDrawer(typeof(VRC3CVRConvertConfig), true)]
public class VRC3CVRConvertConfigDrawer : PropertyDrawer
{
    class T
    {
        public static istring PlayableLayers => new istring("Playable Layers", "Playable Layers");
        public static istring ConvertLocomotionAnimator => new istring("Convert Locomotion Animator (movement)", "Locomotionレイヤーを変換 (移動)");
        public static istring ConvertLocomotionAnimatorDescription => new istring("Replaces ChilloutVR's own locomotion layer with the avatar's Base layer. A Base layer holding only VRChat's proxy placeholders is left to ChilloutVR's locomotion.", "ChilloutVR自身のlocomotionレイヤーをアバターのBaseレイヤーで置き換えます。VRChatのプレースホルダーしか持たないBaseレイヤーはChilloutVRのlocomotionに任せます。");
        public static istring PlayLandingAnimation => new istring("Play the landing animation", "着地アニメーションを再生する");
        public static istring PlayLandingAnimationDescription => new istring("Without this the converted landing freezes to a single pose and the body dips sharply; this plays ChilloutVR's landing animation on its own timing instead.", "OFFだと変換後の着地がポーズ1枚固定になり体が急に沈むため、代わりにChilloutVRの着地アニメーションを本来のタイミングで再生します。");
        public static istring ConvertLocomotionTrackingControl => new istring("Convert Tracking Control in the locomotion layer", "locomotionレイヤーのTracking Controlを変換する");
        public static istring ConvertLocomotionTrackingControlDescription => new istring("Applies to every machine folded into the locomotion layer. Tracking Control, common in VRChat's landing states, causes full-body tracking jitter in ChilloutVR when converted.", "locomotionレイヤーに畳み込まれる機械すべてが対象です。VRChatの着地ステートにありがちなTracking Controlを変換するとCVRでFBTががたつく原因になります。");
        public static istring ConvertAdditiveAnimator => new istring("Convert Additive Animator (additive blend layers)", "Additiveレイヤーを変換");
        public static istring ConvertAdditiveAnimatorDescription => new istring("Additive state machine is commonly used for additively blended animations on the base avatar.", "Additiveステートマシンは、ベースアバターの加算ブレンドアニメーションに一般的に使用されます。");
        public static istring ConvertGestureAnimator => new istring("Convert Gesture Animator (hands)", "Gestureレイヤーを変換 (手)");
        public static istring ConvertGestureAnimatorDescription => new istring("If your avatar overwrites the default finger animations when performing expressions", "アバターが表情を実行するときにデフォルトの指のアニメーションを上書きする場合はON");
        public static istring ConvertActionAnimator => new istring("Convert Action Animator (emotes)", "Actionレイヤーを変換 (エモート)");
        public static istring ConvertActionAnimatorDescription => new istring("Folds the emote machine into the locomotion layer, driven by ChilloutVR's own emote menu. A layer the fold has no equivalent for is left out, with a warning naming the reason.", "エモートの機械をlocomotionレイヤーに畳み込み、ChilloutVR自身のエモートメニューで動かします。畳み込みでは代替できないレイヤーは、理由を挙げた警告を出して対象外になります。");
        public static istring ConvertSittingAnimator => new istring("Convert Sitting Animator (seats)", "Sittingレイヤーを変換 (着席)");
        public static istring ConvertSittingAnimatorDescription => new istring("Only applies to an avatar with its own sitting animations; a stock Sitting layer is left to ChilloutVR's own seated pose.", "独自の着席アニメーションを持つアバターのみが対象で、標準のSittingレイヤーはChilloutVRの着席ポーズに任せます。");
        public static istring ConvertFXAnimator => new istring("Convert FX Animator (blendshapes, particles, ect.)", "FXレイヤーを変換 (ブレンドシェイプ、パーティクルなど)");
        public static istring ConvertFXAnimatorDescription => new istring("FX state machine is commonly used all effects which don't affect the underlying rig, such as blendshapes and particle effects.", "FXステートマシンは、ブレンドシェイプやパーティクルエフェクトなど、基礎的なリグに影響を与えないすべてのエフェクトに一般的に使用されます。");
        public static istring PreserveParameterSyncState => new istring("Preserve parameter sync state", "パラメータの同期状態を保持");
        public static istring PreserveParameterSyncStateDescription => new istring("In ChilloutVR, all Animation parameters that do not have a # prefix in their name will be synchronized. Turning this option on will add a # prefix to parameters that will not be synchronized.", "ChilloutVRでは名前の最初に#が付かないAnimationパラメーターは全て同期されます。このオプションをONにすると同期されないパラメーターに#プレフィクスを付けます。");
        public static istring TrackingControl => new istring("Tracking Control", "トラッキングコントロール");
        public static istring ConvertVRCAnimatorLocomotionControl => new istring("Convert VRC Animator Locomotion Control", "VRC Animator Locomotion Controlを変換");
        public static istring ConvertVRCAnimatorLocomotionControlDescription => new istring("Converts the VRC Animator Locomotion Control to BodyControl", "VRC Animator Locomotion ControlをBodyControlに変換");
        public static istring ConvertVRCAnimatorTrackingControl => new istring("Convert VRC Animator Tracking Control", "VRC Animator Tracking Controlを変換");
        public static istring ConvertVRCAnimatorTrackingControlDescription => new istring("Converts the VRC Animator Tracking Control to BodyControl", "VRC Animator Tracking ControlをBodyControlに変換");
        public static istring ParameterCompatibility => new istring("Parameter Compatibility", "パラメータ互換性");
        public static istring GestureWeightConversionMode => new istring("GestureWeight", "GestureWeight");
        public static istring GestureWeightModeFold => new istring("No latency (a few rare usages incompatible)", "遅延なし (一部の稀な使い方で非互換)");
        public static istring GestureWeightModeDerived => new istring("Covers every usage (1 frame latency)", "すべての使い方に対応 (1フレーム遅延)");
        public static istring GestureWeightConversionModeDescription => new istring("\"No latency\" misses one rare usage (weight-driven motion outside Fist). \"Covers every usage\" handles it too, one frame late. The default is fine for most avatars.", "「遅延なし」はFist以外でweight駆動が動く稀な使い方のみ非対応。「すべての使い方に対応」はそれも再現しますが1フレーム遅れます。通常は既定のままで問題ありません。");
        public static istring ActionZeroWeightStateMode => new istring("Zero-Weight States", "ウエイト0ステート");
        public static istring ActionZeroWeightStatePassThrough => new istring("Leave at once (emotes follow on sooner)", "すぐ抜ける (次のエモートが早く始まる)");
        public static istring ActionZeroWeightStateKeep => new istring("Change nothing (the bicycle pose is seen)", "変更しない (自転車ポーズが見える)");
        public static istring ActionZeroWeightStateModeDescription => new istring("Fixes the time an avatar spends in the bicycle pose -- Unity's own humanoid default -- once an emote is switched or cancelled.", "エモートの切り替え・キャンセル直後に、一定時間アバターが自転車ポーズ（Unity Humanoidの既定ポーズ）になる問題への対処です。");
        public static istring FeedGameStateParameters => new istring("Feed MuteSelf / VRMode / Upright", "MuteSelf / VRMode / Upright を供給");
        public static istring FeedGameStateParametersDescription => new istring("Feeds VRChat's VRMode / MuteSelf / Upright from the game state and syncs them to remote viewers (only the ones the avatar uses; costs sync bits).", "VRChat組み込みのVRMode / MuteSelf / Uprightをゲーム状態から供給し、リモートにも同期します（アバターが使うもののみ。同期容量を消費します）。");
        public static istring VRCComponents => new istring("VRC Components", "VRCコンポーネント");
        public static istring ConvertVRCContactSendersAndReceivers => new istring("Convert VRC Contacts to CVR Pointer and CVR Advanced Avatar Trigger", "VRC ContactをCVR PointerとCVR Advanced Avatar Triggerに変換");
        public static istring ConvertVRCContactSendersAndReceiversDescription => new istring("Unlike VRC Contact, CVR Pointer and Trigger only change values when the contact collides. This difference may cause compatibility issues.", "VRCContactと違って、CVR PointerやTriggerはContactが衝突した時にしか値を変更しません。この差異によって互換性の問題を生じる可能性があります。");
        public static istring CollisionTagConvertionConfig => new istring("Collision Tag Convertion Config", "Collision Tag 変換設定");
        public static istring CollisionTagConvertionConfigWithPaths => new istring("Collision Tag Convertion Config per path", "パスごとのCollision Tag 変換設定");
        public static istring Legacy => new istring("Legacy", "レガシー");
        public static istring CreateVRCContactEquivalentPointers => new istring("Create VRC Contact Equivalent CVR Pointers", "VRC Contact 相当の CVR Pointer を作成");
        public static istring CreateVRCContactEquivalentPointersDescription => new istring("Creates CVR Pointers for VRC default Contact Senders (legacy)", "VRCデフォルトの VRC Contact Senderに相当するCVR Pointerを作成します (レガシー)");
        public static istring AdjustContactParameterSync => new istring("Adjust Contact Parameter Sync", "Contact Receiverに使用されるパラメーターを同期させる");
        public static istring AdjustContactParameterSyncDescription => new istring("Unlike the Contact Receiver, the CVR Advanced Avatar Trigger doesn't operate remotely, so it synchronizes parameters to replicate its functionality.", "CVR Advanced Avatar TriggerはContact Receiverと違ってリモートで動作しないため、パラメーター側で同期させて動作を再現します。");
        public static istring Menu => new istring("Menu", "メニュー");
        public static istring AdjustToVrcMenuOrder => new istring("Adjust to VRC menu order", "VRCメニューの順序に調整");
        public static istring UseHierarchicalMenuName => new istring("Use hierarchical menu name", "階層メニュー名を使用");
        public static istring UseHierarchicalDropdownMenuName => new istring("Use hierarchical dropdown menu name", "ドロップダウンメニュー名も階層化");
        public static istring AddActionMenuModAnnotations => new istring("Add Action Menu Mod annotations", "Action Menu Mod用の種別タグを付与");
        public static istring ConvertVrcConstraints => new istring("Convert VRC Constraints", "VRC Constraintsを変換");
        public static istring ConvertVrcConstraintsDescription => new istring("Converts VRC Constraints to Unity Constraints. VRC-only features (FreezeToWorld etc.) are dropped with warnings.", "VRC ConstraintsをUnity Constraintsに変換します。VRC固有機能（FreezeToWorld等）は警告つきで破棄されます。");
        public static istring ConvertVrcHeadChops => new istring("Convert VRC Head Chops", "VRC Head Chopを変換");
        public static istring ConvertVrcHeadChopsDescription => new istring("Only commonly used scales of 0 or 1 are converted.", "よく使われるスケールが0か1のもののみ変換します。");
        public static istring ConvertVrcSpatialAudioSources => new istring("Convert VRC Spatial Audio Sources", "VRC Spatial Audio Sourceを変換");
        public static istring ConvertVrcSpatialAudioSourcesDescription => new istring("The Audio Source 3D Sound Settings that are ignored in VRChat are adjusted heuristically (experimental).", "VRChatで無視されているAudio Sourceの3D Sound Settingsを雰囲気で補正します（試験的）。");
        public static istring DeleteVRCAvatarDescriptorAndPipelineManager => new istring("Delete VRC Avatar Descriptor and Pipeline Manager", "VRC Avatar DescriptorとPipeline Managerを削除");
        public static istring DeletePhysBonesAndColliders => new istring("Delete PhysBones and colliders", "PhysBonesとコライダーを削除");
        public static istring DeleteContactsDescription => new istring("Always deletes contact receivers and senders", "VRC Contact ReceiverとSenderは常に削除されます");
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        new PropertyDrawerGUI(position, property, true).GUI();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var position = new Rect();
        return new PropertyDrawerGUI(position, property, false).GUI();
    }

    struct PropertyDrawerGUI
    {
        Rect position;
        SerializedProperty vrc3cvr;
        bool draw;

        public PropertyDrawerGUI(Rect position, SerializedProperty property, bool draw)
        {
            this.position = position;
            this.vrc3cvr = property;
            this.draw = draw;
        }

        void ToggleRaw(string propertyName, string labelText, string tooltip = "")
        {
            Height1();
            if (draw)
            {
                var property = vrc3cvr.FindPropertyRelative(propertyName);
                var label = new GUIContent(labelText, tooltip);
                var pos = Indented();
                var indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUI.BeginProperty(pos, label, property);
                property.boolValue = EditorGUI.ToggleLeft(pos, label, property.boolValue);
                EditorGUI.EndProperty();
                EditorGUI.indentLevel = indentLevel;
            }
            LF();
        }

        void Toggle(string propertyName, string labelText)
        {
            ToggleRaw(propertyName, labelText);
            SmallLineGap();
        }

        void Toggle(string propertyName, string labelText, string description)
        {
            ToggleRaw(propertyName, labelText, description);
            HelpBoxRaw(description);
            SmallLineGap();
        }

        void HelpBoxRaw(string message)
        {
            Height(EditorGUIUtility.singleLineHeight * 1.7f);
            if (draw)
            {
                EditorGUI.indentLevel++;
                EditorGUI.HelpBox(Indented(), message, MessageType.None);
                EditorGUI.indentLevel--;
            }
            LF();
        }

        void HelpBox(string message)
        {
            HelpBoxRaw(message);
            SmallLineGap();
        }

        void EnumPopup(string propertyName, string labelText, string[] optionLabels, string description)
        {
            Height1();
            if (draw)
            {
                var property = vrc3cvr.FindPropertyRelative(propertyName);
                var label = new GUIContent(labelText);
                var pos = Indented();
                var indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUI.BeginProperty(pos, label, property);
                property.enumValueIndex = EditorGUI.Popup(pos, labelText, property.enumValueIndex, optionLabels);
                EditorGUI.EndProperty();
                EditorGUI.indentLevel = indentLevel;
            }
            LF();
            HelpBoxRaw(description);
            SmallLineGap();
        }

        void SmallLineGap()
        {
            // HeightMini();
            // LF();
        }

        void RenderLinkRaw(string label, string url)
        {
            Height1();
            if (draw)
            {
                if (position.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);

                    if (Event.current.type == EventType.MouseUp)
                    {
                        Help.BrowseURL(url);
                    }
                }

                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = new Color(0.5f, 0.5f, 1);

                var indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUI.LabelField(Indented(), label, style);
                EditorGUI.indentLevel = indentLevel;
            }
            LF();
        }

        static readonly Dictionary<string, bool> openSections = new Dictionary<string, bool>();

        bool Section(string key, string labelText)
        {
            var open = openSections.TryGetValue(key, out var remembered) && remembered;
            Height1();
            if (draw)
            {
                var indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                open = EditorGUI.Foldout(Indented(), open, labelText, true);
                EditorGUI.indentLevel = indentLevel;
                openSections[key] = open;
            }
            LF();
            return open;
        }

        void RenderLink(string label, string url)
        {
            RenderLinkRaw(label, url);
            SmallLineGap();
        }

        void Height(float height)
        {
            var pos = position;
            pos.height = height;
            position = pos;
        }

        void Height1(int lines = 1)
        {
            Height(EditorGUIUtility.singleLineHeight * lines);
        }

        void HeightMini()
        {
            Height(EditorGUIUtility.singleLineHeight * 0.2f);
        }

        void LF()
        {
            var pos = position;
            pos.y += position.height + EditorGUIUtility.standardVerticalSpacing;
            position = pos;
        }

        Rect Indented()
        {
            return EditorGUI.IndentedRect(position);
        }

        public float GUI()
        {
            if (Section("playableLayers", T.PlayableLayers))
            {
                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.convertLocomotionLayer), T.ConvertLocomotionAnimator, T.ConvertLocomotionAnimatorDescription);

                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.playLandingAnimation), T.PlayLandingAnimation, T.PlayLandingAnimationDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertLocomotionTrackingControl), T.ConvertLocomotionTrackingControl, T.ConvertLocomotionTrackingControlDescription);

                EditorGUI.indentLevel--;

                Toggle(nameof(VRC3CVRConvertConfig.convertAdditiveLayer), T.ConvertAdditiveAnimator, T.ConvertAdditiveAnimatorDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertGestureLayer), T.ConvertGestureAnimator, T.ConvertGestureAnimatorDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertActionLayer), T.ConvertActionAnimator, T.ConvertActionAnimatorDescription);

                EditorGUI.indentLevel++;

                EnumPopup(nameof(VRC3CVRConvertConfig.actionZeroWeightStateMode), T.ActionZeroWeightStateMode,
                    new string[] { T.ActionZeroWeightStatePassThrough, T.ActionZeroWeightStateKeep },
                    T.ActionZeroWeightStateModeDescription);

                EditorGUI.indentLevel--;

                Toggle(nameof(VRC3CVRConvertConfig.convertSittingLayer), T.ConvertSittingAnimator, T.ConvertSittingAnimatorDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertFXLayer), T.ConvertFXAnimator, T.ConvertFXAnimatorDescription);

                EditorGUI.indentLevel--;
            }

            if (Section("trackingControl", T.TrackingControl))
            {
                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.convertVRCAnimatorLocomotionControl), T.ConvertVRCAnimatorLocomotionControl, T.ConvertVRCAnimatorLocomotionControlDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertVRCAnimatorTrackingControl), T.ConvertVRCAnimatorTrackingControl, T.ConvertVRCAnimatorTrackingControlDescription);

                EditorGUI.indentLevel--;
            }

            if (Section("parameterCompatibility", T.ParameterCompatibility))
            {
                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.preserveParameterSyncState), T.PreserveParameterSyncState, T.PreserveParameterSyncStateDescription);

                EnumPopup(nameof(VRC3CVRConvertConfig.gestureWeightConversionMode), T.GestureWeightConversionMode, new string[] { T.GestureWeightModeFold, T.GestureWeightModeDerived }, T.GestureWeightConversionModeDescription);

                Toggle(nameof(VRC3CVRConvertConfig.feedGameStateParameters), T.FeedGameStateParameters, T.FeedGameStateParametersDescription);

                EditorGUI.indentLevel--;
            }

            if (Section("vrcComponents", T.VRCComponents))
            {
                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.convertVRCContactSendersAndReceivers), T.ConvertVRCContactSendersAndReceivers, T.ConvertVRCContactSendersAndReceiversDescription);

                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.adjustContactParameterSync), T.AdjustContactParameterSync, T.AdjustContactParameterSyncDescription);

                EditorGUI.indentLevel--;

                Toggle(nameof(VRC3CVRConvertConfig.convertVrcConstraints), T.ConvertVrcConstraints, T.ConvertVrcConstraintsDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertVrcHeadChops), T.ConvertVrcHeadChops, T.ConvertVrcHeadChopsDescription);

                Toggle(nameof(VRC3CVRConvertConfig.convertVrcSpatialAudioSources), T.ConvertVrcSpatialAudioSources, T.ConvertVrcSpatialAudioSourcesDescription);

                Toggle(nameof(VRC3CVRConvertConfig.shouldDeleteVRCAvatarDescriptorAndPipelineManager), T.DeleteVRCAvatarDescriptorAndPipelineManager);

                Toggle(nameof(VRC3CVRConvertConfig.shouldDeletePhysBones), T.DeletePhysBonesAndColliders, T.DeleteContactsDescription);

                EditorGUI.indentLevel--;
            }

            RenderLink("Physbone -> DynamicBone Tool?", "https://github.com/FACS01-01/PhysBone-to-DynamicBone");

            if (Section("menu", T.Menu))
            {
                EditorGUI.indentLevel++;

                Toggle(nameof(VRC3CVRConvertConfig.adjustToVrcMenuOrder), T.AdjustToVrcMenuOrder);

                Toggle(nameof(VRC3CVRConvertConfig.useHierarchicalMenuName), T.UseHierarchicalMenuName);
                Toggle(nameof(VRC3CVRConvertConfig.useHierarchicalDropdownMenuName), T.UseHierarchicalDropdownMenuName);
                Toggle(nameof(VRC3CVRConvertConfig.addActionMenuModAnnotations), T.AddActionMenuModAnnotations);

                EditorGUI.indentLevel--;
            }

            if (Section("legacy", T.Legacy))
            {
                EditorGUI.indentLevel++;

                var collisionTagConvertionConfigProperty = vrc3cvr.FindPropertyRelative(nameof(VRC3CVRConvertConfig.collisionTagConvertionConfig));
                var collisionTagConvertionConfigLabel = T.CollisionTagConvertionConfig.GUIContent;
                Height(EditorGUI.GetPropertyHeight(collisionTagConvertionConfigProperty, collisionTagConvertionConfigLabel, true));
                if (draw)
                {
                    EditorGUI.PropertyField(position, collisionTagConvertionConfigProperty, collisionTagConvertionConfigLabel, true);
                }
                LF();

                var collisionTagConvertionConfigWithPathsProperty = vrc3cvr.FindPropertyRelative(nameof(VRC3CVRConvertConfig.collisionTagConvertionConfigWithPaths));
                var collisionTagConvertionConfigWithPathsLabel = T.CollisionTagConvertionConfigWithPaths.GUIContent;
                Height(EditorGUI.GetPropertyHeight(collisionTagConvertionConfigWithPathsProperty, collisionTagConvertionConfigWithPathsLabel, true));
                if (draw) EditorGUI.PropertyField(position, collisionTagConvertionConfigWithPathsProperty, collisionTagConvertionConfigWithPathsLabel, true);
                LF();

                Toggle(nameof(VRC3CVRConvertConfig.createVRCContactEquivalentPointers), T.CreateVRCContactEquivalentPointers, T.CreateVRCContactEquivalentPointersDescription);

                EditorGUI.indentLevel--;
            }

            return position.y;
        }
    }
}
#endif

