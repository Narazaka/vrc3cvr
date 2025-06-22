using UnityEngine;
using PeanutTools_VRC3CVR.Localization;
using UnityEditor;

[CustomPropertyDrawer(typeof(VRC3CVRConvertConfig), true)]
public class VRC3CVRConvertConfigDrawer : PropertyDrawer
{
    class T
    {
        public static istring PlayableLayers => new istring("Playable Layers", "Playable Layers");
        public static istring ConvertLocomotionAnimator => new istring("Convert Locomotion Animator (NOT RECOMMEND)", "Locomotionレイヤーを変換 (非推奨)");
        public static istring ConvertLocomotionAnimatorDescription => new istring("Locomotion state machines will very likely not convert over correctly and this option is better left unticked for now", "Locomotionステートマシンは正しく変換されない可能性が高く、このオプションは今のところチェックを外しておくことをお勧めします");
        public static istring ConvertAdditiveAnimator => new istring("Convert Additive Animator (additive blend layers)", "Additiveレイヤーを変換");
        public static istring ConvertAdditiveAnimatorDescription => new istring("Additive state machine is commonly used for additively blended animations on the base avatar. May cause bicycle pose on certain avatars.", "Additiveステートマシンは、ベースアバターの加算ブレンドアニメーションに一般的に使用されます。特定のアバターで自転車ポーズを引き起こす可能性があります。");
        public static istring ConvertGestureAnimator => new istring("Convert Gesture Animator (hands)", "Gestureレイヤーを変換 (手)");
        public static istring ConvertGestureAnimatorDescription => new istring("If your avatar overwrites the default finger animations when performing expressions", "アバターが表情を実行するときにデフォルトの指のアニメーションを上書きする場合はON");
        public static istring ConvertActionAnimator => new istring("Convert Action Animator (NOT RECOMMEND)", "Actionレイヤーを変換 (非推奨)");
        public static istring ConvertActionAnimatorDescription => new istring("Actions (mostly used for emotes) will very likely not convert over correctly and this option is better left unticked for now", "アクション (主にエモートに使用される) は正しく変換されない可能性が高く、このオプションは今のところチェックを外しておくことをお勧めします");
        public static istring ConvertFXAnimator => new istring("Convert FX Animator (blendshapes, particles, ect.)", "FXレイヤーを変換 (ブレンドシェイプ、パーティクルなど)");
        public static istring ConvertFXAnimatorDescription => new istring("FX state machine is commonly used all effects which don't affect the underlying rig, such as blendshapes and particle effects.", "FXステートマシンは、ブレンドシェイプやパーティクルエフェクトなど、基礎的なリグに影響を与えないすべてのエフェクトに一般的に使用されます。");
        public static istring PreserveParameterSyncState => new istring("Preserve parameter sync state", "パラメータの同期状態を保持");
        public static istring PreserveParameterSyncStateDescription => new istring("In ChilloutVR, all Animation parameters that do not have a # prefix in their name will be synchronized. Turning this option on will add a # prefix to parameters that will not be synchronized.", "ChilloutVRでは名前の最初に#が付かないAnimationパラメーターは全て同期されます。このオプションをONにすると同期されないパラメーターに#プレフィクスを付けます。");
        public static istring TrackingControl => new istring("Tracking Control", "トラッキングコントロール");
        public static istring ConvertVRCAnimatorLocomotionControl => new istring("Convert VRC Animator Locomotion Control", "VRC Animator Locomotion Controlを変換");
        public static istring ConvertVRCAnimatorLocomotionControlDescription => new istring("Converts the VRC Animator Locomotion Control to BodyControl", "VRC Animator Locomotion ControlをBodyControlに変換");
        public static istring ConvertVRCAnimatorTrackingControl => new istring("Convert VRC Animator Tracking Control", "VRC Animator Tracking Controlを変換");
        public static istring ConvertVRCAnimatorTrackingControlDescription => new istring("Converts the VRC Animator Tracking Control to BodyControl", "VRC Animator Tracking ControlをBodyControlに変換");
        public static istring VRCContacts => new istring("VRC Contacts", "VRC Contact");
        public static istring ConvertVRCContactSendersAndReceivers => new istring("Convert VRC Contact Senders and Receivers to CVR Pointer and CVR Advanced Avatar Trigger", "VRC Contact SenderとReceiverをCVR PointerとCVR Advanced Avatar Triggerに変換");
        public static istring ConvertVRCContactSendersAndReceiversDescription => new istring("Unlike VRC Contact, CVR Pointer and Trigger only change values when the contact collides. This difference may cause compatibility issues.", "VRCContactと違って、CVR PointerやTriggerはContactが衝突した時にしか値を変更しません。この差異によって互換性の問題を生じる可能性があります。");
        public static istring CollisionTagConvertionConfig => new istring("Collision Tag Convertion Config", "Collision Tag 変換設定");
        public static istring CollisionTagConvertionConfigDescription => new istring("Convert \"Head\" to \"mouth\" and \"Hand\"s and \"Finger\"s to \"index\"?", "\"Hand\"を\"mouth\"に、\"Hand\"等と\"Finger\"等を\"index\"に変換する?");
        public static istring CollisionTagConvertionConfigWithPaths => new istring("Collision Tag Convertion Config per path", "パスごとのCollision Tag 変換設定");
        public static istring CreateVRCContactEquivalentPointers => new istring("Create VRC Contact Equivalent CVR Pointers", "VRC Contact 相当の CVR Pointer を作成");
        public static istring CreateVRCContactEquivalentPointersDescription => new istring("Creates CVR Pointers for VRC default Contact Senders", "VRCデフォルトの VRC Contact Senderに相当するCVR Pointerを作成します");
        public static istring Menu => new istring("Menu", "メニュー");
        public static istring AdjustToVrcMenuOrder => new istring("Adjust to VRC menu order", "VRCメニューの順序に調整");
        public static istring UseHierarchicalMenuName => new istring("Use hierarchical menu name", "階層メニュー名を使用");
        public static istring UseHierarchicalDropdownMenuName => new istring("Use hierarchical dropdown menu name", "ドロップダウンメニュー名も階層化");
        public static istring AddActionMenuModAnnotations => new istring("Add Action Menu Mod annotations", "Action Menu Mod用の種別タグを付与");
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

        void ToggleRaw(string propertyName, string labelText)
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
            ToggleRaw(propertyName, labelText);
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

        void HeaderLabel(string labelText)
        {
            Height1();
            if (draw)
            {
                var indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUI.LabelField(Indented(), labelText);
                EditorGUI.indentLevel = indentLevel;
            }
            LF();
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
            HeaderLabel(T.PlayableLayers);

            EditorGUI.indentLevel++;

            Toggle(nameof(VRC3CVRConvertConfig.convertLocomotionLayer), T.ConvertLocomotionAnimator, T.ConvertLocomotionAnimatorDescription);
            
            Toggle(nameof(VRC3CVRConvertConfig.convertAdditiveLayer), T.ConvertAdditiveAnimator, T.ConvertAdditiveAnimatorDescription);

            Toggle(nameof(VRC3CVRConvertConfig.convertGestureLayer), T.ConvertGestureAnimator, T.ConvertGestureAnimatorDescription);
            
            Toggle(nameof(VRC3CVRConvertConfig.convertActionLayer), T.ConvertActionAnimator, T.ConvertActionAnimatorDescription);
            
            Toggle(nameof(VRC3CVRConvertConfig.convertFXLayer), T.ConvertFXAnimator, T.ConvertFXAnimatorDescription);

            EditorGUI.indentLevel--;

            Toggle(nameof(VRC3CVRConvertConfig.preserveParameterSyncState), T.PreserveParameterSyncState, T.PreserveParameterSyncStateDescription);
            
            HeaderLabel(T.TrackingControl);

            EditorGUI.indentLevel++;

            Toggle(nameof(VRC3CVRConvertConfig.convertVRCAnimatorLocomotionControl), T.ConvertVRCAnimatorLocomotionControl, T.ConvertVRCAnimatorLocomotionControlDescription);
            
            Toggle(nameof(VRC3CVRConvertConfig.convertVRCAnimatorTrackingControl), T.ConvertVRCAnimatorTrackingControl, T.ConvertVRCAnimatorTrackingControlDescription);

            EditorGUI.indentLevel--;

            HeaderLabel(T.VRCContacts);

            EditorGUI.indentLevel++;

            Toggle(nameof(VRC3CVRConvertConfig.convertVRCContactSendersAndReceivers), T.ConvertVRCContactSendersAndReceivers, T.ConvertVRCContactSendersAndReceiversDescription);

            Toggle(nameof(VRC3CVRConvertConfig.createVRCContactEquivalentPointers), T.CreateVRCContactEquivalentPointers, T.CreateVRCContactEquivalentPointersDescription);

            var collisionTagConvertionConfigProperty = vrc3cvr.FindPropertyRelative(nameof(VRC3CVRConvertConfig.collisionTagConvertionConfig));
            var collisionTagConvertionConfigLabel = T.CollisionTagConvertionConfig.GUIContent;
            Height(EditorGUI.GetPropertyHeight(collisionTagConvertionConfigProperty, collisionTagConvertionConfigLabel, true));
            if (draw) EditorGUI.PropertyField(position, collisionTagConvertionConfigProperty, collisionTagConvertionConfigLabel, true);
            LF();
            HelpBox(T.CollisionTagConvertionConfigDescription);

            var collisionTagConvertionConfigWithPathsProperty = vrc3cvr.FindPropertyRelative(nameof(VRC3CVRConvertConfig.collisionTagConvertionConfigWithPaths));
            var collisionTagConvertionConfigWithPathsLabel = T.CollisionTagConvertionConfigWithPaths.GUIContent;
            Height(EditorGUI.GetPropertyHeight(collisionTagConvertionConfigWithPathsProperty, collisionTagConvertionConfigWithPathsLabel, true));
            if (draw) EditorGUI.PropertyField(position, collisionTagConvertionConfigWithPathsProperty, collisionTagConvertionConfigWithPathsLabel, true);
            LF();

            EditorGUI.indentLevel--;

            RenderLink("Physbone -> DynamicBone Tool?", "https://github.com/Dreadrith/PhysBone-Converter");

            HeaderLabel(T.Menu);

            EditorGUI.indentLevel++;

            Toggle(nameof(VRC3CVRConvertConfig.adjustToVrcMenuOrder), T.AdjustToVrcMenuOrder);

            Toggle(nameof(VRC3CVRConvertConfig.useHierarchicalMenuName), T.UseHierarchicalMenuName);
            Toggle(nameof(VRC3CVRConvertConfig.useHierarchicalDropdownMenuName), T.UseHierarchicalDropdownMenuName);
            Toggle(nameof(VRC3CVRConvertConfig.addActionMenuModAnnotations), T.AddActionMenuModAnnotations);

            EditorGUI.indentLevel--;

            Toggle(nameof(VRC3CVRConvertConfig.shouldDeleteVRCAvatarDescriptorAndPipelineManager), T.DeleteVRCAvatarDescriptorAndPipelineManager);

            Toggle(nameof(VRC3CVRConvertConfig.shouldDeletePhysBones), T.DeletePhysBonesAndColliders, T.DeleteContactsDescription);

            return position.y;
        }
    }
}
